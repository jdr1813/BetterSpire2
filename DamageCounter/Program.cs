using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace BetterSpire2;

// ─── Logger ───
public static class ModLog
{
    private static string? _logPath;
    private static readonly object _lock = new();

    private static string LogPath =>
        _logPath ??= System.IO.Path.Combine(OS.GetUserDataDir(), "betterspire2_log.txt");

    public static void Init()
    {
        try
        {
            System.IO.File.WriteAllText(LogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] BetterSpire2 log started\n" +
                $"  OS: {OS.GetName()} / {OS.GetDistributionName()}\n" +
                $"  Godot: {Engine.GetVersionInfo()["string"]}\n");
        }
        catch { }
    }

    public static void Info(string message)
    {
        try
        {
            lock (_lock)
                System.IO.File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss}] {message}\n");
        }
        catch { }
    }

    public static void Error(string context, Exception ex)
    {
        try
        {
            lock (_lock)
                System.IO.File.AppendAllText(LogPath,
                    $"[{DateTime.Now:HH:mm:ss}] ERROR in {context}: {ex}\n");
        }
        catch { }
    }
}

// ─── Settings ───
public static class ModSettings
{
    private static string SettingsPath =>
        System.IO.Path.Combine(OS.GetUserDataDir(), "betterspire2_settings.json");

    public static bool MultiHitTotals = true;
    public static bool PlayerDamageTotal = true;
    public static bool ShowExpectedHp = true;
    public static bool HoldRToRestart = true;
    public static bool SkipSplash = true;

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
            if (dict.TryGetValue("HoldRToRestart", out val)) HoldRToRestart = val;
            if (dict.TryGetValue("SkipSplash", out val)) SkipSplash = val;
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
                ["HoldRToRestart"] = HoldRToRestart,
                ["SkipSplash"] = SkipSplash
            };
            var json = System.Text.Json.JsonSerializer.Serialize(dict);
            System.IO.File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex) { ModLog.Error("ModSettings.Save", ex); }
    }
}

// ─── F1 Settings Menu ───
public static class SettingsMenu
{
    private static PanelContainer? _panel;
    private static bool _visible;

    public static void Toggle()
    {
        if (_visible)
            Hide();
        else
            Show();
    }

    private static void Show()
    {
        if (_panel != null && GodotObject.IsInstanceValid(_panel))
        {
            _panel.Visible = true;
            _visible = true;
            return;
        }

        var game = NGame.Instance;
        if (game == null) return;

        // Main panel
        _panel = new PanelContainer();
        _panel.ZIndex = 200;

        // Style the background
        var styleBox = new StyleBoxFlat();
        styleBox.BgColor = new Color(0.1f, 0.1f, 0.15f, 0.95f);
        styleBox.BorderColor = new Color(0.8f, 0.6f, 0.2f);
        styleBox.SetBorderWidthAll(2);
        styleBox.SetCornerRadiusAll(8);
        styleBox.SetContentMarginAll(16);
        _panel.AddThemeStyleboxOverride("panel", styleBox);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 10);
        _panel.AddChild(vbox);

        // Title
        var title = new Label();
        title.Text = "BetterSpire2 Settings";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeColorOverride("font_color", new Color(0.9f, 0.7f, 0.2f));
        title.AddThemeFontSizeOverride("font_size", 20);
        vbox.AddChild(title);

        // Separator
        vbox.AddChild(new HSeparator());

        // Toggles
        AddToggle(vbox, "Multi-Hit Totals (per enemy)", ModSettings.MultiHitTotals, v => {
            ModSettings.MultiHitTotals = v;
            ModSettings.Save();
            RefreshIntents();
        });
        AddToggle(vbox, "Total Incoming Damage (above player)", ModSettings.PlayerDamageTotal, v => {
            ModSettings.PlayerDamageTotal = v;
            ModSettings.Save();
            if (v) DamageTracker.Recalculate();
            else DamageTracker.Hide();
        });
        AddToggle(vbox, "Show Expected HP After Damage", ModSettings.ShowExpectedHp, v => {
            ModSettings.ShowExpectedHp = v;
            ModSettings.Save();
            DamageTracker.Recalculate();
        });
        AddToggle(vbox, "Hold R to Restart Run", ModSettings.HoldRToRestart, v => {
            ModSettings.HoldRToRestart = v;
            ModSettings.Save();
        });
        AddToggle(vbox, "Skip Splash Screen", ModSettings.SkipSplash, v => {
            ModSettings.SkipSplash = v;
            ModSettings.Save();
        });

