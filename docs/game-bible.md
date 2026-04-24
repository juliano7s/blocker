# Blocker — Game Bible

> This document fully describes the game. Someone who has never seen the original TypeScript codebase can implement the game from this document alone. It is the authoritative design reference for the Godot 4 + C# reimplementation.

---

## 1. Vision

A minimalist, fast-paced RTS where two armies expand across a grid through formation-based tactics and real-time spatial control. No tech trees, no resource meters — everything is physical: blocks move, nest formations spawn units, stun rays freeze enemies, push waves displace blocks, and walls block line-of-sight. The game is decided by map control, economy timing, and formation placement. Victory comes from destroying your opponent's army and eliminating their ability to rebuild.

## 2. Core Concept

Everything is a block on a grid. Units are blocks. Buildings are blocks. The economy is blocks. The entire game state is readable at a glance through color and position. There is no UI for resources, no tech tree menu — everything is physical, spatial, and visible on the board.

Up to 6 players compete on a shared grid, each with a unique color. Players are organized into teams (see Section 12). All players act simultaneously in real-time, and the board resolves on each tick.

---

## 3. Grid and Simulation

### 3.1 Grid

- The game world is a rectangular grid of square cells.
- Standard grid size: **41 wide x 25 tall** (maps can vary).
- Coordinate system: `grid[y][x]` — row-major, origin at top-left.
- Each cell can contain at most one block plus ground properties.

### 3.2 Ticks

- The simulation runs in discrete **ticks** — not turn-based, both players act simultaneously in real-time.
- **Default tick rate**: 12 ticks per second (local and networked). Configurable 4-20 tps.
- Each tick, all blocks act according to their type's rules in a fixed resolution order (see Section 14).
- All timing in this document is expressed in ticks. At 12 tps: 12 ticks = 1 second, 120 ticks = 10 seconds.

### 3.3 Cells

Each cell has:
- **Ground type**: Normal, Boot, Overload, Proto, Terrain, Breakable Wall, Fragile Wall
- **Block**: Optional — at most one block can occupy a cell at any time

---

## 4. Block Types

There are 6 block types organized in a tech tier system, plus 2 variant units spawned from overload ground, plus 1 resource block (Nugget).

### 4.1 Tier 1: Builder

The basic block. The workhorse of the economy.

| Property | Value |
|----------|-------|
| Move speed | Normal (every 3 ticks) |
| Pop cost | 1 |
| Spawned from | Builder Nest |
| Combat role | Vulnerable — killed by standard surrounding or 1 adjacent Soldier |

**Abilities:**
- **Root** (F key): Locks in place. Takes 36 ticks to fully root. Required to participate in formations.
- **Convert to Wall** (W key): Permanent, irreversible conversion to Wall. Only available when fully rooted. Pressing W while rooting queues the convert — it fires automatically when fully rooted.
- **Push** (G key): When fully rooted and not part of a formation, fires push waves in a chosen direction (see Section 9).

### 4.2 Tier 2: Wall

The defensive block. Permanent and immovable.

| Property | Value |
|----------|-------|
| Move speed | Immobile |
| Pop cost | 0 |
| Created by | Builder conversion (irreversible) |
| Combat role | **Immune** to all standard combat — only killed by stun rays and Soldier Tower blasts |

**Role:**
- Protects boot zones, creates chokepoints
- Required ingredient for Soldier Nests, Stunner Nests, and Supply Formations
- Blocks stun rays and push waves (line-of-sight blocker)

### 4.3 Tier 2: Soldier

The melee specialist. Slow but lethal at close range.

| Property | Value |
|----------|-------|
| Move speed | Slow (every 4 ticks) |
| Pop cost | 1 |
| HP | 4 (loses 1 HP per kill; dies at 0) |
| Spawned from | Soldier Nest |
| Combat role | Melee killer — adjacency-based kills with type-specific thresholds |

**Abilities:**
- **Root** (F key): Can root to participate in Stunner Nests or Soldier Towers.
- **Self-Destruct** (D key): When fully rooted, fires kill blasts in all 8 directions (range 3 cells). Destroys the Soldier.

### 4.4 Tier 3: Stunner

The ranged crowd-control unit. Fast and fragile.

| Property | Value |
|----------|-------|
| Move speed | Fast (every 2 ticks) |
| Pop cost | 3 |
| Spawned from | Stunner Nest |
| Combat role | Ranged stun + Wall killer |

**Abilities:**
- **Stun Ray** (S key): Fires 3 rays in a cardinal direction. Range: 5 cells. Stuns all enemies hit for 160 ticks (~13.3s). Walls are **killed** instead of stunned and stop the ray. Ray stops at terrain, walls, and formations, but **pierces** through multiple non-wall enemy units.
- **Cooldown**: 140 ticks after firing — **cannot move** while on cooldown.
- **Root** (F key): Can root to participate in Stun Towers.
- **Self-Destruct** (D key): When fully rooted, fires stun blasts in all 8 directions (range 4 cells). Destroys the Stunner.

### 4.5 Variant: Warden (Overload Builder)

Spawned from Builder Nests on overload ground instead of normal Builders.

| Property | Value |
|----------|-------|
| Move speed | Normal (every 3 ticks) |
| Pop cost | 2 |
| Spawned from | Builder Nest on Overload ground |
| Combat defense | 1 Soldier kills uprooted; 2 Soldiers kill rooted |

