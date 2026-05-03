# Builder Surround Kill

## Summary

Builders gain a new passive ability: when blocks from a team completely encircle opponent units (flood-fill can't reach map edge), trapped units die after a configurable delay. The encirclement considers terrain, walls, nuggets, and the victim's own immobile blocks as part of the trap.

## Mechanic

### Encirclement Detection

Each tick, during the combat step, check each mobile enemy block to determine if it's trapped. A block is **trapped** if a flood-fill from its position cannot reach the map edge within a capped number of cells explored.

**Impassable cells (form the kill ring):**
- Any block from the attacker's team (all types, all states — mobile, rooted, formation)
- Victim's own immobile blocks (rooted blocks, walls, formation members)
- Neutral terrain (walls, breakable walls, fragile walls)
- Nuggets

**Passable cells (escape routes):**
- Empty cells
- Victim's own mobile blocks (not rooted, not in formation)

### Area Cap

The flood-fill is capped at `SurroundKillMaxArea` cells (default: 36). If the flood explores this many cells without being fully enclosed, the block is declared safe. This bounds performance cost and prevents degenerate "wall off half the map" strategies. The cap of 36 allows trapping roughly 10 units in a reasonably shaped encirclement.

### Pre-Filter

Before running the flood-fill, check if the block has at least 2 impassable orthogonal neighbors. If fewer than 2, skip — encirclement is geometrically impossible. This eliminates 90%+ of blocks from the expensive check.

### Trap Timer

Each block gets a `TrapTicks` counter (default 0). When detected as trapped:
- Increment `TrapTicks` each tick
- When `TrapTicks >= SurroundKillDelay`, the block dies

When NOT trapped:
- Reset `TrapTicks` to 0

This means the victim can break free during the delay by uprooting one of their own blocks or killing/pushing an attacker's block.

### Kill Attribution

Track `TrappedByPlayerId` on each block — set to the owner of the last block that moved into or was placed adjacent to the trapped region. This is determined by checking which attacker-team block most recently moved into a neighbor cell of the enclosed area. Stored for future kill-score use but has no gameplay effect now.

### Constants

```csharp
public const int SurroundKillMaxArea = 36;
public const int SurroundKillDelay = 0; // 0 = instant, tune via playtesting
public const int SurroundKillPreFilterMin = 2; // min impassable orthogonal neighbors
```

## Tick Integration

Runs during **Combat (Step 13)**, after standard soldier adjacency kills but before death effects. Order:

1. Standard surrounding (existing soldier logic)
2. Soldier adjacency kills
3. Soldier mutual kills
4. Neutral obstacle combat
5. **Builder surround kill** (new)

This ordering means blocks already killed by soldiers don't get redundantly processed.

## Visual Feedback

When a block has `TrapTicks > 0` (is actively trapped and counting down):
- The block blinks with **accelerating frequency** as the timer progresses
- Blink rate: starts slow (e.g., every 20 frames) and accelerates to every 2-3 frames near death
- Formula: `blinkInterval = lerp(maxInterval, minInterval, TrapTicks / SurroundKillDelay)`
- If `SurroundKillDelay` is 0 (instant kill), no blink — death is immediate

This visual communicates urgency to the victim: "you're trapped, break out NOW."

## Edge Cases

1. **Multiple attackers encircling together:** Teams can cooperate. The kill ring uses all allied blocks. Attribution goes to whichever team member's block last closed the gap.

2. **Victim's own walls used against them:** Yes — the victim's rooted blocks, walls, and formation members count as impassable. Counterplay: uproot during the delay window.

3. **Block moves into an already-enclosed area:** Gets its own trap timer starting at 0. No retroactive instant-death.

4. **Attacker block leaves the ring:** If the ring breaks, all trapped blocks inside have their `TrapTicks` reset to 0 (they pass the flood-fill check next tick).

5. **Walls immune to surround kill:** Walls are immobile and already have their own destruction mechanics (stun rays, etc.). They should be **exempt** from surround kill — only mobile blocks can be trapped.

6. **Rooted victim blocks:** A rooted block that is part of the enclosed region — is it a victim or part of the wall? It's part of the wall (impassable). It cannot be killed by surround since it's immobile. The victim must uproot it to create an escape path for their mobile blocks.

7. **Nuggets inside the ring:** Nuggets are impassable (form the wall). They are not killed by the surround mechanic — they have their own lifecycle (mining).

## Performance

- Pre-filter: 4 lookups per mobile block (~500 blocks = 2,000 ops)
- Flood-fill: Only ~20-50 blocks pass pre-filter, each capped at 36 cells = ~1,800 ops
- Total: ~4,000 operations per tick worst case in a full 3v3
- Comparable cost to pathfinding a single unit

## Files to Modify

- `src/Blocker.Simulation/Core/Constants.cs` — new constants
- `src/Blocker.Simulation/Blocks/Block.cs` — `TrapTicks` and `TrappedByPlayerId` fields
- `src/Blocker.Simulation/Systems/CombatSystem.cs` — new surround kill logic
- `src/Blocker.Simulation/Systems/SurroundKillSystem.cs` — new system (flood-fill, pre-filter, timer)
- `godot/Scripts/Rendering/BlockRenderer.cs` (or equivalent) — blink visual
- `tests/Blocker.Simulation.Tests/` — new test file for surround kill scenarios
- `docs/game-bible.md` — document new mechanic