        // Party section (multiplayer only)
        BuildPartySection(vbox);

        // Hint
        var hint = new Label();
        hint.Text = "Press F1 to close";
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
        hint.AddThemeFontSizeOverride("font_size", 14);
        vbox.AddChild(hint);

        // Add to scene and center it
        var canvasLayer = new CanvasLayer();
        canvasLayer.Layer = 100;
        game.AddChild(canvasLayer);

        var centerContainer = new CenterContainer();
        centerContainer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        canvasLayer.AddChild(centerContainer);
        centerContainer.AddChild(_panel);

        _visible = true;
    }

    private static void AddToggle(VBoxContainer parent, string label, bool initialValue, Action<bool> onToggle)
    {
        var check = new CheckButton();
        check.Text = label;
        check.ButtonPressed = initialValue;
        check.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
        check.AddThemeFontSizeOverride("font_size", 16);
        check.Toggled += pressed => onToggle(pressed);
        parent.AddChild(check);
    }

    public static void Hide()
    {
        if (_panel != null && GodotObject.IsInstanceValid(_panel))
        {
            var centerContainer = _panel.GetParent();
            var canvasLayer = centerContainer?.GetParent();
            if (canvasLayer != null && GodotObject.IsInstanceValid(canvasLayer))
                canvasLayer.QueueFree();
            else
                _panel.QueueFree();
        }
        _panel = null;
        _visible = false;
    }

    private static void RefreshIntents()
    {
        try
        {
            var combatRoom = NCombatRoom.Instance;
            if (combatRoom == null) return;
            foreach (var child in combatRoom.GetChildren())
            {
                if (child is NCreature creature)
                    creature.RefreshIntents();
            }
        }
        catch (Exception ex) { ModLog.Error("SettingsMenu.RefreshIntents", ex); }
    }

    private static void BuildPartySection(VBoxContainer vbox)
    {
        try
        {
            var runManager = RunManager.Instance;
            if (runManager == null || runManager.IsSinglePlayerOrFakeMultiplayer) return;

            var netService = runManager.NetService;
            if (netService == null) return;

            var state = Traverse.Create(runManager).Property<RunState>("State").Value;
            if (state == null) return;

            var players = state.Players;
            if (players == null || players.Count <= 1) return;

            bool isHost = netService.Type == NetGameType.Host;
            ulong localNetId = netService.NetId;

            vbox.AddChild(new HSeparator());

            var partyTitle = new Label();
            partyTitle.Text = "Party";
            partyTitle.HorizontalAlignment = HorizontalAlignment.Center;
            partyTitle.AddThemeColorOverride("font_color", new Color(0.9f, 0.7f, 0.2f));
            partyTitle.AddThemeFontSizeOverride("font_size", 18);
            vbox.AddChild(partyTitle);

            foreach (var player in players)
            {
                bool isSelf = player.NetId == localNetId;
                string steamName = PlatformUtil.GetPlayerName(PlatformType.Steam, player.NetId);
                string charClass = player.Character?.Title?.GetFormattedText();
                string charName = !string.IsNullOrEmpty(steamName)
                    ? (!string.IsNullOrEmpty(charClass) ? $"{steamName} ({charClass})" : steamName)
                    : (charClass ?? $"Player {player.NetId}");

                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", 8);

                var nameLabel = new Label();
                nameLabel.Text = isSelf ? $"{charName} (You)" : charName;
                nameLabel.AddThemeColorOverride("font_color", isSelf
                    ? new Color(0.5f, 0.8f, 0.5f)
                    : new Color(0.9f, 0.9f, 0.9f));
                nameLabel.AddThemeFontSizeOverride("font_size", 16);
                nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                row.AddChild(nameLabel);

                if (!isSelf)
                {
                    // Mute drawings button (anyone can mute)
                    var muteBtn = new Button();
                    bool isMuted = PartyManager.IsDrawingMuted(player.NetId);
                    muteBtn.Text = isMuted ? "Show Drawings" : "Hide Drawings";
                    muteBtn.AddThemeFontSizeOverride("font_size", 14);
                    muteBtn.CustomMinimumSize = new Vector2(130, 0);
                    var capturedNetId = player.NetId;
                    var capturedBtn = muteBtn;
                    muteBtn.Pressed += () =>
                    {
                        PartyManager.ToggleDrawingMute(capturedNetId);
                        bool nowMuted = PartyManager.IsDrawingMuted(capturedNetId);
                        capturedBtn.Text = nowMuted ? "Show Drawings" : "Hide Drawings";
                        if (nowMuted)
                            PartyManager.ClearDrawingsForPlayer(capturedNetId);
                    };
                    row.AddChild(muteBtn);

                    // Kick button (host only)
                    if (isHost)
                    {
                        var kickBtn = new Button();
                        kickBtn.Text = "Kick";
                        kickBtn.AddThemeFontSizeOverride("font_size", 14);
                        kickBtn.AddThemeColorOverride("font_color", new Color(1f, 0.4f, 0.4f));
                        kickBtn.CustomMinimumSize = new Vector2(60, 0);
                        kickBtn.Pressed += () =>
                        {
                            PartyManager.KickPlayer(capturedNetId);
                            Hide();
                        };
                        row.AddChild(kickBtn);
                    }
                }

                vbox.AddChild(row);
            }

            var clearAllBtn = new Button();
            clearAllBtn.Text = "Clear All Drawings";
            clearAllBtn.AddThemeFontSizeOverride("font_size", 14);
            clearAllBtn.AddThemeColorOverride("font_color", new Color(1f, 0.6f, 0.2f));
            clearAllBtn.Pressed += () => PartyManager.ClearAllDrawings();
            vbox.AddChild(clearAllBtn);
        }
        catch (Exception ex) { ModLog.Error("BuildPartySection", ex); }
    }
}

