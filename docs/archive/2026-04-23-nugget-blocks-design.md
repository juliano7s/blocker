# Nugget Blocks — Design Spec

> Mineable resource blocks that add strategic diversity, army scarcity, and new mechanical choices to Blocker.

---

## 1. Purpose

Nuggets address four strategic gaps in the current game:

1. **Strategic diversity** — Another decision axis beyond "what formation to build where." Players must decide how to allocate nuggets across spawning, healing, and fortification.
2. **Army scarcity** — By making army spawns slower or nugget-gated, each combat unit becomes more precious and tactical.
3. **Mechanical diversity** — Nuggets can be sent far to heal frontline soldiers or kept home to fortify defenses. Different playstyles emerge from the same resource.
4. **Map contestation** — Nuggets give builders more purpose and create objectives worth fighting over. Undefended nuggets can be stolen.

---

## 2. Architecture: BlockType + NuggetState Component

Nuggets are a new `BlockType.Nugget`. They live in the existing `GameState.Blocks` list and reuse all block infrastructure — grid occupancy, pathfinding, movement, selection, commands, rendering pipeline.

Nugget-specific state is isolated in a `NuggetState` component. `Block` gains a nullable `NuggetState?` field, only non-null for nuggets.

```
BlockType enum: add Nugget

NuggetState:
  NuggetType: NuggetType     — enum for future variants (Standard, etc.)
  IsMined: bool               — false = neutral/unmined, true = team-owned/freed
  MiningProgress: int          — 0 → MiningTicks, advances per adjacent mining builder per tick
  HealTargetId: int?           — block ID of soldier/jumper to heal
  FortifyTargetPos: GridPos?   — position of wall to fortify

Block additions:
  NuggetState?: NuggetState    — only non-null for Nugget blocks
  MiningTargetId: int?         — only used by Builders; ID of nugget being mined

NuggetType enum:
  Standard                     — future variants added here
```

### PlayerId conventions

- **Unmined nuggets**: `PlayerId = -1` (neutral). Combat systems, elimination, and team checks early-out on `BlockType.Nugget`.
- **Mining in progress**: `PlayerId` updates to the mining team's ID when mining starts (shows team color during mining).
- **Mined nuggets**: `PlayerId` = the mining player's ID. Fully selectable and commandable.

---

## 3. Lifecycle

### 3.1 Unmined (map-placed)

- Placed by the map designer in the map editor. No respawning.
- Neutral (`PlayerId = -1`), not selectable.
- Occupies a cell, blocks movement. Pathfinder treats as impassable.
- Immune to stun. Destroyed by blast rays. Jumper jumps do not destroy unmined nuggets (treated as obstacle — jump stops).
- Does not participate in standard combat (surrounding, soldier adjacency).

### 3.2 Mining

- Player selects builder(s), right-clicks an unmined nugget.
- Builder pathfinds to an adjacent cell, then sets `MiningTargetId` to the nugget's ID.
- Each tick, the nugget counts adjacent builders that have `MiningTargetId` pointing to it.
- Mining progress advances by the number of active miners per tick.
- Base mining time: `NUGGET_MINING_TICKS` (~180 ticks = 15 seconds with 1 builder).
  - 1 builder: ~180 ticks. 2 builders: ~90 ticks. 3 builders: ~60 ticks. 4 builders: ~45 ticks.
- Builder remains mobile and commandable while mining — just needs to stay adjacent. Giving the builder another command clears `MiningTargetId`.
- **Exclusive mining**: Only one team can mine a nugget at a time. If builders from team A are actively mining, team B builders cannot mine that nugget. If all team A miners leave or are killed, team B can right-click to start mining and inherit the existing progress.
- Mining progress persists on the nugget, not the builder.
- `PlayerId` updates to the mining team when mining begins.

### 3.3 Freed

- When `MiningProgress` reaches `NUGGET_MINING_TICKS`, `IsMined` is set to `true`.
- Nugget becomes fully owned, selectable, and commandable.
- **Auto-rally**: Immediately sets `MoveTarget` to the nearest friendly nest center. Player can override with direct move commands at any time.
- Moves at builder speed (`MoveInterval = 3`).
- Auto-rally re-evaluates if the target nest is destroyed (finds next nearest).
- Can be pushed by push waves (like any mobile block).

