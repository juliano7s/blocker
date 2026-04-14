
# 2026.04.13

Prompt:

I want to improve the path way indicator (dashed line) on move commands. Currently it just appears on shift queueing. I want to:
 - Make it appear for all queued or running move commands for the selected unit.
 - Change it so it's not a straight line to the target. I would like to make the actual path of the unit be visible as a dashed line.
 - Bonus if we can add a shader to the line that makes it gently move the dashes

---

Refined Plan: Pathway Indicator

 ### Key Insight: The block doesn't store its path

 Currently GetNextStep runs full A* every tick and returns only one cell. The full path is discarded. We have
 two approaches:

 Option A — Cache path on Block (simulation-side)
 - Add List<GridPos>? CachedPath to Block
 - When MoveTarget is set, compute and store the full path via GetFullPath
 - When the block takes a step, just trim the head of the cached path
 - Invalidate and recompute when the block gets stuck (path changes) or a new target is set
 - Pros: Path is always available, no per-frame A* in rendering, block already knows its route
 - Cons: Memory for all moving blocks (not just selected), more simulation complexity, must keep in sync

 Option B — Compute path on-demand in Godot layer
 - Add GetFullPath to PathfindingSystem
 - In the Godot rendering code, call it for selected blocks each frame
 - Pros: Simple, no simulation changes, only runs for selected blocks
 - Cons: A* runs every frame for each selected block (though capped at 500 nodes, and typically few selected
 blocks)

 I recommend Option A — it's cleaner architecturally. The block should know its own path. It also means
 GetNextStep can use the cached path instead of re-running A* every tick, which is a performance win for the
 simulation itself.

 ### Implementation Plan

 #### Step 1: Add GetFullPath to PathfindingSystem

 - New method: public static List<GridPos>? GetFullPath(GameState state, GridPos from, GridPos target)
 - Returns full path [from, ..., target] or null if unreachable
 - Reconstruct the full path from _cameFrom instead of just the first step

 #### Step 2: Cache path on Block

 - Add List<GridPos>? CachedPath to Block.cs (nullable, null = no active path)
 - Set it when MoveTarget is assigned (in ExecuteCommand and ProcessCommands)
 - After each successful step in Step 12 of GameState.Tick(), trim the head of CachedPath (remove index 0)
 instead of re-running A*
 - Invalidate (set to null) when: MoveTarget is cleared (arrived/stuck/gave up), new target is set, or block
 gets pushed
 - On stuck ticks, recompute path from current position

 #### Step 3: Update GameState.Tick() Step 12 to use cached path

 - Replace GetNextStep call with: if CachedPath != null && CachedPath.Count > 1, use CachedPath[1] as next step
 - Fall back to GetNextStep if cached path is null or seems stale
 - This is actually a performance win — no A* per tick per moving block

 #### Step 4: Expose path data to Godot layer

 - Block.CachedPath is already a public property on the simulation Block
 - The rendering code already reads block.MoveTarget, block.CommandQueue, etc.
 - No new interface needed — just read block.CachedPath from Godot

 #### Step 5: Rewrite path drawing in SelectionManager._Draw()

 - For each selected block:
     - Active path: If block.MoveTarget != null, read block.CachedPath, convert to world coords, draw as dashed
 polyline
     - Queued commands: For each queued move command, compute full path from previous endpoint to new target,
 draw as dashed polyline
     - Chaining: Active path → dot at MoveTarget → path to queued[0] → dot → path to queued[1] → dot → ...
 - Only draw for blocks owned by ControllingPlayer (don't show enemy paths)

 #### Step 6: Animated marching-ants Line2D with shader

 - Create marching_dashes.gdshader — a canvas-item shader that takes a dash_offset uniform and creates a dashed
 line pattern using fract/step, with dashes appearing to move along the line
 - Use a Line2D node per path segment (managed by a simple pool in GridRenderer or a new PathIndicatorManager
 node)
 - In _Process, increment dash_offset based on elapsed time and set it on each active shader material
 - Use a material pool to avoid creating new ShaderMaterials per frame
 - The shader approach avoids per-segment draw calls and lets the GPU handle the dash animation

 ### Questions

 1. Option A (cache on block) vs Option B (compute on demand) — I'm recommending A for performance and
 cleanliness. Do you agree? The bonus is that it makes the simulation itself faster since we avoid redundant A*
 calls.
 1. Path recalculation: When a block steps along its cached path and the next cell is now occupied (another
 block moved there), we need to recompute. I plan to detect this in Step 12 — if CachedPath[1] is blocked,
 invalidate and fall back to full A*. Sound good?
 1. Should the rendering code read CachedPath directly or should I add a dedicated method like
 Block.GetRemainingPath() that returns the subset from current position onward? The cached path includes cells
 already traversed (trimmed from the front), so it should already be "remaining path."
 1. Queued command paths: For queued commands, the block hasn't started moving yet — there's no cached path.
 I'll compute those on the Godot side with GetFullPath (Option B for queued paths only, since they're
 infrequent and only for selected blocks). Acceptable?

1.
  4. Test coverage — Add determinism tests: run same scenario with and without path caching, verify identical outcomes.