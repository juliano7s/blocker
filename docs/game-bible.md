# Blocker — Game Bible

> This document fully describes the game. Someone who has never seen the original TypeScript codebase can implement the game from this document alone. It is the authoritative design reference for the Godot 4 + C# reimplementation.

---

## 1. Vision

A minimalist, fast-paced RTS where two armies expand across a grid through formation-based tactics and real-time spatial control. No tech trees, no resource meters — everything is physical: blocks move, nest formations spawn units, stun rays freeze enemies, push waves displace blocks, and walls block line-of-sight. The game is decided by map control, economy timing, and formation placement. Victory comes from destroying your opponent's army and eliminating their ability to rebuild.

## 2. Core Concept

Everything is a block on a grid. Units are blocks. Buildings are blocks. The economy is blocks. The entire game state is readable at a glance through color and position. There is no UI for resources, no tech tree menu — everything is physical, spatial, and visible on the board.

Two players (Blue = Player 0, Red = Player 1) compete on a shared grid. Each player commands their colored blocks. The game runs in real-time with discrete tick-based simulation underneath — both players act simultaneously, and the board resolves on each tick.

*comment* : Two or more players. I want to have 1v1, 2v2, 3v3 and FFA modes.

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
- **Block reference**: Optional — the block occupying this cell (by ID, not direct reference)

*question*: we are making an implementation statement here (by id not reference). Is this relevant for the design? Is it the best choice for the engine and language we are going to?

---

## 4. Block Types

There are 6 block types organized in a tech tier system, plus 2 variant units spawned from overload ground.

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
- **Stun Ray** (S key): Fires 3 rays in a cardinal direction. Range: 5 cells. Stuns the first enemy hit for 160 ticks (~13.3s). Walls are **killed** instead of stunned. Ray stops at terrain, walls, formations, and the first hit target.
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
- **Jump** (F key → aim → left-click): Leaps up to 5 cells in a cardinal direction, killing all enemy blocks in the path. Stops at terrain, formations, map edges, and neutral walls (breakable → fragile on first hit; fragile → destroyed on second).
- **Combo**: If the jump kills at least one enemy and doesn't hit an obstacle, the Jumper enters **combo-ready** state — can jump again immediately without moving. Issuing a move command consumes the combo and starts a **mobile cooldown** (can move but can't jump).
- **Cooldown**: 120 ticks. If no combo: immobile during cooldown. If combo consumed by moving: mobile but can't jump.
- **HP loss**: Missed jumps (non-combo, triggering immobile cooldown) cost 1 HP. The visual icon shrinks with HP loss (100% → 60% → 20%).
- **Warden counter**: Cannot initiate a jump while inside an enemy Warden's Zone of Control. Can jump *into* a ZoC.

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

Economy is entirely physical and visible. Newly spawned blocks must be moved away from the nest to keep production flowing. If an opponent disrupts this flow (e.g., pushes blocks back toward the nest), the economy chokes.

*comment*: this is no longer true. Nests can't be blocked, spawn happens at the next available cell. We should check how that's working exactly.

---

## 7. Formations (Non-Nest)

Formations are created by rooting specific patterns of blocks. Members cannot move while participating. When a formation loses a member, it enters **TearingDown** state (24 ticks) before dissolving.

*comment*: is this true, tearingdown, if the formation member dies?

### 7.1 Rooting Timing

| Action | Duration |
|--------|----------|
| Fully root | 36 ticks |
| Fully uproot | 24 ticks |

### 7.2 Stun Tower

- **Pattern**: 1 fully rooted Stunner (center) + 1 or more fully rooted Builders (any adjacent direction, including diagonals).
- **Behavior**: Auto-fires stun rays cycling through all Builder directions. Fires one direction every 16 ticks. Only fires if an enemy is within range in that direction.
- Range: 4 cells. Kills Walls, stuns others for 160 ticks.
- Does **not** spawn blocks.

*comment*: this is old design, we need to review behavior.

### 7.3 Soldier Tower

- **Pattern**: 1 fully rooted Soldier (center) + 1 or more fully rooted Builders (adjacent directions).
- **Behavior**: When an enemy is detected within range in any Builder direction, fires kill blasts in all Builder directions simultaneously. Fire interval: 12 ticks.
- Blast range: 5 cells. Kills non-Wall, non-formation enemy blocks.
- Does **not** spawn blocks.

*comment*: this is old design, we need to review behavior.

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

- **Left-click**: Select a single block (own blocks only).
- **Left-click + drag (box select)**: Select multiple own blocks in the rectangle.
- **Shift+click / Shift+drag**: Add to current selection.
- **Double-click**: Select all blocks of the same type on screen.
- **Number keys (1-9, 0)**: Control groups. Press to select. Ctrl+number to assign. Double-tap to center camera on group.
- **Tab**: Switch between players in local 2-player mode.

### 10.2 Movement

- **Right-click**: Move selected blocks to target cell. Clears command queue.
- **Shift+right-click**: Queue a move waypoint (dotted path shown for own blocks).
- **A key (Attack-move)**: Move toward target, but engage enemies encountered along the way.

### 10.3 Abilities

| Key | Action | Condition |
|-----|--------|-----------|
| F | Root/Uproot toggle | Builder, Soldier, Stunner |
| F | Begin Jump aim | Jumper (then left-click to execute) |
| W | Convert to Wall | Builder (queues if still rooting) |
| S | Fire Stun Ray | Stunner (direction toward mouse) |
| G | Toggle Push | Rooted Builder, not in formation (direction toward mouse) |
| D | Self-Destruct | Rooted Soldier or Stunner |
| D | Magnet Pull | Rooted Warden |

