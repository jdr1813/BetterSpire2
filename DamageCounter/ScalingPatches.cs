#if FULL_BUILD
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace BetterSpire2;

/// <summary>
/// Patches multiplayer scaling so that enemy HP, block, and power amounts
/// are scaled based on the number of active (non-kicked/disconnected) players
/// rather than the total player count in RunState.
///
/// When a player is kicked or disconnects mid-combat, existing enemies are
/// rescaled with their HP ratio preserved.
/// </summary>
public static class ScalingPatches
{
    private static readonly Assembly _gameAssembly = typeof(CombatManager).Assembly;

    public static void Apply(Harmony harmony, ref int succeeded, ref int failed)
    {
        // ─── Override playerCount in ScaleMonsterHpForMultiplayer for future combats ───
        var creatureType = typeof(Creature);
        var scaleMethod = AccessTools.Method(creatureType, "ScaleMonsterHpForMultiplayer");
        TryPatch(harmony, ref succeeded, ref failed,
            "Creature.ScaleMonsterHpForMultiplayer",
            scaleMethod,
            prefix: new HarmonyMethod(typeof(ScalingPatches), nameof(ScaleMonsterHpPrefix)));

        // ─── Override GetMultiplayerScaling to use active player count ───
        // This affects block and power scaling automatically since they call this internally.
        // We use a postfix to adjust the returned scaling factor proportionally.
        PatchByTypeName(harmony, ref succeeded, ref failed,
            "MultiplayerScalingModel", "GetMultiplayerScaling",
            postfix: new HarmonyMethod(typeof(ScalingPatches), nameof(GetMultiplayerScalingPostfix)));

        // ─── Hook peer disconnection to trigger mid-combat rescaling ───
        PatchByTypeName(harmony, ref succeeded, ref failed,
            "NetHostGameService", "OnPeerDisconnected",
            postfix: new HarmonyMethod(typeof(ScalingPatches), nameof(OnPeerDisconnectedPostfix)));
    }

    /// <summary>
    /// Returns the number of active (non-kicked) players in the current run.
    /// </summary>
    public static int GetActivePlayerCount()
    {
        try
        {
            var runState = Traverse.Create(RunManager.Instance).Property<RunState>("State").Value;
            if (runState?.Players == null) return 1;

            int active = 0;
            foreach (var player in runState.Players)
            {
                if (!PartyManager.IsPlayerKicked(player))
                    active++;
            }
            return Math.Max(1, active); // never return 0
        }
        catch { return 1; }
    }

    /// <summary>
    /// Prefix for Creature.ScaleMonsterHpForMultiplayer — replaces the playerCount
    /// parameter with the active (non-kicked) player count so new enemies spawn
    /// with correctly scaled HP.
    /// </summary>
    public static void ScaleMonsterHpPrefix(ref int playerCount)
    {
        if (!ModSettings.ScaleToActivePlayers) return;
        if (!PartyManager.HasKickedPlayers) return;

        int activeCount = GetActivePlayerCount();
        if (activeCount != playerCount)
        {
            ModLog.Info($"Scaling patch: overriding playerCount {playerCount} → {activeCount} for HP scaling");
            playerCount = activeCount;
        }
    }

    /// <summary>
    /// Postfix for MultiplayerScalingModel.GetMultiplayerScaling — adjusts the
    /// returned scaling factor proportionally for the active player count.
    ///
    /// The game's formula returns a multiplier based on total players. We scale
    /// it down proportionally: result * (activePlayers / totalPlayers).
    /// This affects block amounts and power amounts that enemies apply,
    /// since those methods call GetMultiplayerScaling internally.
    /// </summary>
    public static void GetMultiplayerScalingPostfix(ref decimal __result)
    {
        if (!ModSettings.ScaleToActivePlayers) return;
        if (!PartyManager.HasKickedPlayers) return;

        try
        {
            var runState = Traverse.Create(RunManager.Instance).Property<RunState>("State").Value;
            if (runState?.Players == null) return;

            int totalPlayers = runState.Players.Count;
            int activePlayers = GetActivePlayerCount();

            if (activePlayers >= totalPlayers || totalPlayers <= 1) return;

            // Scale the multiplier proportionally
            decimal adjusted = __result * activePlayers / totalPlayers;
            ModLog.Info($"Scaling patch: adjusted multiplayer scaling {__result} → {adjusted} " +
                        $"({activePlayers}/{totalPlayers} players)");
            __result = adjusted;
        }
        catch (Exception ex) { ModLog.Error("ScalingPatches.GetMultiplayerScalingPostfix", ex); }
    }

