# Architectural Decision Log (ADR)

> **Agent Instruction**: This is a living record. When making significant changes to the simulation, networking, or core Godot structure, append a new `## [YYYY-MM-DD] Title` section at the top of the list below. Briefly explain the context, the decision made, and the impact.

This log tracks significant architectural decisions in the evolution of Blocker.

## [2026-04-26] Fog of War Architecture
- **Context**: Implementing Fog of War requires deciding where visibility is computed (client vs sim) and how it affects game state.
- **Decision**: Visibility is computed inside the deterministic simulation (`VisibilitySystem`) and stored in `GameState.VisibilityMaps`. Explored maps are hashed for desync detection. Rendering layer reads these maps to drive a Godot shader (`fog_overlay.gdshader`), cull enemies, and render ghost blocks (`GridRenderer.Fog.cs`).
- **Impact**: Ensures FoW behaves identically across multiplayer clients. Prevents cheating by ensuring the client renderer only draws what the sim explicitly marks as visible.

## [2026-04-23] Nugget Blocks Simulation
- **Context**: Needed a way to handle "Nuggets" (resource/objective units) that can be moved and processed.
- **Decision**: Nuggets are implemented as a specialized `BlockType` within the standard simulation. They respect the same movement and push rules as other blocks but have unique interaction triggers (e.g., being "collected" or "processed" by specific structures).
- **Impact**: Simplifies the simulation by reusing the existing `Block` infrastructure rather than creating a separate entity layer.

## [2026-04-21] Map Editor Toolset Upgrade
- **Context**: The original map editor was functional but slow for complex map creation.
- **Decision**: Replaced "Live Symmetry" with "One-shot Mirroring" and added a standard toolset: Pick (eyedropper), Fill (flood fill), Line (Bresenham), and Select+Move.
- **Impact**: Significantly increased map creation speed. Decoupled tool logic into an `EditorMode` enum for cleaner state management.

## [2026-04-17] SimulationTicker & SelectionManager Decoupling
- **Context**: `SelectionManager` was becoming a "God Class" handling both UI and some simulation-adjacent logic. Timing drift was also an issue in networked play.
- **Decision**: 
    1. Introduced `SimulationTicker` to orchestrate accumulator math and timing, ensuring identical tick rates across clients.
    2. Split `SelectionManager` into partial classes (Input, Commands, Drawing) and moved UI-only state to `SelectionState.cs`.
- **Impact**: Improved testability of the tick engine and clarified the boundary between Godot (UI) and Simulation.

## [2026-04-10] Lockstep Multiplayer Implementation
- **Context**: Needed a reliable way to sync 2-6 players over high-latency connections.
- **Decision**: Implemented a classic Lockstep architecture. Commands are collected for a specific tick, hashed state is exchanged for desync detection. 
- **Impact**: Zero-bandwidth scaling for unit counts (only commands are sent). Requires perfect determinism (integer-only math).

## [2026-03-XX] Godot Migration
- **Context**: Original TypeScript/Canvas implementation was hitting performance and maintenance ceilings.
- **Decision**: Migrated to Godot 4 + C# for better rendering performance, native threading, and superior UI tooling.
- **Impact**: Complete rewrite of the presentation layer; simulation logic ported from TS to C#.