**Abilities:**
- **Zone of Control**: Passive aura. Enemies within 4-cell Chebyshev radius move at half speed (their move interval is doubled).
- **Magnet Pull** (D key, fully rooted): Pulls all uprooted enemy blocks within 4-cell radius as close as possible, also diagonally, toward the Warden. Cooldown: 140 ticks.

### 4.6 Variant: Jumper (Overload Soldier)

Spawned from Soldier Nests on overload ground instead of normal Soldiers.

| Property | Value |
|----------|-------|
| Move speed | Normal (every 3 ticks) |
| Pop cost | 2 |
| HP | 3 (missed jumps cost 1 HP; dies instantly at 0) |
| Spawned from | Soldier Nest on Overload ground |
| Combat defense | 1 Soldier kills Jumper (fragile) |

**Abilities:**
- **Jump** (F key → aim → left-click): Leaps up to 5 cells in a cardinal direction, killing all blocks in the path (friendlies and enemies alike). Stops at terrain, formations, map edges, and neutral walls (breakable → fragile on first hit; fragile → destroyed on second).
- **Combo**: If the jump kills at least one unit (enemy or friendly) and doesn't hit an obstacle, the Jumper enters **combo-ready** state — can jump again immediately without moving. Issuing a move command consumes the combo and starts a **mobile cooldown** (can move but can't jump).
- **Cooldown**: 120 ticks. If no combo: immobile during cooldown. If combo consumed by moving: mobile but can't jump.
- **HP loss**: Missed jumps (non-combo, triggering immobile cooldown) cost 1 HP. The visual icon shrinks with HP loss (100% → 60% → 20%).
- **Warden counter**: Cannot initiate a jump while inside an enemy Warden's Zone of Control. Can jump *into* a ZoC.

### 4.7 Resource: Nugget

Mineable resource block. Adds strategic diversity — players decide how to allocate nuggets across spawning, healing, and fortification.

| Property | Value |
|----------|-------|
| Move speed | Normal (every 3 ticks, mined only) |
| Pop cost | 0 |
| Placed by | Map designer (finite, no respawning) |
| Combat role | Non-combatant — immune to surrounding, soldier adjacency, stun |

**Lifecycle:**
- **Unmined**: Neutral (`PlayerId = -1`), not selectable. Occupies cell, blocks movement. Prismatic shimmer visual with diamond shape. Destroyed by blast rays; stops jumper jumps (obstacle).
- **Mining**: Builder right-clicks to mine. Progress = adjacent miners per tick. Base time: 180 ticks (~15s with 1 builder). `PlayerId` updates to mining team. Only one team can mine at a time.
- **Freed**: `IsMined = true`. Fully owned, selectable, commandable. Auto-rallies to nearest friendly nest unless manually moved (Move command permanently disables auto-rally until captured).
- **Consumption**: Three paths, nugget removed on use:
  - *Nest Refine*: Within 3 Chebyshev distance of friendly nest → auto-consumed, grants spawn progress bonus.
  - *Heal Unit*: Right-click damaged soldier/jumper → pathfinds adjacent, heals to full HP on arrival.
  - *Fortify Walls*: Right-click friendly wall → pathfinds adjacent, grants `FortifiedHp` to target + 4 connected walls (BFS).
- **Capture**: Enemy builder orthogonally adjacent (no friendly builder contesting) → instant ownership flip, auto-rally retargets.

**Fortified Walls:**
- `FortifiedHp` absorbs stun ray hits (decrements instead of destroying). When 0, wall is normal again.
- Default `FORTIFIED_WALL_HP = 3`. Affects 5 walls per nugget (target + 4 BFS-connected).

---

## 5. Combat

### 5.1 Standard Surrounding

A block is killed when any of these conditions are met:
- **3+ orthogonal enemies** adjacent (up/down/left/right), OR
- **2 orthogonal enemies + 2 friendly neighbors** (overcrowding — friendlies block escape), OR
- **2 orthogonal enemies + 1 diagonal enemy**

**Important**: Only **Soldiers** count as "enemies" for surrounding thresholds. Builders, Stunners, Walls, Jumpers, and Wardens do not contribute to the enemy count.

**Walls are immune** to all standard surrounding.

### 5.2 Soldier Adjacency Kills

Soldiers kill adjacent enemies based on how many Soldiers are touching the target:

| Target | Soldiers needed | Direction check |
|--------|-----------------|-----------------|
| Uprooted Builder | 1 | Orthogonal |
| Uprooted Soldier | 1 (mutual kill — both die) | Orthogonal |
| Uprooted Warden | 1 | Orthogonal |
| Jumper | 1 | Orthogonal |
| Rooted Builder or Stunner | 2 | All 8 directions |
| Fully rooted Soldier | 2 | All 8 directions |
| Rooted Warden | 2 | All 8 directions |
| Formation member | 3 | Orthogonal |
| Rooted Stunner | 3 | Orthogonal |

- Surviving Soldiers lose 1 HP per enemy killed. A Soldier at 0 HP dies.
- **Walls are immune** to Soldier adjacency kills.

### 5.3 Neutral Obstacles

Two destructible neutral obstacle types:

**Breakable Wall** (`~`):
- Blocks all movement and ray line-of-sight, like terrain.
- Two-hit destruction: first hit (stun ray, blast ray or Jumper) converts to Fragile Wall. Second hit destroys it.