// ─── Party Management (mute drawings, kick) ───
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

// ─── Block drawings from muted players (patched manually for cross-platform compat) ───
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

// ─── Mod Entry Point ───
[ModInitializer("Init")]
public class ModEntry
{
    private static bool _initialized;

    private static readonly Type[] _patchClasses = new[]
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

        // Drawing patches applied manually for cross-platform compatibility
        PatchDrawingMethods(harmony, ref succeeded, ref failed);

        ModLog.Info($"Harmony patching complete: {succeeded} succeeded, {failed} failed");
        ModLog.Info("ModEntry.Init() complete");
    }

    private static void PatchDrawingMethods(Harmony harmony, ref int succeeded, ref int failed)
    {
        // Try to patch HandleDrawingMessage — find the method by name since
        // the signature may vary across platforms
        try
        {
            var drawingMethod = AccessTools.Method(typeof(NMapDrawings), "HandleDrawingMessage");
            if (drawingMethod != null)
            {
                var prefix = new HarmonyMethod(typeof(MuteDrawingsPatch), "Prefix");
                harmony.Patch(drawingMethod, prefix: prefix);
                ModLog.Info($"  Patched: MuteDrawingsPatch (manual)");
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
                ModLog.Info($"  Patched: MuteClearDrawingsPatch (manual)");
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

// ─── Per-enemy: append (total) to multi-hit intent labels ───
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

// ─── Player total: show damage over player and pet ───
public static class DamageTracker
{
    private static readonly Dictionary<Creature, Label> _labels = new();
    private static Font? _cachedFont;

    public static void Recalculate()
    {
        try
        {
            if (!ModSettings.PlayerDamageTotal)
            {
                Hide();
                return;
            }

            // Hide labels when overlay screens are open (deck view, discard pile, potions, etc.)
            var capstone = NCapstoneContainer.Instance;
            if (capstone != null && capstone.InUse)
            {
                Hide();
                return;
            }

            var overlays = NOverlayStack.Instance;
            if (overlays != null && overlays.ScreenCount > 0)
            {
                Hide();
                return;
            }

            var state = CombatManager.Instance.DebugOnlyGetState();
            if (state == null)
            {
                Hide();
                return;
            }

            var playerCreatures = state.PlayerCreatures;

            // Gather all enemy attack hits (same for every player in multiplayer)
            var enemyHits = new List<int>();
            foreach (var enemy in state.Enemies)
            {
                if (enemy.IsDead || enemy.Monster == null) continue;

                foreach (var intent in enemy.Monster.NextMove.Intents)
                {
                    if (intent is AttackIntent attackIntent)
                    {
                        int singleHit = attackIntent.GetSingleDamage(
                            (IEnumerable<Creature>)playerCreatures, enemy);
                        int totalDmg = attackIntent.GetTotalDamage(
                            (IEnumerable<Creature>)playerCreatures, enemy);
                        int numHits = singleHit > 0 ? totalDmg / singleHit : 0;
                        for (int i = 0; i < numHits; i++)
                            enemyHits.Add(singleHit);
                    }
                }
            }

            // Track which creature IDs we actively show labels for (to clean up stale ones)
            var activeCreatures = new HashSet<Creature>();

            // Process each player creature independently
            foreach (var player in playerCreatures)
            {
                if (player.IsDead || player.IsPet || !player.IsPlayer) continue;

                // Find this player's pet
                Creature? pet = null;
                var pets = player.Pets;
                if (pets != null && pets.Count > 0)
                    pet = pets.FirstOrDefault(p => !p.IsDead);

                // Gather per-creature end-of-turn damage
                var endOfTurnHits = new List<(Creature target, int damage)>();
                var endOfTurnUnblockableHits = new List<(Creature target, int damage)>();

                // Collect debuffs for this player and their pet
                foreach (var c in new[] { player }.Concat(pet != null ? new[] { pet } : Array.Empty<Creature>()))
                {
                    if (c.IsDead) continue;

                    var constrict = c.GetPower<ConstrictPower>();
                    if (constrict != null && constrict.Amount > 0)
                        endOfTurnHits.Add((c, constrict.Amount));

                    var demise = c.GetPower<DemisePower>();
                    if (demise != null && demise.Amount > 0)
                        endOfTurnUnblockableHits.Add((c, demise.Amount));

                    foreach (var bomb in c.GetPowerInstances<MagicBombPower>())
                    {
                        if (bomb.Amount > 0 && bomb.Applier != null && !bomb.Applier.IsDead)
                            endOfTurnHits.Add((c, bomb.Amount));
                    }

                    var disintegration = c.GetPower<DisintegrationPower>();
                    if (disintegration != null && disintegration.Amount > 0)
                        endOfTurnHits.Add((c, disintegration.Amount));
                }

                // End-of-turn hand card damage for this player
                if (player.Player?.PlayerCombatState != null)
                {
                    foreach (var card in player.Player.PlayerCombatState.Hand.Cards)
                    {
                        if (!card.HasTurnEndInHandEffect) continue;
                        if (card.DynamicVars.TryGetValue("Damage", out var damageVar))
                        {
                            int dmg = (int)damageVar.BaseValue;
                            if (dmg > 0)
                                endOfTurnHits.Add((player, dmg));
                        }
                        else if (card.DynamicVars.TryGetValue("HpLoss", out var hpLossVar))
                        {
                            int dmg = (int)hpLossVar.BaseValue;
                            if (dmg > 0)
                                endOfTurnUnblockableHits.Add((player, dmg));
                        }
                    }
                }

                int totalDamage = endOfTurnHits.Sum(d => d.damage)
                    + endOfTurnUnblockableHits.Sum(d => d.damage)
                    + enemyHits.Sum();

                if (totalDamage <= 0)
                    continue;

                // Simulate damage for this player
                int simBlock = player.Block;
                bool petAbsorbs = pet != null && !pet.IsDead && pet.MaxHp > 0
                    && (pet.Monster == null || pet.Monster.IsHealthBarVisible);
                int simPetHp = petAbsorbs ? pet!.CurrentHp : 0;
                int simPetBlock = petAbsorbs ? pet!.Block : 0;
                int petAbsorbed = 0;
                int playerTakes = 0;

                // Frost orb passive block
                if (player.Player?.PlayerCombatState?.OrbQueue != null)
                {
                    foreach (var orb in player.Player.PlayerCombatState.OrbQueue.Orbs)
                    {
                        if (orb is FrostOrb frostOrb)
                            simBlock += (int)frostOrb.PassiveVal;
                    }
                }

                // Blockable end-of-turn damage
                foreach (var (target, damage) in endOfTurnHits)
                {
                    if (target.IsPet)
                    {
                        int blocked = Math.Min(simPetBlock, damage);
                        simPetBlock -= blocked;
                        int remaining = damage - blocked;
                        simPetHp -= remaining;
                        petAbsorbed += remaining;
                    }
                    else
                    {
                        int blocked = Math.Min(simBlock, damage);
                        simBlock -= blocked;
                        playerTakes += damage - blocked;
                    }
                }

                // Unblockable end-of-turn damage
                foreach (var (target, damage) in endOfTurnUnblockableHits)
                {
                    if (target.IsPet)
                    {
                        simPetHp -= damage;
                        petAbsorbed += damage;
                    }
                    else
                    {
                        playerTakes += damage;
                    }
                }

                // Enemy attack damage: block -> pet HP -> player HP
                foreach (int hit in enemyHits)
                {
                    int remaining = hit;

                    int blocked = Math.Min(simBlock, remaining);
                    simBlock -= blocked;
                    remaining -= blocked;

                    if (simPetHp > 0 && remaining > 0)
                    {
                        int absorbed = Math.Min(simPetHp, remaining);
                        simPetHp -= absorbed;
                        petAbsorbed += absorbed;
                        remaining -= absorbed;
                    }

                    playerTakes += remaining;
                }

                // Show player label
                activeCreatures.Add(player);
                if (playerTakes > 0)
                {
                    string text = ModSettings.ShowExpectedHp
                        ? $"{playerTakes} ({player.CurrentHp - playerTakes})"
                        : playerTakes.ToString();
                    ShowLabel(player, text, new Color(1f, 0.3f, 0.3f));
                }
                else
                {
                    ShowLabel(player, "0", new Color(0.3f, 1f, 0.3f));
                }

                // Show pet label
                if (petAbsorbs && petAbsorbed > 0)
                {
                    activeCreatures.Add(pet!);
                    ShowLabel(pet!, petAbsorbed.ToString(), new Color(1f, 0.6f, 0.2f));
                }
            }

            // Clean up labels for creatures no longer active
            var stale = _labels.Keys.Where(c => !activeCreatures.Contains(c)).ToList();
            foreach (var c in stale)
                RemoveLabel(c);
        }
        catch (Exception ex) { ModLog.Error("DamageTracker.Recalculate", ex); }
    }

    private static void ShowLabel(Creature creature, string text, Color color)
    {
        var combatRoom = NCombatRoom.Instance;
        if (combatRoom == null) return;

        if (!_labels.TryGetValue(creature, out var label) || !GodotObject.IsInstanceValid(label))
        {
            label = CreateLabel(combatRoom);
            if (label == null) return;
            _labels[creature] = label;
        }

        label.Text = text;
        label.AddThemeColorOverride("font_color", color);
        label.Visible = true;

        var node = combatRoom.GetCreatureNode(creature);
        if (node != null)
        {
            label.GlobalPosition = new Vector2(
                node.GlobalPosition.X + 10,
                node.GlobalPosition.Y - 320
            );
        }
    }

    public static void Hide()
    {
        foreach (var label in _labels.Values)
        {
            if (label != null && GodotObject.IsInstanceValid(label))
                label.QueueFree();
        }
        _labels.Clear();
    }

    private static void RemoveLabel(Creature creature)
    {
        if (_labels.TryGetValue(creature, out var label))
        {
            if (label != null && GodotObject.IsInstanceValid(label))
                label.QueueFree();
            _labels.Remove(creature);
        }
    }

    private static Label? CreateLabel(Node parent)
    {
        var label = new Label();
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.ZIndex = 100;

        if (_cachedFont == null)
            _cachedFont = FindGameFont(parent);
        if (_cachedFont != null)
            label.AddThemeFontOverride("font", _cachedFont);

        label.AddThemeColorOverride("font_color", new Color(1f, 0.3f, 0.3f));
        label.AddThemeFontSizeOverride("font_size", 22);
        label.AddThemeConstantOverride("outline_size", 5);
        label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f));

        parent.AddChild(label);
        return label;
    }

    private static Font? FindGameFont(Node root)
    {
        try
        {
            foreach (var child in root.GetChildren())
            {
                if (child is NCreature creatureNode)
                {
                    var intentContainer = creatureNode.IntentContainer;
                    if (intentContainer == null) continue;

                    foreach (var intentChild in intentContainer.GetChildren())
                    {
                        if (intentChild is NIntent)
                        {
                            var valueLabel = intentChild.GetNodeOrNull<Control>((NodePath)"%Value");
                            if (valueLabel is RichTextLabel rtl)
                            {
                                var font = rtl.GetThemeFont("normal_font");
                                if (font != null) return font;
                            }
                            else if (valueLabel is Label lbl)
                            {
                                var font = lbl.GetThemeFont("font");
                                if (font != null) return font;
                            }
                        }
                    }
                }
            }

            return FindFontRecursive(root, 3);
        }
        catch (Exception ex)
        {
            ModLog.Error("FindGameFont", ex);
            return null;
        }
    }

    private static Font? FindFontRecursive(Node node, int depth)
    {
        if (depth <= 0) return null;
        foreach (var child in node.GetChildren())
        {
            if (child is Label lbl)
            {
                var font = lbl.GetThemeFont("font");
                if (font != null) return font;
            }
            var found = FindFontRecursive(child, depth - 1);
            if (found != null) return found;
        }
        return null;
    }
}

// Recalculate when intents refresh
[HarmonyPatch(typeof(NCreature), nameof(NCreature.RefreshIntents))]
public class RecalcOnRefreshIntentsPatch
{
    static void Postfix() => DamageTracker.Recalculate();
}

// Recalculate when player ends turn
[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.SetReadyToEndTurn))]
public class RecalcOnEndTurnPatch
{
    static void Postfix() => DamageTracker.Recalculate();
}

