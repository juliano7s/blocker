# Lobby Screens Redesign

Status: **design approved** — ready for implementation planning.

## Goal

Port the main menu's grid-based visual style to the multiplayer/AI screens and reorganize the screen flow.

## Screen Flow

```
MainMenu
  ├─ PLAY MULTIPLAYER → LobbyListScreen → GameLobbyScreen (host or joined)
  └─ PLAY VS AI → GameLobbyScreen (single-player mode)
```

## Visual Approach: Hybrid

- **MenuGrid background** on all screens (dark bg + blue grid lines, 28px cells)
- **MenuButton-style** for navigation/action buttons (colored blocks + text, blue idle / orange hover)
- **Themed Godot Controls** for functional elements (inputs, dropdowns, scrollable lists) — dark bg, blue accents, matching the palette (`#44aaff` / `rgba(68,170,255)` primary, `#ff6a33` / `rgba(255,106,51)` accent)

## Screen 1: LobbyListScreen (replaces MultiplayerMenu)

- **Top bar**: Player name input | Lobby name input | HOST NEW button
- **Lobby table**: scrollable list with columns — Lobby name, Players (e.g. 2/4), Map name, JOIN button per row
- **Bottom**: BACK button (left), lobby count label (right)
- Relay connection happens on entry, sends `ListRooms` request
- Relay responds with `RoomList` payload (array of room summaries)
- Periodic refresh or push-based updates from relay

## Screen 2: GameLobbyScreen (shared scene, replaces SlotConfigScreen + MapSelectScreen)

One scene with three modes controlled by flags. Two-column layout: slots left, map right.

### Host mode
- **Header**: lobby name + room code
- **Left column**: Slot list (player names, team tags, ready indicators, kick buttons for non-host), START button at bottom (enabled when all non-host players are ready and 2+ players)
- **Right column**: Map dropdown, Mode dropdown (FFA/Teams), map miniature
- **Bottom of left column**: Chat area (input + message list)
- Host can change map/mode; changes broadcast via relay

### Joined mode
- Same two-column layout, read-only settings: map name + mode shown as labels (no dropdowns), no kick buttons, no START button
- READY toggle button (sends `SetReady` to relay, reflected in RoomState)
- Chat area same as host

### Single-player (Play vs AI) mode
- Same two-column layout, no room code, no READY button, no mode dropdown, no chat
- Slots toggle between Player / AI (inactive) on click
- START GAME button at bottom of slot list
- Map dropdown + miniature on right

## Map Miniature

Full mini `GridRenderer` showing all map elements (terrain, spawns, nests, towers, walls) scaled down to fit the right column. Not just dots — an actual readable map preview.

## Lobby Chat

Reuses the existing chat infrastructure:
- `Protocol.ChatMessage = 0x30` already defined and relay fans it out to room members
- `RelayClient.SendChat(text)` and `ChatReceived` event already exist
- `MessageArea` in-game chat is a `CanvasLayer` with custom draw — too coupled to in-game HUD positioning
- **Lobby chat**: simpler implementation, a themed `VBoxContainer` with message labels + a `LineEdit` input at the bottom of the left column. Subscribe to `relay.ChatReceived` on enter, unsubscribe in `_ExitTree`.

## Relay Protocol Changes

### New messages (session range 0x00–0x0F)

| Byte | Name | Direction | Payload |
|------|------|-----------|---------|
| `0x0C` | `ListRooms` | Client → Server | (empty or optional filter) |
| `0x0D` | `RoomList` | Server → Client | Array of `RoomSummary` (code, name, playerCount, slotCount, mapName, gameMode) |
| `0x0E` | `SetReady` | Client → Server | 1 byte: `0x00` = not ready, `0x01` = ready |

### RoomState changes

- `SlotStateEntry` gains a `bool IsReady` field
- `RoomStatePayload` gains a `string RoomName` field (the lobby name set by host on creation)

### CreateRoom changes

- `CreateRoom` message gains a `roomName` string field (lobby display name, separate from the 4-char code)

### Server-side for ListRooms

- Server iterates rooms where `Lifecycle == Lobby`, builds `RoomSummary` for each
- Sends `RoomList` back to the requesting client
- Can be polled by the client periodically (e.g. every 2-3 seconds) or on manual refresh

### Ready state logic

- Players (non-host) toggle ready with `SetReady`
- Server updates the slot's `IsReady` flag and broadcasts updated `RoomState`
- Host's START button enables when all filled non-host slots have `IsReady == true` and 2+ players present
- Ready state resets when host changes map or mode (server clears all `IsReady` flags on `UpdateRoom`)

## Files Affected

### New files
- `godot/Scripts/UI/LobbyListScreen.cs` — lobby browser
- `godot/Scripts/UI/GameLobbyScreen.cs` — shared host/join/AI lobby
- `godot/Scripts/UI/MapMiniature.cs` — mini GridRenderer for map preview
- `godot/Scripts/UI/LobbyChatPanel.cs` — chat panel for lobby screens
- `godot/Scenes/LobbyList.tscn` — lobby browser scene
- `godot/Scenes/GameLobby.tscn` — game lobby scene

### Modified files
- `src/Blocker.Simulation/Net/Protocol.cs` — new message constants (`ListRooms`, `RoomList`, `SetReady`)
- `src/Blocker.Relay/RelayServer.cs` — handle `ListRooms`, `SetReady`; add room name
- `src/Blocker.Relay/Room.cs` — add `RoomName`, `IsReady` to `SlotInfo`
- `godot/Scripts/Net/RelayClient.cs` — send/receive new messages, update `RoomStatePayload`/`SlotStateEntry`
- `godot/Scripts/UI/MainMenu.cs` — update scene path for PLAY MULTIPLAYER (`LobbyList.tscn`)
- `src/Blocker.Simulation/Net/IRelayClient.cs` — new methods/events if needed
- `src/Blocker.Simulation/Net/FakeRelayClient.cs` — match interface changes

### Removed files
- `godot/Scripts/UI/MultiplayerMenu.cs` → replaced by `LobbyListScreen.cs`
- `godot/Scripts/UI/SlotConfigScreen.cs` → replaced by `GameLobbyScreen.cs`
- `godot/Scripts/UI/MapSelectScreen.cs` → removed (map selection integrated into GameLobbyScreen)
- `godot/Scenes/MultiplayerMenu.tscn` → replaced by `LobbyList.tscn`
- `godot/Scenes/SlotConfig.tscn` → replaced by `GameLobby.tscn`
- `godot/Scenes/MapSelect.tscn` → removed

## Existing Code Patterns to Preserve

- `MultiplayerLaunchData` / `GameLaunchData` statics for cross-scene handoff
- Relay event handlers stored as fields, unsubscribed in `_ExitTree` (see `godot/CLAUDE.md`)
- `DrainInbound` timer pattern for relay message processing
- `MapFileManager` for loading/listing maps
- Rematch flow: `RematchReattach` + `PendingRoomState` stashing must still work
