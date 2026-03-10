using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace BetterSpire2;

/// <summary>
/// F3 panel that shows every player's current hand during combat.
/// Cards shown as portrait + name + resolved description with computed values.
/// </summary>
public static class DeckTracker
{
    private static CanvasLayer? _canvasLayer;
    private static PanelContainer? _panel;
    private static bool _visible;
    private static readonly List<PlayerHandSection> _sections = new();

    public static void Toggle()
    {
        if (_visible)
            Hide();
        else
            Show();
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

        if (_visible) Hide();

        var game = NGame.Instance;
        if (game == null) return;

        _canvasLayer = new CanvasLayer();
        _canvasLayer.Layer = 100;
        game.AddChild(_canvasLayer);

        var scrollContainer = new ScrollContainer();
        scrollContainer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        scrollContainer.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
        _canvasLayer.AddChild(scrollContainer);

        var centerH = new CenterContainer();
        centerH.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        centerH.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        scrollContainer.AddChild(centerH);

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
        vbox.AddThemeConstantOverride("separation", 12);
        _panel.AddChild(vbox);

        var title = new Label();
        title.Text = "Party Hands";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeColorOverride("font_color", new Color(0.9f, 0.7f, 0.2f));
        title.AddThemeFontSizeOverride("font_size", 20);
        vbox.AddChild(title);

        foreach (var player in runState.Players)
        {
            var section = new PlayerHandSection(player, runState);
            section.AddTo(vbox);
            _sections.Add(section);
        }

        var hint = new Label();
        hint.Text = "Press F3 to close";
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
        hint.AddThemeFontSizeOverride("font_size", 14);
        vbox.AddChild(hint);

        centerH.AddChild(_panel);
        _visible = true;
    }

    public static void Hide()
    {
        foreach (var section in _sections)
            section.Cleanup();
        _sections.Clear();

        if (_canvasLayer != null && GodotObject.IsInstanceValid(_canvasLayer))
            _canvasLayer.QueueFree();

        _canvasLayer = null;
        _panel = null;
        _visible = false;
    }