### 3.4 Consumption

Three consumption paths. In all cases the nugget is removed from the game on consumption.

**Nest Refine**: Nugget enters within 3 Chebyshev distance of a friendly nest center → auto-consumed. The nest type determines which unit-type config applies (e.g. a Soldier Nest uses the Soldier nugget config). See Section 5.

**Heal Unit**: Player selects nugget, right-clicks a damaged friendly soldier or jumper. Nugget pathfinds to adjacent cell. Consumed on arrival, target healed to full HP.

**Fortify Walls**: Player selects nugget, right-clicks a friendly wall. Nugget pathfinds to adjacent cell. Consumed on arrival, target wall + 4 connected walls (BFS outward) gain `FortifiedHp`.

### 3.5 Capture

- Enemy builder orthogonally adjacent to a freed (mined) nugget → instant ownership flip.
- **Contest blocking**: If a friendly builder is also orthogonally adjacent, capture is blocked.
- On capture: `PlayerId` flips, auto-rally retargets to new owner's nearest nest.

### 3.6 Destruction

- Blast rays (soldier tower, self-destruct) destroy nuggets in any state (mined or unmined).
- Jumper jumps destroy mined nuggets in the jump path. Unmined nuggets stop jumps (treated as obstacle).
- Stun rays do NOT affect nuggets (immune to stun).
- Standard combat (surrounding, soldier adjacency) does NOT affect nuggets.

---

## 4. Simulation System

### 4.1 NuggetSystem

New system responsible for mining progression, auto-rally, consumption, and capture. Runs each tick.

**Tick resolution placement**: Between step 8 (push waves) and step 9 (command queues):

```
...
8.   Push waves advance
8.5  NuggetSystem — mining, consumption, capture, auto-rally
9.   Command queues
...
```

**Per-tick logic:**

1. **Mining**: For each unmined nugget with `PlayerId != -1`, count adjacent builders with `MiningTargetId` pointing to this nugget. Advance `MiningProgress` by count. If `MiningProgress >= NUGGET_MINING_TICKS` → set `IsMined = true`, emit `NuggetFreed` visual event, set auto-rally target.

2. **Consumption**: For each mined nugget:
   - If within 3 Chebyshev distance of a friendly nest center → consume, apply refine effect.
   - If adjacent to `HealTargetId` block → consume, heal target to full HP.
   - If adjacent to `FortifyTargetPos` wall → consume, fortify 5 walls (BFS).

3. **Capture**: For each mined nugget: check for enemy builders adjacent with no friendly builders adjacent → flip ownership, retarget auto-rally.

4. **Auto-rally maintenance**: For mined nuggets with no `MoveTarget`, no `HealTargetId`, and no `FortifyTargetPos` → set `MoveTarget` to nearest friendly nest.

### 4.2 New Commands

| CommandType | Input | Validation |
|-------------|-------|------------|
| `MineNugget` | Builder selected → right-click unmined nugget | Builder only. Target must be unmined nugget with no active miners from another team. |
| `HealWithNugget` | Nugget selected → right-click damaged soldier/jumper | Nugget must be mined. Target must be friendly, damaged soldier or jumper. |
| `FortifyWithNugget` | Nugget selected → right-click friendly wall | Nugget must be mined. Target must be friendly wall. |

Standard `Move` and `AttackMove` commands work on mined nuggets (overrides auto-rally).

### 4.3 Combat System Changes

- `CombatSystem`: Skip `BlockType.Nugget` entirely — no surrounding, no soldier adjacency interaction.
- `EliminationSystem`: Skip nuggets — they don't count toward army or builder counts.
- `StunSystem`: Stun rays stop at nuggets (nugget blocks line-of-sight like a wall, but is not destroyed or stunned).
- `ExplosionSystem` / blast rays: Destroy nuggets on hit (same as killing a block).
- `JumperSystem`: Jumps destroy mined (mobile) nuggets in the path. Unmined nuggets act as obstacles (stop the jump).
- `PushSystem`: Push waves affect mined (mobile) nuggets normally. Unmined nuggets block push waves (impassable).

### 4.4 Movement