    /// <summary>
    /// Rescale all living enemies in the current combat to match the new
    /// active player count. Preserves each enemy's HP ratio.
    /// </summary>
    public static void RescaleCurrentCombat()
    {
        if (!ModSettings.ScaleToActivePlayers) return;
        RescaleCurrentCombatTo(GetActivePlayerCount());
    }

    /// <summary>
    /// Rescale all living enemies to match a specific target player count.
    /// Used both for scaling down (kick/disconnect) and restoring (toggle off).
    /// </summary>
    public static void RescaleCurrentCombatTo(int targetPlayerCount)
    {
        try
        {
            var combat = CombatManager.Instance;
            if (combat == null) return;

            var state = combat.DebugOnlyGetState();
            if (state == null) return;

            var enemies = state.Enemies;
            if (enemies == null || enemies.Count == 0) return;

            var encounter = state.Encounter;
            var runState = Traverse.Create(RunManager.Instance).Property<RunState>("State").Value;
            if (runState == null) return;

            int totalPlayers = runState.Players.Count;
            targetPlayerCount = Math.Clamp(targetPlayerCount, 1, totalPlayers);

            int actIndex = runState.CurrentActIndex;

            ModLog.Info($"Scaling patch: rescaling combat to {targetPlayerCount} players (of {totalPlayers} total)");

            foreach (var enemy in enemies)
            {
                if (enemy.IsDead) continue;

                try
                {
                    RescaleCreature(enemy, encounter, totalPlayers, targetPlayerCount, actIndex);
                }
                catch (Exception ex)
                {
                    ModLog.Error($"Scaling patch: failed to rescale enemy", ex);
                }
            }
        }
        catch (Exception ex) { ModLog.Error("ScalingPatches.RescaleCurrentCombatTo", ex); }
    }

    private static void RescaleCreature(Creature creature, object? encounter,
        int oldPlayerCount, int newPlayerCount, int actIndex)
    {
        int currentHp = creature.CurrentHp;
        int currentMaxHp = creature.MaxHp;
        if (currentMaxHp <= 0) return;

        // Get the original unscaled HP if available
        int? originalMaxHp = creature.MonsterMaxHpBeforeModification;

        // Calculate what the new max HP should be by rescaling from the original base
        int newMaxHp;
        if (originalMaxHp.HasValue && originalMaxHp.Value > 0)
        {
            // We have the original unscaled HP — use the game's own scaling method
            // to calculate what it should be for the new player count
            var scaleMethod = AccessTools.Method(typeof(Creature), "ScaleHpForMultiplayer");
            if (scaleMethod != null && encounter != null)
            {
                try
                {
                    var scaledHp = scaleMethod.Invoke(creature,
                        new object[] { (decimal)originalMaxHp.Value, encounter, newPlayerCount, actIndex });
                    newMaxHp = (int)Math.Round((decimal)scaledHp!);
                }
                catch
                {
                    // Fallback: proportional scaling
                    newMaxHp = ScaleProportionally(originalMaxHp.Value, currentMaxHp, oldPlayerCount, newPlayerCount);
                }
            }
            else
            {
                newMaxHp = ScaleProportionally(originalMaxHp.Value, currentMaxHp, oldPlayerCount, newPlayerCount);
            }
        }
        else
        {
            // No original HP stored — scale proportionally from current max
            // Ratio: newMax = currentMax * (newPlayerCount / oldPlayerCount)
            // This is approximate but reasonable
            newMaxHp = (int)Math.Round((double)currentMaxHp * newPlayerCount / oldPlayerCount);
        }

        newMaxHp = Math.Max(1, newMaxHp);

        // Preserve HP ratio
        double hpRatio = (double)currentHp / currentMaxHp;
        int newCurrentHp = Math.Max(1, (int)Math.Round(hpRatio * newMaxHp));

        ModLog.Info($"Scaling patch: rescaled enemy — MaxHP {currentMaxHp} → {newMaxHp}, " +
                    $"CurrentHP {currentHp} → {newCurrentHp} (ratio {hpRatio:P0})");

        // Apply the new values via internal setters (properties are read-only)
        var setMaxHp = AccessTools.Method(typeof(Creature), "SetMaxHpInternal");
        var setCurrentHp = AccessTools.Method(typeof(Creature), "SetCurrentHpInternal");

        if (setMaxHp != null && setCurrentHp != null)
        {
            setMaxHp.Invoke(creature, new object[] { (decimal)newMaxHp });
            setCurrentHp.Invoke(creature, new object[] { (decimal)newCurrentHp });
        }
        else
        {
            // Fallback: use Traverse to set backing fields directly
            var traverse = Traverse.Create(creature);
            traverse.Field("_maxHp").SetValue(newMaxHp);
            traverse.Field("_currentHp").SetValue(newCurrentHp);
        }
    }

