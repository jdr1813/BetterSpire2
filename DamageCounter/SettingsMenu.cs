using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using System;

namespace BetterSpire2;

public static class SettingsMenu
{
    private static PanelContainer? _panel;
    private static CanvasLayer? _canvasLayer;
    private static bool _visible;
    private static Vector2? _savedPosition;
    private static bool _dragging;
    private static Vector2 _dragOffset;

    public static void Toggle()
    {
        if (_visible)
            Hide();
        else
            Show();
    }

    /// <summary>
    /// Called from InputPatch for mouse events while settings is visible.
    /// Handles drag-to-move and click-outside-to-close.
    /// </summary>
    public static void HandleMouseInput(InputEvent @event)
    {
        if (!_visible || _panel == null || !GodotObject.IsInstanceValid(_panel)) return;

        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            var mousePos = mb.Position;
            var rect = _panel.GetGlobalRect();

            if (mb.Pressed)
            {
                if (rect.HasPoint(mousePos))
                {
                    _dragging = true;
                    _dragOffset = mousePos - _panel.Position;
                }
                else if (!DeckTracker.IsPointInPanel(mousePos))
                {
                    _dragging = false;
                    Hide();
                }
            }
            else
            {
                _dragging = false;
            }
        }
        else if (@event is InputEventMouseMotion mm && _dragging)
        {
            _panel.Position = mm.Position - _dragOffset;
        }
    }

    private static void Show()
    {
        if (_visible) Hide();

        var game = NGame.Instance;
        if (game == null) return;

        _canvasLayer = new CanvasLayer();
        _canvasLayer.Layer = 10;
        game.AddChild(_canvasLayer);

        _panel = new PanelContainer();
        _panel.ZIndex = 200;

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

        // Title bar with close button
        var titleBar = new HBoxContainer();
        titleBar.AddThemeConstantOverride("separation", 8);
        vbox.AddChild(titleBar);

        var title = new Label();
        title.Text = "BetterSpire2 Settings";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        title.MouseFilter = Control.MouseFilterEnum.Ignore;
        title.AddThemeColorOverride("font_color", new Color(0.9f, 0.7f, 0.2f));
        title.AddThemeFontSizeOverride("font_size", 20);
        titleBar.AddChild(title);

        var closeBtn = new Button();
        closeBtn.Text = "X";
        closeBtn.CustomMinimumSize = new Vector2(28, 28);
        closeBtn.Pressed += () => Hide();
        var closeStyle = new StyleBoxFlat();
        closeStyle.BgColor = new Color(0.3f, 0.1f, 0.1f);
        closeStyle.BorderColor = new Color(0.6f, 0.2f, 0.2f);
        closeStyle.SetBorderWidthAll(1);
        closeStyle.SetCornerRadiusAll(4);
        closeStyle.SetContentMarginAll(2);
        closeBtn.AddThemeStyleboxOverride("normal", closeStyle);
        var closeHover = new StyleBoxFlat();
        closeHover.BgColor = new Color(0.5f, 0.15f, 0.15f);
        closeHover.BorderColor = new Color(0.8f, 0.3f, 0.3f);
        closeHover.SetBorderWidthAll(1);
        closeHover.SetCornerRadiusAll(4);
        closeHover.SetContentMarginAll(2);
        closeBtn.AddThemeStyleboxOverride("hover", closeHover);
        closeBtn.AddThemeStyleboxOverride("pressed", closeHover);
        closeBtn.AddThemeColorOverride("font_color", new Color(0.9f, 0.4f, 0.4f));
        closeBtn.AddThemeFontSizeOverride("font_size", 14);
        titleBar.AddChild(closeBtn);

        vbox.AddChild(new HSeparator());

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
        AddToggle(vbox, "Show Teammate Hand in Combat", ModSettings.ShowTeammateHand, v => {
            ModSettings.ShowTeammateHand = v;
            ModSettings.Save();
        });

        BuildPartySection(vbox);

        var hint = new Label();
        hint.Text = "Drag to move | Click outside to close";
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.MouseFilter = Control.MouseFilterEnum.Ignore;
        hint.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
        hint.AddThemeFontSizeOverride("font_size", 14);
        vbox.AddChild(hint);

        _canvasLayer.AddChild(_panel);

        // Position: restore saved or center on screen
        if (_savedPosition.HasValue)
        {
            _panel.Position = _savedPosition.Value;
        }
        else
        {
            var viewport = game.GetViewport()?.GetVisibleRect().Size ?? new Vector2(1920, 1080);
            _panel.CallDeferred("set_position", new Vector2(viewport.X / 2 - 200, viewport.Y / 2 - 200));
        }

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

    public static bool IsPointInPanel(Vector2 point)
    {
        if (!_visible || _panel == null || !GodotObject.IsInstanceValid(_panel)) return false;
        return _panel.GetGlobalRect().HasPoint(point);
    }

    public static void Hide()
    {
        if (_panel != null && GodotObject.IsInstanceValid(_panel))
            _savedPosition = _panel.Position;

        if (_canvasLayer != null && GodotObject.IsInstanceValid(_canvasLayer))
            _canvasLayer.QueueFree();

        _canvasLayer = null;
        _panel = null;
        _visible = false;
        _dragging = false;
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

            if (isHost)
            {
                AddToggle(vbox, "Scale difficulty to active players", ModSettings.ScaleToActivePlayers, v => {
                    ModSettings.ScaleToActivePlayers = v;
                    ModSettings.Save();
                    ScalingPatches.OnSettingToggled(v);
                });
            }
        }
        catch (Exception ex) { ModLog.Error("BuildPartySection", ex); }
    }
}