- Mined nuggets: `MoveInterval = 3` (builder speed). Use standard pathfinding.
- Unmined nuggets: `IsImmobile` returns true. Block movement and pathfinding.

### 4.5 Population

Nuggets cost 0 population. They don't count toward pop cap.

---

## 5. Nest Refine Configuration

Per-unit-type settings that control how nuggets interact with spawning. Stored as configurable constants.

```
Per unit type (Builder, Soldier, Stunner, Warden, Jumper):
  NuggetRequired: bool    — if true, nest won't spawn without a consumed nugget
  NuggetSpawnBonus: int    — percentage of spawn progress added (100 = instant, 50 = half)
```

**Behavior:**

- **NuggetRequired = true**: Nest tracks whether a nugget has been consumed. Spawn progress only advances when a nugget is "loaded." One nugget = one spawn. After spawning, nest needs another nugget before progress resumes.
- **NuggetSpawnBonus > 0**: On consumption, spawn progress increases by `SpawnTicks * NuggetSpawnBonus / 100`. If this pushes progress past the spawn threshold, unit spawns immediately.
- Both can be active simultaneously: nugget is required, and when consumed it also grants a progress bonus (potentially instant spawn).

**Default values**: TBD during playtesting. Initial suggestion: all `NuggetRequired = false`, `NuggetSpawnBonus = 1.0` (instant spawn on nugget consumption). This makes nuggets a powerful bonus without gating basic production.

---

## 6. Wall Fortification

### 6.1 Fortified State

Walls gain a new integer field:

```
Block:
  FortifiedHp: int  — stun ray hits remaining before destruction (0 = normal wall)
```

### 6.2 Fortify Mechanic

- Nugget consumed → target wall + 4 connected walls (BFS outward from target) set `FortifiedHp = FORTIFIED_WALL_HP` (configurable, e.g. 3).
- Orthogonal BFS selects walls connected to the target — they don't need to be adjacent to the clicked wall, just connected through other walls via orthogonal neighbors.
- Total: 5 walls fortified per nugget.

### 6.3 Fortified Wall Behavior

- Stun ray hits a fortified wall: `FortifiedHp--` instead of destroying it.
- When `FortifiedHp` reaches 0: wall becomes a normal wall again (next stun ray hit destroys it).
- Only stun rays interact with fortification. All other mechanics treat fortified walls identically to normal walls.
- Re-fortification: A nugget can re-fortify walls that have taken damage (HP below max). Resets HP to full.

---

## 7. Map Integration

- Nuggets are placed in the map editor as a new block type option.
- Map data stores nugget positions like any other block.
- No respawning — nuggets are finite per map.
- Map design consideration: place nuggets at contested midpoints to create strategic objectives.

---

## 8. HUD

- **Top bar**: Show owned (freed) nugget count per player. Nugget icon + count.
- **Cursor changes**: Mining cursor (builder → unmined nugget), heal cursor (nugget → damaged unit), fortify cursor (nugget → wall).
- **Selection**: Mined nuggets selectable like any mobile unit. Included in box-select. Not included in backtick quick-select (combat units only).

---

## 9. Visuals

### 9.1 Unmined Nugget — Idle

- White/silver crystalline block with prismatic shimmer (subtle rainbow light refraction shifting across surface).
- Diamond shape rendered in center.
- Gentle sparkle particles floating around the block.
- Faint ambient glow (~15% alpha, cool white).
- No gold tones — avoids clash with yellow team color.

### 9.2 Mining — Active

- Spark particles flying from nugget toward mining builders.
- Nugget vibrates/shakes subtly; intensity grows with progress.
- Cracks of light appear on the surface, growing with progress (visual progress indicator — no UI bar).
- Prismatic shimmer intensifies as mining nears completion.
- **Sound**: Hook up event for mining sound (OGG provided by jjack).

### 9.3 Mining Complete — Freed

- Radial light burst from nugget (white/prismatic).
- Grid lightning effect: outward burst from all edges (reuse existing lightning system).
- Team-colored diamond appears in the center of the block (no border, just the diamond).
- **Sound**: Hook up event for nugget freed (OGG provided by jjack).
- Sparkle particles scatter outward then settle.

### 9.4 Freed Nugget — Moving