    /// <summary>
    /// Resolves card description with computed values (accounting for Strength, Weak, etc).
    /// Tries GetDescriptionForPile first, falls back to manual DynamicVar substitution.
    /// </summary>
    private static string ResolveDescription(object cardObj)
    {
        try
        {
            var tCard = Traverse.Create(cardObj);

            // GetDescriptionForPile returns fully computed description with modifiers applied
            try
            {
                var pile = tCard.Property("Pile").GetValue();
                object? pileTypeEnum = null;
                if (pile != null)
                    pileTypeEnum = Traverse.Create(pile).Property("PileType").GetValue();

                var method = cardObj.GetType().GetMethod("GetDescriptionForPile");
                if (method != null)
                {
                    // Second param is target Creature — null works fine
                    var result = method.Invoke(cardObj, new[] { pileTypeEnum, null });
                    if (result is string text && !string.IsNullOrEmpty(text))
                    {
                        return CleanDescriptionText(text);
                    }
                }
            }
            catch { }

            // Fallback: manual resolution from Description LocString + DynamicVars
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
        // Count consecutive [img]res://...icon.png[/img] sequences and replace with "N Icon"
        // e.g. [img]res://...energy_icon.png[/img][img]res://...energy_icon.png[/img] → "2 Energy"
        text = Regex.Replace(text,
            @"(\[img\]res://\S+?/([^/]+?)(?:_icon)?\.png\[/img\])+",
            match =>
            {
                // Count how many [img]...[/img] tags are in this consecutive run
                int count = Regex.Matches(match.Value, @"\[img\]").Count;
                // Extract the label from the first icon's filename
                var nameMatch = Regex.Match(match.Value, @"res://\S+?/([^/]+?)(?:_icon)?\.png");
                string label = nameMatch.Success ? nameMatch.Groups[1].Value : "?";
                // Capitalize and map known names
                label = MapIconLabel(label);
                return count > 1 ? $"{count} {label}" : label;
            },
            RegexOptions.IgnoreCase);

        // Strip remaining BBCode-style tags
        text = Regex.Replace(text, @"\[/?[^\]]+\]", "");
        // Clean up any leftover res:// references
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
        // Capitalize first letter for unknown icons
        return char.ToUpper(filename[0]) + filename.Substring(1);
    }

    /// <summary>
    /// Replaces {VarName:format()} placeholders with DynamicVar IntValue.
    /// </summary>
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

    private class PlayerHandSection
    {
        private readonly Player _player;
        private readonly CardPile? _hand;
        private readonly List<Control> _cardControls = new();
        private HBoxContainer? _cardRow;
        private Label? _nameLabel;
        private readonly ulong _localNetId;
        private const int MaxHandSize = 10;

        private const int DisplayWidth = 120;
        private const int PortraitHeight = 100;

        private static readonly Color AttackColor = new(0.9f, 0.35f, 0.3f);
        private static readonly Color SkillColor = new(0.3f, 0.55f, 0.9f);
        private static readonly Color PowerColor = new(0.9f, 0.75f, 0.2f);
        private static readonly Color StatusColor = new(0.5f, 0.5f, 0.5f);
        private static readonly Color CurseColor = new(0.7f, 0.2f, 0.7f);
        private static readonly Color DefaultCardColor = new(0.7f, 0.7f, 0.7f);

        public PlayerHandSection(Player player, RunState runState)
        {
            _player = player;
            _hand = player.PlayerCombatState?.Hand;

            try
            {
                var netService = RunManager.Instance?.NetService;
                _localNetId = netService?.NetId ?? 0;
            }
            catch { _localNetId = 0; }
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
            if (isSelf)
                displayName += " (You)";

            _nameLabel = new Label();
            UpdateNameLabel(displayName);
            _nameLabel.AddThemeColorOverride("font_color", isSelf
                ? new Color(0.5f, 0.8f, 0.5f)
                : new Color(0.9f, 0.9f, 0.9f));
            _nameLabel.AddThemeFontSizeOverride("font_size", 16);
            parent.AddChild(_nameLabel);

            _cardRow = new HBoxContainer();
            _cardRow.AddThemeConstantOverride("separation", 8);
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

                    var cardControl = CreateCardVisual(card);
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
                overflow.AddThemeFontSizeOverride("font_size", 14);
                _cardRow.AddChild(overflow);
                _cardControls.Add(overflow);
            }
        }

        private static Control CreateCardVisual(dynamic card)
        {
            try
            {
                object cardObj = (object)card;
                var tCard = Traverse.Create(cardObj);

                Texture2D? frame = null;
                Texture2D? portrait = null;
                string cardName = "???";
                string costText = "";
                string description = "";
                Color typeColor = DefaultCardColor;

                try { frame = tCard.Property<Texture2D>("Frame").Value; } catch { }
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

                if (portrait != null || frame != null)
                    return BuildPortraitCard(frame, portrait, cardName, costText, description, typeColor);
            }
            catch (Exception ex)
            {
                ModLog.Error("CreateCardVisual", ex);
            }

            return CreateCardFallback(card);
        }

        private static Control BuildPortraitCard(
            Texture2D? frame, Texture2D? portrait,
            string cardName, string costText, string description, Color typeColor)
        {
            var wrapper = new PanelContainer();
            wrapper.CustomMinimumSize = new Vector2(DisplayWidth, 0);

            var style = new StyleBoxFlat();
            style.BgColor = new Color(0.08f, 0.08f, 0.12f, 0.95f);
            style.BorderColor = typeColor;
            style.SetBorderWidthAll(2);
            style.SetCornerRadiusAll(6);
            style.SetContentMarginAll(0);
            wrapper.AddThemeStyleboxOverride("panel", style);

            var vbox = new VBoxContainer();
            vbox.AddThemeConstantOverride("separation", 0);
            wrapper.AddChild(vbox);

            // Portrait area
            if (frame != null || portrait != null)
            {
                var imageContainer = new Control();
                imageContainer.CustomMinimumSize = new Vector2(DisplayWidth, PortraitHeight);
                imageContainer.ClipContents = true;
                vbox.AddChild(imageContainer);

                if (frame != null)
                {
                    var frameRect = new TextureRect();
                    frameRect.Texture = frame;
                    frameRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
                    frameRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
                    frameRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                    frameRect.MouseFilter = Control.MouseFilterEnum.Ignore;
                    imageContainer.AddChild(frameRect);
                }

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

                // Energy cost badge
                if (!string.IsNullOrEmpty(costText))
                {
                    var costBg = new PanelContainer();
                    var costStyle = new StyleBoxFlat();
                    costStyle.BgColor = new Color(0, 0, 0, 0.7f);
                    costStyle.SetCornerRadiusAll(4);
                    costStyle.ContentMarginLeft = 6;
                    costStyle.ContentMarginRight = 6;
                    costStyle.ContentMarginTop = 2;
                    costStyle.ContentMarginBottom = 2;
                    costBg.AddThemeStyleboxOverride("panel", costStyle);
                    costBg.Position = new Vector2(3, 2);
                    costBg.MouseFilter = Control.MouseFilterEnum.Ignore;

                    var costInner = new Label();
                    costInner.Text = costText;
                    costInner.AddThemeColorOverride("font_color", Colors.White);
                    costInner.AddThemeFontSizeOverride("font_size", 14);
                    costBg.AddChild(costInner);

                    imageContainer.AddChild(costBg);
                }
            }

            // Card name
            var nameBar = new PanelContainer();
            var nameStyle = new StyleBoxFlat();
            nameStyle.BgColor = new Color(0.05f, 0.05f, 0.08f, 0.95f);
            nameStyle.SetContentMarginAll(0);
            nameStyle.ContentMarginLeft = 4;
            nameStyle.ContentMarginRight = 4;
            nameStyle.ContentMarginTop = 2;
            nameStyle.ContentMarginBottom = 1;
            nameBar.AddThemeStyleboxOverride("panel", nameStyle);

            var nameLabel = new Label();
            nameLabel.Text = cardName;
            nameLabel.HorizontalAlignment = HorizontalAlignment.Center;
            nameLabel.AddThemeColorOverride("font_color", typeColor);
            nameLabel.AddThemeFontSizeOverride("font_size", 10);
            nameLabel.ClipText = true;
            nameBar.AddChild(nameLabel);
            vbox.AddChild(nameBar);

            // Description text
            if (!string.IsNullOrEmpty(description))
            {
                var descBar = new PanelContainer();
                var descStyle = new StyleBoxFlat();
                descStyle.BgColor = new Color(0.05f, 0.05f, 0.08f, 0.95f);
                descStyle.SetContentMarginAll(0);
                descStyle.ContentMarginLeft = 4;
                descStyle.ContentMarginRight = 4;
                descStyle.ContentMarginTop = 1;
                descStyle.ContentMarginBottom = 4;
                descBar.AddThemeStyleboxOverride("panel", descStyle);
                descBar.CustomMinimumSize = new Vector2(DisplayWidth, 0);

                var descLabel = new Label();
                descLabel.Text = description;
                descLabel.HorizontalAlignment = HorizontalAlignment.Center;
                descLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));
                descLabel.AddThemeFontSizeOverride("font_size", 9);
                descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                descLabel.CustomMinimumSize = new Vector2(DisplayWidth - 8, 0);
                descBar.AddChild(descLabel);
                vbox.AddChild(descBar);
            }

            return wrapper;
        }

        private static PanelContainer CreateCardFallback(dynamic card)
        {
            string cardName = "???";
            Color borderColor = DefaultCardColor;

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
            style.BorderColor = borderColor;
            style.SetBorderWidthAll(1);
            style.BorderWidthLeft = 3;
            style.SetCornerRadiusAll(4);
            style.ContentMarginLeft = 8;
            style.ContentMarginRight = 8;
            style.ContentMarginTop = 4;
            style.ContentMarginBottom = 4;
            panel.AddThemeStyleboxOverride("panel", style);

            var label = new Label();
            label.Text = cardName;
            label.AddThemeColorOverride("font_color", borderColor);
            label.AddThemeFontSizeOverride("font_size", 14);
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
}
