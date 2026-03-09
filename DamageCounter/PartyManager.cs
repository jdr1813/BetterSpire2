using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace BetterSpire2;

public static class PartyManager
{
    private static readonly HashSet<ulong> _mutedDrawings = new();
    private static readonly HashSet<ulong> _kickedPlayers = new();

    public static bool IsDrawingMuted(ulong netId) => _mutedDrawings.Contains(netId);
    public static bool IsKicked(ulong netId) => _kickedPlayers.Contains(netId);
    public static bool HasKickedPlayers => _kickedPlayers.Count > 0;

    /// <summary>
    /// Un-kick a player (e.g. when they reconnect after being reinvited).
    /// </summary>
    public static void UnkickPlayer(ulong netId)
    {
        if (_kickedPlayers.Remove(netId))
            ModLog.Info($"Un-kicked player {netId} (reconnected)");
    }

    /// <summary>
    /// Track a player who disconnected naturally (not kicked by us).
    /// They're treated the same as kicked for scaling and synchronizer purposes.
    /// </summary>
    public static void TrackDisconnectedPlayer(ulong netId)
    {
        if (_kickedPlayers.Add(netId))
            ModLog.Info($"Tracking disconnected player {netId}");
    }

    /// <summary>
    /// Check if a Player object is kicked by matching their NetId.
    /// </summary>
    public static bool IsPlayerKicked(Player player)
    {
        try { return _kickedPlayers.Contains(player.NetId); }
        catch { return false; }
    }

    /// <summary>
    /// Get the index (slot) of kicked players in RunState.Players.
    /// Used by synchronizers that track readiness by player index.
    /// </summary>
    public static List<int> GetKickedPlayerIndices()
    {
        var indices = new List<int>();
        try
        {
            var runState = Traverse.Create(RunManager.Instance).Property<RunState>("State").Value;
            var players = runState?.Players;
            if (players == null) return indices;
            for (int i = 0; i < players.Count; i++)
            {
                if (_kickedPlayers.Contains(players[i].NetId))
                    indices.Add(i);
            }
        }
        catch (Exception ex) { ModLog.Error("PartyManager.GetKickedPlayerIndices", ex); }
        return indices;
    }

    public static void ToggleDrawingMute(ulong netId)
    {
        if (!_mutedDrawings.Remove(netId))
            _mutedDrawings.Add(netId);
    }

    public static void KickPlayer(ulong netId)
    {
        try
        {
            var runManager = RunManager.Instance;
            if (runManager == null) return;

            var netService = runManager.NetService;
            if (netService == null || netService.Type != NetGameType.Host) return;

            // Track as kicked — patches will auto-resolve their actions
            _kickedPlayers.Add(netId);
            ModLog.Info($"Kicked player {netId}");

            // Disconnect at network level — this automatically triggers
            // RunLobby.OnDisconnectedFromClientAsHost → RemotePlayerDisconnected
            if (netService is INetHostGameService hostService)
                hostService.DisconnectClient(netId, NetError.Kicked, true);

            // Rescale current combat if we're in one
            ScalingPatches.RescaleCurrentCombat();
        }
        catch (Exception ex) { ModLog.Error("PartyManager.KickPlayer", ex); }
    }

    // ─── Drawing Management ───

    public static NMapDrawings? MapDrawings;

    private static readonly MethodInfo _getDrawingState =
        AccessTools.Method(typeof(NMapDrawings), "GetDrawingStateForPlayer");
    private static readonly MethodInfo _clearForPlayer =
        AccessTools.Method(typeof(NMapDrawings), "ClearAllLinesForPlayer");
    private static readonly MethodInfo _clearAll =
        AccessTools.Method(typeof(NMapDrawings), "ClearAllLines");

    public static void ClearDrawingsForPlayer(ulong netId)
    {
        try
        {
            if (MapDrawings == null || _getDrawingState == null || _clearForPlayer == null) return;
            var state = _getDrawingState.Invoke(MapDrawings, new object[] { netId });
            if (state != null)
                _clearForPlayer.Invoke(MapDrawings, new object[] { state });
        }
        catch (Exception ex) { ModLog.Error("PartyManager.ClearDrawingsForPlayer", ex); }
    }

    public static void ClearAllDrawings()
    {
        try
        {
            if (MapDrawings == null || _clearAll == null) return;
            _clearAll.Invoke(MapDrawings, null);
        }
        catch (Exception ex) { ModLog.Error("PartyManager.ClearAllDrawings", ex); }
    }

    public static void ClearMutes() => _mutedDrawings.Clear();
    public static void ClearKicked()
    {
        if (_kickedPlayers.Count > 0)
        {
            ModLog.Info($"PartyManager: clearing {_kickedPlayers.Count} kicked player(s) for new run");
            _kickedPlayers.Clear();
        }
    }
}

// ─── Block drawings from muted players ───

public class MuteDrawingsPatch
{
    public static bool Prefix(NMapDrawings __instance, ulong senderId)
    {
        PartyManager.MapDrawings = __instance;
        return !PartyManager.IsDrawingMuted(senderId);
    }
}

public class MuteClearDrawingsPatch
{
    public static bool Prefix(ulong senderId)
    {
        return !PartyManager.IsDrawingMuted(senderId);
    }
}
