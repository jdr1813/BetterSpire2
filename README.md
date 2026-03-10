# BetterSpire2 - Slay the Spire 2 Mod

A quality-of-life mod for Slay the Spire 2 that adds damage tracking, a teammate hand viewer, quick restart, and more.

## Features

### 1. Multi-Hit Totals (per enemy)
- **What it does:** Appends a `(total)` value to multi-hit enemy intent labels. For example, if an enemy attacks for `6x3`, the label becomes `6x3 (18)`.
- **How it works:** Patches `NIntent.UpdateVisuals` (Harmony Postfix). After the game renders the intent label, it checks if the attack is multi-hit (total > single hit damage). If so, it appends ` (totalDamage)` to the existing label text.
- **Key classes:** `IntentLabelPatch`, uses `AttackIntent.GetSingleDamage()` and `AttackIntent.GetTotalDamage()`.

### 2. Total Incoming Damage (above player/pet)
- **What it does:** Displays a number above the player (and pet, if applicable) showing how much HP damage they will take this turn after accounting for block, pet absorption, debuff powers, and end-of-turn card effects.
- **How it works:** The `DamageTracker` class gathers damage from three sources, simulates the full damage sequence, and renders Godot `Label` nodes positioned above each creature.
  - **Red number** above player = damage the player will take to HP.
  - **Green "0"** above player = player is fully protected (block/pet absorbs everything).
  - **Orange number** above pet = damage the pet will absorb.
  - **Pet filtering:** Only pets with `MaxHp > 0` and `Monster.IsHealthBarVisible == true` are treated as damage absorbers. This excludes non-hittable pets like Pael's Legion, which exist as creatures but have no real HP pool.

#### Damage sources tracked

**Enemy attack intents** — all `AttackIntent`s (single and multi-hit).

**End-of-turn damage powers (hardcoded — must be updated if new ones are added):**
| Power | Blockable? | Notes |
|---|---|---|
| `ConstrictPower` | Yes | `Amount` damage per turn |
| `DemisePower` | **No (unblockable)** | `Amount` damage per turn, bypasses block entirely |
| `MagicBombPower` | Yes | `Amount` damage, instanced (can have multiple), only fires if applier enemy is alive |
| `DisintegrationPower` | Yes | `Amount` damage per turn, stacks cumulatively (6 → 13 → 21 from Knowledge Demon) |

**End-of-turn hand card damage (generic — automatically handles all current and future cards):**
Any card with `HasTurnEndInHandEffect == true` is included. Cards with a `"Damage"` DynamicVar are treated as blockable; cards with an `"HpLoss"` DynamicVar (e.g. Beckon) are treated as unblockable (HP loss bypasses block).
Currently covers: Burn, Decay, Toxic, Debt, Doubt, Shame, Regret, Beckon, BadLuck, Infection.
New cards matching this pattern are picked up automatically with no code changes.

**Frost orb passive block (Defect):**
The simulation accounts for Defect's frost orbs, which grant passive block at end of turn before enemies attack. Each `FrostOrb` in `PlayerCombatState.OrbQueue.Orbs` contributes its `PassiveVal` (which already accounts for Focus) to the simulated block total.

#### Simulation order
1. Frost orb passive block added to simulated block total
2. Blockable end-of-turn damage (powers + cards with `Damage`) → consumed by block first
3. Unblockable end-of-turn damage (Demise + cards with `HpLoss` like Beckon) → straight to HP
4. Enemy attack damage → remaining block → pet HP → player HP

- **Recalculation triggers** (each is a separate Harmony patch):
  - `NCreature.RefreshIntents` (Postfix) - when enemy intents update.
  - `CombatManager.SetReadyToEndTurn` (Postfix) - when player ends turn.
  - `NIntent._Process` (Postfix) - periodic recalc every 250ms during combat to catch block/HP changes from card plays.
- **Cleanup triggers** (hide labels):
  - `CombatManager.Reset` (Postfix)
  - `CombatManager.EndCombatInternal` (Prefix) - victory
  - `CombatManager.LoseCombat` (Prefix) - defeat
- **Overlay hiding:** Labels automatically hide when full-screen overlays are open:
  - `NCapstoneContainer.InUse` - deck view, discard pile, exhaust pile screens
  - `NOverlayStack.ScreenCount > 0` - card selection overlays (potion picks, card rewards, etc.)
- **Font:** Attempts to match the game's font by searching the scene tree for existing intent labels, falling back to a recursive font search (depth 3).

