using Godot;
using System;
using System.Collections.Generic;

namespace BetterSpire2;

public static class ModSettings
{
    private static string SettingsPath =>
        System.IO.Path.Combine(OS.GetUserDataDir(), "betterspire2_settings.json");

    public static bool MultiHitTotals = true;
    public static bool PlayerDamageTotal = true;
    public static bool ShowExpectedHp = true;
    public static bool SkipSplash = true;
    public static bool ShowTeammateHand = true;
#if FULL_BUILD
    public static bool HoldRToRestart = true;
    public static bool ScaleToActivePlayers = true;
    public static bool AutoConfirmSingleCard = true;
#endif

    public static void Load()
    {
        try
        {
            if (!System.IO.File.Exists(SettingsPath)) return;
            var json = System.IO.File.ReadAllText(SettingsPath);
            var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, bool>>(json);
            if (dict == null) return;
            if (dict.TryGetValue("MultiHitTotals", out var val)) MultiHitTotals = val;
            if (dict.TryGetValue("PlayerDamageTotal", out val)) PlayerDamageTotal = val;
            if (dict.TryGetValue("ShowExpectedHp", out val)) ShowExpectedHp = val;
            if (dict.TryGetValue("SkipSplash", out val)) SkipSplash = val;
            if (dict.TryGetValue("ShowTeammateHand", out val)) ShowTeammateHand = val;
#if FULL_BUILD
            if (dict.TryGetValue("HoldRToRestart", out val)) HoldRToRestart = val;
            if (dict.TryGetValue("ScaleToActivePlayers", out val)) ScaleToActivePlayers = val;
            if (dict.TryGetValue("AutoConfirmSingleCard", out val)) AutoConfirmSingleCard = val;
#endif
        }
        catch (Exception ex) { ModLog.Error("ModSettings.Load", ex); }
    }

    public static void Save()
    {
        try
        {
            var dict = new Dictionary<string, bool>
            {
                ["MultiHitTotals"] = MultiHitTotals,
                ["PlayerDamageTotal"] = PlayerDamageTotal,
                ["ShowExpectedHp"] = ShowExpectedHp,
                ["SkipSplash"] = SkipSplash,
                ["ShowTeammateHand"] = ShowTeammateHand,
#if FULL_BUILD
                ["HoldRToRestart"] = HoldRToRestart,
                ["ScaleToActivePlayers"] = ScaleToActivePlayers,
                ["AutoConfirmSingleCard"] = AutoConfirmSingleCard,
#endif
            };
            var json = System.Text.Json.JsonSerializer.Serialize(dict);
            System.IO.File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex) { ModLog.Error("ModSettings.Save", ex); }
    }
}
