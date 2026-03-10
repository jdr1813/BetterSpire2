using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace BetterSpire2;

/// <summary>
/// F3 panel that shows every player's current hand during combat.
/// Compact card thumbnails with native game tooltips on hover.
/// Draggable title bar.
/// </summary>
public static class DeckTracker
{
    // Set > 0 to duplicate the local player's hand N times for testing layout.
    public static int DebugDuplicatePlayers = 0;

    private static CanvasLayer? _canvasLayer;
    private static HandPanel? _panel;
    private static bool _visible;
    private static readonly List<PlayerHandSection> _sections = new();

    // Pagination
    private static int _currentPage;
    private const int PlayersPerPage = 4;

    // Persisted position across show/hide
    private static Vector2? _savedPosition;

    public static void Toggle()
    {
        if (_visible)
            Hide();
        else
            Show();
    }

    public static void NextPage()
    {
        if (!_visible) return;
        int totalPlayers = GetPlayerCount();
        int maxPage = Math.Max(0, (totalPlayers - 1) / PlayersPerPage);
        if (_currentPage < maxPage)
        {
            _currentPage++;
            Refresh();
        }
    }

    public static void PrevPage()
    {
        if (!_visible) return;
        if (_currentPage > 0)
        {
            _currentPage--;
            Refresh();
        }
    }

    private static int GetPlayerCount()
    {
        var runState = Traverse.Create(RunManager.Instance).Property<RunState>("State").Value;
        if (runState?.Players == null) return 0;
        return DebugDuplicatePlayers > 0 ? DebugDuplicatePlayers : runState.Players.Count;
    }

    private static void Show()
    {
        if (!ModSettings.ShowTeammateHand) return;

        var combat = CombatManager.Instance;
        if (combat == null) return;
        var combatState = combat.DebugOnlyGetState();
        if (combatState == null) return;

        var runState = Traverse.Create(RunManager.Instance).Property<RunState>("State").Value;
        if (runState?.Players == null || runState.Players.Count == 0) return;

        _currentPage = 0;
        BuildUI(runState);
    }

    private static void Refresh()
    {
        // Save position before rebuild
        if (_panel != null && GodotObject.IsInstanceValid(_panel))
            _savedPosition = _panel.Position;

        var runState = Traverse.Create(RunManager.Instance).Property<RunState>("State").Value;
        if (runState?.Players == null) return;
        CleanupUI();
        BuildUI(runState);
    }

    private static void BuildUI(RunState runState)
    {
        if (_visible) CleanupUI();

        var game = NGame.Instance;
        if (game == null) return;

        _canvasLayer = new CanvasLayer();
        _canvasLayer.Layer = 10;
        game.AddChild(_canvasLayer);

        // Build player list (with debug duplication)
        var players = new List<Player>();
        if (DebugDuplicatePlayers > 0)
        {
            var localPlayer = runState.Players[0];
            for (int i = 0; i < DebugDuplicatePlayers; i++)
                players.Add(localPlayer);
        }
        else
        {
            players.AddRange(runState.Players);
        }

        int totalPlayers = players.Count;
        int maxPage = Math.Max(0, (totalPlayers - 1) / PlayersPerPage);
        _currentPage = Math.Min(_currentPage, maxPage);
        int startIdx = _currentPage * PlayersPerPage;
        int endIdx = Math.Min(startIdx + PlayersPerPage, totalPlayers);

        _panel = new HandPanel();
        _panel.ZIndex = 200;

        var styleBox = new StyleBoxFlat();
        styleBox.BgColor = new Color(0.1f, 0.1f, 0.15f, 0.95f);
        styleBox.BorderColor = new Color(0.8f, 0.6f, 0.2f);
        styleBox.SetBorderWidthAll(2);
        styleBox.SetCornerRadiusAll(8);
        styleBox.SetContentMarginAll(14);
        _panel.AddThemeStyleboxOverride("panel", styleBox);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 6);
        _panel.AddChild(vbox);

        // Title bar — drag area + page controls + close button
        var titleBar = new HBoxContainer();
        titleBar.AddThemeConstantOverride("separation", 8);
        titleBar.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(titleBar);

        if (maxPage > 0)
        {
            var prevBtn = CreateArrowButton("<", _currentPage > 0);
            prevBtn.Pressed += () => PrevPage();
            titleBar.AddChild(prevBtn);
        }