    /// <summary>
    /// Fallback proportional scaling when we can't use the game's formula.
    /// </summary>
    private static int ScaleProportionally(int originalHp, int currentMaxHp, int oldPlayerCount, int newPlayerCount)
    {
        // Simple ratio: scale currentMax by the player count ratio
        return (int)Math.Round((double)currentMaxHp * newPlayerCount / oldPlayerCount);
    }

    /// <summary>
    /// Called when the host toggles the scaling setting mid-run.
    /// If turned on: rescale to active player count.
    /// If turned off: restore to full party scaling.
    /// </summary>
    public static void OnSettingToggled(bool enabled)
    {
        if (!PartyManager.HasKickedPlayers) return;

        try
        {
            var runState = Traverse.Create(RunManager.Instance).Property<RunState>("State").Value;
            if (runState == null) return;

            int totalPlayers = runState.Players.Count;
            int targetCount = enabled ? GetActivePlayerCount() : totalPlayers;

            RescaleCurrentCombatTo(targetCount);
        }
        catch (Exception ex) { ModLog.Error("ScalingPatches.OnSettingToggled", ex); }
    }

    /// <summary>
    /// When a peer disconnects (kicked or lost connection), add them to the kicked
    /// list (if not already there) and rescale current combat if applicable.
    /// </summary>
    public static void OnPeerDisconnectedPostfix(ulong peerId)
    {
        try
        {
            // If they disconnected naturally (not kicked by us), track them too
            if (!PartyManager.IsKicked(peerId))
            {
                PartyManager.TrackDisconnectedPlayer(peerId);
                ModLog.Info($"Scaling patch: player {peerId} disconnected, tracking for scaling");
            }

            // Rescale current combat if we're in one
            RescaleCurrentCombat();
        }
        catch (Exception ex) { ModLog.Error("ScalingPatches.OnPeerDisconnectedPostfix", ex); }
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
                ModLog.Info($"  Skipped scaling patch: {name} — method not found");
                failed++;
                return;
            }
            harmony.Patch(target, prefix: prefix, postfix: postfix);
            ModLog.Info($"  Patched: {name} (scaling)");
            succeeded++;
        }
        catch (Exception ex)
        {
            ModLog.Error($"Scaling patch {name}", ex);
            failed++;
        }
    }

    private static void PatchByTypeName(Harmony harmony, ref int succeeded, ref int failed,
        string typeName, string methodName, HarmonyMethod? prefix = null, HarmonyMethod? postfix = null)
    {
        var type = FindType(typeName);
        if (type == null)
        {
            ModLog.Info($"  Skipped scaling patch: {typeName}.{methodName} — type not found");
            failed++;
            return;
        }
        var method = AccessTools.Method(type, methodName);
        TryPatch(harmony, ref succeeded, ref failed, $"{typeName}.{methodName}", method, prefix, postfix);
    }
}
#endif
