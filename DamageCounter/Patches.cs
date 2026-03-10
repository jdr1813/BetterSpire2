using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using System;
using System.Collections.Generic;

namespace BetterSpire2;

[HarmonyPatch(typeof(NIntent), "UpdateVisuals")]
public class IntentLabelPatch
{
    static void Postfix(
        AbstractIntent ____intent,
        Creature ____owner,
        IEnumerable<Creature> ____targets,
        MegaCrit.Sts2.addons.mega_text.MegaRichTextLabel ____valueLabel)
    {
        try
        {
            if (!ModSettings.MultiHitTotals) return;
            if (____intent is not AttackIntent attackIntent) return;
            if (____targets == null || ____owner == null || ____valueLabel == null) return;

            int singleDamage = attackIntent.GetSingleDamage(____targets, ____owner);
            int totalDamage = attackIntent.GetTotalDamage(____targets, ____owner);

            if (singleDamage > 0 && totalDamage > singleDamage)
            {
                string existing = ____valueLabel.Text?.Trim() ?? "";
                if (!existing.Contains("("))
                {
                    ____valueLabel.Text = existing + $" ({totalDamage})";
                }
            }
        }
        catch (Exception ex) { ModLog.Error("IntentLabelPatch", ex); }
    }
}

[HarmonyPatch(typeof(NCreature), nameof(NCreature.RefreshIntents))]
public class RecalcOnRefreshIntentsPatch
{
    static void Postfix() => DamageTracker.Recalculate();
}

[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.SetReadyToEndTurn))]
public class RecalcOnEndTurnPatch
{
    static void Postfix() => DamageTracker.Recalculate();
}

[HarmonyPatch(typeof(NIntent), "_Process")]
public class RecalcPeriodicPatch
{
    private static ulong _lastRecalcMs;

    static void Postfix()
    {
        ulong now = Time.GetTicksMsec();
        if (now - _lastRecalcMs < 250) return;
        _lastRecalcMs = now;
        DamageTracker.Recalculate();
        KickPatches.PeriodicCombatAutoReady();
    }
}

[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.Reset))]
public class HideOnResetPatch
{
    static void Postfix() => DamageTracker.Hide();
}

[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.EndCombatInternal))]
public class HideOnWinPatch
{
    static void Prefix() => DamageTracker.Hide();
}

[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.LoseCombat))]
public class HideOnLosePatch
{
    static void Prefix() => DamageTracker.Hide();
}

[HarmonyPatch(typeof(NGame), "_Input")]
public class InputPatch
{
    private static ulong _pressStartTime;
    private static bool _isPressed;
    private static bool _triggered;

    static void Postfix(InputEvent inputEvent)
    {
        try
        {
            if (inputEvent is InputEventKey f1Event
                && f1Event.Keycode == Key.F1
                && f1Event.Pressed
                && !f1Event.IsEcho())
            {
                SettingsMenu.Toggle();
                return;
            }

            if (inputEvent is InputEventKey f3Event
                && f3Event.Keycode == Key.F3
                && f3Event.Pressed
                && !f3Event.IsEcho())
            {
                DeckTracker.Toggle();
                return;
            }

            if (inputEvent is InputEventKey pgEvent && pgEvent.Pressed && !pgEvent.IsEcho())
            {
                if (pgEvent.Keycode == Key.Pagedown) { DeckTracker.NextPage(); return; }
                if (pgEvent.Keycode == Key.Pageup) { DeckTracker.PrevPage(); return; }
            }

            // Mouse events for panel drag/close
            if (inputEvent is InputEventMouseButton or InputEventMouseMotion)
            {
                SettingsMenu.HandleMouseInput(inputEvent);
                DeckTracker.HandleMouseInput(inputEvent);
            }

            if (!ModSettings.HoldRToRestart) return;

            if (inputEvent is InputEventKey keyEvent && keyEvent.Keycode == Key.R)
            {
                if (keyEvent.Pressed && !keyEvent.IsEcho())
                {
                    _pressStartTime = Time.GetTicksMsec();
                    _isPressed = true;
                    _triggered = false;
                }
                else if (!keyEvent.Pressed)
                {
                    _isPressed = false;
                    _triggered = false;
                }
            }

            if (_isPressed && !_triggered)
            {
                ulong elapsed = Time.GetTicksMsec() - _pressStartTime;
                if (elapsed >= 1500)
                {
                    _triggered = true;
                    RestartTracker.RestartRun();
                }
            }
        }
        catch (Exception ex) { ModLog.Error("InputPatch", ex); }
    }
}

[HarmonyPatch(typeof(NGame), "LaunchMainMenu")]
public class SkipSplashPatch
{
    static void Prefix(ref bool skipLogo)
    {
        if (ModSettings.SkipSplash)
            skipLogo = true;

        // Clear kicked players when returning to menu (new run = clean slate)
        PartyManager.ClearKicked();
        PartyManager.ClearMutes();
    }
}
