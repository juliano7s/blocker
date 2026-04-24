# Blocker Wiki Index

Welcome to the Blocker technical wiki. This is the entry point for understanding the system's current state and historical decisions.

## 🧭 Core Documentation

- **[Architecture](architecture.md)**: The technical foundation. Layer separation, simulation logic, Godot integration, and networking.
- **[Game Bible](game-bible.md)**: The authoritative design reference. Mechanics, constants, unit types, and game rules.
- **[Decision Log (ADR)](ADR.md)**: A chronological record of architectural decisions and why they were made.

## 🏗️ System Overview

### [Simulation Layer](../src/Blocker.Simulation/)
- **State**: `GameState.cs` is the single source of truth.
- **Tick Engine**: Deterministic, integer-only logic.
- **Key Systems**: Combat, Formations, Spawning, Push, and Abilities.

### [Godot Layer](../godot/)
- **Rendering**: Interpolated reading of GameState.
- **Input**: `SelectionManager` (Godot Input → Simulation Commands).
- **UI**: HUD, Minimap, and Menu systems.

### [Networking](../src/Blocker.Relay/)
- **Protocol**: Custom binary over WebSocket.
- **Sync**: Lockstep coordination with state hashing for desync detection.

## 🗄️ Archive
- **[Historical Specs & Plans](archive/)**: Completed mission documents and initial design brainstorms.