        var title = new Label();
        string titleText = "Party Hands";
        if (maxPage > 0)
            titleText += $"  [{_currentPage + 1}/{maxPage + 1}]";
        title.Text = titleText;
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        title.MouseFilter = Control.MouseFilterEnum.Ignore;
        title.AddThemeColorOverride("font_color", new Color(0.9f, 0.7f, 0.2f));
        title.AddThemeFontSizeOverride("font_size", 18);
        titleBar.AddChild(title);

        if (maxPage > 0)
        {
            var nextBtn = CreateArrowButton(">", _currentPage < maxPage);
            nextBtn.Pressed += () => NextPage();
            titleBar.AddChild(nextBtn);
        }

        // Close button
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

        ulong localNetId = 0;
        try { localNetId = RunManager.Instance?.NetService?.NetId ?? 0; } catch { }

        for (int i = startIdx; i < endIdx; i++)
        {
            var player = players[i];
            string debugSuffix = DebugDuplicatePlayers > 0 ? $" #{i + 1}" : null;
            var section = new PlayerHandSection(player, localNetId, debugSuffix);
            section.AddTo(vbox);
            _sections.Add(section);
        }

        var hint = new Label();
        hint.Text = "Drag to move | Click outside to close";
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.MouseFilter = Control.MouseFilterEnum.Ignore;
        hint.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
        hint.AddThemeFontSizeOverride("font_size", 11);
        vbox.AddChild(hint);

        _canvasLayer.AddChild(_panel);

        // Position: restore saved or center on screen
        if (_savedPosition.HasValue)
        {
            _panel.Position = _savedPosition.Value;
        }
        else
        {
            // Defer centering until layout is resolved
            _panel.CallDeferred("set_position", GetCenteredPosition());
        }

