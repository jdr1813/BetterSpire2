using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using SysRandom = System.Random;

namespace BetterSpire2;

/// <summary>
/// Patches synchronizer check methods so the game skips kicked players
/// instead of waiting forever for their response.
///
/// Key insight: most synchronizers don't have a separate "AllPlayersReady" method.
/// Instead they use inline LINQ checks (e.g. _votes.All(v => v != null)) inside
/// the vote/ready method itself. So we must use PREFIX patches to pre-fill
/// kicked player slots BEFORE the inline check runs.
/// </summary>
public static class KickPatches
{
    private static readonly Assembly _gameAssembly = typeof(CombatManager).Assembly;
    private static readonly SysRandom _rng = new();

    /// <summary>
    /// From a list of votes (with kicked players excluded), pick the majority vote.
    /// On a tie, randomly pick among the tied options.
    /// </summary>
    private static object? GetMajorityVote(System.Collections.IList list, List<int> kickedIndices)
    {
        var voteCounts = new Dictionary<string, (int count, object vote)>();
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == null || kickedIndices.Contains(i)) continue;
            string key = list[i].ToString() ?? "";
            if (voteCounts.ContainsKey(key))
                voteCounts[key] = (voteCounts[key].count + 1, voteCounts[key].vote);
            else
                voteCounts[key] = (1, list[i]);
        }

        if (voteCounts.Count == 0) return null;

        // Find the max vote count
        int maxCount = 0;
        foreach (var kvp in voteCounts)
            if (kvp.Value.count > maxCount)
                maxCount = kvp.Value.count;

        // Collect all options tied at the max
        var tied = new List<object>();
        foreach (var kvp in voteCounts)
            if (kvp.Value.count == maxCount)
                tied.Add(kvp.Value.vote);

        // If clear winner, use it. If tied, pick randomly.
        return tied.Count == 1 ? tied[0] : tied[_rng.Next(tied.Count)];
    }

    public static void Apply(Harmony harmony, ref int succeeded, ref int failed)
    {
        // ─── Combat (Postfix — has separate check method) ───

        TryPatch(harmony, ref succeeded, ref failed,
            "AllPlayersReadyToEndTurn",
            AccessTools.Method(typeof(CombatManager), "AllPlayersReadyToEndTurn"),
            postfix: new HarmonyMethod(typeof(KickPatches), nameof(AllPlayersReadyPostfix)));

        TryPatch(harmony, ref succeeded, ref failed,
            "SetReadyToEndTurn_KickAutoReady",
            AccessTools.Method(typeof(CombatManager), nameof(CombatManager.SetReadyToEndTurn)),
            postfix: new HarmonyMethod(typeof(KickPatches), nameof(AutoReadyKickedPlayersForCombat)));

        // Also patch SetReadyToBeginEnemyTurn — game waits for all players before enemy turn
        TryPatch(harmony, ref succeeded, ref failed,
            "SetReadyToBeginEnemyTurn_KickAutoReady",
            AccessTools.Method(typeof(CombatManager), "SetReadyToBeginEnemyTurn"),
            postfix: new HarmonyMethod(typeof(KickPatches), nameof(AutoReadyKickedPlayersForCombat)));

        // ─── Map voting (POSTFIX — fill kicked vote then trigger move) ───
        // PlayerVotedForMapCoord(Player, RunLocation, Nullable<MapCoord>)
        // _votes is List<Nullable<MapVote>> indexed by player slot
        // The inline LINQ check already ran and skipped because kicked player is null.
        // We fill their slot and manually call MoveToMapCoord if all are now filled.
        PatchByTypeName(harmony, ref succeeded, ref failed,
            "MapSelectionSynchronizer", "PlayerVotedForMapCoord",
            postfix: new HarmonyMethod(typeof(KickPatches), nameof(MapVotePostfix)));

        // ─── Act change (Postfix — has IsWaitingForOtherPlayers bool method) ───
        PatchByTypeName(harmony, ref succeeded, ref failed,
            "ActChangeSynchronizer", "IsWaitingForOtherPlayers",
            postfix: new HarmonyMethod(typeof(KickPatches), nameof(IsWaitingPostfix)));

        // Also prefix SetLocalPlayerReady and OnPlayerReady
        PatchByTypeName(harmony, ref succeeded, ref failed,
            "ActChangeSynchronizer", "SetLocalPlayerReady",
            prefix: new HarmonyMethod(typeof(KickPatches), nameof(PreFillKickedReadyPlayers)));
        PatchByTypeName(harmony, ref succeeded, ref failed,
            "ActChangeSynchronizer", "OnPlayerReady",
            prefix: new HarmonyMethod(typeof(KickPatches), nameof(PreFillKickedReadyPlayers)));

        // ─── Event voting (POSTFIX — fill kicked vote then trigger choice) ───
        PatchByTypeName(harmony, ref succeeded, ref failed,
            "EventSynchronizer", "PlayerVotedForSharedOptionIndex",
            postfix: new HarmonyMethod(typeof(KickPatches), nameof(EventVotePostfix)));

        // ─── Treasure room relic voting ───
        // After the host picks, auto-assign kicked player the last remaining relic
        PatchByTypeName(harmony, ref succeeded, ref failed,
            "TreasureRoomRelicSynchronizer", "OnPicked",
            postfix: new HarmonyMethod(typeof(KickPatches), nameof(TreasureVotePostfix)));

        // ─── PlayerChoiceSynchronizer — async wait, skip for kicked players ───
        PatchByTypeName(harmony, ref succeeded, ref failed,
            "PlayerChoiceSynchronizer", "WaitForRemoteChoice",
            prefix: new HarmonyMethod(typeof(KickPatches), nameof(WaitForRemoteChoicePrefix)));

        // ─── PeerInputSynchronizer — prevent crash when getting state for kicked player ───
        // The game's ForceGetStateForPlayer throws InvalidOperationException for disconnected
        // players, which causes NHandImageCollection.UpdateHandVisibility to crash on every
        // input event in the treasure room (and potentially other rooms).
        PatchByTypeName(harmony, ref succeeded, ref failed,
            "PeerInputSynchronizer", "ForceGetStateForPlayer",
            prefix: new HarmonyMethod(typeof(KickPatches), nameof(ForceGetStatePrefix)));

        // ─── Un-kick players when they reconnect (e.g. reinvited after a kick) ───
        PatchByTypeName(harmony, ref succeeded, ref failed,
            "NetHostGameService", "OnPeerConnected",
            postfix: new HarmonyMethod(typeof(KickPatches), nameof(OnPeerConnectedPostfix)));

        // ─── Clear kicked players on new run ───
        TryPatch(harmony, ref succeeded, ref failed,
            "RunManager.InitializeNewRun_ClearKicked",
            AccessTools.Method(typeof(RunManager), "InitializeNewRun"),
            prefix: new HarmonyMethod(typeof(KickPatches), nameof(ClearKickedOnNewRun)));
    }

    public static void ClearKickedOnNewRun()
    {
        PartyManager.ClearKicked();
    }

    /// <summary>
    /// When a peer connects, remove them from the kicked list.
    /// Handles the case where a player is kicked, the run ends, and they're reinvited.
    /// </summary>
    public static void OnPeerConnectedPostfix(ulong peerId)
    {
        PartyManager.UnkickPlayer(peerId);
    }

    // ─── Helpers ───

    private static Type? FindType(string typeName)
    {
        foreach (var t in _gameAssembly.GetTypes())
            if (t.Name == typeName)
                return t;
        return null;
    }

    private static void TryPatch(Harmony harmony, ref int succeeded, ref int failed,
        string name, MethodInfo? target, HarmonyMethod? prefix = null, HarmonyMethod? postfix = null)
    {
        try
        {
            if (target == null)
            {
                ModLog.Info($"  Skipped kick patch: {name} — method not found");
                failed++;
                return;
            }
            harmony.Patch(target, prefix: prefix, postfix: postfix);
            ModLog.Info($"  Patched: {name} (kick)");
            succeeded++;
        }
        catch (Exception ex)
        {
            ModLog.Error($"Kick patch {name}", ex);
            failed++;
        }
    }

    private static void PatchByTypeName(Harmony harmony, ref int succeeded, ref int failed,
        string typeName, string methodName, HarmonyMethod? prefix = null, HarmonyMethod? postfix = null)
    {
        var type = FindType(typeName);
        if (type == null)
        {
            ModLog.Info($"  Skipped kick patch: {typeName}.{methodName} — type not found");
            failed++;
            return;
        }
        var method = AccessTools.Method(type, methodName);
        TryPatch(harmony, ref succeeded, ref failed, $"{typeName}.{methodName}", method, prefix, postfix);
    }

    // ─── PREFIX patches (run BEFORE the method's inline checks) ───

    /// <summary>
    /// After a player votes for a map coord, fill kicked player slots and
    /// manually trigger MoveToMapCoord if all slots are now filled.
    /// </summary>
    public static void MapVotePostfix(object __instance)
    {
        try
        {
            if (!PartyManager.HasKickedPlayers) return;
            var kickedIndices = PartyManager.GetKickedPlayerIndices();
            if (kickedIndices.Count == 0) return;

            var traverse = Traverse.Create(__instance);
            var value = traverse.Field("_votes").GetValue();
            if (value is not System.Collections.IList list || list.Count == 0) return;

            // Check if all REAL players have voted — don't fill until they have
            bool allRealPlayersVoted = true;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null && !kickedIndices.Contains(i))
                {
                    allRealPlayersVoted = false;
                    break;
                }
            }
            if (!allRealPlayersVoted) return;

            // Pick majority vote (random tiebreak if tied)
            var majorityVote = GetMajorityVote(list, kickedIndices);
            if (majorityVote == null) return;

            foreach (int idx in kickedIndices)
                if (idx < list.Count && list[idx] == null)
                    list[idx] = majorityVote;

            ModLog.Info("Kick patch: filled kicked player map vote with majority choice");
            var moveMethod = AccessTools.Method(__instance.GetType(), "MoveToMapCoord");
            moveMethod?.Invoke(__instance, null);
        }
        catch (Exception ex) { ModLog.Error("KickPatches.MapVotePostfix", ex); }
    }

    /// <summary>
    /// After a player votes for an event option, fill kicked player slots and
    /// trigger ChooseSharedEventOption if all voted.
    /// </summary>
    public static void EventVotePostfix(object __instance)
    {
        try
        {
            if (!PartyManager.HasKickedPlayers) return;
            var kickedIndices = PartyManager.GetKickedPlayerIndices();
            if (kickedIndices.Count == 0) return;

            var traverse = Traverse.Create(__instance);
            var value = traverse.Field("_playerVotes").GetValue();
            if (value is not System.Collections.IList list || list.Count == 0) return;

            // Wait for all real players to vote first
            bool allRealPlayersVoted = true;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null && !kickedIndices.Contains(i))
                {
                    allRealPlayersVoted = false;
                    break;
                }
            }
            if (!allRealPlayersVoted) return;

            // Pick majority vote (random tiebreak if tied)
            var majorityVote = GetMajorityVote(list, kickedIndices);
            if (majorityVote == null) return;

            foreach (int idx in kickedIndices)
                if (idx < list.Count && list[idx] == null)
                    list[idx] = majorityVote;

            ModLog.Info("Kick patch: filled kicked player event vote with majority choice");
            var chooseMethod = AccessTools.Method(__instance.GetType(), "ChooseSharedEventOption");
            chooseMethod?.Invoke(__instance, null);
        }
        catch (Exception ex) { ModLog.Error("KickPatches.EventVotePostfix", ex); }
    }

    /// <summary>
    /// After a real player picks a relic, auto-assign kicked players the last
    /// remaining relic so the real players get first choice.
    /// Uses OnPicked() directly so the game handles vote types and resolution.
    /// </summary>
    public static void TreasureVotePostfix(object __instance)
    {
        try
        {
            if (!PartyManager.HasKickedPlayers) return;

            var traverse = Traverse.Create(__instance);
            var value = traverse.Field("_votes").GetValue();
            if (value is not System.Collections.IList list || list.Count == 0) return;

            var kickedIndices = PartyManager.GetKickedPlayerIndices();
            if (kickedIndices.Count == 0) return;

            // Check if all REAL players have voted — don't fill until they have
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null && !kickedIndices.Contains(i))
                    return; // a real player hasn't picked yet
            }

            // Check if kicked players already have votes
            bool anyKickedNeedsVote = false;
            foreach (int idx in kickedIndices)
                if (idx < list.Count && list[idx] == null)
                { anyKickedNeedsVote = true; break; }
            if (!anyKickedNeedsVote) return;

            // Collect which relic indices are already taken
            var takenIndices = new HashSet<int>();
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null)
                {
                    try { takenIndices.Add(Convert.ToInt32(list[i])); }
                    catch { }
                }
            }

            // Get the players list to pass to OnPicked
            var runState = Traverse.Create(RunManager.Instance).Property<RunState>("State").Value;
            var players = runState?.Players;
            if (players == null) return;

            var onPickedMethod = AccessTools.Method(__instance.GetType(), "OnPicked");
            if (onPickedMethod == null)
            {
                ModLog.Info("Kick patch: OnPicked method not found on treasure synchronizer");
                return;
            }

            // Find total relic count — try common field names
            int relicCount = 0;
            foreach (var fieldName in new[] { "_currentRelics", "_relics", "_availableRelics" })
            {
                var relicField = traverse.Field(fieldName).GetValue();
                if (relicField is System.Collections.IList relicList)
                {
                    relicCount = relicList.Count;
                    ModLog.Info($"Kick patch: found {relicCount} relics in field '{fieldName}'");
                    break;
                }
            }
            // If we couldn't find relics, estimate from player count
            if (relicCount == 0) relicCount = list.Count;

            foreach (int playerIdx in kickedIndices)
            {
                if (playerIdx >= list.Count || list[playerIdx] != null) continue;
                if (playerIdx >= players.Count) continue;

                // Find the highest index relic not taken
                int assignedRelic = -1;
                for (int r = relicCount - 1; r >= 0; r--)
                {
                    if (!takenIndices.Contains(r))
                    {
                        assignedRelic = r;
                        break;
                    }
                }
                if (assignedRelic < 0) assignedRelic = 0;

                try
                {
                    onPickedMethod.Invoke(__instance, new object[] { players[playerIdx], assignedRelic });
                    takenIndices.Add(assignedRelic);
                    ModLog.Info($"Kick patch: called OnPicked for kicked player slot {playerIdx} with relic index {assignedRelic}");
                }
                catch (Exception ex)
                {
                    ModLog.Error($"Kick patch: OnPicked failed for slot {playerIdx}", ex);
                }
            }
        }
        catch (Exception ex) { ModLog.Error("KickPatches.TreasureVotePostfix", ex); }
    }

    /// <summary>
    /// If WaitForRemoteChoice is called for a kicked player, skip the async wait
    /// and return an immediate result so the game doesn't hang.
    /// </summary>
    public static bool WaitForRemoteChoicePrefix(object __instance, Player player, ref object __result, MethodBase __originalMethod)
    {
        try
        {
            if (!PartyManager.IsPlayerKicked(player)) return true; // run original

            // Need to return a completed Task<PlayerChoiceResult>
            // Get the return type from the original method
            var returnType = (__originalMethod as MethodInfo)!.ReturnType; // Task<PlayerChoiceResult>
            var resultType = returnType.GetGenericArguments()[0]; // PlayerChoiceResult

            // Create a default instance of PlayerChoiceResult
            var defaultResult = Activator.CreateInstance(resultType);

            // Create Task.FromResult(defaultResult)
            var fromResultMethod = typeof(System.Threading.Tasks.Task)
                .GetMethod("FromResult")!
                .MakeGenericMethod(resultType);
            __result = fromResultMethod.Invoke(null, new[] { defaultResult })!;

            ModLog.Info($"Kick patch: skipped WaitForRemoteChoice for kicked player {player.NetId}");
            return false; // skip original
        }
        catch (Exception ex)
        {
            ModLog.Error("KickPatches.WaitForRemoteChoicePrefix", ex);
            return true; // run original on error
        }
    }

    /// <summary>
    /// Intercept ForceGetStateForPlayer so it returns a default PeerInputState
    /// for kicked players instead of throwing InvalidOperationException.
    /// This prevents the crash loop in NHandImageCollection.UpdateHandVisibility
    /// and any other code that queries input state for disconnected players.
    /// </summary>
    public static bool ForceGetStatePrefix(ulong playerId, object __instance, ref object __result)
    {
        try
        {
            if (!PartyManager.IsKicked(playerId)) return true; // run original

            // Use GetOrCreateStateForPlayer which won't throw — it creates a default state
            var getOrCreate = AccessTools.Method(__instance.GetType(), "GetOrCreateStateForPlayer");
            if (getOrCreate != null)
            {
                __result = getOrCreate.Invoke(__instance, new object[] { playerId });
                return false; // skip original
            }

            // Fallback: try GetStateForPlayer (returns null/default if not found)
            var getState = AccessTools.Method(__instance.GetType(), "GetStateForPlayer");
            if (getState != null)
            {
                __result = getState.Invoke(__instance, new object[] { playerId });
                return false;
            }
        }
        catch (Exception ex) { ModLog.Error("KickPatches.ForceGetStatePrefix", ex); }
        return true; // run original as last resort
    }

    /// <summary>
    /// Pre-fill kicked players' slots in _readyPlayers (List of bool).
    /// Works for ActChangeSynchronizer._readyPlayers.
    /// </summary>
    public static void PreFillKickedReadyPlayers(object __instance)
    {
        try
        {
            if (!PartyManager.HasKickedPlayers) return;
            var kickedIndices = PartyManager.GetKickedPlayerIndices();
            if (kickedIndices.Count == 0) return;

            var list = Traverse.Create(__instance).Field("_readyPlayers").GetValue<List<bool>>();
            if (list == null) return;

            foreach (int idx in kickedIndices)
                if (idx < list.Count)
                    list[idx] = true;
        }
        catch (Exception ex) { ModLog.Error("KickPatches.PreFillKickedReadyPlayers", ex); }
    }

    // ─── POSTFIX patches ───

    public static void AllPlayersReadyPostfix(ref bool __result, CombatManager __instance)
    {
        try
        {
            if (__result || !PartyManager.HasKickedPlayers) return;

            var runState = Traverse.Create(RunManager.Instance).Property<RunState>("State").Value;
            var players = runState?.Players;
            if (players == null) return;

            var readyEndTurn = Traverse.Create(__instance)
                .Field("_playersReadyToEndTurn").GetValue<HashSet<Player>>();

            bool allAccountedFor = true;
            foreach (var player in players)
            {
                if (PartyManager.IsPlayerKicked(player)) continue;
                if (readyEndTurn == null || !readyEndTurn.Contains(player))
                {
                    allAccountedFor = false;
                    break;
                }
            }

            if (allAccountedFor)
            {
                __result = true;
                ModLog.Info("Kick patch: overriding AllPlayersReady to true");
            }
        }
        catch (Exception ex) { ModLog.Error("KickPatches.AllPlayersReadyPostfix", ex); }
    }

    public static void AutoReadyKickedPlayersForCombat(CombatManager __instance)
    {
        try
        {
            if (!PartyManager.HasKickedPlayers) return;

            var runState = Traverse.Create(RunManager.Instance).Property<RunState>("State").Value;
            var players = runState?.Players;
            if (players == null) return;

            var traverse = Traverse.Create(__instance);
            var readyEndTurn = traverse.Field("_playersReadyToEndTurn").GetValue<HashSet<Player>>();
            var readyBeginEnemy = traverse.Field("_playersReadyToBeginEnemyTurn").GetValue<HashSet<Player>>();

            foreach (var player in players)
            {
                if (!PartyManager.IsPlayerKicked(player)) continue;
                readyEndTurn?.Add(player);
                readyBeginEnemy?.Add(player);
            }
        }
        catch (Exception ex) { ModLog.Error("KickPatches.AutoReadyKickedPlayersForCombat", ex); }
    }

    /// <summary>
    /// Called periodically (every 250ms) during combat from RecalcPeriodicPatch.
    /// Ensures kicked players are always in the ready sets, even if the sets
    /// were cleared between turns.
    /// </summary>
    public static void PeriodicCombatAutoReady()
    {
        try
        {
            if (!PartyManager.HasKickedPlayers) return;
            var combat = CombatManager.Instance;
            if (combat == null) return;
            AutoReadyKickedPlayersForCombat(combat);
        }
        catch { /* silently ignore periodic errors */ }
    }

    public static void IsWaitingPostfix(ref bool __result, object __instance)
    {
        try
        {
            if (!__result || !PartyManager.HasKickedPlayers) return;

            var list = Traverse.Create(__instance).Field("_readyPlayers").GetValue<List<bool>>();
            if (list == null) return;

            var kickedIndices = PartyManager.GetKickedPlayerIndices();
            if (kickedIndices.Count == 0) return;

            bool onlyKickedWaiting = true;
            for (int i = 0; i < list.Count; i++)
            {
                if (!list[i] && !kickedIndices.Contains(i))
                {
                    onlyKickedWaiting = false;
                    break;
                }
            }

            if (onlyKickedWaiting)
            {
                foreach (int idx in kickedIndices)
                    if (idx < list.Count)
                        list[idx] = true;
                __result = false;
                ModLog.Info("Kick patch: overriding IsWaitingForOtherPlayers to false");
            }
        }
        catch (Exception ex) { ModLog.Error("KickPatches.IsWaitingPostfix", ex); }
    }

}
