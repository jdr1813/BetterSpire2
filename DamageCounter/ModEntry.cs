using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using System;

namespace BetterSpire2;

[ModInitializer("Init")]
public class ModEntry
{
    private static bool _initialized;

    private static readonly Type[] _patchClasses =
    {
        typeof(IntentLabelPatch),
        typeof(RecalcOnRefreshIntentsPatch),
        typeof(RecalcOnEndTurnPatch),
        typeof(RecalcPeriodicPatch),
        typeof(HideOnResetPatch),
        typeof(HideOnWinPatch),
        typeof(HideOnLosePatch),
        typeof(InputPatch),
        typeof(SkipSplashPatch),
    };

    public static void Init()
    {
        if (_initialized) return;
        _initialized = true;

        ModLog.Init();
        ModLog.Info("ModEntry.Init() starting");

        ModSettings.Load();
        ModLog.Info("Settings loaded");

        var harmony = new Harmony("com.jdr.betterspire2");
        int succeeded = 0;
        int failed = 0;

        foreach (var patchClass in _patchClasses)
        {
            try
            {
                harmony.CreateClassProcessor(patchClass).Patch();
                ModLog.Info($"  Patched: {patchClass.Name}");
                succeeded++;
            }
            catch (Exception ex)
            {
                ModLog.Error($"Patch {patchClass.Name}", ex);
                failed++;
            }
        }

        PatchDrawingMethods(harmony, ref succeeded, ref failed);
        KickPatches.Apply(harmony, ref succeeded, ref failed);
        ScalingPatches.Apply(harmony, ref succeeded, ref failed);

        ModLog.Info($"Harmony patching complete: {succeeded} succeeded, {failed} failed");
        ModLog.Info("ModEntry.Init() complete");
    }

    private static void PatchDrawingMethods(Harmony harmony, ref int succeeded, ref int failed)
    {
        try
        {
            var drawingMethod = AccessTools.Method(typeof(NMapDrawings), "HandleDrawingMessage");
            if (drawingMethod != null)
            {
                var prefix = new HarmonyMethod(typeof(MuteDrawingsPatch), "Prefix");
                harmony.Patch(drawingMethod, prefix: prefix);
                ModLog.Info("  Patched: MuteDrawingsPatch (manual)");
                succeeded++;
            }
            else
            {
                ModLog.Info("  Skipped: MuteDrawingsPatch — method not found");
                failed++;
            }
        }
        catch (Exception ex)
        {
            ModLog.Error("Patch MuteDrawingsPatch (manual)", ex);
            failed++;
        }

        try
        {
            var clearMethod = AccessTools.Method(typeof(NMapDrawings), "HandleClearMapDrawingsMessage");
            if (clearMethod != null)
            {
                var prefix = new HarmonyMethod(typeof(MuteClearDrawingsPatch), "Prefix");
                harmony.Patch(clearMethod, prefix: prefix);
                ModLog.Info("  Patched: MuteClearDrawingsPatch (manual)");
                succeeded++;
            }
            else
            {
                ModLog.Info("  Skipped: MuteClearDrawingsPatch — method not found");
                failed++;
            }
        }
        catch (Exception ex)
        {
            ModLog.Error("Patch MuteClearDrawingsPatch (manual)", ex);
            failed++;
        }
    }
}