**Fragile Wall** (`=`):
- Blocks all movement and ray line-of-sight.
- Destroyed by: stun ray, Jumper jump, or 2+ adjacent Soldiers (any player, all 8 directions).

---

## 6. Economy: Nest Zones and Spawning

### 6.1 Nest Zones

Certain cells are **nest-capable zones** (visually highlighted). Three types:

| Zone | Visual | Effect |
|------|--------|--------|
| Boot ground | Dark green | Standard spawn rate |
| Overload ground | Purple | Spawns variant units (Warden from Builder Nest, Jumper from Soldier Nest). Stunner Nests unaffected. |
| Proto ground | Faint dashed | 5x slower spawn rate |

### 6.2 Nest Types

Nests are formations that spawn units. Created by rooting specific patterns of blocks around a nest zone cell.

| Nest | Formation | Spawns (Boot/Proto) | Spawns (Overload) | Spawn Time | Overload Time |
|------|-----------|---------------------|--------------------|------------|---------------|
| Builder Nest | 3 Builders rooted orthogonal to center | Builder | **Warden** | 140 ticks | 220 ticks |
| Soldier Nest | 3 Builders + 2 Walls (5-block cross) | Soldier | **Jumper** | 260 ticks | 300 ticks |
| Stunner Nest | 3 Soldiers + 2 Walls (5-block cross) | Stunner | Stunner | 360 ticks | 360 ticks |

Proto ground multiplies all spawn times by 5x.

**5-block pattern** (Soldier and Stunner Nests):
```
[a][w]
   [a]   <-- nest zone cell at center
[a][w]
```
Where `[a]` = main unit (Builder or Soldier) and `[w]` = Wall. The two Walls occupy diagonal positions relative to the center.

### 6.3 Automatic Nest Upgrade

A Builder Nest upgrades automatically to a Soldier Nest when 2 free Walls appear in the correct diagonal positions — no need to dissolve and reform.

### 6.4 Spawn Mechanics

- Spawn progress accumulates each tick while required members are present and owned.
- Progress **pauses** if any nest member is stunned.
- Progress **resets** if ownership changes or a member leaves.
- Newly spawned blocks auto-move one cell away from the center so they don't block production.
- Spawning is **blocked** by the population cap (see Section 8).

### 6.5 Nest Congestion

Economy is entirely physical and visible. When the center cell is occupied, spawning searches outward (BFS up to 3 cells) for the nearest free cell. If no free cell exists within range, spawn progress holds and retries each tick. Congestion delays production but doesn't permanently block it — however, a heavily congested nest spawns units further from the center, which is strategically disadvantageous.

---

## 7. Formations (Non-Nest)

Formations are created by rooting specific patterns of blocks. Members cannot move while participating.

**Formation dissolution** has two paths:
- **Member killed** (combat, stun ray, jump): If membership drops below minimum, the formation **dissolves instantly** — remaining members are freed immediately.
- **Member uproots voluntarily** (player toggles root): The formation enters **TearingDown** state (24 ticks) before dissolving, giving time for the player to reconsider.

### 7.1 Rooting Timing

| Action | Duration |
|--------|----------|
| Fully root | 36 ticks |
| Fully uproot | 24 ticks |

### 7.2 Stun Tower

- **Creation**: Player selects a fully rooted Stunner and presses T. The Stunner becomes the center, and one adjacent fully rooted Builder (same owner) joins as the direction arm. If multiple adjacent Builders exist, the first found in sweep order is chosen. Only one Builder is used — towers are single-direction at creation.
- **Adding/removing arms**: Additional adjacent rooted Builders can join the tower, adding fire directions. If a Builder member is uprooted or killed, its direction is removed and `builderDirections` is recomputed from remaining members. The tower dissolves if the center Stunner is lost or no Builders remain.
- **Firing behavior**: Scans builder directions for enemies. When an enemy is found, begins a **firing cycle** that sweeps through all builder directions sequentially. Fires 3 parallel rays (center + 2 perpendicular side rays) in the current direction every 16 ticks, then advances to the next direction. Only starts a new cycle when the previous one finishes and an enemy is detected.
- Range: 4 cells. Kills Walls, stuns others for 160 ticks.
- Does **not** spawn blocks.

### 7.3 Soldier Tower

- **Creation**: Same as Stun Tower but with a fully rooted Soldier as center (T key). Single Builder arm at creation.
- **Adding/removing arms**: Same dynamic builder management as Stun Tower.
- **Firing behavior**: Scans all builder directions for enemies (line-of-sight blocked by walls, terrain, breakable/fragile walls). When any enemy is detected, fires kill blast rays in **all** builder directions simultaneously. Fire interval: 12 ticks.
- Blast range: 5 cells. Kills non-Wall, non-formation enemy blocks. Stops at walls.
- Does **not** spawn blocks.

### 7.4 Supply Formation

- **Pattern**: 3 free Walls in an L-shape (1 corner Wall + 2 perpendicular Wall neighbors).
- **Effect**: Each Supply Formation adds **+7 to the population cap**.
- The only way to increase population capacity.

---

## 8. Population Cap

| Property | Value |
|----------|-------|
| Base cap | 0 (no Supply = no spawning) |
| Per Supply Formation | +7 |
| Builder cost | 1 |
| Wall cost | 0 |
| Soldier cost | 1 |
| Stunner cost | 3 |
| Warden cost | 2 |
| Jumper cost | 2 |

