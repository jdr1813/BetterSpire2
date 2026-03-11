using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterSpire2;

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

            var combatManager = CombatManager.Instance;
            var state = combatManager.DebugOnlyGetState();
            if (state == null)
            {
                Hide();
                return;
            }

            // Lock display during enemy turn — block/HP changes mid-animation cause flickering
            if (combatManager.IsEnemyTurnStarted)
                return;

            var playerCreatures = state.PlayerCreatures;

            var enemyHits = new List<int>();
            foreach (var enemy in state.Enemies)
            {
                if (enemy.IsDead || enemy.Monster == null) continue;

                // Poison ticks before enemies attack — skip enemies that will die from poison
                var poison = enemy.GetPower<PoisonPower>();
                if (poison != null && poison.Amount >= enemy.CurrentHp)
                    continue;

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

            var activeCreatures = new HashSet<Creature>();

            foreach (var player in playerCreatures)
            {
                if (player.IsDead || player.IsPet || !player.IsPlayer) continue;

                Creature? pet = null;
                var pets = player.Pets;
                if (pets != null && pets.Count > 0)
                    pet = pets.FirstOrDefault(p => !p.IsDead);

                var endOfTurnHits = new List<(Creature target, int damage)>();
                var endOfTurnUnblockableHits = new List<(Creature target, int damage)>();

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

                int simBlock = player.Block;
                bool petAbsorbs = pet != null && !pet.IsDead && pet.MaxHp > 0
                    && (pet.Monster == null || pet.Monster.IsHealthBarVisible);
                int simPetHp = petAbsorbs ? pet!.CurrentHp : 0;
                int simPetBlock = petAbsorbs ? pet!.Block : 0;
                int petAbsorbed = 0;
                int playerTakes = 0;

                // End-of-turn block sources — only add during player turn (before already applied)
                if (!combatManager.IsEnemyTurnStarted)
                {
                    // Orichalcum — grants block at end of turn only if player has 0 block
                    // Check before adding plating, since the game checks original block
                    if (simBlock == 0 && player.Player?.Relics != null)
                    {
                        if (player.Player.Relics.OfType<Orichalcum>().Any())
                            simBlock += 6;
                        if (player.Player.Relics.OfType<FakeOrichalcum>().Any())
                            simBlock += 3;
                    }

                    // Plating (Metallicize) — grants block at end of turn before enemies attack
                    var plating = player.GetPower<PlatingPower>();
                    if (plating != null && plating.Amount > 0)
                        simBlock += plating.Amount;
                }

                // Intangible — reduces all damage instances to 1
                bool intangible = player.GetPower<IntangiblePower>() != null;

                // Tungsten Rod relic — reduces each HP loss by 1 (min 1)
                bool hasTungstenRod = player.Player?.Relics?.OfType<TungstenRod>().Any() == true;

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
                foreach (var (target, dmg) in endOfTurnHits)
                {
                    int damage = (intangible && !target.IsPet) ? Math.Min(dmg, 1) : dmg;
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
                        int hpLoss = damage - blocked;
                        if (hpLoss > 0 && hasTungstenRod)
                            hpLoss = Math.Max(hpLoss - 1, 0);
                        playerTakes += hpLoss;
                    }
                }

                // Unblockable end-of-turn damage
                foreach (var (target, dmg) in endOfTurnUnblockableHits)
                {
                    int damage = (intangible && !target.IsPet) ? Math.Min(dmg, 1) : dmg;
                    if (target.IsPet)
                    {
                        simPetHp -= damage;
                        petAbsorbed += damage;
                    }
                    else
                    {
                        if (hasTungstenRod && damage > 0)
                            damage = Math.Max(damage - 1, 0);
                        playerTakes += damage;
                    }
                }

                // Enemy attack damage: block -> pet HP -> player HP
                foreach (int hit in enemyHits)
                {
                    int remaining = intangible ? Math.Min(hit, 1) : hit;

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

                    if (remaining > 0 && hasTungstenRod)
                        remaining = Math.Max(remaining - 1, 0);

                    playerTakes += remaining;
                }

                // Regen heals at AfterTurnEnd (before enemy attacks) — factor into expected HP
                int regenHeal = 0;
                var regen = player.GetPower<RegenPower>();
                if (regen != null && regen.Amount > 0)
                    regenHeal = regen.Amount;

                activeCreatures.Add(player);
                if (playerTakes > 0)
                {
                    string text = ModSettings.ShowExpectedHp
                        ? $"{playerTakes} ({Math.Min(player.CurrentHp - playerTakes + regenHeal, player.MaxHp)})"
                        : playerTakes.ToString();
                    ShowLabel(player, text, new Color(1f, 0.3f, 0.3f));
                }
                else
                {
                    if (ModSettings.ShowExpectedHp && regenHeal > 0)
                        ShowLabel(player, $"0 ({Math.Min(player.CurrentHp + regenHeal, player.MaxHp)})", new Color(0.3f, 1f, 0.3f));
                    else
                        ShowLabel(player, "0", new Color(0.3f, 1f, 0.3f));
                }

                if (petAbsorbs && petAbsorbed > 0)
                {
                    activeCreatures.Add(pet!);
                    ShowLabel(pet!, petAbsorbed.ToString(), new Color(1f, 0.6f, 0.2f));
                }
            }

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