### 3. Hold R to Restart Run
- **What it does:** Hold the R key for 1.5 seconds to abandon the current run and start a new one with the same character, acts, modifiers, and ascension level (but a new seed).
- **How it works:** `InputPatch` patches `NGame._Input` to track R key press duration. When 1.5s elapses, `RestartTracker.RestartRun()` fires:
  1. Captures current run state (character, acts, modifiers, ascension) via `RunManager.Instance` and Harmony `Traverse`.
  2. Calls `NGame.ReturnToMainMenu()` asynchronously.
  3. Waits 0.5s via a Godot scene tree timer.
  4. Creates a new `Player`, `RunState`, and calls `RunManager.SetUpNewSinglePlayer()` + `NGame.StartRun()` via reflection (`AccessTools.Method`).
- **Error logging:** On failure, writes to `<user_data_dir>/betterspire2_error.txt`.

### 4. Skip Splash Screen
- **What it does:** Skips the logo/splash animation on game startup.
- **How it works:** Patches `NGame.LaunchMainMenu` (Harmony Prefix), setting the `skipLogo` parameter to `true`.

### 5. Teammate Hand Viewer
- **What it does:** Press **F3** to open a compact panel showing every player's current hand in combat. Each card is rendered as a small thumbnail (100x80) with the card name. Hovering over any card shows a full tooltip with the card's description using the game's native `NHoverTipSet` system.
- **How it works:** `DeckTracker` class reads each player's hand from `PlayerCombatState.Hand.Cards` via `CombatManager.DebugOnlyGetState()`. Cards are displayed in a grid layout grouped by player.
  - **Pagination:** If there are more than 4 players, `<` `>` arrow buttons appear to page through them. Page Up / Page Down keyboard shortcuts also work.
  - **Tooltips:** Each card is wrapped in a `CardPanel` (custom `PanelContainer` subclass) that wires up `MouseEntered`/`MouseExited` signals. On hover, it creates an `IHoverTip` via boxed struct reflection (since `HoverTip` is a `record struct` that takes `LocString`, not plain strings) and calls `NHoverTipSet.CreateAndShow()`. The tooltip node is reparented from the game's `HoverTipsContainer` into the mod's `CanvasLayer` so it renders above the panel.
  - **Drag/close:** Mouse events are routed through `InputPatch` to `DeckTracker.HandleMouseInput()`. Supports click-and-drag to reposition, click-outside-to-close (with mutual awareness of SettingsMenu via `IsPointInPanel()`), and a close button.
  - **Position memory:** Panel position is saved across open/close cycles.
- **Key classes:** `DeckTracker`, `DeckTracker.CardPanel`, `DeckTracker.HandPanel`.

## Settings

All features can be toggled on/off individually.

- **In-game menu:** Press **F1** to open/close the settings panel (styled Godot UI overlay on `CanvasLayer` 10, z-index 200). The panel is draggable, supports click-outside-to-close, and has a close button. Position is remembered between opens.
- **Persistence:** Settings are saved as a JSON file of `bool` values at `<user_data_dir>/betterspire2_settings.json`.
- **Defaults:** All features are **enabled** by default.

### Settings file format
```json
{
  "MultiHitTotals": true,
  "PlayerDamageTotal": true,
  "ShowExpectedHp": true,
  "HoldRToRestart": true,
  "SkipSplash": true,
  "ScaleToActivePlayers": true,
  "ShowTeammateHand": true
}
```

## Project Structure

```
DamageCounter/
  DamageCounter.sln              # Solution file
  DamageCounter/
    DamageCounter.csproj         # Project file (outputs BetterSpire2.dll)
    Program.cs                   # Entry point, core classes, multiplayer
    Patches.cs                   # All Harmony patches
    DamageTracker.cs             # Incoming damage simulation + labels
    DeckTracker.cs               # F3 teammate hand viewer
    RestartTracker.cs            # Hold-R restart logic
    SettingsMenu.cs              # F1 settings panel
    ModSettings.cs               # Settings persistence (JSON)
    ModLog.cs                    # File logger
```

### Key source files

| File / Class                   | Description                                      |
|--------------------------------|--------------------------------------------------|
| `ModLog`                       | File logger (writes to betterspire2_log.txt)     |
| `ModSettings`                  | Static settings with JSON load/save              |
| `SettingsMenu`                 | F1 toggle panel (Godot UI) + party section       |
| `DeckTracker`                  | F3 hand viewer with card tooltips + pagination   |
| `PartyManager`                 | Multiplayer: mute drawings, kick, clear drawings |
| `MuteDrawingsPatch`            | Block drawings from muted players (manual patch) |
| `MuteClearDrawingsPatch`       | Block clear from muted players (manual patch)    |
| `ModEntry`                     | Entry point - loads settings, applies patches    |
| `IntentLabelPatch`             | Multi-hit total label (Harmony postfix)          |
| `DamageTracker`                | Player/pet damage simulation + label rendering   |
| Recalc/Hide patches            | Triggers for DamageTracker updates and cleanup   |
| `RestartTracker`               | Hold-R restart logic + async restart flow        |
| `InputPatch`                   | F1/F3 menu, PgUp/PgDn, hold-R input handling    |
| `SkipSplashPatch`              | Splash screen skip                               |