// Periodically recalculate during combat (catches block/pet HP changes from card plays)
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
    }
}

// Hide when combat resets
[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.Reset))]
public class HideOnResetPatch
{
    static void Postfix() => DamageTracker.Hide();
}

// Hide when combat ends (victory)
[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.EndCombatInternal))]
public class HideOnWinPatch
{
    static void Prefix() => DamageTracker.Hide();
}

// Hide when player loses
[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.LoseCombat))]
public class HideOnLosePatch
{
    static void Prefix() => DamageTracker.Hide();
}

// ─── Hold R to restart run with same character ───
public static class RestartTracker
{
    private static CharacterModel? _character;
    private static List<ActModel>? _acts;
    private static List<ModifierModel>? _modifiers;
    private static int _ascension;
    private static bool _restartPending;

    public static void RestartRun()
    {
        try
        {
            if (_restartPending) return;

            var instance = RunManager.Instance;
            if (instance == null) return;
            var state = Traverse.Create(instance).Property<RunState>("State").Value;
            if (state == null) return;

            _character = state.Players[0].Character;
            _acts = state.Acts.ToList();
            _modifiers = state.Modifiers.ToList();
            _ascension = state.AscensionLevel;
            _restartPending = true;

            TaskHelper.RunSafely(RestartRunAsync());
        }
        catch (Exception ex)
        {
            _restartPending = false;
            ModLog.Error("RestartTracker.RestartRun", ex);
        }
    }

