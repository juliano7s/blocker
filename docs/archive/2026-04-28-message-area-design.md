# Message Area Design

In-game message log and chat input. Center-aligned at the bottom of the screen, above the HUD panels. Shows game info, alerts, team pings, and multiplayer chat. Active in both singleplayer (system + alerts) and multiplayer (all categories).

## Message Data Model

```csharp
enum MessageCategory { System, Alert, TeamPing, Chat }

struct MessageEntry
{
    MessageCategory Category;
    string Text;
    Color? PlayerColor;    // null for System messages
    string? PlayerName;    // null for System messages
    float GameTime;        // game time in seconds (tick / TPS)
    double CreatedAtReal;  // OS.GetTicksMsec() for fade timing
}
```

Stored in a circular buffer of 50 entries. Only the most recent 8 are drawn on screen at once.

### Category Colors

| Category  | Name Color        | Text Color        | Example                          |
|-----------|-------------------|-------------------|----------------------------------|
| System    | —                 | `#a0aec0` (dim)   | Builder nest formed              |
| Alert     | —                 | `#fc8181` (red)    | Nest under attack (future)       |
| Alert     | —                 | `#f6ad55` (amber)  | Population cap reached           |
| TeamPing  | player color      | player color       | [Blue] Enemy here                |
| Chat      | player color      | `#e2e8f0` (white)  | [Blue]: push mid together?       |

Timestamps shown as `[MM:SS]` in `#718096` before each message.

## Layout & Positioning

The message area occupies the center-bottom gap between the minimap (left) and selection/command panels (right).

```
┌─────────────────────────────────────────────────────────┐
│  Top Bar (HudOverlay)                                   │
├─────────────────────────────────────────────────────────┤
│                                                         │
│                    Game Grid                             │
│                                                         │
│              ┌──────────────────────┐                   │
│              │  [01:23] Nest formed  │ ← messages grow  │
│              │  [02:45] ⚠ Attack!   │   upward          │
│              │  [03:10] P3 elim'd   │                   │
│              │  [04:02] [B]: push?  │                   │
│              │  Chat: nice push_|   │ ← input (Enter)   │
│              └──────────────────────┘                   │
│ ┌──────────┐                        ┌────────┬────────┐ │
│ │ Minimap  │                        │ Select │  Cmd   │ │
│ └──────────┘                        └────────┴────────┘ │
└─────────────────────────────────────────────────────────┘
```

**Positioning constants** (in HudStyles):
- Width: `420px`, center-aligned horizontally
- Bottom edge: sits `HudStyles.BottomPanelMargin + max(MinimapSize, CommandCardHeight) + 8px` above the screen bottom — just above the tallest bottom panel
- Messages stack upward from the bottom of the area
- Max visible lines: 8

## Interaction Model

### States

The message area has two states: **Passive** and **Active**.

**Passive (default):**
- New messages appear, display for 5 seconds, then fade out over 1 second
- No input field visible
- Messages don't consume mouse input (MouseFilter.Ignore)
- Faded-out messages are not drawn

**Active (Enter pressed):**
- All recent messages (up to 8) become fully visible (alpha = 1.0)
- Chat input bar appears at the bottom with a `LineEdit`
- `LineEdit` grabs focus immediately
- Pressing Enter with text: sends the message, returns to Passive
- Pressing Enter with empty input: returns to Passive
- Pressing Escape: returns to Passive (discards input)
- On return to Passive: all visible messages get a fresh 5-second display timer before fading

### Input Blocking

When Active, the `LineEdit` consumes keyboard input. The message area sets `MouseFilter.Stop` on the input bar only. Game input (selection, commands, camera) remains fully functional via mouse. The `_UnhandledInput` handler for Enter is consumed with `AcceptEvent()` so it doesn't trigger game commands.

## Rendering (Hybrid _Draw + LineEdit)

### Message Log — Custom _Draw()

A `Control` node inside a `CanvasLayer` (Layer 10, same as HudBar/HudOverlay). Draws messages bottom-to-top using `DrawString`:

```
For each visible message (newest first, up to 8):
    y = baseY - (index * lineHeight)
    alpha = computed from fade state
    Draw timestamp in #718096 at alpha
    Draw player name in PlayerColor at alpha (if present)
    Draw text in category color at alpha
```

Line height: 18px. Font size: `HudStyles.FontSizeSmall` (11).

Fade calculation per message:
```
elapsed = now - message.CreatedAtReal
if elapsed < displayDuration (5s):
    alpha = 1.0
elif elapsed < displayDuration + fadeDuration (1s):
    alpha = 1.0 - (elapsed - displayDuration) / fadeDuration
else:
    alpha = 0.0 (skip drawing)
```

When Active, all visible messages are drawn at alpha = 1.0 regardless of age.

The draw control calls `QueueRedraw()` every frame only when messages are visible or fading. When all messages are fully faded and state is Passive, no redraws occur.

### Chat Input — Godot LineEdit

