# Nugget Blocks — Godot Integration (Input, Rendering, Effects, Audio)

## Context

The Nugget block simulation is fully implemented and tested (266 xUnit tests, 14 commits on main). The design spec is at `docs/superpowers/specs/2026-04-23-nugget-blocks-design.md` — read it first, especially Sections 3 (Lifecycle), 4.2 (Commands), 8 (HUD), and 9 (Visuals).

A basic map editor placement and minimal static rendering were added, but the full Godot integration is missing. This prompt covers everything needed to make nuggets fully playable and visually complete.

## What's already done

### Simulation (complete)
- `BlockType.Nugget`, `NuggetState`, `NuggetType` — data model
- `NuggetSystem` — mining, capture, consumption (heal/fortify/nest refine), auto-rally
- `CommandType.MineNugget`, `HealWithNugget`, `FortifyWithNugget` — processed in `GameState.ExecuteCommand`
- Combat exclusions in CombatSystem, EliminationSystem, StunSystem (fortified walls), JumperSystem
- NestSystem nugget-required gating + NuggetLoaded
- StateHasher covers all nugget state
- CommandSerializer round-trips all nugget commands
- 9 VisualEventTypes emitted: NuggetMiningStarted, NuggetFreed, NuggetCaptured, NuggetRefineConsumed, NuggetHealConsumed, NuggetFortifyConsumed, CommandMineIssued, CommandHealIssued, CommandFortifyIssued

### Godot (minimal)
- Map editor: Nugget button in "Resources" section of `EditorToolbar.cs`, placed as neutral (PlayerId=-1)
- `SpriteFactory.cs`: Nugget in types array, white/silver base color, neutral sprite cached at key (-1)
- `BlockIconPainter.cs`: Nugget case with white diamond
- `GridRenderer.Blocks.cs`: Static nugget rendering — silver body + diamond overlay (team-colored when mined, white when unmined). Method: `DrawNuggetDiamond`

## What's missing — organized by system

### 1. Input: SelectionManager (nugget commands)

**File:** `godot/Scripts/Input/SelectionManager.Commands.cs`

The current `HandleRightClick` always emits `CommandType.Move`. It needs context-aware right-click:

- **Builder(s) selected → right-click unmined nugget**: Emit `CommandType.MineNugget` with builder IDs and nugget position as TargetPos. Builders should pathfind to adjacent cells.
- **Mined nugget selected → right-click damaged friendly soldier/jumper**: Emit `CommandType.HealWithNugget` with nugget ID and target block position.
- **Mined nugget selected → right-click friendly wall**: Emit `CommandType.FortifyWithNugget` with nugget ID and target wall position.
- **Mined nugget selected → right-click empty cell**: Normal `CommandType.Move` (already works).

**File:** `godot/Scripts/Input/SelectionManager.Input.cs`

Selection filtering:
- Line 284: `block.PlayerId == ControllingPlayer && block.Type != BlockType.Wall` — unmined nuggets have PlayerId=-1 so they're already excluded from selection. Mined nuggets owned by the player WILL be selectable. This is correct per spec.
- Line 307: Box select also filters by PlayerId — same logic, correct.
- Backtick quick-select (line 189-194): Already filters to Soldier/Stunner only — nuggets excluded. Correct per spec.

### 2. GridRenderer: Animated rendering (spec Section 9)

**File:** `godot/Scripts/Rendering/GridRenderer.Blocks.cs`

The current `DrawNuggetDiamond` is static. Needs:

**9.1 Unmined Idle**: Subtle prismatic shimmer effect — cycle hue slightly over time on the diamond/body. Faint ambient glow (radial, ~15% alpha, cool white). Gentle sparkle: a few small bright dots that fade in/out at random positions within the block rect.

**9.2 Mining Active**: Check `block.NuggetState is { IsMined: false } && block.PlayerId != -1` to detect active mining. Vibrate/shake: offset the block rect by a small random amount that grows with `MiningProgress / NuggetMiningTicks`. Draw cracks of light: thin bright lines that grow with progress (visual progress indicator — no UI bar needed). Intensify the shimmer.

**9.4 Freed Moving**: When `block.MoveTarget.HasValue`, draw trailing sparkle particles behind the movement direction.

