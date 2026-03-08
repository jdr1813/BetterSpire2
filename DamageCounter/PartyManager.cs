using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace BetterSpire2;

public static class PartyManager
{
    private static readonly HashSet<ulong> _mutedDrawings = new();

    public static bool IsDrawingMuted(ulong netId) => _mutedDrawings.Contains(netId);

    public static void ToggleDrawingMute(ulong netId)
    {
        if (!_mutedDrawings.Remove(netId))
            _mutedDrawings.Add(netId);
    }

    public static void KickPlayer(ulong netId)
    {
        try
        {
            var netService = RunManager.Instance?.NetService;
            if (netService == null || netService.Type != NetGameType.Host) return;

            if (netService is INetHostGameService hostService)
                hostService.DisconnectClient(netId, NetError.Kicked, true);
        }
        catch (Exception ex) { ModLog.Error("PartyManager.KickPlayer", ex); }
    }

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
}

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
