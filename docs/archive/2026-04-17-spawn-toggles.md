# Spawn Toggles Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move spawn toggles into the top bar (centered), cover all 5 unit types with real sprites, and wire a new `ToggleSpawn` simulation command so the feature is lockstep-safe.

**Architecture:** Simulation layer gets `ToggleSpawn` command + `Player.SpawnDisabled` state + NestSystem check. Godot layer refactors `SpawnToggles` (5 types, sprites, glow ring, hover cursor, tooltip, Alt hotkeys) and moves it into `HudOverlay` centered in the top bar. Signal flows: SpawnToggles → HudOverlay → GameManager → SelectionManager.

**Tech Stack:** C# / Godot 4 / xUnit. No relay server changes needed.

---

## File Map

| File | Change |
|------|--------|
| `src/Blocker.Simulation/Commands/Command.cs` | Add `BlockType? UnitType` field; add `ToggleSpawn` to `CommandType` |
| `src/Blocker.Simulation/Core/Player.cs` | Add `HashSet<BlockType> SpawnDisabled` |
| `src/Blocker.Simulation/Core/GameState.cs` | Handle `ToggleSpawn` in `ProcessCommands` |
| `src/Blocker.Simulation/Systems/NestSystem.cs` | Check `SpawnDisabled` before spawning in `TickSpawning` |
| `src/Blocker.Simulation/Net/CommandSerializer.cs` | Encode/decode `UnitType` field |
| `src/Blocker.Simulation/Net/StateHasher.cs` | Mix `SpawnDisabled` into player hash |
| `godot/Scripts/Rendering/SpawnToggles.cs` | Full refactor: 5 types, sprites, glow ring, hover, tooltip, Alt hotkeys |
| `godot/Scripts/Rendering/HudOverlay.cs` | Add SpawnToggles child; relay signal; pass state + player |
| `godot/Scripts/Game/GameManager.cs` | Remove standalone SpawnToggles setup; wire HudOverlay signal |
| `tests/Blocker.Simulation.Tests/NestTests.cs` | Add spawn-toggle suppression tests |
| `tests/Blocker.Simulation.Tests/Net/CommandSerializerTests.cs` | Add UnitType roundtrip test |
| `tests/Blocker.Simulation.Tests/Net/StateHasherTests.cs` | Add SpawnDisabled hash test |

---

## Task 1: Add `ToggleSpawn` to `CommandType` and `UnitType` to `Command`

**Files:**
- Modify: `src/Blocker.Simulation/Commands/Command.cs`

