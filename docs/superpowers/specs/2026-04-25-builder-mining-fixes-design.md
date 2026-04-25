# Builder Mining Fixes — Design Spec
**Date**: 2026-04-25

## Problem

Two bugs in builder mining behavior:

1. **Shift-queued mine commands interrupt ongoing mining.** Queuing MineNugget B while mining A fires immediately when the builder arrives at A, because `IsBlockIdle` only checks `MoveTarget.HasValue` — a mining builder (no MoveTarget, but MiningTargetId set) is wrongly considered idle.

2. **Stuck builder abandons mining and does nothing.** When a builder can't path to its nugget target and gives up, it clears MoveTarget but leaves MiningTargetId set and idles. It should try a nearby unmined nugget instead.

---

## Fix 1: Shift-queue respects active mining

**File**: `src/Blocker.Simulation/Core/GameState.cs`  
**Method**: `IsBlockIdle`

Add one condition: if the block has `MiningTargetId` set, it is not idle.

```csharp
if (block.MiningTargetId.HasValue) return false;
```

This ensures queued commands only fire once the current nugget is fully mined and NuggetSystem clears `MiningTargetId` from all assigned builders.

No other changes needed — the existing queue infrastructure handles the rest.

---

## Fix 2: Stuck builder retries nearby nugget

### New constant

**File**: `src/Blocker.Simulation/Core/Constants.cs`

```csharp
public const int BuilderLineOfSight = 5; // Chebyshev radius — also future fog-of-war LOS
```

### New method

**File**: `src/Blocker.Simulation/Systems/NuggetSystem.cs`  
**Method**: `FindFallbackMiningTarget(GameState state, Block builder, int excludeNuggetId)`

- Scans all blocks within Chebyshev distance `BuilderLineOfSight` from `builder.Pos`
- Filter: `Type == Nugget`, `NuggetState.IsMined == false`, `Id != excludeNuggetId`
- Rejects nuggets being mined by an enemy team (same check as MineNugget command)
- Returns the nearest by Manhattan distance, or null if none found

### Give-up hook

**File**: `src/Blocker.Simulation/Core/GameState.cs`  
**Location**: Movement loop give-up block (`block.StuckTicks >= Constants.MoveGiveUpTicks`)

When the builder gives up and has `MiningTargetId` set:
1. Record `excludeId = block.MiningTargetId.Value`
2. Clear `block.MiningTargetId = null`
3. Call `NuggetSystem.FindFallbackMiningTarget(this, block, excludeId)`
4. If a fallback is found, assign it: set `MoveTarget`, `MiningTargetId`, and `nugget.PlayerId` — exactly as MineNugget command does

If no fallback exists, the builder simply idles (current give-up behavior).

---

## Constants

| Constant | Value | Notes |
|---|---|---|
| `BuilderLineOfSight` | 5 | Chebyshev radius for fallback search; future fog-of-war LOS |

---

## What does NOT change

- The MineNugget command path is unchanged
- Shift-queueing non-mining commands is unaffected
- NuggetSystem mining tick, consumption, and auto-rally are unchanged
- No new Block fields needed