**Fortified wall overlay**: For any wall with `block.FortifiedHp > 0`, draw a small diamond sparkle overlay on the wall. This is a persistent indicator, not a one-shot effect.

### 3. EffectManager: Visual event handlers

**File:** `godot/Scripts/Rendering/EffectManager.cs`

9 new `VisualEventType` values are emitted by the simulation but have no handlers. Add cases to the switch in the event processing method:

| Event | Effect (spec section) |
|---|---|
| `NuggetFreed` (9.3) | Radial light burst (white/prismatic). Grid lightning burst outward. Use existing `EffectFactory.LightningBurst`. |
| `NuggetCaptured` (9.8) | Short sharp grid lightning burst at capture point. Brief particle burst. |
| `NuggetRefineConsumed` (9.5) | ConvergingDrain effect centered on nest (use existing `EffectFactory.LightningConverge` or similar). Nest glows briefly. |
| `NuggetHealConsumed` (9.6) | Particles flow into healed unit position. Brief restoration glow/aura. |
| `NuggetFortifyConsumed` (9.7) | Grid lightning connecting fortified walls briefly. |
| `CommandMineIssued` | Command receipt feedback (similar to existing command events). |
| `CommandHealIssued` | Command receipt feedback. |
| `CommandFortifyIssued` | Command receipt feedback. |
| `NuggetMiningStarted` | Subtle start indicator (optional — mining progress is shown by GridRenderer). |

Also update the `IsPrivateEvent` check (line ~125) — `CommandMineIssued`, `CommandHealIssued`, `CommandFortifyIssued` should be private (only visible to the issuing player).

### 4. AudioManager: Sound mappings

**File:** `godot/Scripts/Audio/AudioManager.cs`

Add nugget events to:
- `PrivateEvents` HashSet (~line 27): Add `CommandMineIssued`, `CommandHealIssued`, `CommandFortifyIssued`
- Sound mapping switch (~line 151): Map nugget events to sounds. For now, reuse existing sounds or map to `null` — the spec says "hook up event for X (OGG provided by jjack)" meaning sound files will be provided later. Just wire the events so they don't silently drop.

### 5. HUD: Nugget count display

**Spec Section 8**: Top bar should show owned (freed) nugget count per player. Nugget icon + count.

Check how the existing HUD displays population or other per-player stats and add a nugget counter alongside. Count: `state.Blocks.Count(b => b.Type == BlockType.Nugget && b.NuggetState?.IsMined == true && b.PlayerId == playerId)`.

### 6. Death effect: White/prismatic variant

**Spec Section 9.9**: When a nugget is destroyed (blast ray, jumper), the existing death animation should play but with white/prismatic fragments instead of team-colored. Check `GridRenderer` or wherever the death/inflation→explosion effect is drawn and add a color override for `BlockType.Nugget`.

## Key implementation notes

- **PlayerId = -1**: Unmined nuggets are neutral. `GetPalette(-1)` returns the default palette (safe). Selection filters already exclude non-owned blocks. But watch for any code that assumes PlayerId >= 0.
- **Determinism boundary**: All rendering code is read-only on GameState. Effects and animations are purely visual — they don't affect simulation. Particle positions can use floats freely.
- **Existing effect infrastructure**: `EffectFactory` has `LightningBurst`, `LightningConverge`, `SpiralTrace`, `CrossContract`, `CellPerimeter` — reuse these for nugget effects rather than building new systems.
- **Testing**: Launch Godot, open map editor, place nuggets, save map, start game, mine with builders, verify all visual events fire and effects display. Test heal/fortify/capture in-game.

## Suggested task order

1. **Input first** — wire SelectionManager so nugget commands can be issued. Without this, nothing else is testable in-game.
2. **EffectManager handlers** — wire the 9 visual events to existing effect primitives. This makes events visible immediately.
3. **AudioManager** — wire events (even to placeholder/null sounds).
4. **GridRenderer animations** — shimmer, mining progress, sparkles, fortified wall overlay.
5. **HUD** — nugget count display.
6. **Death effect** — white/prismatic variant.
7. **Polish** — cursor changes, particle trails, refine implosion.