- [ ] **Add `ToggleSpawn` to the enum and `UnitType` to the record**

  Open `src/Blocker.Simulation/Commands/Command.cs` and apply both changes:

  ```csharp
  public enum CommandType
  {
      Move,
      Root,
      ConvertToWall,
      FireStunRay,
      SelfDestruct,
      CreateTower,
      TogglePush,
      MagnetPull,
      Jump,
      AttackMove,
      Surrender,
      ToggleSpawn,  // Player-level: toggle spawn for a unit type. UnitType field required.
  }

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

- [ ] **Build to confirm no errors**

  ```bash
  dotnet build src/Blocker.Simulation/Blocker.Simulation.csproj
  ```
  Expected: Build succeeded, 0 errors.

- [ ] **Commit**

  ```bash
  git add src/Blocker.Simulation/Commands/Command.cs
  git commit -m "feat(sim): add ToggleSpawn command type and UnitType field to Command"
  ```

---

## Task 2: Add `SpawnDisabled` to `Player` and handle `ToggleSpawn` in `GameState`

**Files:**
- Modify: `src/Blocker.Simulation/Core/Player.cs`
- Modify: `src/Blocker.Simulation/Core/GameState.cs`

- [ ] **Add `SpawnDisabled` to `Player`**

  Replace the entire content of `src/Blocker.Simulation/Core/Player.cs`:

  ```csharp
  using Blocker.Simulation.Blocks;

  namespace Blocker.Simulation.Core;

  public class Player
  {
      public int Id { get; init; }
      public int TeamId { get; init; }
      public int Population { get; set; }
      public int MaxPopulation { get; set; }
      public bool IsEliminated { get; set; }

      /// <summary>Unit types whose nests will not spawn. Empty = all enabled.</summary>
      public HashSet<BlockType> SpawnDisabled { get; } = new();
  }
  ```

- [ ] **Handle `ToggleSpawn` in `GameState.ProcessCommands`**

  In `src/Blocker.Simulation/Core/GameState.cs`, find the `Surrender` handler in `ProcessCommands` (around line 150). After the `Surrender` block and before the `foreach (var blockId in cmd.BlockIds)` loop, add:

  ```csharp
  if (cmd.Type == CommandType.ToggleSpawn)
  {
      if (cmd.UnitType is { } unitType)
      {
          var player = Players.FirstOrDefault(p => p.Id == cmd.PlayerId);
          if (player != null && !player.IsEliminated)
          {
              if (!player.SpawnDisabled.Remove(unitType))
                  player.SpawnDisabled.Add(unitType);
          }
      }
      continue;
  }
  ```

- [ ] **Build**

  ```bash
  dotnet build src/Blocker.Simulation/Blocker.Simulation.csproj
  ```
  Expected: Build succeeded, 0 errors.

- [ ] **Commit**

  ```bash
  git add src/Blocker.Simulation/Core/Player.cs src/Blocker.Simulation/Core/GameState.cs
  git commit -m "feat(sim): add Player.SpawnDisabled and handle ToggleSpawn command"
  ```

---

## Task 3: Check `SpawnDisabled` in `NestSystem.TickSpawning`

**Files:**
- Modify: `src/Blocker.Simulation/Systems/NestSystem.cs`
- Modify: `tests/Blocker.Simulation.Tests/NestTests.cs`

- [ ] **Write the failing tests**

  In `tests/Blocker.Simulation.Tests/NestTests.cs`, add these tests at the end of the class:

  ```csharp
  [Fact]
  public void ToggleSpawn_Disabled_SuppressesUnitSpawn()
  {
      var state = CreateState();
      var center = new GridPos(5, 5);
      SetGroundType(state, center, GroundType.Boot);

      AddRootedBlock(state, BlockType.Builder, 0, new GridPos(5, 4));
      AddRootedBlock(state, BlockType.Builder, 0, new GridPos(6, 5));
      AddRootedBlock(state, BlockType.Builder, 0, new GridPos(5, 6));
      NestSystem.DetectNests(state);
      Assert.Single(state.Nests);

      // Disable Builder spawning for player 0
      state.Players[0].SpawnDisabled.Add(BlockType.Builder);

      // Advance to spawn threshold
      var nest = state.Nests[0];
      var ground = state.Grid[center].Ground;
      int spawnTicks = nest.GetSpawnTicks(ground);
      nest.SpawnProgress = spawnTicks - 1;
      NestSystem.TickSpawning(state);

      // No unit should have been spawned
      Assert.DoesNotContain(state.Blocks, b => b.Type == BlockType.Builder && b.FormationId == null);
      // Progress reset for retry
      Assert.Equal(0, nest.SpawnProgress);
  }

  [Fact]
  public void ToggleSpawn_ReEnabled_AllowsSpawnAgain()
  {
      var state = CreateState();
      var center = new GridPos(5, 5);
      SetGroundType(state, center, GroundType.Boot);

      AddRootedBlock(state, BlockType.Builder, 0, new GridPos(5, 4));
      AddRootedBlock(state, BlockType.Builder, 0, new GridPos(6, 5));
      AddRootedBlock(state, BlockType.Builder, 0, new GridPos(5, 6));
      NestSystem.DetectNests(state);

      var nest = state.Nests[0];
      var ground = state.Grid[center].Ground;
      int spawnTicks = nest.GetSpawnTicks(ground);

      // Disable then re-enable
      state.Players[0].SpawnDisabled.Add(BlockType.Builder);
      state.Players[0].SpawnDisabled.Remove(BlockType.Builder);

      int blocksBefore = state.Blocks.Count;
      nest.SpawnProgress = spawnTicks - 1;
      NestSystem.TickSpawning(state);

      Assert.True(state.Blocks.Count > blocksBefore);
  }
  ```

- [ ] **Run tests to confirm they fail**

  ```bash
  dotnet test tests/Blocker.Simulation.Tests/Blocker.Simulation.Tests.csproj --filter "ToggleSpawn_Disabled_SuppressesUnitSpawn|ToggleSpawn_ReEnabled_AllowsSpawnAgain"
  ```
  Expected: 2 tests FAIL.

- [ ] **Add the spawn-disabled check in `NestSystem.TickSpawning`**

  In `src/Blocker.Simulation/Systems/NestSystem.cs`, find the `TickSpawning` method. After the pop-cap check block (after `continue;` on congested), add the spawn-disabled check just before `state.AddBlock(...)`:

  The section currently reads:
  ```csharp
  // Find spawn cell: center first, then BFS outward up to 3 cells
  var spawnPos = FindSpawnCell(state, nest.Center);
  if (!spawnPos.HasValue)
  {
      // Congested — hold and retry
      nest.SpawnProgress = spawnTicks;
      continue;
  }

  // Spawn the unit (AddBlock handles HP for soldier/jumper)
  var spawned = state.AddBlock(spawnType, nest.PlayerId, spawnPos.Value);
  ```

  Change it to:

  ```csharp
  // Find spawn cell: center first, then BFS outward up to 3 cells
  var spawnPos = FindSpawnCell(state, nest.Center);
  if (!spawnPos.HasValue)
  {
      // Congested — hold and retry
      nest.SpawnProgress = spawnTicks;
      continue;
  }

  // Check spawn toggle — skip unit but reset progress for retry next cycle
  if (player != null && player.SpawnDisabled.Contains(spawnType))
  {
      nest.SpawnProgress = 0;
      continue;
  }

  // Spawn the unit (AddBlock handles HP for soldier/jumper)
  var spawned = state.AddBlock(spawnType, nest.PlayerId, spawnPos.Value);
  ```

- [ ] **Run tests to confirm they pass**

  ```bash
  dotnet test tests/Blocker.Simulation.Tests/Blocker.Simulation.Tests.csproj --filter "ToggleSpawn_Disabled_SuppressesUnitSpawn|ToggleSpawn_ReEnabled_AllowsSpawnAgain"
  ```
  Expected: 2 tests PASS.

- [ ] **Run full test suite**

  ```bash
  dotnet test tests/Blocker.Simulation.Tests/Blocker.Simulation.Tests.csproj
  ```
  Expected: All tests pass.

- [ ] **Commit**

  ```bash
  git add src/Blocker.Simulation/Systems/NestSystem.cs tests/Blocker.Simulation.Tests/NestTests.cs
  git commit -m "feat(sim): suppress spawn when unit type is in player SpawnDisabled"
  ```

---

## Task 4: Update `CommandSerializer` to encode/decode `UnitType`

**Files:**
- Modify: `src/Blocker.Simulation/Net/CommandSerializer.cs`
- Modify: `tests/Blocker.Simulation.Tests/Net/CommandSerializerTests.cs`

- [ ] **Write the failing test**

  In `tests/Blocker.Simulation.Tests/Net/CommandSerializerTests.cs`, add after existing tests:

  ```csharp
  [Fact]
  public void ToggleSpawn_UnitType_Roundtrips()
  {
      var input = new TickCommands(
          PlayerId: 0,
          Tick: 10,
          Commands: new[]
          {
              new Command(0, CommandType.ToggleSpawn, new List<int>(),
                  UnitType: BlockType.Soldier),
              new Command(0, CommandType.ToggleSpawn, new List<int>(),
                  UnitType: BlockType.Jumper),
          });

      var bytes = CommandSerializer.Serialize(input);
      var output = CommandSerializer.Deserialize(bytes);

      Assert.Equal(2, output.Commands.Count);
      Assert.Equal(BlockType.Soldier, output.Commands[0].UnitType);
      Assert.Equal(BlockType.Jumper,  output.Commands[1].UnitType);
  }

  [Fact]
  public void Commands_Without_UnitType_Deserialize_As_Null()
  {
      var input = new TickCommands(
          PlayerId: 0,
          Tick: 1,
          Commands: new[] { new Command(0, CommandType.Move, new List<int> { 1 }, TargetPos: new GridPos(2, 3)) });

      var bytes = CommandSerializer.Serialize(input);
      var output = CommandSerializer.Deserialize(bytes);

      Assert.Null(output.Commands[0].UnitType);
  }
  ```

- [ ] **Run tests to confirm they fail**

  ```bash
  dotnet test tests/Blocker.Simulation.Tests/Blocker.Simulation.Tests.csproj --filter "ToggleSpawn_UnitType_Roundtrips|Commands_Without_UnitType_Deserialize_As_Null"
  ```
  Expected: 2 tests FAIL (UnitType always null after deserialization).

- [ ] **Update `CommandSerializer` to encode/decode `UnitType`**

  Replace the entire content of `src/Blocker.Simulation/Net/CommandSerializer.cs`:

  ```csharp
  using Blocker.Simulation.Blocks;
  using Blocker.Simulation.Commands;
  using Blocker.Simulation.Core;

  namespace Blocker.Simulation.Net;

  /// <summary>
  /// Binary wire format for a TickCommands payload. Does NOT include the 0x10
  /// type byte — the caller prepends it.
  ///
  /// Layout:
  ///   [tick: varint]
  ///   [playerId: byte]
  ///   [count: varint]
  ///   per command:
  ///     [type: byte]
  ///     [blockCount: varint]
  ///     [blockIds: varint × blockCount]
  ///     [flags: byte]         bit0 = hasTargetPos, bit1 = hasDirection, bit2 = queue, bit3 = hasUnitType
  ///     [targetX, targetY: varint × 2] if hasTargetPos
  ///     [direction: byte]               if hasDirection
  ///     [unitType: byte]                if hasUnitType
  ///
  /// Determinism: no dictionary iteration, no locale, no floats, little-endian only.
  /// </summary>
  public static class CommandSerializer
  {
      private const byte FlagHasTargetPos = 0x01;
      private const byte FlagHasDirection = 0x02;
      private const byte FlagQueue        = 0x04;
      private const byte FlagHasUnitType  = 0x08;

      public static byte[] Serialize(TickCommands tc)
      {
          int bound = 16;
          foreach (var c in tc.Commands)
              bound += 24 + c.BlockIds.Count * Varint.MaxBytes;

          var buf = new byte[bound];
          int i = 0;
          i += Varint.Write(buf, i, (uint)tc.Tick);
          buf[i++] = (byte)tc.PlayerId;
          i += Varint.Write(buf, i, (uint)tc.Commands.Count);

          foreach (var c in tc.Commands)
          {
              buf[i++] = (byte)c.Type;
              i += Varint.Write(buf, i, (uint)c.BlockIds.Count);
              foreach (var id in c.BlockIds)
                  i += Varint.Write(buf, i, (uint)id);
              byte flags = 0;
              if (c.TargetPos.HasValue)  flags |= FlagHasTargetPos;
              if (c.Direction.HasValue)  flags |= FlagHasDirection;
              if (c.Queue)               flags |= FlagQueue;
              if (c.UnitType.HasValue)   flags |= FlagHasUnitType;
              buf[i++] = flags;
              if (c.TargetPos.HasValue)
              {
                  i += Varint.Write(buf, i, (uint)c.TargetPos.Value.X);
                  i += Varint.Write(buf, i, (uint)c.TargetPos.Value.Y);
              }
              if (c.Direction.HasValue)
                  buf[i++] = (byte)c.Direction.Value;
              if (c.UnitType.HasValue)
                  buf[i++] = (byte)c.UnitType.Value;
          }

          var result = new byte[i];
          Array.Copy(buf, result, i);
          return result;
      }

      public static TickCommands Deserialize(ReadOnlySpan<byte> buf)
      {
          int i = 0;
          var (tick, n1) = Varint.Read(buf, i); i += n1;
          int playerId = buf[i++];
          var (count, n2) = Varint.Read(buf, i); i += n2;

          var list = new List<Command>((int)count);
          for (uint c = 0; c < count; c++)
          {
              var type = (CommandType)buf[i++];
              var (blockCount, n3) = Varint.Read(buf, i); i += n3;
              var ids = new List<int>((int)blockCount);
              for (uint b = 0; b < blockCount; b++)
              {
                  var (id, n4) = Varint.Read(buf, i); i += n4;
                  ids.Add((int)id);
              }
              byte flags = buf[i++];
              GridPos? target = null;
              Direction? dir = null;
              BlockType? unitType = null;
              if ((flags & FlagHasTargetPos) != 0)
              {
                  var (x, n5) = Varint.Read(buf, i); i += n5;
                  var (y, n6) = Varint.Read(buf, i); i += n6;
                  target = new GridPos((int)x, (int)y);
              }
              if ((flags & FlagHasDirection) != 0)
                  dir = (Direction)buf[i++];
              bool queue = (flags & FlagQueue) != 0;
              if ((flags & FlagHasUnitType) != 0)
                  unitType = (BlockType)buf[i++];
              list.Add(new Command(playerId, type, ids, target, dir, queue, unitType));
          }
          return new TickCommands(playerId, (int)tick, list);
      }

      /// <summary>
      /// Cheap header peek used by the relay for auth. Does not allocate.
      /// </summary>
      public static (int tick, int playerId) PeekTickAndPlayer(ReadOnlySpan<byte> buf)
      {
          var (tick, consumed) = Varint.Read(buf, 0);
          int playerId = buf[consumed];
          return ((int)tick, playerId);
      }
  }
  ```

- [ ] **Run tests to confirm they pass**

  ```bash
  dotnet test tests/Blocker.Simulation.Tests/Blocker.Simulation.Tests.csproj --filter "ToggleSpawn_UnitType_Roundtrips|Commands_Without_UnitType_Deserialize_As_Null"
  ```
  Expected: 2 tests PASS.

- [ ] **Run full test suite**

  ```bash
  dotnet test tests/Blocker.Simulation.Tests/Blocker.Simulation.Tests.csproj
  ```
  Expected: All tests pass.

- [ ] **Commit**

  ```bash
  git add src/Blocker.Simulation/Net/CommandSerializer.cs tests/Blocker.Simulation.Tests/Net/CommandSerializerTests.cs
  git commit -m "feat(sim): encode/decode UnitType in CommandSerializer"
  ```

---

## Task 5: Update `StateHasher` to include `SpawnDisabled`

**Files:**
- Modify: `src/Blocker.Simulation/Net/StateHasher.cs`
- Modify: `tests/Blocker.Simulation.Tests/Net/StateHasherTests.cs`

- [ ] **Write the failing test**

  In `tests/Blocker.Simulation.Tests/Net/StateHasherTests.cs`, add:

  ```csharp
  [Fact]
  public void SpawnDisabled_Change_Changes_Hash()
  {
      var a = MakeState();
      var b = MakeState();

      b.Players[0].SpawnDisabled.Add(BlockType.Soldier);

      Assert.NotEqual(StateHasher.Hash(a), StateHasher.Hash(b));
  }

  [Fact]
  public void SpawnDisabled_Same_Content_Same_Hash()
  {
      var a = MakeState();
      var b = MakeState();

      a.Players[0].SpawnDisabled.Add(BlockType.Builder);
      b.Players[0].SpawnDisabled.Add(BlockType.Builder);

      Assert.Equal(StateHasher.Hash(a), StateHasher.Hash(b));
  }
  ```

- [ ] **Run tests to confirm they fail**

  ```bash
  dotnet test tests/Blocker.Simulation.Tests/Blocker.Simulation.Tests.csproj --filter "SpawnDisabled_Change_Changes_Hash|SpawnDisabled_Same_Content_Same_Hash"
  ```
  Expected: 2 tests FAIL.

- [ ] **Update `StateHasher` to mix in `SpawnDisabled`**

  In `src/Blocker.Simulation/Net/StateHasher.cs`, find the players loop. After `MixI32(ref h, p.IsEliminated ? 1 : 0);`, add:

  ```csharp
  // SpawnDisabled — sorted for determinism
  var disabledSorted = p.SpawnDisabled.Select(t => (int)t).OrderBy(t => t).ToArray();
  MixI32(ref h, disabledSorted.Length);
  foreach (var t in disabledSorted)
      MixI32(ref h, t);
  ```

- [ ] **Run tests to confirm they pass**

  ```bash
  dotnet test tests/Blocker.Simulation.Tests/Blocker.Simulation.Tests.csproj --filter "SpawnDisabled_Change_Changes_Hash|SpawnDisabled_Same_Content_Same_Hash"
  ```
  Expected: 2 tests PASS.

- [ ] **Run full test suite**

  ```bash
  dotnet test tests/Blocker.Simulation.Tests/Blocker.Simulation.Tests.csproj
  ```
  Expected: All tests pass.

- [ ] **Commit**

  ```bash
  git add src/Blocker.Simulation/Net/StateHasher.cs tests/Blocker.Simulation.Tests/Net/StateHasherTests.cs
  git commit -m "feat(sim): include SpawnDisabled in StateHasher"
  ```

---

## Task 6: Refactor `SpawnToggles.cs`

**Files:**
- Modify: `godot/Scripts/Rendering/SpawnToggles.cs`

Replace the entire file. The new version:
- Covers 5 unit types
- Draws real sprites via `SpriteFactory`
- Shows glow ring when enabled, dims sprite when disabled
- Tracks hover for pointer cursor + hover highlight
- Draws tooltip on hover
- Reads toggle state from `GameState` (not local array)
- Hotkeys: Alt+Q/W/E/A/S

- [ ] **Replace `SpawnToggles.cs`**

  ```csharp
  using Blocker.Game.Config;
  using Blocker.Simulation.Blocks;
  using Blocker.Simulation.Core;
  using Godot;

  namespace Blocker.Game.Rendering;

  /// <summary>
  /// Horizontal row of spawn-toggle buttons centered in the top bar.
  /// One button per spawnable unit type. Reads toggle state from GameState.
  /// </summary>
  public partial class SpawnToggles : Control
  {
      [Signal] public delegate void SpawnToggleChangedEventHandler(int unitType);

      private GameState? _gameState;
      private int _controllingPlayer;
      private int _hoveredIndex = -1;

      public const float ButtonSize = 30f;
      public const float ButtonGap = 8f;
      public const int UnitCount = 5;
      public const float TotalWidth = UnitCount * ButtonSize + (UnitCount - 1) * ButtonGap;

      private static readonly BlockType[] UnitTypes =
          [BlockType.Builder, BlockType.Soldier, BlockType.Stunner, BlockType.Warden, BlockType.Jumper];

      private static readonly Color[] GlowColors =
      [
          new(0.231f, 0.510f, 0.965f), // Builder  #3b82f6
          new(0.133f, 0.773f, 0.369f), // Soldier  #22c55e
          new(0.659f, 0.333f, 0.969f), // Stunner  #a855f7
          new(0.231f, 0.510f, 0.965f), // Warden   same as Builder
          new(0.133f, 0.773f, 0.369f), // Jumper   same as Soldier
      ];

      private static readonly Key[] HotkeyKeys =
          [Key.Q, Key.W, Key.E, Key.A, Key.S];

      private static readonly string[] HotkeyLabels =
          ["Alt+Q", "Alt+W", "Alt+E", "Alt+A", "Alt+S"];

      private static readonly string[] UnitNames =
          ["Builder", "Soldier", "Stunner", "Warden", "Jumper"];

      public void SetGameState(GameState state) => _gameState = state;
      public void SetControllingPlayer(int playerId) => _controllingPlayer = playerId;

      public override void _Ready()
      {
          CustomMinimumSize = new Vector2(TotalWidth, ButtonSize);
          Size = CustomMinimumSize;
          MouseFilter = MouseFilterEnum.Stop;
      }

      public override CursorShape _GetCursorShape(Vector2 position)
          => GetButtonIndexAt(position) >= 0 ? CursorShape.PointingHand : CursorShape.Arrow;

      public override void _Draw()
      {
          var font = ThemeDB.FallbackFont;

          for (int i = 0; i < UnitCount; i++)
          {
              var btnRect = GetButtonRect(i);
              bool enabled = IsEnabled(i);
              bool hovered = i == _hoveredIndex;
              var glowColor = GlowColors[i];

              // Button background
              DrawRect(btnRect, new Color(0f, 0f, 0f, hovered ? 0.35f : 0.2f));

              // Sprite
              var sprite = SpriteFactory.GetSprite(UnitTypes[i], _controllingPlayer);
              if (sprite != null)
              {
                  float spriteInset = (ButtonSize - 22f) / 2f;
                  var spriteRect = new Rect2(
                      btnRect.Position + new Vector2(spriteInset, spriteInset),
                      new Vector2(22f, 22f));
                  DrawTextureRect(sprite, spriteRect, false,
                      enabled ? Colors.White : new Color(1f, 1f, 1f, 0.28f));
              }

              // Glow ring when enabled
              if (enabled)
              {
                  DrawRect(btnRect, glowColor with { A = 0.0f });
                  DrawRect(btnRect, glowColor, false, 2f);
                  // Outer soft glow
                  var outerRect = btnRect.Grow(1.5f);
                  DrawRect(outerRect, glowColor with { A = 0.25f }, false, 1f);
              }

              // Hover brightness overlay
              if (hovered)
                  DrawRect(btnRect, new Color(1f, 1f, 1f, 0.08f));
          }

          // Tooltip for hovered button
          if (_hoveredIndex >= 0)
          {
              var btnRect = GetButtonRect(_hoveredIndex);
              string tipText = $"{HotkeyLabels[_hoveredIndex]} — {UnitNames[_hoveredIndex]}";
              var tipSize = font.GetStringSize(tipText, HorizontalAlignment.Left, -1, HudStyles.FontSizeSmall);
              float tipPad = 5f;
              float tipW = tipSize.X + tipPad * 2;
              float tipH = tipSize.Y + tipPad * 2;
              float tipX = btnRect.GetCenter().X - tipW / 2f;
              float tipY = btnRect.Position.Y - tipH - 4f;
              var tipRect = new Rect2(tipX, tipY, tipW, tipH);
              DrawRect(tipRect, new Color(0.05f, 0.07f, 0.10f, 0.92f));
              DrawRect(tipRect, HudStyles.PanelBorder, false, 1f);
              DrawString(font, new Vector2(tipX + tipPad, tipY + tipPad + tipSize.Y - 2f),
                  tipText, HorizontalAlignment.Left, -1, HudStyles.FontSizeSmall, HudStyles.TextPrimary);
          }
      }

      public override void _GuiInput(InputEvent @event)
      {
          if (@event is InputEventMouseMotion mm)
          {
              int newHover = GetButtonIndexAt(mm.Position);
              if (newHover != _hoveredIndex)
              {
                  _hoveredIndex = newHover;
                  QueueRedraw();
              }
          }
          else if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
          {
              int index = GetButtonIndexAt(mb.Position);
              if (index >= 0)
              {
                  EmitSignal(SignalName.SpawnToggleChanged, (int)UnitTypes[index]);
                  AcceptEvent();
              }
          }
      }

      public override void _UnhandledKeyInput(InputEvent @event)
      {
          if (@event is InputEventKey key && key.Pressed && !key.Echo && key.AltPressed)
          {
              int index = -1;
              for (int i = 0; i < HotkeyKeys.Length; i++)
              {
                  if (key.Keycode == HotkeyKeys[i]) { index = i; break; }
              }
              if (index >= 0)
              {
                  EmitSignal(SignalName.SpawnToggleChanged, (int)UnitTypes[index]);
                  GetViewport().SetInputAsHandled();
              }
          }
      }

      private bool IsEnabled(int index)
      {
          if (_gameState == null) return true;
          var player = _gameState.Players.Find(p => p.Id == _controllingPlayer);
          return player == null || !player.SpawnDisabled.Contains(UnitTypes[index]);
      }

      private static Rect2 GetButtonRect(int index)
      {
          float x = index * (ButtonSize + ButtonGap);
          return new Rect2(x, 0, ButtonSize, ButtonSize);
      }

      private int GetButtonIndexAt(Vector2 pos)
      {
          for (int i = 0; i < UnitCount; i++)
              if (GetButtonRect(i).HasPoint(pos)) return i;
          return -1;
      }
  }
  ```

- [ ] **Build Godot project**

  ```bash
  dotnet build godot/Blocker.Game.csproj
  ```
  Expected: Build succeeded, 0 errors.

- [ ] **Commit**

  ```bash
  git add godot/Scripts/Rendering/SpawnToggles.cs
  git commit -m "feat(ui): refactor SpawnToggles — 5 types, sprites, glow ring, hover, tooltip, Alt hotkeys"
  ```

---

## Task 7: Move `SpawnToggles` into `HudOverlay` and wire `GameManager`

**Files:**
- Modify: `godot/Scripts/Rendering/HudOverlay.cs`
- Modify: `godot/Scripts/Game/GameManager.cs`

- [ ] **Add `SpawnToggles` to `HudOverlay`**

  In `godot/Scripts/Rendering/HudOverlay.cs`:

  1. Add field below `private bool _showDebugFps = false;`:
     ```csharp
     private SpawnToggles _spawnToggles = null!;
     ```

  2. Add signal below the existing signals:
     ```csharp
     [Signal] public delegate void SpawnToggleChangedEventHandler(int unitType);
     ```

  3. At the end of `_Ready()`, before the closing brace, add:
     ```csharp
     // Spawn toggle buttons — centered in top bar
     _spawnToggles = new SpawnToggles();
     _spawnToggles.AnchorLeft = 0.5f;
     _spawnToggles.AnchorRight = 0.5f;
     _spawnToggles.AnchorTop = 0f;
     _spawnToggles.AnchorBottom = 0f;
     _spawnToggles.OffsetLeft = -SpawnToggles.TotalWidth / 2f;
     _spawnToggles.OffsetRight = SpawnToggles.TotalWidth / 2f;
     _spawnToggles.OffsetTop = (HudStyles.TopBarHeight - SpawnToggles.ButtonSize) / 2f;
     _spawnToggles.OffsetBottom = _spawnToggles.OffsetTop + SpawnToggles.ButtonSize;
     _spawnToggles.SpawnToggleChanged += type => EmitSignal(SignalName.SpawnToggleChanged, type);
     AddChild(_spawnToggles);
     ```

  4. Update `SetGameState` to pass state to `_spawnToggles`:
     ```csharp
     public void SetGameState(GameState state)
     {
         _gameState = state;
         _spawnToggles?.SetGameState(state);
     }
     ```

  5. Update `SetControllingPlayer` to pass player to `_spawnToggles`:
     ```csharp
     public void SetControllingPlayer(int playerId)
     {
         _controllingPlayer = playerId;
         _spawnToggles?.SetControllingPlayer(playerId);
     }
     ```

- [ ] **Update `GameManager` — remove old SpawnToggles setup, wire new signal**

  In `godot/Scripts/Game/GameManager.cs`:

  1. Remove the `private SpawnToggles _spawnToggles = null!;` field.

  2. Remove the entire spawn toggles setup block (lines ~141–152):
     ```csharp
     // Set up floating spawn toggles (top-right of game area)
     _spawnToggles = new SpawnToggles();
     var togglesLayer = new CanvasLayer { Layer = 10 };
     AddChild(togglesLayer);
     var togglesAnchor = new Control();
     togglesAnchor.SetAnchorsPreset(Control.LayoutPreset.TopRight);
     togglesAnchor.OffsetLeft = -70;
     togglesAnchor.OffsetTop = HudStyles.TopBarHeight + 20;
     togglesAnchor.OffsetRight = -20;
     togglesAnchor.OffsetBottom = HudStyles.TopBarHeight + 130;
     togglesLayer.AddChild(togglesAnchor);
     togglesAnchor.AddChild(_spawnToggles);
     ```

  3. After `_hud.SetSurrenderHandler(...)` (or wherever `_hud` is wired), add:
     ```csharp
     _hud.SpawnToggleChanged += unitType =>
         _selectionManager.IssueCommand(new Blocker.Simulation.Commands.Command(
             _selectionManager.ControllingPlayer,
             Blocker.Simulation.Commands.CommandType.ToggleSpawn,
             new System.Collections.Generic.List<int>(),
             UnitType: (BlockType)unitType));
     ```

- [ ] **Build Godot project**

  ```bash
  dotnet build godot/Blocker.Game.csproj
  ```
  Expected: Build succeeded, 0 errors.

- [ ] **Commit**

  ```bash
  git add godot/Scripts/Rendering/HudOverlay.cs godot/Scripts/Game/GameManager.cs
  git commit -m "feat(ui): move SpawnToggles into HudOverlay top bar center and wire GameManager"
  ```

---

## Task 8: Manual playtest

- [ ] **Open the Godot project and run a game**

  Launch `godot/project.godot` and start a singleplayer game.

  Check:
  - Five unit sprite buttons appear centered in the top bar
  - Sprites match the controlling player's palette color
  - Buttons have glow rings by default (all enabled)
  - Clicking a button dims its sprite and removes the ring
  - Clicking again re-enables it
  - Hovering shows a pointer cursor and the tooltip (e.g. `Alt+W — Soldier`)
  - Alt+Q through Alt+S toggle the respective units
  - After toggling off Builder, Builder nests stop producing Builders (wait one spawn cycle)
  - After re-enabling, Builders spawn again
  - No existing features broken (minimap, command card, selection panel, hotkeys for control groups)