A `LineEdit` node, child of the same CanvasLayer. Styled to match HUD:
- Background: `HudStyles.PanelBgTop` (#141922)
- Border: `HudStyles.PanelBorder` (#2d3748)
- Text color: `HudStyles.TextPrimary`
- Font size: `HudStyles.FontSizeSmall`
- Prefix label "Chat:" in player color (drawn via the `_Draw()` control, not a separate Label)
- Max length: 128 characters

Shown/hidden by toggling `Visible` on Enter/Escape.

## Message Sources

### 1. VisualEvent → System/Alert Messages (Singleplayer + Multiplayer)

The `MessageArea` reads `GameState.VisualEvents` each tick (same pattern as EffectManager) and maps select events to messages:

| VisualEvent              | Category | Text                              |
|--------------------------|----------|-----------------------------------|
| PlayerEliminated         | System   | "Player {N} eliminated"           |
| PopCapWarning            | Alert    | "Population cap reached"          |
| GameOver                 | Alert    | "Game over — Team {N} wins"       |

Not every VisualEvent needs a message — most are too frequent or not player-relevant. Only notable game-state changes get logged.

Additionally, some system messages are triggered directly by Godot UI actions rather than VisualEvents:

| Source                   | Category | Text                              |
|--------------------------|----------|-----------------------------------|
| SpawnToggle changed      | System   | "Spawn builder enabled/disabled"  |
| SpawnToggle changed      | System   | "Spawn soldier enabled/disabled"  |
| SpawnToggle changed      | System   | "Spawn stunner enabled/disabled"  |
| Nest refine toggled      | System   | "Refine nugget enabled/disabled"  |

These are pushed via `MessageArea.AddSystemMessage()` from the code that handles the toggle.

The VisualEvent mapping lives in a simple switch in the Godot layer (not simulation). Easy to tune — add/remove events without touching simulation code.

### 2. Chat Messages (Multiplayer Only)

New relay protocol message using the reserved social range:

```
0x30 ChatMessage
  [1 byte]  senderId (slot index)
  [1 byte]  textLength
  [N bytes] UTF-8 text (max 128 bytes)
```

Flow:
1. Player types message, presses Enter
2. `MessageArea` calls `RelayClient.SendChat(text)`
3. Relay broadcasts to all players in the room (including sender for confirmation)
4. `RelayClient` fires `ChatReceived(int slotId, string text)` event
5. `MessageArea` creates a Chat `MessageEntry`

Chat is **not** routed through lockstep/commands — it's fire-and-forget social traffic. No determinism requirement. Messages may arrive at slightly different times on different clients, which is fine for chat.

`IRelayClient` gets two new members:
```csharp
void SendChat(string text);
event Action<int /*slotId*/, string /*text*/>? ChatReceived;
```

`FakeRelayClient` implements them as no-ops (singleplayer has no chat).

### 3. Team Pings (Multiplayer Only)

Ping messages use the same relay social channel:

```
0x31 TeamPing
  [1 byte]  senderId
  [1 byte]  pingType (enum: Help, EnemyHere, OnMyWay, Acknowledge)
  [2 bytes] gridX (int16)
  [2 bytes] gridY (int16)
```

Flow:
1. Player triggers ping (Alt+click on minimap or a ping wheel — future feature)
2. Relay broadcasts to teammates (team-filtered on client, or relay-side if we add team routing later)
3. `MessageArea` displays "[Blue] Enemy here" in player color
4. Minimap/grid shows a brief ping indicator at the location (separate visual, not part of this spec)

Ping types:
```csharp
enum PingType : byte { Help = 0, EnemyHere = 1, OnMyWay = 2, Acknowledge = 3 }
```

For the initial implementation, pings can be deferred — chat + system messages are the core. Pings are listed here for protocol reservation.

## Component Architecture

```
GameManager
├── HudOverlay (CanvasLayer 10) — existing
├── HudBar (CanvasLayer 10) — existing
└── MessageArea (CanvasLayer 10) — NEW
    ├── MessageDrawControl (Control) — _Draw() for message log
    └── ChatInput (LineEdit) — text input, hidden by default
```

`MessageArea` is a `CanvasLayer` created by `GameManager` alongside HudBar/HudOverlay. GameManager passes it:
- `GameState` reference (for VisualEvent reading + game time)
- `GameConfig` (for player color palette)
- Controlling player ID
- `IRelayClient?` (null in singleplayer — chat disabled)
- Player slot assignments (slot → name/color mapping)

### MessageArea Public API

```csharp
public partial class MessageArea : CanvasLayer
{
    public void SetGameState(GameState state);
    public void SetConfig(GameConfig config);
    public void SetControllingPlayer(int playerId);
    public void SetRelay(IRelayClient? relay, Dictionary<int, string>? slotNames);
    public void AddSystemMessage(string text);
    public void AddAlert(string text);
}
```

`AddSystemMessage` and `AddAlert` are public so other systems (e.g., GameOverOverlay, future tutorials) can push messages without going through VisualEvents.

## Performance

- `_Draw()` only when messages are visible or fading — idle games have zero overhead
- Circular buffer avoids allocations after warmup
- No per-frame string allocations — message text is built once on creation
- `QueueRedraw()` throttled: skip when all alphas are 0 and state is Passive
- Chat relay messages are tiny (max ~130 bytes) and infrequent

## Testing

- **Unit tests** (xUnit): Protocol serialization/deserialization for ChatMessage and TeamPing (0x30, 0x31)
- **Unit tests**: Circular buffer — add 60 messages, verify oldest are evicted, newest 50 retained
- **Unit tests**: VisualEvent → message mapping — given a VisualEvent, verify correct category and text
- **Manual**: Verify positioning in different window sizes, fade timing, Enter/Escape flow, chat send/receive in multiplayer