        _visible = true;
    }

    private static Vector2 GetCenteredPosition()
    {
        var viewport = NGame.Instance?.GetViewport()?.GetVisibleRect().Size ?? new Vector2(1920, 1080);
        // Start centered horizontally, near the top
        float x = viewport.X / 2 - 500;
        if (x < 0) x = 0;
        return new Vector2(x, 20);
    }

    private static Button CreateArrowButton(string text, bool enabled)
    {
        var btn = new Button();
        btn.Text = text;
        btn.CustomMinimumSize = new Vector2(32, 28);
        btn.Disabled = !enabled;

        var style = new StyleBoxFlat();
        style.BgColor = enabled ? new Color(0.2f, 0.18f, 0.12f) : new Color(0.15f, 0.15f, 0.15f);
        style.BorderColor = enabled ? new Color(0.8f, 0.6f, 0.2f) : new Color(0.4f, 0.4f, 0.4f);
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(4);
        style.SetContentMarginAll(4);
        btn.AddThemeStyleboxOverride("normal", style);

        var hoverStyle = new StyleBoxFlat();
        hoverStyle.BgColor = new Color(0.3f, 0.25f, 0.15f);
        hoverStyle.BorderColor = new Color(0.9f, 0.7f, 0.2f);
        hoverStyle.SetBorderWidthAll(1);
        hoverStyle.SetCornerRadiusAll(4);
        hoverStyle.SetContentMarginAll(4);
        btn.AddThemeStyleboxOverride("hover", hoverStyle);

        var pressedStyle = new StyleBoxFlat();
        pressedStyle.BgColor = new Color(0.35f, 0.3f, 0.15f);
        pressedStyle.BorderColor = new Color(1f, 0.8f, 0.3f);
        pressedStyle.SetBorderWidthAll(1);
        pressedStyle.SetCornerRadiusAll(4);
        pressedStyle.SetContentMarginAll(4);
        btn.AddThemeStyleboxOverride("pressed", pressedStyle);

        btn.AddThemeStyleboxOverride("disabled", style);
        btn.AddThemeColorOverride("font_color", enabled ? new Color(0.9f, 0.7f, 0.2f) : new Color(0.4f, 0.4f, 0.4f));
        btn.AddThemeColorOverride("font_disabled_color", new Color(0.4f, 0.4f, 0.4f));
        btn.AddThemeFontSizeOverride("font_size", 16);

        return btn;
    }

    public static bool IsPointInPanel(Vector2 point)
    {
        if (!_visible || _panel == null || !GodotObject.IsInstanceValid(_panel)) return false;
        return _panel.GetGlobalRect().HasPoint(point);
    }

    public static void Hide()
    {
        // Save position before closing
        if (_panel != null && GodotObject.IsInstanceValid(_panel))
            _savedPosition = _panel.Position;

        CleanupUI();
        _visible = false;
    }

    private static void CleanupUI()
    {
        foreach (var section in _sections)
            section.Cleanup();
        _sections.Clear();

        if (_canvasLayer != null && GodotObject.IsInstanceValid(_canvasLayer))
            _canvasLayer.QueueFree();

        _canvasLayer = null;
        _panel = null;
    }

    /// <summary>
    /// Resolves card description with computed values.
    /// </summary>
    internal static string ResolveDescription(object cardObj)
    {
        try
        {
            var tCard = Traverse.Create(cardObj);

            try
            {
                var pile = tCard.Property("Pile").GetValue();
                object? pileTypeEnum = null;
                if (pile != null)
                    pileTypeEnum = Traverse.Create(pile).Property("PileType").GetValue();

                var method = cardObj.GetType().GetMethod("GetDescriptionForPile");
                if (method != null)
                {
                    var result = method.Invoke(cardObj, new[] { pileTypeEnum, null });
                    if (result is string text && !string.IsNullOrEmpty(text))
                        return CleanDescriptionText(text);
                }
            }
            catch { }

            var descObj = tCard.Property("Description").GetValue();
            if (descObj == null) return "";

            string template = Traverse.Create(descObj).Method("GetFormattedText").GetValue<string>();
            if (string.IsNullOrEmpty(template)) return "";

            string resolved = ResolveVarPlaceholders(cardObj, template);
            return CleanDescriptionText(resolved);
        }
        catch { return ""; }
    }

    private static string CleanDescriptionText(string text)
    {
        text = Regex.Replace(text,
            @"(\[img\]res://\S+?/([^/]+?)(?:_icon)?\.png\[/img\])+",
            match =>
            {
                int count = Regex.Matches(match.Value, @"\[img\]").Count;
                var nameMatch = Regex.Match(match.Value, @"res://\S+?/([^/]+?)(?:_icon)?\.png");
                string label = nameMatch.Success ? nameMatch.Groups[1].Value : "?";
                label = MapIconLabel(label);
                return count > 1 ? $"{count} {label}" : label;
            },
            RegexOptions.IgnoreCase);

        text = Regex.Replace(text, @"\[/?[^\]]+\]", "");
        text = Regex.Replace(text, @"res://\S+", "");
        return text.Trim();
    }

    private static string MapIconLabel(string filename)
    {
        filename = filename.ToLowerInvariant();
        if (filename.Contains("energy")) return "Energy";
        if (filename.Contains("block")) return "Block";
        if (filename.Contains("star")) return "Star";
        if (filename.Contains("orb")) return "Orb";
        return char.ToUpper(filename[0]) + filename.Substring(1);
    }

    private static string ResolveVarPlaceholders(object cardObj, string text)
    {
        var dvObj = Traverse.Create(cardObj).Property("DynamicVars").GetValue();
        if (dvObj == null) return text;

        return Regex.Replace(text, @"\{(\w+)(?::[^}]*)?\}", match =>
        {
            string varName = match.Groups[1].Value;
            try
            {
                var tryGet = dvObj.GetType().GetMethod("TryGetValue");
                if (tryGet != null)
                {
                    var args = new object[] { varName, null! };
                    bool found = (bool)tryGet.Invoke(dvObj, args)!;
                    if (found && args[1] != null)
                    {
                        int val = Traverse.Create(args[1]).Property<int>("IntValue").Value;
                        return val.ToString();
                    }
                }
            }
            catch { }
            return match.Value;
        });
    }

    /// <summary>
    /// Main panel that handles dragging (via _Input for reliability) and
    /// click-outside-to-close behavior.
    /// </summary>
    private class HandPanel : PanelContainer { }

    // Drag/click-outside state — driven from InputPatch
    private static bool _dragging;
    private static Vector2 _dragOffset;

    /// <summary>
    /// Called from InputPatch for all mouse events while the panel is visible.
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
                else if (!SettingsMenu.IsPointInPanel(mousePos))
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

    private class PlayerHandSection
    {
        private readonly Player _player;
        private readonly CardPile? _hand;
        private readonly List<Control> _cardControls = new();
        private HBoxContainer? _cardRow;
        private Label? _nameLabel;
        private readonly ulong _localNetId;
        private readonly string? _debugSuffix;
        private const int MaxHandSize = 12;

        // Card sizing
        private const int CardWidth = 100;
        private const int PortraitHeight = 80;

        private static readonly Color AttackColor = new(0.9f, 0.35f, 0.3f);
        private static readonly Color SkillColor = new(0.3f, 0.55f, 0.9f);
        private static readonly Color PowerColor = new(0.9f, 0.75f, 0.2f);
        private static readonly Color StatusColor = new(0.5f, 0.5f, 0.5f);
        private static readonly Color CurseColor = new(0.7f, 0.2f, 0.7f);
        private static readonly Color DefaultCardColor = new(0.7f, 0.7f, 0.7f);

        public PlayerHandSection(Player player, ulong localNetId, string? debugSuffix = null)
        {
            _player = player;
            _hand = player.PlayerCombatState?.Hand;
            _localNetId = localNetId;
            _debugSuffix = debugSuffix;
        }

        public void AddTo(VBoxContainer parent)
        {
            parent.AddChild(new HSeparator());

            bool isSelf = _player.NetId == _localNetId;
            string steamName = PlatformUtil.GetPlayerName(PlatformType.Steam, _player.NetId);
            string charClass = _player.Character?.Title?.GetFormattedText();
            string displayName = !string.IsNullOrEmpty(steamName) ? steamName : (charClass ?? "Player");
            if (!string.IsNullOrEmpty(charClass) && !string.IsNullOrEmpty(steamName))
                displayName = $"{steamName} ({charClass})";
            if (isSelf && _debugSuffix == null)
                displayName += " (You)";
            if (_debugSuffix != null)
                displayName += _debugSuffix;

            _nameLabel = new Label();
            UpdateNameLabel(displayName);
            _nameLabel.AddThemeColorOverride("font_color", isSelf
                ? new Color(0.5f, 0.8f, 0.5f)
                : new Color(0.9f, 0.9f, 0.9f));
            _nameLabel.AddThemeFontSizeOverride("font_size", 14);
            parent.AddChild(_nameLabel);

            _cardRow = new HBoxContainer();
            _cardRow.AddThemeConstantOverride("separation", 6);
            parent.AddChild(_cardRow);

            RefreshCards();

            if (_hand != null)
                _hand.ContentsChanged += OnHandChanged;
        }

        public void Cleanup()
        {
            if (_hand != null)
                _hand.ContentsChanged -= OnHandChanged;
            ClearCards();
        }

        private void OnHandChanged()
        {
            try { RefreshCards(); }
            catch (Exception ex) { ModLog.Error("PlayerHandSection.OnHandChanged", ex); }
        }

        private void RefreshCards()
        {
            if (_cardRow == null || !GodotObject.IsInstanceValid(_cardRow)) return;
            if (_hand == null) return;

            ClearCards();

            int count = _hand.Cards?.Count ?? 0;
            UpdateNameLabel(null, count);

            if (count == 0) return;

            int toShow = Math.Min(count, MaxHandSize);
            for (int i = 0; i < toShow; i++)
            {
                try
                {
                    var card = _hand.Cards[i];
                    if (card == null) continue;

                    var cardControl = CreateCompactCard(card);
                    _cardRow.AddChild(cardControl);
                    _cardControls.Add(cardControl);
                }
                catch (Exception ex)
                {
                    ModLog.Error($"PlayerHandSection: failed to create card {i}", ex);
                }
            }

            if (count > MaxHandSize)
            {
                var overflow = new Label();
                overflow.Text = $"+{count - MaxHandSize}";
                overflow.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
                overflow.AddThemeFontSizeOverride("font_size", 12);
                _cardRow.AddChild(overflow);
                _cardControls.Add(overflow);
            }
        }

        private static Control CreateCompactCard(dynamic card)
        {
            try
            {
                object cardObj = (object)card;
                var tCard = Traverse.Create(cardObj);

                Texture2D? portrait = null;
                string cardName = "???";
                string costText = "";
                string description = "";
                Color typeColor = DefaultCardColor;

                try { portrait = tCard.Property<Texture2D>("Portrait").Value; } catch { }

                try
                {
                    string title = tCard.Property<string>("Title").Value;
                    if (!string.IsNullOrEmpty(title)) cardName = title;
                    bool upgraded = tCard.Property<bool>("IsUpgraded").Value;
                    if (upgraded && cardName != "???") cardName += "+";
                }
                catch { }

                try
                {
                    var energyCostObj = tCard.Property("EnergyCost").GetValue();
                    if (energyCostObj != null)
                    {
                        bool costsX = Traverse.Create(energyCostObj).Property<bool>("CostsX").Value;
                        if (costsX) costText = "X";
                        else
                        {
                            int cost = Traverse.Create(energyCostObj).Property<int>("Canonical").Value;
                            if (cost >= 0) costText = cost.ToString();
                        }
                    }
                }
                catch { }

                try
                {
                    var cardType = tCard.Property("Type").GetValue();
                    if (cardType != null)
                    {
                        string typeName = cardType.ToString() ?? "";
                        if (typeName.Contains("Attack")) typeColor = AttackColor;
                        else if (typeName.Contains("Skill")) typeColor = SkillColor;
                        else if (typeName.Contains("Power")) typeColor = PowerColor;
                        else if (typeName.Contains("Status")) typeColor = StatusColor;
                        else if (typeName.Contains("Curse")) typeColor = CurseColor;
                    }
                }
                catch { }

                description = ResolveDescription(cardObj);

                return BuildCompactCard(portrait, cardName, costText, description, typeColor);
            }
            catch (Exception ex)
            {
                ModLog.Error("CreateCompactCard", ex);
            }

            return CreateCardFallback(card);
        }

        private static Control BuildCompactCard(
            Texture2D? portrait, string cardName, string costText,
            string description, Color typeColor)
        {
            var wrapper = new CardPanel();
            wrapper.CustomMinimumSize = new Vector2(CardWidth, PortraitHeight + 20);
            wrapper.CardName = cardName;
            wrapper.CardDescription = description;
            wrapper.WireUpTooltip();

            var style = new StyleBoxFlat();
            style.BgColor = new Color(0.08f, 0.08f, 0.12f, 0.95f);
            style.BorderColor = typeColor;
            style.SetBorderWidthAll(1);
            style.SetCornerRadiusAll(4);
            style.SetContentMarginAll(0);
            wrapper.AddThemeStyleboxOverride("panel", style);

            var vbox = new VBoxContainer();
            vbox.AddThemeConstantOverride("separation", 0);
            vbox.MouseFilter = Control.MouseFilterEnum.Pass;
            wrapper.AddChild(vbox);

            // Portrait
            var imageContainer = new Control();
            imageContainer.CustomMinimumSize = new Vector2(CardWidth, PortraitHeight);
            imageContainer.ClipContents = true;
            imageContainer.MouseFilter = Control.MouseFilterEnum.Pass;
            vbox.AddChild(imageContainer);

            if (portrait != null)
            {
                var portraitRect = new TextureRect();
                portraitRect.Texture = portrait;
                portraitRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
                portraitRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
                portraitRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                portraitRect.MouseFilter = Control.MouseFilterEnum.Ignore;
                imageContainer.AddChild(portraitRect);
            }

            // Energy cost badge (top-left)
            if (!string.IsNullOrEmpty(costText))
            {
                var costBg = new PanelContainer();
                costBg.MouseFilter = Control.MouseFilterEnum.Ignore;
                var costStyle = new StyleBoxFlat();
                costStyle.BgColor = new Color(0, 0, 0, 0.75f);
                costStyle.SetCornerRadiusAll(3);
                costStyle.ContentMarginLeft = 5;
                costStyle.ContentMarginRight = 5;
                costStyle.ContentMarginTop = 1;
                costStyle.ContentMarginBottom = 1;
                costBg.AddThemeStyleboxOverride("panel", costStyle);
                costBg.Position = new Vector2(2, 1);

                var costLabel = new Label();
                costLabel.Text = costText;
                costLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
                costLabel.AddThemeColorOverride("font_color", Colors.White);
                costLabel.AddThemeFontSizeOverride("font_size", 13);
                costBg.AddChild(costLabel);

                imageContainer.AddChild(costBg);
            }

            // Card name bar
            var nameBar = new PanelContainer();
            nameBar.MouseFilter = Control.MouseFilterEnum.Pass;
            var nameStyle = new StyleBoxFlat();
            nameStyle.BgColor = new Color(0.05f, 0.05f, 0.08f, 0.95f);
            nameStyle.SetContentMarginAll(0);
            nameStyle.ContentMarginLeft = 3;
            nameStyle.ContentMarginRight = 3;
            nameStyle.ContentMarginTop = 2;
            nameStyle.ContentMarginBottom = 2;
            nameBar.AddThemeStyleboxOverride("panel", nameStyle);

            var nameLabel = new Label();
            nameLabel.Text = cardName;
            nameLabel.HorizontalAlignment = HorizontalAlignment.Center;
            nameLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
            nameLabel.AddThemeColorOverride("font_color", typeColor);
            nameLabel.AddThemeFontSizeOverride("font_size", 11);
            nameLabel.ClipText = true;
            nameLabel.CustomMinimumSize = new Vector2(CardWidth - 6, 0);
            nameBar.AddChild(nameLabel);
            vbox.AddChild(nameBar);

            return wrapper;
        }

        private static PanelContainer CreateCardFallback(dynamic card)
        {
            string cardName = "???";
            try
            {
                object cardObj = (object)card;
                var tCard = Traverse.Create(cardObj);
                string title = tCard.Property<string>("Title").Value;
                if (!string.IsNullOrEmpty(title)) cardName = title;
            }
            catch { }

            var panel = new PanelContainer();
            var style = new StyleBoxFlat();
            style.BgColor = new Color(0.15f, 0.15f, 0.2f, 0.9f);
            style.BorderColor = DefaultCardColor;
            style.SetBorderWidthAll(1);
            style.SetCornerRadiusAll(4);
            style.ContentMarginLeft = 4;
            style.ContentMarginRight = 4;
            style.ContentMarginTop = 2;
            style.ContentMarginBottom = 2;
            panel.AddThemeStyleboxOverride("panel", style);

            var label = new Label();
            label.Text = cardName;
            label.AddThemeColorOverride("font_color", DefaultCardColor);
            label.AddThemeFontSizeOverride("font_size", 11);
            panel.AddChild(label);

            return panel;
        }

        private void ClearCards()
        {
            foreach (var ctrl in _cardControls)
            {
                if (GodotObject.IsInstanceValid(ctrl))
                    ctrl.QueueFree();
            }
            _cardControls.Clear();
        }

        private void UpdateNameLabel(string? baseName = null, int? count = null)
        {
            if (_nameLabel == null || !GodotObject.IsInstanceValid(_nameLabel)) return;
            int c = count ?? _hand?.Cards?.Count ?? 0;
            if (baseName != null)
                _nameLabel.Text = $"{baseName} — Hand ({c})";
            else
            {
                string current = _nameLabel.Text;
                int dashIdx = current.IndexOf('—');
                if (dashIdx > 0)
                    _nameLabel.Text = current.Substring(0, dashIdx) + $"— Hand ({c})";
            }
        }
    }

    /// <summary>
    /// Creates a HoverTip with plain strings (bypassing LocString requirement).
    /// HoverTip is a record struct — must box it to mutate via reflection.
    /// </summary>
    private static IHoverTip CreateHoverTip(string title, string description)
    {
        object box = new HoverTip();
        var type = typeof(HoverTip);
        type.GetProperty("Title")!.SetValue(box, title);
        type.GetProperty("Description")!.SetValue(box, description);
        type.GetProperty("Id")!.SetValue(box, $"BetterSpire2_{title}");
        return (IHoverTip)box;
    }

    /// <summary>
    /// Card container that uses the game's native NHoverTipSet tooltip system.
    /// </summary>
    private class CardPanel : PanelContainer
    {
        public string CardName = "";
        public string CardDescription = "";

        public void WireUpTooltip()
        {
            MouseFilter = MouseFilterEnum.Stop;
            MouseEntered += OnMouseEntered;
            MouseExited += OnMouseExited;
        }

        private void OnMouseEntered()
        {
            try
            {
                var tip = CreateHoverTip(CardName, CardDescription);
                var tipNode = NHoverTipSet.CreateAndShow(this, tip);
                // Reparent tooltip into our CanvasLayer so it renders on top of the panel
                var parent = tipNode.GetParent();
                if (parent != null)
                    parent.RemoveChild(tipNode);
                _canvasLayer?.AddChild(tipNode);
                tipNode.ZIndex = 500;
                tipNode.GlobalPosition = GetViewport().GetMousePosition() + new Vector2(16, 0);
            }
            catch (Exception ex)
            {
                ModLog.Error("CardPanel tooltip show", ex);
            }
        }

        private void OnMouseExited()
        {
            try
            {
                NHoverTipSet.Remove(this);
            }
            catch (Exception ex)
            {
                ModLog.Error("CardPanel tooltip hide", ex);
            }
        }
    }
}