- White/silver crystalline block with team-colored diamond in center.
- Subtle trailing sparkle particles while moving.
- Standard movement interpolation.

### 9.5 Nest Refine Effect

- Nugget shrinks toward nest center (implosion).
- Particles pull inward toward nest.
- Nest glows briefly with prismatic light.
- Grid effect: **ConvergingDrain** (existing effect) centered on nest — lines converge inward.
- Spawn progress bar jumps or fills visibly.
- **Sound**: Hook up event for nest refine (OGG provided by jjack).

### 9.6 Heal Effect

- Nugget dissolves into golden/prismatic particles.
- Particles flow into the healed unit.
- Unit flashes with restoration glow.
- HP indicators restore (soldier sword arms reappear).
- Brief aura around healed unit.
- **Sound**: Hook up event for heal (OGG provided by jjack).

### 9.7 Fortify Effect

- Nugget shatters into 5 diamond shards.
- Each shard flies to a target wall in BFS order (sequential, not simultaneous).
- Wall sparkles on shard impact.
- Diamond sparkle overlay persists on fortified walls (permanent visual indicator).
- Grid lightning connects the 5 walls briefly.
- **Sound**: Hook up event for fortify (OGG provided by jjack).

### 9.8 Capture Effect

- Diamond color transitions to new owner (fast color morph).
- Brief particle burst (old color dissipates, new color fills).
- Grid lightning: short sharp burst at capture point.
- **Sound**: Hook up event for capture (OGG provided by jjack).

### 9.9 Destruction Effect

- Standard death effect animation (inflation → brick explosion) but with white/prismatic fragments instead of team-colored.
- Extra sparkle particles on destruction.

---

## 10. Constants

| Constant | Value | Notes |
|----------|-------|-------|
| `NUGGET_MINING_TICKS` | 180 | Base mining time with 1 builder (~15s at 12 tps) |
| `NUGGET_MOVE_INTERVAL` | 3 | Same as builder speed |
| `NUGGET_POP_COST` | 0 | No population cost |
| `NUGGET_REFINE_RADIUS` | 3 | Chebyshev distance from nest center for auto-consumption |
| `FORTIFIED_WALL_HP` | 3 | Stun ray hits before fortification breaks (tunable) |
| `FORTIFIED_WALL_COUNT` | 5 | Walls fortified per nugget (target + 4 BFS) |
| `NUGGET_BUILDER_BONUS` | 100 | Default NuggetSpawnBonus % for builder nests |
| `NUGGET_SOLDIER_BONUS` | 100 | Default NuggetSpawnBonus % for soldier nests |
| `NUGGET_STUNNER_BONUS` | 100 | Default NuggetSpawnBonus % for stunner nests |
| `NUGGET_WARDEN_BONUS` | 100 | Default NuggetSpawnBonus % for warden nests |
| `NUGGET_JUMPER_BONUS` | 100 | Default NuggetSpawnBonus % for jumper nests |
| `NUGGET_REQUIRED_BUILDER` | false | Whether builder nests require nuggets |
| `NUGGET_REQUIRED_SOLDIER` | false | Whether soldier nests require nuggets |
| `NUGGET_REQUIRED_STUNNER` | false | Whether stunner nests require nuggets |
| `NUGGET_REQUIRED_WARDEN` | false | Whether warden nests require nuggets |
| `NUGGET_REQUIRED_JUMPER` | false | Whether jumper nests require nuggets |

---

## 11. Determinism

Nugget system follows all determinism rules from the game bible:

- No floats in simulation. Mining progress, fortification HP, and spawn bonus are all integers (bonus stored as percentage: 100 = full).
- Mining progress advances deterministically (count adjacent miners, add to progress).
- Auto-rally target selection uses deterministic nearest-nest search (consistent tie-breaking by nest ID).
- Capture checks use deterministic adjacency scan order.
- All nugget actions flow through the command system.

---

## 12. Future Extensibility

- `NuggetType` enum allows adding variant nuggets with different effects, mining times, or visuals.
- Respawning nuggets can be added as a map property (spawn timer on designated cells).
- New consumption effects can be added by extending `NuggetSystem` consumption checks.
- Nugget-specific formations or interactions with towers are possible future additions.
