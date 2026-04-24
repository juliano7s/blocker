# Spawn Toggles Design

**Date**: 2026-04-17
**Status**: Approved

## Summary

Move spawn toggles from a floating top-right panel into the top bar (centered), cover all 5 spawnable unit types, use real unit sprites, and wire the toggles to a new simulation command so they're lockstep-safe.

The current `SpawnToggles` component exists as UI only — the signal it emits is never subscribed to and there is no simulation backend. This feature implements the full stack.

---

## Visual Design

### Layout

Five buttons centered horizontally in the 42px top bar, between the player info (left) and the menu button (right).

- Button size: ~30×30px
- Gap between buttons: ~8px
- Sprites: `SpriteFactory.GetSprite(type, controllingPlayerId)` — uses the player's own palette

### Toggle States

| State | Appearance |
|-------|-----------|
| **Enabled** | Full-opacity sprite + glow ring in unit color |
| **Disabled** | Sprite dimmed to ~28% opacity, no ring |

No ✕ overlay — dimming alone is sufficient. Avoids confusion with the Soldier's gold X arms.

### Hotkey Labels

No corner label on the button. On hover, a tooltip appears above the button showing the full shortcut and unit name, e.g. `Alt+Q — Toggle Builder Spawn`.

### Interactivity

- Cursor changes to pointer on hover
- Hover state: subtle brightness increase on the button background
- Click toggles the unit type

---

## Simulation Layer (`src/Blocker.Simulation/`)

### Command

Add a `BlockType? UnitType` field to `Command` in `Commands/Command.cs`:

```csharp
public record Command(
    int PlayerId,
    CommandType Type,
    List<int> BlockIds,
    GridPos? TargetPos = null,
    Direction? Direction = null,
    bool Queue = false,
    BlockType? UnitType = null   // used by ToggleSpawn
);
```

Add `ToggleSpawn` to `CommandType`:

```csharp
ToggleSpawn,  // Player-level: toggle spawn for a specific unit type. UnitType field required.
```

### Player State

Add to `Player.cs`:

```csharp
public HashSet<BlockType> SpawnDisabled { get; } = new();
```

Default = empty = all types enabled. `SpawnDisabled` contains types the player has paused.

### GameState

Handle in `ProcessCommands`:

```csharp
case CommandType.ToggleSpawn:
    if (cmd.UnitType is { } unitType)
    {
        var player = state.Players.Find(p => p.Id == cmd.PlayerId);
        if (player != null)
        {
            if (!player.SpawnDisabled.Remove(unitType))
                player.SpawnDisabled.Add(unitType);
        }
    }
    break;
```

### NestSystem

After resolving the `BlockType` to spawn, check before creating the unit:

```csharp
var spawnType = nest.GetSpawnBlockType(ground);
var owner = state.Players.Find(p => p.Id == nest.PlayerId);
if (owner != null && owner.SpawnDisabled.Contains(spawnType))
{
    // Suppress spawn — progress ticks normally, unit just doesn't appear
    nest.SpawnProgress = 0; // reset progress so it retries next cycle
    return;
}
// ... create unit normally
```

Spawn progress ticks normally when a type is toggled off. The unit is skipped when the threshold is reached and progress resets for the next cycle.

### Serialization

`CommandSerializer` must encode/decode `UnitType` for the new command type. `StateHasher` must include `SpawnDisabled` in the player hash.

---

## Godot Layer (`godot/`)

### SpawnToggles.cs

Refactor the existing component:

- **5 unit types**: `[Builder, Soldier, Stunner, Warden, Jumper]`
- **Unit colors** (for glow ring):
  - Builder: `#3b82f6`
  - Soldier: `#22c55e`
  - Stunner: `#a855f7`
  - Warden: `#3b82f6` (same palette as Builder, distinguished by sprite)
  - Jumper: Soldier palette color
- **State source**: read from `GameState.Players` each frame — no local `_spawnEnabled[]` array
- **Sprites**: drawn via `SpriteFactory.GetSprite(type, controllingPlayerId)`
- **Hover**: track mouse position in `_GuiInput`, set `DefaultCursorShape = CursorShape.PointingHand` on hover, draw a bright overlay rect on the hovered button
- **Signal**: `SpawnToggleChanged(BlockType unitType)` — emitted on click or hotkey

### Hotkeys

Handle `Alt+Q/W/E/A/S` in `_UnhandledKeyInput`:

```csharp
if (key.AltPressed && key.Pressed && !key.Echo)
{
    var type = key.Keycode switch {
        Key.Q => BlockType.Builder,
        Key.W => BlockType.Soldier,
        Key.E => BlockType.Stunner,
        Key.A => BlockType.Warden,
        Key.S => BlockType.Jumper,
        _ => (BlockType?)null
    };
    if (type != null) EmitSignal(SignalName.SpawnToggleChanged, (int)type.Value);
}
```

### HudOverlay / GameManager

- **Move anchor**: `SpawnToggles` moves from `GameManager`'s standalone `CanvasLayer` into `HudOverlay`, positioned at center-top of the top bar
- **Wire signal**: `GameManager` subscribes to `SpawnToggleChanged` → calls `SelectionManager.IssueCommand(new Command(playerId, CommandType.ToggleSpawn, [], UnitType: type))`
- **Pass state**: `HudOverlay.SetGameState(state)` already exists; `SpawnToggles` reads `state.Players.Find(p => p.Id == controllingPlayer).SpawnDisabled`
- **Controlling player**: `SpawnToggles` needs the controlling player ID to read the right player's state and fetch the right sprite palette

---

## Determinism

`ToggleSpawn` is a simulation command — it flows through the lockstep pipeline just like `Move` or `Surrender`. Every peer applies the toggle at the same tick. No client-side state divergence possible.

---

## Out of Scope

- Showing opponent spawn toggle states (spectator/observer feature)
- Per-nest overrides (global per-player toggle only)
- Persisting toggle state across matches
