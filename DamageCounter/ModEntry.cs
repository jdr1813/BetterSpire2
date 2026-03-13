using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using System;
using System.Runtime.InteropServices;

namespace BetterSpire2;

[ModInitializer("Init")]
public class ModEntry
{
    [DllImport("libdl.so.2")]
    private static extern IntPtr dlopen(string filename, int flags);

    [DllImport("libdl.so.2")]
    private static extern IntPtr dlerror();

    private static IntPtr _libgccHandle;
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

        // Linux: pre-load libgcc_s so Harmony's mm-exhelper.so can resolve _Unwind_RaiseException
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            ModLog.Info("Linux detected — loading libgcc_s for Harmony compatibility");
            _libgccHandle = dlopen("libgcc_s.so.1", 2 | 256); // RTLD_NOW | RTLD_GLOBAL
            if (_libgccHandle == IntPtr.Zero)
                ModLog.Info($"  dlopen failed: {Marshal.PtrToStringAnsi(dlerror())}");
            else
                ModLog.Info("  libgcc_s loaded successfully");
        }

        ModSettings.Load();
        ModLog.Info("Settings loaded");

#if LITE_BUILD
        var harmony = new Harmony("com.jdr.betterspire2lite");
#else
        var harmony = new Harmony("com.jdr.betterspire2");
#endif
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
#if FULL_BUILD
        KickPatches.Apply(harmony, ref succeeded, ref failed);
        ScalingPatches.Apply(harmony, ref succeeded, ref failed);
        AutoConfirmPatches.Apply(harmony, ref succeeded, ref failed);
#endif

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
