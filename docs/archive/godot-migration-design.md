# Godot Migration Design Spec

## Decision

Reimplement the game in **Godot 4 + C#** as a native desktop application (Windows + Mac). The current TypeScript + Canvas 2D browser game serves as the design reference — its documented mechanics and design intent are the spec, not the code itself.

### Why move

The browser sandbox limits the RTS experience in ways that can't be worked around:
- No real cursor confinement (Pointer Lock hides cursor; no "visible but trapped" option)
- Input latency from the browser event loop (~1-2 frames inherent delay)
- CPU-bound Canvas 2D rendering ceiling — large maps + effects + many units will struggle
- No system-level window management (fullscreen quirks, multi-monitor)
- Audio autoplay policy, limited mixing control

### Why Godot 4 + C#

- **Native windowing/input**: OS-level cursor confinement, instant key/mouse response
- **GPU-accelerated 2D**: Vulkan/OpenGL backend, built-in shaders, particles, bloom, lighting
- **Content pipeline**: Sprite atlases, animation system, audio bus mixing — all built-in
- **C#**: Known language, readable/reviewable code, syntactically close to current TypeScript
- **Text-based scenes**: `.tscn`/`.tres` files are plain text — entire project can be built and maintained via code, no GUI required
- **Cross-platform**: Windows, Mac, Linux native exports
- **Code-first workflow**: All implementation done via code and text files. Godot editor is optional for visual tweaking but never required.

### What we give up

- **Zero-install browser play**: Users will need to download the game. Acceptable tradeoff for control quality.
- **WebRTC P2P**: C# lacks clean WebRTC. Server-relayed WebSocket/UDP replaces it (single-digit ms difference for same-region players).
- **Existing codebase**: Full rewrite. The design docs and game bible carry forward, not the TypeScript implementation.

---

## Migration Plan

### Phase 1: Game Bible

**Goal**: A standalone design document that fully describes the game — mechanics, controls, visuals, audio, multiplayer, editor. Someone who never saw the TypeScript codebase can implement the game from this document.

**Process**:
1. I draft sections by reading the current codebase, docs, and constants
2. jjack reviews each section: **keep as-is**, **redesign**, or **drop**
3. Iterate until the bible represents the game we want to build, not just what exists

**Sections**:
- **Units & Blocks**: All 6 types + Wall. Stats, behaviors, abilities, upgrade paths
- **Combat**: Surrounding rules, soldier adjacency tiers, wall immunity, overcrowding
- **Economy**: Nests, spawning, population/supply, boot/overload/proto ground effects
- **Formations**: Detection patterns, root/uproot timing, nest types, upgrade rules
- **Abilities**: Stun rays, push waves, jump, warden ZoC/magnet pull, self-destruct
- **Controls**: Selection, movement, command queueing, blueprints, hotkeys, paint mode
- **Map System**: Format, terrain types, editor tools, map sizes
- **Multiplayer**: Lockstep model, lobby system, team play (2v2/3v3/FFA), chat, replays
- **Single Player**: AI opponents, tutorial, scenarios
- **Visuals**: Rendering style, animations (procedural + sprite-based), effects (bloom, particles, lasers), HUD, minimap
- **Audio**: Sound events, music system, procedural + file-based sounds, spatial awareness
- **Future / Maybe**: Fog of war, new unit types (virus, sticker, wall-breaker), hospital formation

**Output**: `docs/game-bible.md` in the new Godot repo `D:/claude/blocker`.

### Phase 2: Godot Architecture Design

**Goal**: Define how the game bible maps to Godot's structure. This becomes the CLAUDE.md and technical foundation for the new repo.

**Key decisions**:
- **Simulation layer**: Pure C# library with zero Godot dependencies (same principle as current TS simulation — deterministic, portable, testable in isolation)
- **Rendering layer**: Godot scenes + scripts that read simulation state and render it. Sprites, shaders, particles, animations all here.
- **Input layer**: Godot's InputEvent system → translated to simulation CommandPayloads
- **Networking**: C# networking libraries (WebSocket or reliable UDP), custom lockstep sync, signaling server
- **Content pipeline**: How sprites, spritesheets, sounds, and Blender/Clip Studio animations get into the project
- **Project layout**: Folder structure, scene hierarchy, script organization, asset organization

**Output**: Architecture doc + CLAUDE.md in the new Godot repo.

### Phase 3: Incremental Implementation

**Goal**: Build the game in playable milestones. Each milestone is testable and provides feedback opportunities.

Exact milestones defined after Phase 2, but roughly:
1. **Grid + Camera + Input**: Render a grid, scroll around, select cells. Prove the feel is right.
2. **Core Simulation**: Builders, walls, nests, combat, movement, pathfinding. Two players hot-seat.
3. **All Units + Abilities**: Soldiers, stunners, wardens, jumpers, push, stun rays, jump.
4. **Visuals + Audio**: Sprite system, procedural animations, effects, sound events, music.
5. **Multiplayer + Lobby**: Networking, online play, chat, replays.
6. **AI + Tutorial + Editor**: Single player content, map editor.
7. **Polish + Large Maps**: Minimap, large map support, cursor confinement, visual polish.

### Phase 4: Parity Verification + Beyond

- Side-by-side gameplay comparison with the TS version
- Address any feel/responsiveness differences
- Then build the features that motivated the move: large maps, team play, sprite animations, advanced effects

---

## Testing Strategy

- **Simulation**: Unit tests in C# (xUnit or NUnit). The simulation being pure C# with no Godot deps means it's fully testable outside the engine.
- **Integration**: Play each milestone manually. jjack tests gameplay feel, controls, responsiveness.
- **Multiplayer**: Two instances on same machine for local testing. Online testing against the deployed signaling server.
- **Replay determinism**: Record and replay to verify simulation consistency.
- **Cross-platform**: Test Windows builds throughout. Mac builds at key milestones.

---

## What This Spec Does NOT Cover

- Detailed game mechanics (that's the game bible, Phase 1)
- Detailed Godot architecture (that's Phase 2)
- Implementation details (that's Phase 3 plans)

This spec covers the **migration strategy**: why, how, and in what order.
