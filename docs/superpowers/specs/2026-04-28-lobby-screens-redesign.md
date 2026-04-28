# Lobby Screens Redesign

Status: **design in progress** — layout approved, ready for implementation planning.

## Goal

Port the main menu's grid-based visual style to the multiplayer/AI screens and reorganize the screen flow.

## Screen Flow

```
MainMenu
  ├─ PLAY MULTIPLAYER → LobbyListScreen → GameLobbyScreen (host or joined)
  └─ PLAY VS AI → GameLobbyScreen (single-player mode)
```

## Visual Approach: Hybrid (approved)

- **MenuGrid background** on all screens (dark bg + blue grid lines, 28px cells)
- **MenuButton-style** for navigation/action buttons (colored blocks + text, blue idle / orange hover)
- **Themed Godot Controls** for functional elements (inputs, dropdowns, scrollable lists) — dark bg, blue accents, matching the palette
- **No custom-drawn inputs/dropdowns** — standard Controls styled to match

## Screen 1: Lobby List (replaces MultiplayerMenu)

Currently connects to relay and shows Host/Join buttons with a room code input. Redesign:

- **Top bar**: Player name input | Lobby name input | HOST NEW button
- **Lobby table**: scrollable list with columns — Lobby name, Players (e.g. 2/4), Map name, JOIN button per row
- **Bottom**: BACK button (left), lobby count label (right)
- Relay connection happens on entry (same as today)
- Note: relay currently uses room codes, not a lobby list API. This screen needs relay protocol changes to support listing rooms — or we keep the room code input as a fallback alongside the lobby list. **Decision needed on relay protocol.**

## Screen 2: GameLobbyScreen (shared scene, replaces SlotConfigScreen + MapSelectScreen)

One scene with three modes controlled by flags:

### Host mode
- **Header**: lobby name + room code
- **Left column**: Slot list (player names, team tags, kick buttons for non-host), READY + START buttons at bottom
- **Right column**: Map dropdown, Mode dropdown (FFA/Teams), map miniature preview
- Host can change map/mode; changes broadcast via relay

### Joined mode
- Same layout but read-only: no map/mode dropdowns (just labels), no kick buttons, no START button
- READY button only

### Single-player (Play vs AI) mode
- Same layout, no room code, no READY button, no mode dropdown
- Slots toggle between Player / AI (inactive) on click
- START GAME button at bottom of slot list
- Map dropdown + miniature on right

## Map Miniature

Small preview rendering showing map dimensions and spawn positions (colored dots). Could be a simplified GridRenderer or a custom draw. Detail TBD during implementation.

## Files Affected

- `godot/Scripts/UI/MultiplayerMenu.cs` → replaced by new `LobbyListScreen.cs`
- `godot/Scripts/UI/SlotConfigScreen.cs` → replaced by new `GameLobbyScreen.cs`
- `godot/Scripts/UI/MapSelectScreen.cs` → removed (map selection integrated into GameLobbyScreen)
- `godot/Scenes/MultiplayerMenu.tscn` → replaced
- `godot/Scenes/SlotConfig.tscn` → replaced
- `godot/Scenes/MapSelect.tscn` → removed
- `godot/Scripts/UI/MenuButton.cs` — reused as-is
- `godot/Scripts/UI/MenuGrid.cs` — reused as-is

## Open Questions

1. **Lobby list protocol**: The relay currently uses 4-char room codes (create/join). Listing available lobbies requires a new relay message type (ListRooms / RoomList). Do we add that, or keep room codes as the join mechanism and skip the lobby list for now?
2. **Map miniature rendering**: Simple colored-dot diagram vs actual mini GridRenderer?
3. **Ready state**: Currently there's no ready protocol — host just clicks Start when 2+ players are in. Do we add a ready toggle, or keep it simple?

## Existing Code Patterns to Preserve

- `MultiplayerLaunchData` / `GameLaunchData` statics for cross-scene handoff
- Relay event handlers stored as fields, unsubscribed in `_ExitTree`
- `DrainInbound` timer pattern for relay message processing
- `MapFileManager` for loading/listing maps