    private static async Task RestartRunAsync()
    {
        try
        {
            var game = NGame.Instance;
            if (game == null) return;

            await game.ReturnToMainMenu();
            await game.ToSignal(game.GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);

            string newSeed = SeedHelper.GetRandomSeed();
            var player = Player.CreateForNewRun(_character, SaveManager.Instance.GenerateUnlockStateFromProgress(), 1UL);
            var players = new List<Player> { player };
            var runState = RunState.CreateForNewRun(players, _acts, _modifiers, _ascension, newSeed);
            RunManager.Instance.SetUpNewSinglePlayer(runState, true);

            var startRunMethod = AccessTools.Method(typeof(NGame), "StartRun");
            var task = (Task)startRunMethod.Invoke(game, new object[] { runState });
            await task;
        }
        catch (Exception ex)
        {
            ModLog.Error("RestartRunAsync", ex);
        }
        finally
        {
            _restartPending = false;
        }
    }
}

// ─── Input handler: F1 for settings, hold R to restart ───
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
            // F1 toggles settings menu
            if (inputEvent is InputEventKey f1Event
                && f1Event.Keycode == Key.F1
                && f1Event.Pressed
                && !f1Event.IsEcho())
            {
                SettingsMenu.Toggle();
                return;
            }

            // Hold R to restart
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

// ─── Skip splash screen / logo animation on startup ───
[HarmonyPatch(typeof(NGame), "LaunchMainMenu")]
public class SkipSplashPatch
{
    static void Prefix(ref bool skipLogo)
    {
        if (ModSettings.SkipSplash)
            skipLogo = true;
    }
}
