
# 2026-04-16

Here is my proposed plan to improve the robustness of the Relay Server (Item 6):

  1. Lagging Client Detection (Stall Prevention)
  Problem: In lockstep networking, if one client lags severely or stops sending data, the entire simulation
  stalls for everyone else waiting for their input.
  Solution:
   * Add a HighestSeenTick property to the Connection class to track how far each individual client has
     progressed.
   * Update the StartReaper background task (which currently only cleans up idle rooms every minute) to run
     more frequently (e.g., every 5 seconds).
   * During this check, if any client in a Playing room falls too far behind the room's overall HighestSeenTick
     (e.g., > 60 ticks, which is 5 seconds of simulation time), forcibly close their WebSocket. The existing
     disconnect logic will then broadcast a PlayerLeft message, allowing the remaining players to continue the
     game without stalling.

  2. Dead Connection Detection
  Problem: If a client's internet connection drops silently (half-open TCP connection), the server might not
  realize they are gone for 15+ minutes, stalling the room the entire time.
  Solution:
   * Leverage the existing LastMessageAt property on the Connection object.
   * In the frequent StartReaper check, automatically disconnect any client that hasn't sent a message in the
     last 10–15 seconds. This guarantees dead connections are dropped quickly, freeing up the lockstep
     simulation.

  3. Hardened Packet Parsing
  Problem: While some message handlers use try-catch to swallow errors, Varint parsing and other byte
  manipulation might still cause IndexOutOfRangeException if a client sends maliciously small or malformed
  packets.
  Solution:
   * Audit the parsing logic in RelayServer.cs (specifically HandleCreateRoom, HandleJoinRoom, and
     HandleUpdateRoom) to ensure strict boundary checks are performed before accessing the payload spans.
   * Explicitly disconnect clients that send malformed payloads (protocol violation) rather than just dropping
     the packet, as this usually indicates a tampered client or severe desync.


# 2026.04.15

## From Gemini 3.1 Pro

Based on an analysis of the repository structure and core files, here is a comprehensive review of the "Blocker" project in its current state.

### 1. Architecture

The project has a robust, well-defined architecture tailored for a deterministic multiplayer game. It employs a strict 3-layer separation:

*   **Simulation Layer (`src/Blocker.Simulation`)**: This is the core engine of the game. It's written in pure C# without any dependencies on the Godot engine. This isolation is excellent because it guarantees that the core game logic can execute deterministically across different platforms, which is an absolute requirement for the lockstep networking model. It handles the deterministic tick system, state management (`GameState.cs`), and game rules via distinct systems (Combat, Nests, Towers, etc.).
*   **Networking Layer (`src/Blocker.Relay`)**: A dedicated C# application serving as a relay server to facilitate multiplayer. It manages rooms, connections, and routes the deterministic commands between clients.
*   **Presentation/Client Layer (`godot/`)**: The Godot project acts strictly as a "dumb" client. It handles rendering, audio, and user input. It takes the state from the Simulation layer, interpolates it for smooth visuals, and translates user actions into `Commands` that are sent back to the simulation. The use of `VisualEvents` to bridge simulation state changes to the rendering layer is a strong, decoupled pattern.

**Assessment:** Highly commendable architectural foundation. The separation of concerns between simulation and presentation is textbook for this genre.

### 2. Code

The codebase is organized cleanly into C# projects and Godot specific assets.

*   **Modularity**: The simulation logic is modularized into distinct `Systems` (e.g., `CombatSystem.cs`), which makes it easier to maintain and test individual game rules.
*   **Testing**: There is a dedicated test suite (`tests/Blocker.Simulation.Tests`) covering core mechanics (Combat, Pathfinding, Eliminating, etc.). This is crucial for a deterministic simulation where regressions are deadly.
*   **Central Hub**: `GameState.cs` acts as the central hub, processing commands and running the tick loop.
*   **Potential Bottlenecks**: In the Godot layer, classes like `SelectionManager.cs` tend to become massive (handling selection logic, command building, UI state, and raw input).

**Assessment:** The code structure is professional. The pure C# simulation allows for fast, headless testing which is a massive productivity boost.

### 3. Design & Tooling

*   **Custom Tooling**: The project includes custom tooling, such as a Map Editor built directly into the Godot project (`godot/Scripts/Editor/MapEditorScene.cs`), and a python script for converting maps (`scripts/convert_map.py`). This shows maturity in the development pipeline.
*   **Visuals & Shaders**: A rich set of custom shaders (`godot/Assets/Shaders/` including bloom, film grain, screen distortion) indicates a strong focus on "game feel" and visual polish.
*   **Documentation**: The `docs/` folder contains comprehensive design documents (`architecture.md`, `game-bible.md`, and numerous spec/plan documents for the HUD, map system, multiplayer, etc.). This is exceptionally good practice and helps align the team.

**Assessment:** The project is treated like a serious production, not just a prototype, evidenced by the custom editor, shader work, and extensive design documentation.

---

### Potential Improvements & Recommendations

Here are the key areas for potential improvement moving forward:

1.  **Refactor Godot Input/Selection Handling**:
    *   **Issue**: Input and selection managers (like `SelectionManager.cs` or equivalent classes handling input) often become "god objects" in RTS-style games.
    *   **Recommendation**: Break these down into smaller, focused components: `InputState` (tracks raw mouse/keyboard), `SelectionLogic` (handles what is currently highlighted/selected), and a `CommandBuilder` (translates selections + clicks into deterministic simulation commands).

