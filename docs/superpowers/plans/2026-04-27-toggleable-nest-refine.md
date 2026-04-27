# Toggleable Nest Nugget Refining — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make nugget consumption toggleable per nest, with a new command card button and a marching-ants shader for the refine zone.

**Architecture:** Add `RefineEnabled` boolean to `Nest`, a new `ToggleRefine` command type, filter auto-rally and consumption by that flag, add a command card button following the existing toggle pattern, and replace the `grid_rings.gdshader` nest zone visual with a dedicated `nest_refine.gdshader` (marching ants perimeter + sparkles).

**Tech Stack:** C# (simulation + Godot), xUnit, GLSL (Godot shaders)

---

## File Map

| Action | File | Responsibility |
|--------|------|---------------|
| Modify | `src/Blocker.Simulation/Core/Nest.cs` | Add `RefineEnabled` property |
| Modify | `src/Blocker.Simulation/Commands/Command.cs` | Add `ToggleRefine` to `CommandType` enum |
| Modify | `src/Blocker.Simulation/Core/GameState.cs` | Handle `ToggleRefine` in `ProcessCommands`, add helper to find nest for block |
| Modify | `src/Blocker.Simulation/Systems/NuggetSystem.cs` | Filter by `RefineEnabled` in auto-rally and consumption; re-route nuggets on toggle |
| Modify | `src/Blocker.Simulation/Net/StateHasher.cs` | Include `RefineEnabled` in state hash |
| Modify | `tests/Blocker.Simulation.Tests/NuggetTests.cs` | All unit tests for the feature |
| Modify | `godot/Scripts/Input/CommandAction.cs` | Add `RefineNuggets` enum value |
| Modify | `godot/Scripts/Rendering/CommandCard.cs` | Add "Refine Nuggets" button definition |
| Modify | `godot/Scripts/Input/SelectionManager.Commands.cs` | Handle `RefineNuggets` action → emit `ToggleRefine` command |
| Create | `godot/Assets/Shaders/nest_refine.gdshader` | Marching ants perimeter + sparkle shader |
| Modify | `godot/Scripts/Rendering/GridRenderer.Effects.cs` | Switch nest refine zones to new shader |

---

### Task 1: Add `RefineEnabled` to Nest + `ToggleRefine` Command Type

**Files:**
- Modify: `src/Blocker.Simulation/Core/Nest.cs:14-35`
- Modify: `src/Blocker.Simulation/Commands/Command.cs:1-23`
- Modify: `src/Blocker.Simulation/Net/StateHasher.cs:74-87`
- Test: `tests/Blocker.Simulation.Tests/NuggetTests.cs`

- [ ] **Step 1: Write failing test — default RefineEnabled is true**

Add to `NuggetTests.cs`:

```csharp
[Fact]
public void Nest_RefineEnabled_DefaultTrue()
{
    var nest = new Nest
    {
        Id = 1,
        Type = NestType.Builder,
        PlayerId = 0,
        Center = new GridPos(5, 5),
    };

    Assert.True(nest.RefineEnabled);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Blocker.Simulation.Tests --filter Nest_RefineEnabled_DefaultTrue -v n`
Expected: FAIL — `Nest` does not have `RefineEnabled` property

- [ ] **Step 3: Add `RefineEnabled` to `Nest`**

In `src/Blocker.Simulation/Core/Nest.cs`, add after the `NuggetLoaded` property (after line 30):

```csharp
/// <summary>Whether this nest accepts incoming nuggets for refining.</summary>
public bool RefineEnabled { get; set; } = true;
```

- [ ] **Step 4: Add `ToggleRefine` to `CommandType` enum**

In `src/Blocker.Simulation/Commands/Command.cs`, add after `FortifyWithNugget` (line 22):

```csharp
ToggleRefine,       // Nest-level: toggle nugget refining on/off. BlockIds = nest members.
```

- [ ] **Step 5: Add `RefineEnabled` to state hash**

In `src/Blocker.Simulation/Net/StateHasher.cs`, add after line 86 (`MixI32(ref h, n.NuggetLoaded ? 1 : 0);`):

