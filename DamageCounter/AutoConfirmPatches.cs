#if FULL_BUILD
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Models;
using System;
using System.Collections.Generic;

namespace BetterSpire2;

public static class AutoConfirmPatches
{
    public static void Apply(Harmony harmony, ref int succeeded, ref int failed)
    {
        // Patch NPlayerHand.SelectCardInSimpleMode — this is what runs when a card is clicked
        // during discard/retain selection. It does NOT call CheckIfSelectionComplete, so we
        // add a postfix that auto-confirms when the exact required count is selected.
        try
        {
            var method = AccessTools.Method(typeof(NPlayerHand), "SelectCardInSimpleMode");
            if (method != null)
            {
                var postfix = new HarmonyMethod(typeof(AutoConfirmPatches), nameof(HandSelectPostfix));
                harmony.Patch(method, postfix: postfix);
                ModLog.Info("  Patched: NPlayerHand.SelectCardInSimpleMode (auto-confirm)");
                succeeded++;
            }
            else
            {
                ModLog.Info("  Skipped: NPlayerHand.SelectCardInSimpleMode — method not found");
                failed++;
            }
        }
        catch (Exception ex)
        {
            ModLog.Error("Patch NPlayerHand.SelectCardInSimpleMode", ex);
            failed++;
        }

        // Patch NSimpleCardSelectScreen.CheckIfSelectionComplete (grid card selection)
        try
        {
            var screenMethod = AccessTools.Method(typeof(NSimpleCardSelectScreen), "CheckIfSelectionComplete");
            if (screenMethod != null)
            {
                var postfix = new HarmonyMethod(typeof(AutoConfirmPatches), nameof(ScreenCheckPostfix));
                harmony.Patch(screenMethod, postfix: postfix);
                ModLog.Info("  Patched: NSimpleCardSelectScreen.CheckIfSelectionComplete (auto-confirm)");
                succeeded++;
            }
            else
            {
                ModLog.Info("  Skipped: NSimpleCardSelectScreen.CheckIfSelectionComplete — method not found");
                failed++;
            }
        }
        catch (Exception ex)
        {
            ModLog.Error("Patch NSimpleCardSelectScreen.CheckIfSelectionComplete", ex);
            failed++;
        }
    }

    private static void HandSelectPostfix(NPlayerHand __instance)
    {
        try
        {
            if (!ModSettings.AutoConfirmSingleCard) return;

            var prefsField = Traverse.Create(__instance).Field<CardSelectorPrefs>("_prefs");
            if (prefsField == null) return;
            var prefs = prefsField.Value;

            var selectedCards = Traverse.Create(__instance).Field<List<CardModel>>("_selectedCards").Value;
            int selectedCount = selectedCards?.Count ?? 0;

            ModLog.Info($"AutoConfirm Hand: min={prefs.MinSelect} max={prefs.MaxSelect} selected={selectedCount}");

            // Auto-confirm when the player has selected the maximum allowed
            if (prefs.MaxSelect < 1) return;
            if (selectedCards == null || selectedCount != prefs.MaxSelect) return;

            // Press the confirm button to complete the selection
            var confirmMethod = AccessTools.Method(__instance.GetType(), "OnSelectModeConfirmButtonPressed");
            if (confirmMethod != null)
            {
                ModLog.Info($"AutoConfirm: confirming hand selection ({selectedCount} card(s))");
                confirmMethod.Invoke(__instance, new object[] { null! });
            }
        }
        catch (Exception ex) { ModLog.Error("HandSelectPostfix", ex); }
    }

    private static void ScreenCheckPostfix(NSimpleCardSelectScreen __instance)
    {
        try
        {
            if (!ModSettings.AutoConfirmSingleCard) return;

            var prefsField = Traverse.Create(__instance).Field<CardSelectorPrefs>("_prefs");
            if (prefsField == null) return;
            var prefs = prefsField.Value;

            var selectedCards = Traverse.Create(__instance).Field<HashSet<CardModel>>("_selectedCards").Value;
            int selectedCount = selectedCards?.Count ?? 0;

            ModLog.Info($"AutoConfirm Screen: min={prefs.MinSelect} max={prefs.MaxSelect} selected={selectedCount}");

            if (prefs.MaxSelect < 1) return;
            if (selectedCards == null || selectedCount != prefs.MaxSelect) return;

            var completeMethod = AccessTools.Method(__instance.GetType(), "CompleteSelection");
            if (completeMethod != null)
            {
                ModLog.Info($"AutoConfirm: confirming screen selection ({selectedCount} card(s))");
                completeMethod.Invoke(__instance, null);
            }
        }
        catch (Exception ex) { ModLog.Error("ScreenCheckPostfix", ex); }
    }
}
#endif