2.  **Relay Server Robustness (`Blocker.Relay`)**:
    *   **Issue**: Lockstep networking is highly sensitive to latency and packet loss.
    *   **Recommendation**: Ensure the Relay server has robust error handling for late, duplicate, or malformed packets. Implement mechanisms to prevent whole-room stalls if a single client lags behind (e.g., dynamic turn lengths or strict timeout disconnects).

3.  **Simulation Tick Integrity (`GameState.Tick`)**:
    *   **Issue**: Determinism relies on the *exact* execution order of systems.
    *   **Recommendation**: Audit the `GameState.Tick` method. Ensure that system execution order strictly follows the specifications in the `game-bible.md`. Add unit tests that verify the execution order doesn't inadvertently change.

4.  **Complex Interaction Testing (`Blocker.Simulation.Tests`)**:
    *   **Issue**: While individual systems have tests (e.g., `PushTests`, `StunTests`), bugs often hide in the intersections.
    *   **Recommendation**: Expand the integration tests to cover complex system interactions. For example, test what happens when a push wave intersects with a stun ray on the exact same tick.

5.  **Godot to C# Type Safety**:
    *   **Issue**: Passing data between Godot nodes and the C# simulation can sometimes rely on weak typing or string names if not careful.
    *   **Recommendation**: Ensure that the bridge between the Godot layer and the Simulation layer is heavily typed, perhaps using Source Generators or strict interfaces to avoid runtime reflection costs and bugs.


## From Auto Gemini 3
✦ Based on my review of the codebase, project documentation, and simulation logic, here is an assessment of the
  current state of Blocker.

  1. Architectural Assessment
  The project's architecture is exceptionally strong and well-disciplined.

   * Clean Layer Separation: The decoupling between Blocker.Simulation (pure C#) and Blocker.Game (Godot) is a
     textbook example of high-quality engine-agnostic design. By keeping the simulation in a separate assembly using
     only primitive types and grid-based logic, the project ensures perfect determinism, which is essential for the
     planned lockstep networking.
   * Visual Events Pattern: The use of VisualEvents to bridge the simulation and rendering layers is excellent.
     Instead of Godot scripts "watching" simulation state changes, the simulation emits what happened (e.g.,
     BlockDied), allowing the rendering layer to react with one-off effects (tendrils, explosions) without polluting
     the core logic.
   * Deterministic Loop: Using a fixed-tick simulation (TickRunner.cs) ensures that game logic runs consistently
     regardless of the user's frame rate.

  2. Code Quality & Implementation
  The code is idiomatic, performant, and highly modular.

   * Modular Systems: Mechanics like TowerSystem, StunSystem, and NestSystem are implemented as static systems
     operating on a GameState. This functional approach minimizes state-related bugs and makes the code very easy to
     test.
   * Robust Testing: The test suite (tests/Blocker.Simulation.Tests) is comprehensive. Having dedicated tests for
     complex edge cases (e.g., EliminationTests, PathfindingTests) gives high confidence in the simulation's
     integrity.
   * Grid Logic: The use of GridPos and custom direction types ensures that movement and spatial queries are
     integer-based, avoiding the floating-point drift that often plagues multiplayer games.

  3. Design & Configuration
   * Data-Driven Balance: The bridge between GameConfig.cs (Godot Resource) and SimulationConfig.cs is well-handled.
     It allows designers to tweak balance in the Godot inspector while the simulation remains unaware of Godot's
     existence.
   * Rich Visual Feedback: Despite the grid-based nature, the system supports complex visual states like "heat-up"
     death animations and "chevron-shaped" rays, showing a high level of polish.

  4. Potential Improvements & Risks

  Rendering Efficiency (Scalability)
   * Risk: GridRenderer.Blocks.cs currently draws cell backgrounds and grid lines using a nested loop and DrawRect
     calls.
   * Improvement: On very large maps (e.g., 150x150), this can lead to thousands of draw calls per frame. Consider
     using a custom shader on a single large ColorRect to draw the grid background and lines, or leveraging Godot's
     TileMap for the terrain layer to take advantage of internal batching.

  Input & Selection Manager Bloat
   * Risk: SelectionManager.cs has become a "God Class" that handles mouse input, drag selection, control groups,
     blueprint placement, and command queuing.
   * Improvement: Consider splitting this into specialized components: InputHandler (raw events), SelectionState
     (active IDs), and BlueprintController.

  Visual Event Memory
   * Risk: GameState.VisualEvents is a list that grows as events happen.
   * Improvement: Ensure these are cleared strictly every tick (or "consumed" by the renderer) to prevent a memory
     leak in long-running games. (Currently, the simulation appears to clear them, but a Queue<T> or a
     double-buffered approach might be safer for multi-threaded scenarios).

  Network Latency Handling
   * Risk: The current architecture is perfect for lockstep, but lockstep is notoriously sensitive to latency (the
     "input delay" feel).
   * Improvement: As the project matures, consider implementing a prediction/rollback layer for the local player's
     movement to mask latency, though this is significantly more complex in a grid-based deterministic simulation.

  Summary
  The project is in an excellent technical state. The foundations are solid enough to support a commercial-grade
  deterministic multiplayer RTS. The primary focus moving forward should be on maintaining this decoupling while
  optimizing the rendering layer for larger maps.


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