```csharp
MixI32(ref h, n.RefineEnabled ? 1 : 0);
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/Blocker.Simulation.Tests --filter Nest_RefineEnabled_DefaultTrue -v n`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add src/Blocker.Simulation/Core/Nest.cs src/Blocker.Simulation/Commands/Command.cs src/Blocker.Simulation/Net/StateHasher.cs tests/Blocker.Simulation.Tests/NuggetTests.cs
git commit -m "feat: add RefineEnabled to Nest and ToggleRefine command type"
```

---

### Task 2: Handle `ToggleRefine` Command in `ProcessCommands`

**Files:**
- Modify: `src/Blocker.Simulation/Core/GameState.cs:146-176`
- Test: `tests/Blocker.Simulation.Tests/NuggetTests.cs`

- [ ] **Step 1: Write failing test — ToggleRefine flips RefineEnabled**

Add to `NuggetTests.cs`:

```csharp
[Fact]
public void ToggleRefine_FlipsNestRefineEnabled()
{
    var state = CreateState();

    // Create a nest manually: 3 builders rooted at orthogonal positions around center (5,5)
    var b1 = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 4));
    var b2 = state.AddBlock(BlockType.Builder, 0, new GridPos(4, 5));
    var b3 = state.AddBlock(BlockType.Builder, 0, new GridPos(6, 5));
    var nest = new Nest
    {
        Id = state.NextNestId(),
        Type = NestType.Builder,
        PlayerId = 0,
        Center = new GridPos(5, 5),
    };
    nest.MemberIds.AddRange([b1.Id, b2.Id, b3.Id]);
    b1.FormationId = nest.Id;
    b2.FormationId = nest.Id;
    b3.FormationId = nest.Id;
    state.Nests.Add(nest);

    Assert.True(nest.RefineEnabled);

    // Toggle off
    state.ProcessCommands([new Command(0, CommandType.ToggleRefine, [b1.Id])]);
    Assert.False(nest.RefineEnabled);

    // Toggle back on
    state.ProcessCommands([new Command(0, CommandType.ToggleRefine, [b2.Id])]);
    Assert.True(nest.RefineEnabled);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Blocker.Simulation.Tests --filter ToggleRefine_FlipsNestRefineEnabled -v n`
Expected: FAIL — ToggleRefine not handled, RefineEnabled stays true

- [ ] **Step 3: Implement ToggleRefine in ProcessCommands**

In `src/Blocker.Simulation/Core/GameState.cs`, add a new block after the `ToggleSpawn` handler (after line 176), before the per-block loop:

```csharp
if (cmd.Type == CommandType.ToggleRefine)
{
    foreach (var blockId in cmd.BlockIds)
    {
        var block = GetBlock(blockId);
        if (block == null || block.PlayerId != cmd.PlayerId) continue;
        var nest = Nests.FirstOrDefault(n => n.MemberIds.Contains(blockId));
        if (nest == null) continue;
        nest.RefineEnabled = !nest.RefineEnabled;
    }
    continue;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Blocker.Simulation.Tests --filter ToggleRefine_FlipsNestRefineEnabled -v n`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Blocker.Simulation/Core/GameState.cs tests/Blocker.Simulation.Tests/NuggetTests.cs
git commit -m "feat: handle ToggleRefine command in ProcessCommands"
```

---

### Task 3: Filter Auto-Rally and Consumption by `RefineEnabled`

**Files:**
- Modify: `src/Blocker.Simulation/Systems/NuggetSystem.cs:119-151` (TryConsumeNestRefine)
- Modify: `src/Blocker.Simulation/Systems/NuggetSystem.cs:371-391` (SetAutoRallyTarget)
- Test: `tests/Blocker.Simulation.Tests/NuggetTests.cs`

- [ ] **Step 1: Write failing test — auto-rally skips disabled nests**

```csharp
[Fact]
public void AutoRally_SkipsDisabledNest_GoesToEnabled()
{
    var state = CreateState();

    // Close nest at (3,3) — disabled
    var nestClose = new Nest
    {
        Id = state.NextNestId(),
        Type = NestType.Builder,
        PlayerId = 0,
        Center = new GridPos(3, 3),
        RefineEnabled = false,
    };
    state.Nests.Add(nestClose);

    // Far nest at (15,15) — enabled
    var nestFar = new Nest
    {
        Id = state.NextNestId(),
        Type = NestType.Builder,
        PlayerId = 0,
        Center = new GridPos(15, 15),
    };
    state.Nests.Add(nestFar);

    // Nugget at (4,4) — closer to nestClose
    var nugget = state.AddBlock(BlockType.Nugget, 0, new GridPos(4, 4));
    nugget.NuggetState!.IsMined = true;

    NuggetSystem.Tick(state);

    Assert.Equal(new GridPos(15, 15), nugget.MoveTarget);
}
```

- [ ] **Step 2: Write failing test — auto-rally stays put when all nests disabled**

```csharp
[Fact]
public void AutoRally_AllNestsDisabled_NuggetStaysPut()
{
    var state = CreateState();

    var nest = new Nest
    {
        Id = state.NextNestId(),
        Type = NestType.Builder,
        PlayerId = 0,
        Center = new GridPos(3, 3),
        RefineEnabled = false,
    };
    state.Nests.Add(nest);

    var nugget = state.AddBlock(BlockType.Nugget, 0, new GridPos(10, 10));
    nugget.NuggetState!.IsMined = true;

    NuggetSystem.Tick(state);

    Assert.Null(nugget.MoveTarget);
}
```

- [ ] **Step 3: Write failing test — consumption skips disabled nest**

```csharp
[Fact]
public void Consumption_SkipsDisabledNest()
{
    var state = CreateState();

    var nest = new Nest
    {
        Id = state.NextNestId(),
        Type = NestType.Builder,
        PlayerId = 0,
        Center = new GridPos(5, 5),
        RefineEnabled = false,
    };
    state.Nests.Add(nest);

    // Nugget within refine radius
    var nugget = state.AddBlock(BlockType.Nugget, 0, new GridPos(5, 6));
    nugget.NuggetState!.IsMined = true;
    nugget.NuggetState.ManuallyMoved = true; // prevent auto-rally from changing target

    NuggetSystem.Tick(state);

    // Nugget should NOT be consumed
    Assert.NotNull(state.GetBlock(nugget.Id));
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test tests/Blocker.Simulation.Tests --filter "AutoRally_SkipsDisabledNest|AutoRally_AllNestsDisabled|Consumption_SkipsDisabledNest" -v n`
Expected: All 3 FAIL

- [ ] **Step 5: Filter `SetAutoRallyTarget` by `RefineEnabled`**

In `src/Blocker.Simulation/Systems/NuggetSystem.cs`, modify `SetAutoRallyTarget` (line 377-391). Add a filter after the player check:

```csharp
private static void SetAutoRallyTarget(GameState state, Block nugget)
{
    GridPos? nearest = null;
    int bestDist = int.MaxValue;
    int bestNestId = int.MaxValue;

    foreach (var nest in state.Nests)
    {
        if (nest.PlayerId != nugget.PlayerId) continue;
        if (!nest.RefineEnabled) continue;
        int dist = nugget.Pos.ChebyshevDistance(nest.Center);
        if (dist < bestDist || (dist == bestDist && nest.Id < bestNestId))
        {
            bestDist = dist;
            bestNestId = nest.Id;
            nearest = nest.Center;
        }
    }

    if (nearest.HasValue)
        nugget.MoveTarget = nearest.Value;
}
```

- [ ] **Step 6: Filter `TryConsumeNestRefine` by `RefineEnabled`**

In `src/Blocker.Simulation/Systems/NuggetSystem.cs`, in `TryConsumeNestRefine` (line 119-151), add after the player check (line 123):

```csharp
if (!nest.RefineEnabled) continue;
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/Blocker.Simulation.Tests --filter "AutoRally_SkipsDisabledNest|AutoRally_AllNestsDisabled|Consumption_SkipsDisabledNest" -v n`
Expected: All 3 PASS

- [ ] **Step 8: Commit**

```bash
git add src/Blocker.Simulation/Systems/NuggetSystem.cs tests/Blocker.Simulation.Tests/NuggetTests.cs
git commit -m "feat: filter nugget auto-rally and consumption by RefineEnabled"
```

---

### Task 4: Re-Route Nuggets on Toggle

**Files:**
- Modify: `src/Blocker.Simulation/Core/GameState.cs` (ToggleRefine handler)
- Modify: `src/Blocker.Simulation/Systems/NuggetSystem.cs` (make `SetAutoRallyTarget` internal)
- Test: `tests/Blocker.Simulation.Tests/NuggetTests.cs`

- [ ] **Step 1: Write failing test — disable re-routes nuggets to next nest**

```csharp
[Fact]
public void ToggleRefine_Disable_ReroutesNuggetsToNextNest()
{
    var state = CreateState();

    // Nest A at (3,3) — will be disabled
    var b1 = state.AddBlock(BlockType.Builder, 0, new GridPos(3, 2));
    var nestA = new Nest
    {
        Id = state.NextNestId(),
        Type = NestType.Builder,
        PlayerId = 0,
        Center = new GridPos(3, 3),
    };
    nestA.MemberIds.Add(b1.Id);
    b1.FormationId = nestA.Id;
    state.Nests.Add(nestA);

    // Nest B at (15,15) — stays enabled
    var nestB = new Nest
    {
        Id = state.NextNestId(),
        Type = NestType.Builder,
        PlayerId = 0,
        Center = new GridPos(15, 15),
    };
    state.Nests.Add(nestB);

    // Nugget auto-rallying to nest A
    var nugget = state.AddBlock(BlockType.Nugget, 0, new GridPos(5, 5));
    nugget.NuggetState!.IsMined = true;
    nugget.MoveTarget = nestA.Center;

    // Disable nest A
    state.ProcessCommands([new Command(0, CommandType.ToggleRefine, [b1.Id])]);

    // Nugget should re-route to nest B
    Assert.Equal(new GridPos(15, 15), nugget.MoveTarget);
}
```

- [ ] **Step 2: Write failing test — disable with no other nest → nugget stops**

```csharp
[Fact]
public void ToggleRefine_Disable_NoOtherNest_NuggetStops()
{
    var state = CreateState();

    var b1 = state.AddBlock(BlockType.Builder, 0, new GridPos(3, 2));
    var nest = new Nest
    {
        Id = state.NextNestId(),
        Type = NestType.Builder,
        PlayerId = 0,
        Center = new GridPos(3, 3),
    };
    nest.MemberIds.Add(b1.Id);
    b1.FormationId = nest.Id;
    state.Nests.Add(nest);

    var nugget = state.AddBlock(BlockType.Nugget, 0, new GridPos(5, 5));
    nugget.NuggetState!.IsMined = true;
    nugget.MoveTarget = nest.Center;

    state.ProcessCommands([new Command(0, CommandType.ToggleRefine, [b1.Id])]);

    Assert.Null(nugget.MoveTarget);
}
```

- [ ] **Step 3: Write failing test — manually-moved nuggets keep their target**

```csharp
[Fact]
public void ToggleRefine_Disable_ManuallyMovedNuggetKeepsTarget()
{
    var state = CreateState();

    var b1 = state.AddBlock(BlockType.Builder, 0, new GridPos(3, 2));
    var nest = new Nest
    {
        Id = state.NextNestId(),
        Type = NestType.Builder,
        PlayerId = 0,
        Center = new GridPos(3, 3),
    };
    nest.MemberIds.Add(b1.Id);
    b1.FormationId = nest.Id;
    state.Nests.Add(nest);

    var nugget = state.AddBlock(BlockType.Nugget, 0, new GridPos(5, 5));
    nugget.NuggetState!.IsMined = true;
    nugget.NuggetState.ManuallyMoved = true;
    nugget.MoveTarget = nest.Center;

    state.ProcessCommands([new Command(0, CommandType.ToggleRefine, [b1.Id])]);

    // Manual override — target unchanged
    Assert.Equal(new GridPos(3, 3), nugget.MoveTarget);
}
```

- [ ] **Step 4: Write failing test — enable wakes up idle nuggets**

```csharp
[Fact]
public void ToggleRefine_Enable_WakesIdleNuggets()
{
    var state = CreateState();

    var b1 = state.AddBlock(BlockType.Builder, 0, new GridPos(3, 2));
    var nest = new Nest
    {
        Id = state.NextNestId(),
        Type = NestType.Builder,
        PlayerId = 0,
        Center = new GridPos(3, 3),
        RefineEnabled = false,
    };
    nest.MemberIds.Add(b1.Id);
    b1.FormationId = nest.Id;
    state.Nests.Add(nest);

    // Idle mined nugget — no target, not manually moved
    var nugget = state.AddBlock(BlockType.Nugget, 0, new GridPos(10, 10));
    nugget.NuggetState!.IsMined = true;

    // Enable refine
    state.ProcessCommands([new Command(0, CommandType.ToggleRefine, [b1.Id])]);

    Assert.Equal(new GridPos(3, 3), nugget.MoveTarget);
}
```

- [ ] **Step 5: Run tests to verify they fail**

Run: `dotnet test tests/Blocker.Simulation.Tests --filter "ToggleRefine_Disable_Reroutes|ToggleRefine_Disable_NoOtherNest|ToggleRefine_Disable_ManuallyMoved|ToggleRefine_Enable_Wakes" -v n`
Expected: All 4 FAIL

- [ ] **Step 6: Make `SetAutoRallyTarget` internal for cross-class access**

In `src/Blocker.Simulation/Systems/NuggetSystem.cs`, change the visibility of `SetAutoRallyTarget` from `private` to `internal`:

```csharp
internal static void SetAutoRallyTarget(GameState state, Block nugget)
```

- [ ] **Step 7: Implement re-routing in ToggleRefine handler**

In `src/Blocker.Simulation/Core/GameState.cs`, replace the `ToggleRefine` handler from Task 2 with:

```csharp
if (cmd.Type == CommandType.ToggleRefine)
{
    var toggled = new HashSet<int>();
    foreach (var blockId in cmd.BlockIds)
    {
        var block = GetBlock(blockId);
        if (block == null || block.PlayerId != cmd.PlayerId) continue;
        var nest = Nests.FirstOrDefault(n => n.MemberIds.Contains(blockId));
        if (nest == null || !toggled.Add(nest.Id)) continue;
        nest.RefineEnabled = !nest.RefineEnabled;
    }

    // Re-route affected nuggets
    foreach (var nestId in toggled)
    {
        var nest = Nests.FirstOrDefault(n => n.Id == nestId);
        if (nest == null) continue;

        if (!nest.RefineEnabled)
        {
            // Disabled: re-route auto-rallying nuggets targeting this nest
            foreach (var block in Blocks)
            {
                if (block.Type != BlockType.Nugget) continue;
                if (block.NuggetState is not { IsMined: true }) continue;
                if (block.NuggetState.ManuallyMoved) continue;
                if (block.MoveTarget != nest.Center) continue;
                block.MoveTarget = null;
                NuggetSystem.SetAutoRallyTarget(this, block);
            }
        }
        else
        {
            // Enabled: wake idle mined nuggets
            foreach (var block in Blocks)
            {
                if (block.Type != BlockType.Nugget) continue;
                if (block.NuggetState is not { IsMined: true }) continue;
                if (block.NuggetState.ManuallyMoved) continue;
                if (block.MoveTarget.HasValue) continue;
                if (block.NuggetState.HealTargetId.HasValue) continue;
                if (block.NuggetState.FortifyTargetPos.HasValue) continue;
                if (block.PlayerId != nest.PlayerId) continue;
                NuggetSystem.SetAutoRallyTarget(this, block);
            }
        }
    }
    continue;
}
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test tests/Blocker.Simulation.Tests --filter "ToggleRefine_Disable_Reroutes|ToggleRefine_Disable_NoOtherNest|ToggleRefine_Disable_ManuallyMoved|ToggleRefine_Enable_Wakes" -v n`
Expected: All 4 PASS

- [ ] **Step 9: Run full test suite**

Run: `dotnet test tests/Blocker.Simulation.Tests -v n`
Expected: All tests PASS (no regressions)

- [ ] **Step 10: Commit**

```bash
git add src/Blocker.Simulation/Core/GameState.cs src/Blocker.Simulation/Systems/NuggetSystem.cs tests/Blocker.Simulation.Tests/NuggetTests.cs
git commit -m "feat: re-route nuggets when RefineEnabled toggled"
```

---

### Task 5: Command Card — "Refine Nuggets" Button

**Files:**
- Modify: `godot/Scripts/Input/CommandAction.cs`
- Modify: `godot/Scripts/Rendering/CommandCard.cs:31-42`
- Modify: `godot/Scripts/Input/SelectionManager.Commands.cs:17-62`

- [ ] **Step 1: Add `RefineNuggets` to `CommandAction` enum**

In `godot/Scripts/Input/CommandAction.cs`, add after `Tower`:

```csharp
RefineNuggets
```

- [ ] **Step 2: Add button definition to `CommandCard.AllCommands`**

In `godot/Scripts/Rendering/CommandCard.cs`, add to the `AllCommands` array (after the Tower entry, before the closing `];`):

```csharp
new(CommandAction.RefineNuggets, "Refine Nuggets", "◆", "N", b => b.IsInFormation),
```

Note: `b.IsInFormation` matches any block that is a nest (or formation) member. This is the closest available predicate — nest members always have `FormationId` set. The button won't appear for non-nest formations (like supplies/towers) because those blocks have different types and the command handler will validate the actual nest membership.

- [ ] **Step 3: Handle `RefineNuggets` action in `SelectionManager.IssueCommand`**

In `godot/Scripts/Input/SelectionManager.Commands.cs`, add a new case in the `IssueCommand` switch (before the closing `}`):

```csharp
case CommandAction.RefineNuggets:
    IssueCommandToSelected(CommandType.ToggleRefine, queue);
    break;
```

- [ ] **Step 4: Add `ToggleRefine` to `GetRelevantBlocks`**

In `godot/Scripts/Input/SelectionManager.Commands.cs`, add a new case in the `GetRelevantBlocks` switch (before the `_ =>` default):

```csharp
CommandType.ToggleRefine => _state.SelectedBlocks
    .Where(b => b.IsInFormation).ToList(),
```

- [ ] **Step 5: Build to verify compilation**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add godot/Scripts/Input/CommandAction.cs godot/Scripts/Rendering/CommandCard.cs godot/Scripts/Input/SelectionManager.Commands.cs
git commit -m "feat: add Refine Nuggets button to command card"
```

---

### Task 6: Marching Ants Shader

**Files:**
- Create: `godot/Assets/Shaders/nest_refine.gdshader`

- [ ] **Step 1: Create the shader file**

Create `godot/Assets/Shaders/nest_refine.gdshader`:

```glsl
// Marching ants perimeter + interior sparkles for nest refine zone.
// CPU sets center + time uniforms. GPU computes per-pixel.
shader_type canvas_item;
render_mode blend_add;

uniform vec2 center = vec2(5.5, 5.5);
uniform vec2 grid_size = vec2(30.0, 30.0);
uniform float cell_size = 28.0;
uniform float max_radius = 3.0;
uniform float time_ms = 0.0;
uniform float march_speed = 40.0;
uniform vec4 zone_color : source_color = vec4(0.55, 0.67, 1.0, 0.8);

// Dash pattern
const float DASH_LEN = 8.0;
const float GAP_LEN = 5.0;
const float PATTERN_LEN = DASH_LEN + GAP_LEN;
const float LINE_HALF_W = 1.25; // pixels

// Sparkle params
const int SPARKLE_COUNT = 24;
const float SPARKLE_THRESHOLD = 0.3;

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

void fragment() {
    vec2 px = UV * grid_size * cell_size;
    vec2 gp = UV * grid_size;

    // Chebyshev distance from center
    vec2 d = abs(gp - center);
    float cheb = max(d.x, d.y);

    // Zone boundary in pixels
    float r = max_radius * cell_size;
    vec2 center_px = center * cell_size;
    vec2 local = px - center_px;

    // Rectangle edges (pixels from center)
    float half_w = max_radius * cell_size;
    float half_h = max_radius * cell_size;

    // Distance to each edge
    float dist_left   = abs(local.x + half_w);
    float dist_right  = abs(local.x - half_w);
    float dist_top    = abs(local.y + half_h);
    float dist_bottom = abs(local.y - half_h);

    // Check if on horizontal or vertical edge (within line width)
    bool on_top    = dist_top    < LINE_HALF_W && abs(local.x) <= half_w;
    bool on_bottom = dist_bottom < LINE_HALF_W && abs(local.x) <= half_w;
    bool on_left   = dist_left   < LINE_HALF_W && abs(local.y) <= half_h;
    bool on_right  = dist_right  < LINE_HALF_W && abs(local.y) <= half_h;

    float alpha = 0.0;

    if (on_top || on_bottom || on_left || on_right) {
        // Compute position along perimeter for marching dash pattern
        // Walk: top (left→right), right (top→bottom), bottom (right→left), left (bottom→top)
        float perimeter_pos = 0.0;
        float side_w = 2.0 * half_w;
        float side_h = 2.0 * half_h;

        if (on_top) {
            perimeter_pos = local.x + half_w; // 0 to side_w
        } else if (on_right) {
            perimeter_pos = side_w + (local.y + half_h); // side_w to side_w + side_h
        } else if (on_bottom) {
            perimeter_pos = side_w + side_h + (half_w - local.x); // continuing
        } else if (on_left) {
            perimeter_pos = 2.0 * side_w + side_h + (half_h - local.y);
        }

        float march_offset = time_ms * 0.001 * march_speed;
        float t = mod(perimeter_pos + march_offset, PATTERN_LEN);
        float dash = step(t, DASH_LEN);

        // Anti-alias: fade at dash edges
        float edge_fade = 1.0;
        if (on_top)    edge_fade = 1.0 - dist_top / LINE_HALF_W;
        if (on_bottom) edge_fade = 1.0 - dist_bottom / LINE_HALF_W;
        if (on_left)   edge_fade = 1.0 - dist_left / LINE_HALF_W;
        if (on_right)  edge_fade = 1.0 - dist_right / LINE_HALF_W;

        alpha = dash * edge_fade * zone_color.a;
    }

    // Interior sparkles — only inside the zone
    if (cheb < max_radius - 0.1) {
        // Skip center cell
        vec2 rel = gp - center;
        if (abs(rel.x) > 0.5 || abs(rel.y) > 0.5) {
            float time_s = time_ms * 0.001;
            for (int i = 0; i < SPARKLE_COUNT; i++) {
                float fi = float(i);
                vec2 seed = vec2(fi * 0.73, fi * 1.37);
                float sx = center.x + (hash(seed) - 0.5) * (max_radius * 2.0 - 1.0);
                float sy = center.y + (hash(seed + 1.0) - 0.5) * (max_radius * 2.0 - 1.0);

                float sparkle_cheb = max(abs(sx - center.x), abs(sy - center.y));
                if (sparkle_cheb >= max_radius - 0.1) continue;

                float phase = hash(seed + 2.0) * 6.2832;
                float speed = 0.5 + hash(seed + 3.0) * 2.0;
                float brightness = sin(time_s * speed + phase);
                if (brightness < SPARKLE_THRESHOLD) continue;
                brightness = (brightness - SPARKLE_THRESHOLD) / (1.0 - SPARKLE_THRESHOLD);

                vec2 sp = vec2(sx, sy) * cell_size;
                vec2 diff = px - sp;

                // Diamond shape (4-point star)
                float size = (1.5 + brightness * 2.5);
                float diamond = abs(diff.x) / size + abs(diff.y) / (size * 0.4);
                float diamond2 = abs(diff.x) / (size * 0.4) + abs(diff.y) / size;
                float star = min(diamond, diamond2);

                if (star < 1.0) {
                    float sparkle_alpha = (1.0 - star) * brightness * 0.8;
                    // Alternate gold and blue
                    vec3 sparkle_col = mod(fi, 3.0) < 1.0
                        ? vec3(1.0, 0.94, 0.7)  // warm gold
                        : vec3(0.7, 0.86, 1.0);  // cool blue
                    // Additive blend with existing
                    alpha = max(alpha, sparkle_alpha);
                    if (sparkle_alpha > alpha * 0.5) {
                        COLOR = vec4(sparkle_col * sparkle_alpha + zone_color.rgb * (alpha - sparkle_alpha * 0.5), max(alpha, sparkle_alpha));
                        return;
                    }
                }
            }
        }
    }

    if (alpha < 0.005) discard;
    COLOR = vec4(zone_color.rgb, alpha);
}
```

- [ ] **Step 2: Commit**

```bash
git add godot/Assets/Shaders/nest_refine.gdshader
git commit -m "feat: add marching ants + sparkles shader for nest refine zone"
```

---

### Task 7: Wire Up Shader in GridRenderer + Toggle Visibility

**Files:**
- Modify: `godot/Scripts/Rendering/GridRenderer.Effects.cs:92-152`

- [ ] **Step 1: Add shader reference and replace the nest refine zone rendering**

In `godot/Scripts/Rendering/GridRenderer.Effects.cs`, add a new static field next to the existing `_gridRingsShader` (after line 16):

```csharp
private static readonly Shader _nestRefineShader = GD.Load<Shader>("res://Assets/Shaders/nest_refine.gdshader");
```

- [ ] **Step 2: Update `UpdateNestRefineZones` to use new shader and filter by `RefineEnabled`**

Replace the entire `UpdateNestRefineZones` method (lines 92-152) with:

```csharp
private void UpdateNestRefineZones()
{
    if (_gameState == null) return;

    var grid = _gameState.Grid;
    int refineR = Simulation.Core.Constants.NuggetRefineRadius;
    var gridPixelSize = new Vector2(grid.Width * CellSize, grid.Height * CellSize);

    var activeNests = new HashSet<int>();
    foreach (var nest in _gameState.Nests)
    {
        if (!nest.RefineEnabled) continue;
        if (_localVisibility != null && !_localVisibility.IsVisible(nest.Center)) continue;

        activeNests.Add(nest.Id);

        if (!_nestRefineRects.TryGetValue(nest.Id, out var rect))
        {
            var mat = new ShaderMaterial { Shader = _nestRefineShader };

            mat.SetShaderParameter("grid_size", new Vector2(grid.Width, grid.Height));
            mat.SetShaderParameter("cell_size", CellSize);
            mat.SetShaderParameter("max_radius", (float)refineR + 1f);
            mat.SetShaderParameter("zone_color", new Color(0.55f, 0.67f, 1.0f, 0.8f));
            mat.SetShaderParameter("march_speed", 40f);
            mat.SetShaderParameter("time_ms", 0f);

            rect = new ColorRect
            {
                Position = new Vector2(GridPadding, GridPadding),
                Size = gridPixelSize,
                Material = mat,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            AddChild(rect);
            _nestRefineRects[nest.Id] = rect;
        }

        var mat2 = (ShaderMaterial)rect.Material;
        mat2.SetShaderParameter("center", new Vector2(nest.Center.X + 0.5f, nest.Center.Y + 0.5f));
        mat2.SetShaderParameter("time_ms", (float)Time.GetTicksMsec());
    }

    var stale = new List<int>();
    foreach (var (id, rect) in _nestRefineRects)
    {
        if (!activeNests.Contains(id))
        {
            rect.QueueFree();
            stale.Add(id);
        }
    }
    foreach (var id in stale)
        _nestRefineRects.Remove(id);
}
```

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add godot/Scripts/Rendering/GridRenderer.Effects.cs
git commit -m "feat: wire marching ants shader for nest refine zones with RefineEnabled toggle"
```

---

### Task 8: Update Game Bible

**Files:**
- Modify: `docs/game-bible.md`

- [ ] **Step 1: Update nugget/nest documentation**

Find the nugget section (§4.7 or equivalent) and nest section (§6) in `docs/game-bible.md`. Add:

In the nest section, add a property description:
> **Refine Enabled** (default: true) — controls whether this nest accepts incoming nuggets for refining. Can be toggled via the "Refine Nuggets" command (hotkey: N). When disabled, mined nuggets will not auto-rally to this nest, and nuggets within its refine radius will not be consumed.

In the nugget auto-rally section, update:
> Mined nuggets auto-rally to the nearest friendly nest **with refining enabled**. If no enabled nest exists, the nugget stays at its mined location.

- [ ] **Step 2: Commit**

```bash
git add docs/game-bible.md
git commit -m "docs: update game bible with toggleable nest refining"
```

---

### Task 9: Final Verification

- [ ] **Step 1: Run full test suite**

Run: `dotnet test tests/Blocker.Simulation.Tests -v n`
Expected: All tests PASS

- [ ] **Step 2: Build entire solution**

Run: `dotnet build blocker.sln`
Expected: Build succeeded with no errors

- [ ] **Step 3: Manual playtest checklist**

Open Godot, run the game, and verify:
1. Form a nest → marching ants perimeter visible with sparkles inside
2. Select a nest block → "Refine Nuggets" button appears in command card
3. Click "Refine Nuggets" → marching ants disappear, button state changes
4. Click again → ants reappear, button re-highlights
5. Mine a nugget with only disabled nests → nugget stays at mined location
6. Enable a nest → idle nuggets start moving toward it
7. Disable a nest while nuggets are in flight → they re-route or stop
8. Manually move a nugget toward a disabled nest → it keeps going (manual override)