All ability keys support **Shift+key** to queue the action after the current command.

### 10.4 Blueprint Mode

Builders can be sent to a target location with automatic root + convert queued. This streamlines wall placement — select Builders, activate blueprint, click destination cells. The system auto-queues: move → root → convert.

### 10.5 Camera

- **Edge scrolling**: Move camera by pushing cursor to screen edges.
- **Arrow keys / WASD**: Pan camera.
- **Mouse wheel**: Zoom in/out.
- **Minimap** (bottom-right): Click to jump camera. Shows all blocks and terrain.

### 10.6 Command Queueing

Each block has a command queue. Shift+action appends to the queue; non-shift actions are immediate and clear the queue. The queue processes one entry per tick when preconditions are met (idle for move, fully rooted for convert, etc.).

---

## 11. Win Conditions

### 11.1 Elimination

A player is defeated when **all three** conditions are true simultaneously:
1. **No army** — zero Soldiers, Stunners, Wardens, or Jumpers (Walls don't count)
2. **No active nests** — cannot produce new units
3. **Fewer than 3 Builders** — cannot form a new nest

If both players meet these conditions simultaneously, the player with more total blocks wins.

### 11.2 Surrender

A player can concede at any time via the Surrender button (multiplayer only).

---

## 12. Multiplayer

### 12.1 Architecture: Lockstep

Both clients run identical deterministic simulation. Only player commands are exchanged — not game state. This works because the simulation is fully deterministic (no RNG, no floats, grid-based). Both clients produce identical state from identical inputs. Only ~100 bytes/tick of commands are transmitted.

### 12.2 Tick Rate and Responsiveness

- Default: 12 tps (local and networked), configurable 4-20.
- Input delay: 1 tick, adaptive to 2 ticks if RTT > 80ms.
- Visual interpolation between `prevPos` and `pos` keeps movement smooth regardless of tick rate.

### 12.3 Desync Detection

After each tick, clients exchange FNV-1a hashes of the full game state. Hash mismatch = game pauses with diagnostic info.

### 12.4 Online Lobby

- Players connect to a signaling server to browse, host, and join lobbies in real-time.
- Host creates a named lobby → another player joins → host starts → both transition to gameplay.
- Map selection happens in the lobby.

### 12.5 Determinism Rules

Any game mechanic **must** follow these rules:
1. No randomness in simulation (`Math.random()` / equivalent is forbidden)
2. No floating-point arithmetic — all positions and distances are integer grid coordinates
3. All player actions flow through the command system and are validated
4. Tick budget: at 12 tps, each tick is ~83ms — keep processing fast
5. No side effects in simulation — only game state mutations

---

## 13. Single Player

### 13.1 Play vs AI

Three AI opponent types:

- **Easy Bot**: Simple rule-based scripted AI. Runs client-side, no server needed.
- **Aggressive Bot**: More aggressive rule-based variant. Also client-side.
- **Trained Model**: PPO-trained neural network running server-side. Generates macro-actions every 6 ticks. The client connects via WebSocket; the server acts as Player 1 (Red).

### 13.2 Tutorial

A 14-step guided tutorial teaches core mechanics: selection, movement, rooting, wall conversion, nest building, combat, and abilities. Each step has a specific goal, overlay instructions, and snapshot state.

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

## 15. Map System

### 15.1 Format

Maps are plain-text files. One character per cell, one line per row. Width and height are inferred from the grid.

**Single-layer format**: Ground and units in one grid (legacy, fully supported).

**Two-layer format**: Terrain grid + `---` separator + units grid. Both layers must have matching dimensions. The only format that can represent a unit on a boot/overload/proto cell.

### 15.2 Character Legend

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
- Player 0 = Blue palette. Player 1 = Red palette.
- Smooth movement interpolation between ticks for fluid animation.

### 16.2 Visual Effects

- **Stun rays**: Animated beam traveling cell-by-cell with fade trail.
- **Push waves**: Expanding wavefront with displacement feedback.
- **Boot ground**: Ambient particle/lightning effects.
- **Death effects**: Block destruction animation (type-specific, timed).
- **Rooting progress**: Visual indicator showing root completion percentage.
- **Stun state**: Distinct visual treatment for stunned blocks.
- **Jump trail**: Jumper leaves a kill trail along the jump path.
- **Combo indicator**: Visual cue when Jumper is in combo-ready state.
- **Warden ZoC**: Visible aura showing the slow zone radius.
- **Selection**: Highlight ring on selected blocks. Box select rectangle.
- **Command queue path**: Dotted line through queued move waypoints (own blocks only in multiplayer).

### 16.3 HUD

- **Population display**: Current pop / max pop per player.
- **Nest timers**: Spawn progress bars near active nests.
- **Selection info**: Type and count of selected blocks.
- **Minimap**: Bottom-right corner. Shows terrain, blocks, and nest zones. Click to jump camera.
- **Keybind hints**: Contextual key prompts based on selection.

---

## 17. Audio

### 17.1 Sound Events

Sound effects are triggered by game events:
- Block selection click
- Movement command confirmation
- Root start / root complete
- Wall conversion
- Stun ray fire + hit
- Push wave fire
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

---

## 20. Replays

Games are recorded as a sparse list of commands per tick. On playback, the same commands are re-applied to a fresh game state tick by tick — the deterministic simulation reproduces the exact game. Replay files include a simulation version; loading a replay with a mismatched version shows a compatibility warning.

---

*This document is the design authority. When the code and this document disagree, update whichever is wrong — but discuss first.*