## Build & Deployment

### Prerequisites
- .NET 9.0 SDK
- Slay the Spire 2 installed (for `sts2.dll` reference)

### Dependencies (NuGet)
- `GodotSharp` 4.4.0 - Godot engine bindings (the game runs on Godot)
- `Lib.Harmony` 2.4.2 - Runtime method patching
- `MonoMod.Core` 1.2.3 - Low-level runtime detours (required by Harmony on ARM64)

### Game DLL reference
The project references the game assembly directly via the `Sts2Dir` MSBuild property in `DamageCounter.csproj`. Update this property to match your Steam install path:
```xml
<Sts2Dir>G:\STEAM\Install\steamapps\common\Slay the Spire 2</Sts2Dir>
```

### Build output
- Assembly name: `BetterSpire2.dll`
- On build, the `CopyToMods` target automatically copies the DLL to `$(Sts2Dir)\mods\` if the mods folder exists.

### Entry point
The game's mod loader calls `ModEntry.Init()` (via `[ModInitializer("Init")]`), which:
1. Initializes file logging
2. Loads settings from disk
3. Creates a Harmony instance (`com.jdr.betterspire2`)
4. Applies each patch class individually with try/catch (one failing patch doesn't break others)
5. Manually patches drawing-related methods via `harmony.Patch()` for cross-platform compatibility

## Harmony Patches Summary

| Patch Class                | Target                              | Type    | Purpose                              |
|----------------------------|-------------------------------------|---------|--------------------------------------|
| `IntentLabelPatch`         | `NIntent.UpdateVisuals`             | Postfix | Append multi-hit totals to labels    |
| `RecalcOnRefreshIntentsPatch` | `NCreature.RefreshIntents`       | Postfix | Recalculate damage tracker           |
| `RecalcOnEndTurnPatch`     | `CombatManager.SetReadyToEndTurn`   | Postfix | Recalculate damage tracker           |
| `RecalcPeriodicPatch`      | `NIntent._Process`                  | Postfix | Recalculate every 250ms             |
| `HideOnResetPatch`         | `CombatManager.Reset`              | Postfix | Hide damage labels                   |
| `HideOnWinPatch`           | `CombatManager.EndCombatInternal`   | Prefix  | Hide damage labels on victory        |
| `HideOnLosePatch`          | `CombatManager.LoseCombat`         | Prefix  | Hide damage labels on defeat         |
| `InputPatch`               | `NGame._Input`                      | Postfix | F1/F3 menus, PgUp/PgDn, hold-R restart |
| `SkipSplashPatch`          | `NGame.LaunchMainMenu`             | Prefix  | Set `skipLogo = true`                |
| `MuteDrawingsPatch`*       | `NMapDrawings.HandleDrawingMessage` | Prefix  | Block drawings from muted players    |
| `MuteClearDrawingsPatch`*  | `NMapDrawings.HandleClearMapDrawingsMessage` | Prefix | Block clear from muted players |

\* Patched manually (not via `[HarmonyPatch]` attributes) for cross-platform compatibility.

## Key Game APIs Used

- **`CombatManager.Instance.DebugOnlyGetState()`** - Access current combat state (enemies, player creatures)
- **`AttackIntent.GetSingleDamage()` / `GetTotalDamage()`** - Calculate damage values per intent
- **`RunManager.Instance`** + `Traverse` - Access run state (character, acts, modifiers, ascension)
- **`NGame.Instance.ReturnToMainMenu()`** - Async return to menu
- **`NGame.StartRun()`** - Start a new run (invoked via reflection)
- **`Player.CreateForNewRun()`** / `RunState.CreateForNewRun()`** - Create new run objects
- **`NCombatRoom.Instance.GetCreatureNode()`** - Get the Godot node for a creature (for label positioning)
- **`TaskHelper.RunSafely()`** - Fire-and-forget async task runner from the game
- **`Creature.GetPower<T>()`** / `GetPowerInstances<T>()` - Get debuff powers on a creature (for damage calculation)
- **`PowerModel.Amount`** - The stacked amount of a power (used as damage value for Constrict, Demise, MagicBomb, Disintegration)
- **`PlayerCombatState.Hand.Cards`** - Cards currently in the player's hand (for end-of-turn card damage)
- **`CardModel.HasTurnEndInHandEffect`** - Flag indicating a card has an end-of-turn effect while in hand
- **`CardModel.DynamicVars["Damage"].BaseValue`** - The base damage value of a card's damage variable
- **`CardModel.DynamicVars["HpLoss"].BaseValue`** - The base HP loss value (unblockable, e.g. Beckon)
- **`PlayerCombatState.OrbQueue.Orbs`** - List of channeled orbs (for frost orb passive block calculation)
- **`OrbModel.PassiveVal`** - The passive value of an orb (returns `decimal`, cast to `int`); for `FrostOrb` this is the block amount
- **`Creature.MaxHp`** / `Monster.IsHealthBarVisible` - Used to filter out non-hittable pets (e.g. Pael's Legion)
- **`NHoverTipSet.CreateAndShow()`** / `NHoverTipSet.Remove()` - Game's native tooltip system (used by DeckTracker for card tooltips)
- **`HoverTip` record struct** - Tooltip data container; set via boxed reflection since constructors require `LocString`
- **`CardModel.Title` / `CardModel.Description`** - Card name and description text (via `GetFormattedText()`)

## Multiplayer Support

All features work in multiplayer. Each player sees their own damage numbers calculated independently based on their own block, pet, and debuffs. Both players need the mod installed, but versions don't need to match.

### Party Management (F1 → Party)

In multiplayer, the F1 settings menu includes a Party section showing all players by Steam name and character class. From here you can:
- **Hide Drawings** — Block another player's map drawings from appearing on your screen. Also clears their existing drawings when toggled.
- **Clear All Drawings** — Wipe all drawings from the map at once.
- **Kick (Host only)** — Remove a player from the session.

### Key multiplayer implementation details

- **`PartyManager`** — Manages per-player drawing mute state, kick via `INetHostGameService.DisconnectClient`, and drawing clear via reflection on `NMapDrawings` private methods.
- **`MuteDrawingsPatch` / `MuteClearDrawingsPatch`** — Harmony Prefix patches on `NMapDrawings.HandleDrawingMessage` and `HandleClearMapDrawingsMessage`. Patched manually (not via attributes) for cross-platform compatibility.
- **Steam names** — Resolved via `PlatformUtil.GetPlayerName(PlatformType.Steam, netId)`.

## Updating for New Game Versions

Things most likely to break on a game update:

1. **Method signatures changed** - Any of the patched methods could be renamed, have parameters added/removed, or be refactored. Check the Harmony patches table above against the new `sts2.dll`.
2. **`DebugOnlyGetState()` removed** - This is a debug API that could be removed. The damage tracker depends on it entirely.
3. **`NGame.StartRun` signature changed** - The restart feature calls this via reflection; any parameter changes will break it silently.
4. **`RunState` / `Player` constructor changes** - The restart feature creates these manually.
5. **Godot version bump** - The project pins `GodotSharp 4.4.0`. If the game upgrades Godot, update this dependency.
6. **Intent rendering changes** - If intent label structure changes (e.g., `MegaRichTextLabel` replaced), the multi-hit patch will need updating.
7. **Pet/creature hierarchy changes** - The damage simulation assumes `Creature.Pets` exists and that pet absorbs damage before player.
8. **New end-of-turn damage powers added** - Powers are hardcoded (Constrict, Demise, MagicBomb, Disintegration). If a new power deals damage at end of turn, it must be added manually to `DamageTracker.Recalculate()`. Search the DLL for `AfterTurnEnd` or `AfterTurnEndLate` in the `Powers` namespace to find candidates. End-of-turn hand cards are handled generically and don't need updating.

### Debugging tips
- All errors are logged to `<user_data_dir>/betterspire2_log.txt` with timestamps and stack traces
- The log also records OS info, patch success/failure counts, and settings load status on startup
- All patches have `try/catch` blocks that log errors without crashing the game
- Use `GD.Print()` / `GD.PrintErr()` for debug logging (shows in Godot console)

## Known Issues

- **Mac (ARM64 / Apple Silicon)** is currently unsupported. The game's Harmony/MonoMod runtime does not function on macOS ARM64 — `MonoMod.Core.dll` is missing from the Mac distribution and the native interop layer is absent. All Harmony patches fail with `NotImplementedException`. This is a game engine issue affecting all Harmony-based mods.
- **Linux** is currently unsupported. MonoMod's native detour helper (`mm-exhelper.so`) fails to load at runtime with `undefined symbol: _Unwind_RaiseException`, likely due to a missing libgcc/libunwind linkage in the Steam Runtime environment. All Harmony patches fail with `DllNotFoundException`. This is a game engine issue affecting all Harmony-based mods.
- Both platforms will work automatically once the developers patch the game's Harmony/MonoMod runtime.