Spawning is blocked when `currentPop + spawnCost > maxPop`.

---

## 9. Push Mechanic

A fully rooted Builder (not part of a formation) can toggle push mode (G key, direction snapped to mouse angle).

| Property | Value |
|----------|-------|
| Wave interval | Every 8 ticks |
| Wave range | 4 cells |
| Max displacement | 3 cells per push |

- Waves stop at terrain, Walls, rooted blocks, formations, map edges.
- **Chain push**: Pushes adjacent mobile blocks in sequence (back-to-front) up to map boundaries.
- **Two-cell hit zone**: Wave checks head cell and next cell simultaneously to prevent units slipping between waves.
- Affects **both friendly and enemy** blocks.

---

## 10. Controls and Input

### 10.1 Selection

- **Left-click**: Select a single block (own blocks only, excludes Walls).
- **Left-click + drag (box select)**: Select multiple own blocks in the rectangle. Mobile-priority filter: if movable blocks exist in the box, only those are selected.
- **Shift+click / Shift+drag**: Add to current selection.
- **Double-click**: Select all blocks of the same type AND rooting state, visible on screen.
- **Ctrl+click**: Select all blocks of the same type AND rooting state on screen, visible on screen.
- **Backtick (`` ` ``)**: Quick-select all uprooted Soldiers and Stunners (mobile combat force).
- **Control groups (0-9)**: Press number to select group. Ctrl+number to assign current selection. Double-tap to center camera on group.
- **Escape**: Clear selection (or cancel active mode).
- **Tab**: Switch between players in local mode.

### 10.2 Movement

- **Right-click**: Move selected blocks to target cell. Clears command queue.
- **Shift+right-click**: Queue a move waypoint (dotted path shown for own blocks).
- **Right-click + drag (paint mode)**: Drag paints target cells; on release, units are distributed via closest-first greedy Manhattan distance assignment. Shift+right-drag queues as waypoints.
- **A key (Attack-move)**: Press A, then left-click target. Blocks move toward target but pause when adjacent to enemies.

### 10.3 Abilities

| Key | Action | Condition |
|-----|--------|-----------|
| F | Root/Uproot toggle | Builder, Soldier, Stunner, Warden |
| F | Jump (direction toward mouse) | Jumper |
| W | Convert to Wall | Builder (queues if still rooting) |
| S | Fire Stun Ray | Stunner (per-unit direction snap toward mouse) |
| G | Toggle Push | Rooted Builder, not in formation (per-unit direction snap) |
| D | Self-Destruct | Rooted Soldier or Stunner |
| D | Magnet Pull | Rooted Warden |
| T | Create Tower | Rooted Soldier or Stunner (needs adjacent rooted Builder) |

All ability keys support **Shift+key** to queue the action after the current command. Commands are filtered to relevant block types only (e.g., S only affects Stunners in a mixed selection). Direction-based commands snap per-unit from each block's position to the mouse.

### 10.4 Blueprint Mode

Six formation blueprints available via number keys:

| Key | Blueprint | Units Needed |
|-----|-----------|-------------|
| 1 | Builder Nest | 3 Builders |
| 2 | Soldier Nest | 3 Builders + 2 Walls |
| 3 | Stunner Nest | 3 Soldiers + 2 Walls |
| 4 | Supply Formation | 3 Walls (L-shape) |
| 5 | Stun Tower | 1 Stunner + 1 Builder |
| 6 | Soldier Tower | 1 Soldier + 1 Builder |

- **R**: Rotate blueprint 90° clockwise.
- **Left-click**: Place blueprint. Units dispatched via closest-first greedy assignment, auto-queuing move → root (and → convert for wall roles). Dispatched units removed from selection.
- **Shift+click**: Place and keep blueprint active for multiple placements.
- **X**: Clear all placed blueprint ghosts.
- **Right-click / Escape**: Cancel blueprint mode.

Ghost previews persist for 15 seconds, fading over time. Gap-filling: cells already occupied by friendly blocks are skipped.

### 10.5 Camera

- **Edge scrolling**: Move camera by pushing cursor to screen edges.
- **Arrow keys / WASD**: Pan camera.
- **Mouse wheel**: Zoom in/out.
- **Minimap** (bottom-right): Click to jump camera. Shows all blocks and terrain.

### 10.6 Command Queueing

Each block has a command queue. Shift+action appends to the queue; non-shift actions are immediate and clear the queue. The queue processes one entry per tick when preconditions are met (idle for move, fully rooted for convert, etc.).

**Auto-chaining**: Some commands implicitly queue a follow-up without requiring Shift. For example, pressing W while a Builder is still rooting auto-queues the Wall conversion — it fires as soon as the root completes. Similarly, Blueprint Mode auto-queues move → root → convert as a single action.

---

## 11. Win Conditions

### 11.1 Elimination

A player is defeated when **all three** conditions are true simultaneously:
1. **No army** — zero Soldiers, Stunners, Wardens, or Jumpers (Walls don't count)
2. **No active nests** — cannot produce new units
3. **Fewer than 3 Builders** — cannot form a new nest

In team modes, a player's defeat does not eliminate the team — the team fights on with remaining players. A **team** is eliminated when all its players are defeated. Last team standing wins.

If all remaining teams meet elimination conditions simultaneously, the team with more total blocks wins.

### 11.2 Surrender

A player can concede at any time. In team modes, a player's surrender eliminates only that player — teammates continue.

---

## 12. Multiplayer

### 12.1 Game Modes and Teams

Up to **6 players**. Two modes implemented:

| Mode | Team assignment | Slot count |
|------|----------------|------------|
| **FFA** | Each slot is its own team (`teamId = slotId`) | 2-6 |
| **Teams** | Consecutive pairs share a team (`teamId = slotId / 2`) | 2, 4, or 6 (must be even) |

The core abstraction is **teams**, not player count. All game rules use `GameState.AreEnemies` / `AreAllies` for hostility checks:

- Combat, ZoC, jumper blocking, stun rays, tower targeting — all team-aware
- Push waves affect all blocks regardless of team (intentional friendly fire)
- Ownership checks (command auth, spawning, formations) use `PlayerId`, not team

### 12.2 Team Win Conditions

A **team** is eliminated when all players on that team meet the elimination criteria (no army, no nests, fewer than 3 Builders). Last team standing wins.

In FFA, each player is their own team — standard elimination rules apply per player.

### 12.3 Architecture: Lockstep

All clients run identical deterministic simulation. Only player commands are exchanged — not game state. The simulation is fully deterministic (no RNG, no floats, grid-based), so all clients produce identical state from identical inputs.

For 2+ player games, networking uses a relay server (WebSocket or reliable UDP) rather than peer-to-peer mesh — simpler topology, single-digit ms overhead for same-region players.

### 12.4 Tick Rate and Responsiveness

- Default: 12 tps (local and networked), configurable 4-20.
- Input delay: 1 tick, adaptive to 2 ticks if RTT > 80ms.
- Visual interpolation between `prevPos` and `pos` keeps movement smooth regardless of tick rate.

### 12.5 Desync Detection

After each tick, clients exchange FNV-1a hashes of the full game state. Hash mismatch = game pauses with diagnostic info.

### 12.6 Online Lobby

- Players connect to a server to browse, host, and join lobbies in real-time.
- Host creates a named lobby, selects map and game mode, assigns teams.
- Players join and pick team slots. Host starts when all slots are filled (or marked as AI).
- Map selection and team configuration happen in the lobby.

### 12.7 Determinism Rules

Any game mechanic **must** follow these rules:
1. No randomness in simulation — deterministic RNG seeded per match is acceptable, but no system-level random
2. No floating-point arithmetic — all positions and distances are integer grid coordinates
3. All player actions flow through the command system and are validated
4. Tick budget: at 12 tps, each tick is ~83ms — keep processing fast
5. No side effects in simulation — only game state mutations

---

## 13. Single Player - ** Let's skip for now and implement after the visuals are done **

### 13.1 Play vs AI

Two scripted AI opponent types ship with the initial release:

- **Easy Bot**: Simple rule-based scripted AI. Prioritizes economy, defensive play.
- **Aggressive Bot**: More aggressive rule-based variant. Pushes early and contests boot zones.

Both run locally — no server required.

> **Deferred**: Trained neural network AI and tutorial system will be designed separately after core gameplay is solid.

---

## 14. Tick Resolution Order

Each tick resolves in this exact order:

1. Clear push flags from previous tick
2. **Formations** — advance root/uproot progress, detect patterns, upgrade nests
3. **Stun Towers** — fire stun rays in sweep cycle
4. **Soldier Towers** — fire blast rays when enemies detected
5. **Stun** — advance stun ray heads, apply stun effects, decay cooldowns
6. **Variant cooldowns** — tick Jumper jump cooldowns, Warden pull cooldowns
7. **Push fire** — rooted Builders fire new push waves
8. **Push waves** — advance wave heads, displace blocks
9. **Command queues** — pop and execute front queued command per block
10. **Warden ZoC** — compute Zone of Control slow fields
11. **Snap prevPos** — store previous positions for interpolation (except pushed/jumped blocks)
12. **Movement** — per-type intervals: Stunners every 2 ticks, Builders/Wardens/Jumpers every 3, Soldiers every 4
13. **Combat** — surrounding kills + Soldier adjacency kills + HP decay
14. **Spawning** — increment nest timers, spawn blocks when ready
15. **Death effects** — decrement and remove expired visual effects
16. **Increment tick**

---

## 15. Map System - ** Let's skip for now and implement after the visuals are done **

Maps in the Godot version will support larger grids than the original 41x25. The map format will be redesigned during Phase 2 (Architecture Design) — it may use Godot's TileMap, a custom binary format, or an editor-native format. The plain-text format below is preserved as reference for what information a map must encode.

### 15.1 Reference Format (from TypeScript version)

The original game uses plain-text files — one character per cell, one line per row. Width and height are inferred from the grid. Two variants exist: single-layer (ground + units mixed) and two-layer (terrain grid + `---` separator + units grid).

### 15.2 Cell Types (what maps must encode)

**Ground types:**

| Char | Meaning |
|------|---------|
| `.` | Empty normal cell |
| `f` | Boot ground — standard spawn rate |
| `o` | Overload ground — variant unit spawning |
| `p` | Proto ground — 5x slower spawn rate |
| `#` | Terrain — impassable, immune to all |
| `~` | Breakable wall — two-hit destructible |
| `=` | Fragile wall — one-hit destructible |

**Player blocks:**

| P0 (lowercase) | P1 (UPPERCASE) | Block |
|-----------------|----------------|-------|
| `b` | `B` | Builder |
| `w` | `W` | Wall |
| `s` | `S` | Soldier |
| `n` | `N` | Stunner |

**Rooted blocks**: `1-4` (P0: Builder/Wall/Soldier/Stunner), `5-8` (P1: same order).

### 15.3 Map Design Principles

- Boot zones distributed to create contested areas (center-biased).
- Both players start on opposite ends with nearby boot zones for initial economy.
- Map symmetry ensures fairness.
- Terrain creates natural chokepoints and flanking opportunities.

---

## 16. Visuals and Rendering

### 16.1 Rendering Style

- 2D top-down grid. Each cell is a square.
- Blocks are rendered as colored squares with type-specific inner details.
- Up to 6 players supported. Each player has a unique color palette (Blue, Red, Yellow, Green + 2 more TBD).
- Smooth movement interpolation between ticks for fluid animation.

**Asset strategy — procedural fallback**: All block visuals have a **procedural rendering** implementation (code-drawn shapes, animations, and effects). PNG sprites can optionally replace the procedural visuals when available, but the game must be fully playable and visually complete without any sprite assets. This is the same pattern used for audio — sound files enhance the experience but the game works without them. The procedural visuals described below are the baseline; sprites are an upgrade layer on top.

Up to 6 players supported. Each player has a unique color palette (Blue, Red, Yellow, Green + 2 more TBD). See Section 12 for multiplayer modes and team system.

### 16.2 Block Visuals by Type

Each block is a colored square with a type-specific inner element:

**Builder**: Sprite-based icon, no procedural animation. Player-colored square.

**Soldier — Spinning Swords**:
- 4 diagonal gold sword arms rotating around center (TL, TR, BL, BR).
- Periodic spin: 500ms ease-in-out rotation every 4 seconds, staggered per block.
- Visible arms indicate HP: 4 HP = all 4, 3 HP = 3 arms, etc. Arms disappear BR → BL → TL → TR.
- **Arm pop animation**: When HP drops, the lost arm flies outward with spinning motion (3.5π spin over 520ms), gold glow trail, cubic ease-out fade.

**Stunner — Diamond Spin**:
- Outer diamond (player color) + inner diamond (white highlight, 50% opacity).
- Same 4-second spin cycle as Soldier. Radial glow intensifies when stun ray fires.

**Warden — Shield Pulse**:
- Periodic 500ms flash every 4 seconds (cubic ease in-out).
- Breathing bob: ±2% vertical sway. Scale pulses with flash intensity.
- Radial glow peaks at flash moment.

**Jumper — Lava Ball**:
- Radial gradient circle: bright highlight → core color → dark rim.
- **HP scaling**: 3 HP = 100% size, 2 HP = 60%, 1 HP = 20%. Dramatic visual degradation.
- Slow 2-second pulse glow. White flash overlay on damage.

**Wall**: Static player-colored square, no animation.

### 16.3 Rooting Visuals

**While Rooting (progress 0→100%)**:
- **Diagonal stripes**: Animated hash lines scrolling rightward across the block. Alpha increases from 8% to 38%.
- **Growing corner anchors**: Gray lines extend inward from all four corners, reaching 35% of block size at full root. Line width 2→3.5px.
- **Spinning perimeter segment**: White highlight line rotates around the block's edges (3 full rotations over the root duration), 20% of perimeter visible, 35% alpha.

**Fully Rooted**:
- **Inset shadow**: Dark gradients on top/left edges, light catch on bottom-right — "sunken into the grid" appearance.
- **Static corner anchors**: Full size, 80% opacity.
- **Frozen diagonal stripes**: Pattern stops scrolling, remains at 38% alpha.

**While Uprooting**:
- Stripes scroll in reverse (leftward).
- Shrinking inner rectangle overlay.

### 16.4 Combat Threat Indicators

**Soldier Threat Corners**: When a block is adjacent to Soldiers but below the kill threshold, red pulsing corner marks appear:
- Corners light up clockwise: TL (1 Soldier), TR (2), BR (3).
- Double stroke: thin 2.5px red line + outer glow (5px, 30% alpha).
- Pulsing brightness oscillation (0.55→1.0).
- Thresholds: Formation members = 3, Rooted Stunner = 3, Rooted Soldier = 2, Mobile units = 1.
- Hidden while stunned.

### 16.5 Grid Lightning Effects

The signature visual system. Procedural lightning patterns travel along grid lines, triggered by game events. Multi-pass rendering with bloom creates a dramatic electric feel.

**Lightning Types** (each with unique shape and timing):

| Trigger | Shape | Segments | Duration | Trail Width |
|---------|-------|----------|----------|-------------|
| Movement | Backward trail from moving block | ~30 | 1200ms | 3.0 cells |
| Root start | Radiates outward from all 4 edges | ~28 | 900ms | 1.2 cells |
| Uproot start | Same as root but fades outer-first | ~28 | 900ms | 1.2 cells |
| Death (explosion) | Long burst from all edges | ~56 | 1800ms | 2.8 cells |
| Wall convert | Short fast burst from all edges | ~14 | 450ms | 2.5 cells |
| Formation complete | Clockwise spiral from center | ~40 | 1800ms | 4.0 cells |
| Uproot complete | Small cross contracting inward | 12 (4×3) | 600ms | 1.5 cells |
| Death effect | 8-arm jittery zigzag burst | 8 arms | 400ms | 1.0 cells |
| Selection | 3 concentric squares | 3 squares | 350ms | 1.5 cells |
| Builder spawn | 4 single-line arms, staggered | 4 arms | 800ms | — |
| Soldier spawn | 6 jittery arms from back edge | 6 arms | 1000ms | — |
| Stunner spawn | Fast branching from all edges | ~36 | 600ms | — |

**Rendering passes** (per lightning effect):
1. Outer bloom: 9px wide, 6% alpha (additive)
2. Inner bloom halo: 5px, 15% alpha
3. Colored core: 1.8px, 80% alpha
4. White-hot tip: 1px on bright segments (>55% brightness)
5. Shimmer: all brightness × (0.8 + 0.2 × sin(age × 0.006))

**Sparks**: Up to 30 per effect, 8% spawn chance per visible segment, travel outward, 400ms lifetime.

**Wave animation**: easeOutCubic progress, wave travels at 1.4× overshoot to create trailing fade.

### 16.6 Stun Ray Visuals

- Traveling pulse wave along ray path (1D Gaussian ring, 800ms cycle).
- Blue tint for stun rays, orange for blast rays.
- Base 15% alpha + ring strength up to 70% with edge fade.
- Ray advances cell-by-cell at configured interval, then fades over STUN_RAY_FADE ticks.

### 16.7 Frozen/Stunned Block

- **Ice blue overlay**: `rgba(140, 200, 255)` pulsing between 15-25% alpha.
- **Frost crack lines**: 4 directional cracks from edges inward (3 of 4 crack sets visible per frame, cycling).
- **Crystalline border**: Pulsing `rgba(140, 210, 255)` at 30-50% alpha.
- **Radial frost glow**: Soft gradient from center outward.

### 16.8 Push Wave Visuals

- **Double chevron motif** pointing in push direction (e.g., `>>` for right push).
- Core glow: 40% alpha at full cell size. Outer glow: 15% alpha at 140% cell size.
- Chevron arms: 2px white lines, 22% cell-size length.
- Player-colored teal/cyan tint.

### 16.9 Jump Trail (Jumper)

- **Core streak**: Bright narrow line (8% cell width) from origin to landing, fading over 500ms.
- **Wide glow**: Dimmer broad trail (30% cell width) behind core.
- **Ember particles**: Deterministic pseudo-random along path, drift laterally.
- **Impact flash**: Radial bloom at landing for first 30% of animation.
- Tail recedes from origin (head moves 30% toward landing).

### 16.10 Warden Zone of Control

- Smooth expanding pulse wave (2500ms cycle, 1.4× overshoot).
- Gaussian-shaped ring travels outward, lighting up cells in player color.
- Base 5% alpha + ring peak at 14% with edge fade.
- Octagonal shape (Chebyshev radius minus corners).

### 16.11 Death Effect Animation

**Phase 1 — Inflation (3 ticks)**:
- Block inflates 1.0→1.15× scale.
- White flash overlay peaking at 50% intensity.
- 4×4 crack grid appearing at 0→60% alpha.

**Phase 2 — Brick Explosion (7 ticks)**:
- Expanding radial glow burst (30%→130% cell size).
- 28 fragments launched from center: 2-5.5px rectangles, player-colored.
- Each fragment has outward velocity, rotation (3-7× spin), gravity arc.
- Alpha fades as (1 - progress).

### 16.12 Selection and Targeting

- **Selected block**: White dashed border (2px, [3,3] dash). "W" indicator on eligible Builders.
- **Drag select**: Dashed rectangle (1px, [4,4] dash) with subtle fill.
- **Move target**: Small filled circle (cell/6 radius).
- **Hover**: Faint dashed border (40% alpha).
- **Command queue path**: Dotted line through waypoints (own blocks only in multiplayer).

### 16.13 Formation Visuals

- **Outer border**: Double-stroke glow (4px at 35% + 1.5px full alpha), color per formation type.
- **Center diamond**: Rotating diamond, 18% cell size, formation-type color.
- **Corner brackets**: 4 L-shaped marks, 4px long, matching border color.
- **Tearing down**: Shrinking inner rectangle, neutral gray.
- **Member connectors**: Dashed lines between formation members (40% alpha).

### 16.14 Post-Processing

- **Bloom**: Downscaled 50% copy, 6px blur, composited at 35% opacity additive. Creates soft glow on lightning, impacts, and bright elements.
- **Kill flash**: White screen flash when enemy unit dies, multiplicative decay.

### 16.15 HUD

- **Population display**: Current pop / max pop per player.
- **Block count bar**: Top margin bar comparing relative non-wall block counts per player.
- **Nest timers**: Spawn progress bars near active nests.
- **Selection info**: Type and count of selected blocks.
- **Minimap**: Bottom-right corner. Shows terrain, blocks, and nest zones. Click to jump camera.
- **Keybind hints**: Contextual key prompts based on current selection.
- **Chat messages**: Player name + text with timestamps, rendered in margin area.

---

## 17. Audio

**Asset strategy — procedural fallback**: All audio effects have a **procedural** implementation - Refer to `D:/claude/mini-rts/src/audio/` for how these are implemented in the Typescript prototype. OGG audio files can optionally replace the procedural effects, but the game must be fully playable without any audio assets.

### 17.1 Sound Events

Sound effects are triggered by game events:
- All player issued commands: move, root, wall, uproot, explode, stun, etc.
- Block selection click
- Root complete
- Uproot complete
- Wall converted
- Nests/Supply formed
- Stun ray hit
- Push wave hit
- Combat kills
- Nest spawn
- Self-destruct explosion
- Jump launch + landing
- Warden magnet pull
- Pop cap warning (when at capacity)
- Win / loss fanfare

### 17.2 Music

Background music during gameplay. Separate tracks or adaptive layers based on game state (peaceful early game vs. intense combat).

---

## 18. Constants Reference

| Constant | Value | Notes |
|----------|-------|-------|
| GRID_WIDTH | 41 | Default grid width |
| GRID_HEIGHT | 25 | Default grid height |
| NEST_ROOT_TICKS | 36 | Ticks to fully root |
| NEST_UPROOT_TICKS | 24 | Ticks to fully uproot |
| SPAWN_TICKS | 140 | Builder nest spawn time |
| SPAWN_TICKS_SOLDIER | 260 | Soldier nest spawn time |
| SPAWN_TICKS_STUNNER | 360 | Stunner nest spawn time |
| SPAWN_TICKS_WARDEN | 220 | Warden spawn on overload |
| SPAWN_TICKS_JUMPER | 300 | Jumper spawn on overload |
| MOVE_INTERVAL | 3 | Builder/Warden/Jumper move rate |
| SOLDIER_MOVE_INTERVAL | 4 | Soldier move rate |
| STUNNER_MOVE_INTERVAL | 2 | Stunner move rate |
| STUN_DURATION | 160 | Ticks target stays stunned |
| STUN_COOLDOWN | 140 | Stunner cooldown after firing |
| STUN_RANGE | 5 | Stun ray range in cells |
| STUN_TOWER_FIRE_INTERVAL | 16 | Ticks between tower shots |
| STUN_TOWER_RANGE | 4 | Stun Tower ray range |
| SOLDIER_TOWER_FIRE_INTERVAL | 12 | Ticks between tower volleys |
| SOLDIER_TOWER_RANGE | 5 | Soldier Tower blast range |
| PUSH_WAVE_INTERVAL | 8 | Ticks between push fires |
| PUSH_RANGE | 4 | Push wave range in cells |
| PUSH_KNOCKBACK | 3 | Max displacement per push |
| SOLDIER_EXPLODE_RANGE | 3 | Self-destruct blast range |
| SOLDIER_MAX_HP | 4 | Soldier starting HP |
| SUPPLY_POP_CAP | 7 | Pop per Supply Formation |
| SUPPLY_MEMBERS | 3 | Walls needed for Supply |
| FRAGILE_WALL_SOLDIER_THRESHOLD | 2 | Adjacent soldiers to break fragile wall |
| WARDEN_ZOC_RADIUS | 4 | Zone of Control radius |
| WARDEN_PULL_RADIUS | 4 | Magnet Pull range |
| WARDEN_PULL_COOLDOWN | 140 | Pull cooldown ticks |
| JUMPER_JUMP_RANGE | 5 | Jump distance in cells |
| JUMPER_JUMP_COOLDOWN | 120 | Jump cooldown ticks |
| JUMPER_MAX_HP | 3 | Jumper starting HP |
| STUN_RAY_FADE | 8 | Ticks for ray visual fade |
| STUN_UNIT_RAY_ADVANCE_INTERVAL | 2 | Ticks per cell for unit stun rays |
| STUN_TOWER_RAY_ADVANCE_INTERVAL | 2 | Ticks per cell for tower stun rays |
| BLAST_UNIT_RAY_ADVANCE_INTERVAL | 1 | Ticks per cell for unit blast rays |
| BLAST_TOWER_RAY_ADVANCE_INTERVAL | 1 | Ticks per cell for tower blast rays |
| PUSH_WAVE_ADVANCE_INTERVAL | 1 | Ticks per cell for push waves |
| PUSH_WAVE_FADE | 6 | Ticks for push wave visual fade |
| DEATH_EFFECT_TICKS | 10 | Duration of death animation |

---

## 19. Future / Maybe

These features are not part of the current game but are being considered:

- **Fog of War**: Proximity-based vision (only see cells adjacent to own blocks). Could be a game mode toggle.
- **New unit types**: Virus (spreads through enemy formations?), Sticker (immobilizes on contact?), Wall-Breaker (specialized anti-wall?)
- **Hospital Formation**: Heals damaged Soldiers/Jumpers?
- **Domination win condition**: Control X% of boot zones for N consecutive ticks.
- **Spectator mode**: Watch live games without participating.
- **Reconnection**: Rejoin after disconnect in multiplayer.
- **Replays**: Games recorded as tick-by-tick command logs. Playback re-simulates from commands. Version-tagged for compatibility.
- **Toggle Spawning**: Each unit has a toggle to keep spawning it or not

---

## 20. Replays

Games are recorded as a sparse list of commands per tick. On playback, the same commands are re-applied to a fresh game state tick by tick — the deterministic simulation reproduces the exact game. Replay files include a simulation version; loading a replay with a mismatched version shows a compatibility warning.

---

*This document is the design authority. When the code and this document disagree, update whichever is wrong — but discuss first.*
