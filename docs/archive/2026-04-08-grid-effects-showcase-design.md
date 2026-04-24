# Grid Effects Showcase — Design Spec

## Goal

Standalone Godot test scene to explore and compare grid-line-based visual effects. A visual playground for iterating on effect styles before committing them to the real game renderer.

## Scene Structure

**`EffectShowcase.tscn`** — single scene, no simulation dependency.

Nodes:
- `EffectShowcase` (Node2D) — draws grid + effects via `_Draw()`, manages effect state
- `ButtonPanel` (VBoxContainer, anchored top-left) — one button per effect type

**One script: `EffectShowcase.cs`** — everything in one file. Grid drawing, effect generation, animation, button wiring. No abstractions, no base classes.

### Grid

- 30x30 cells, dark background
- Fixed camera (no scrolling)
- All effects spawn from center cell (15, 15)
- Cell size matches game: 28px

## Effect System Core

### Data Model

Each effect is a collection of line segments with animation metadata:

```
LightSegment { X1, Y1, X2, Y2, Dist }
```

- `X1/Y1/X2/Y2` — world-space endpoints (float, not snapped to int for loose effects)
- `Dist` — distance from origin in hops, drives wave-front timing

```
GridEffect { Segments[], MaxDist, T, Duration, TrailDist, Color, Sparks[] }
```

- `T` — 0..1 animation progress
- `TrailDist` — how many dist-units the glow trail spans behind the wave front
- `Sparks` — tiny particles that fly off bright segments

### Rendering (multi-pass in _Draw)

Ported from TS prototype's approach, adapted to Godot `DrawLine`/`DrawRect`:

1. **Outer bloom** — wide (9px) faint line, low alpha
2. **Inner bloom** — medium (5px) line, moderate alpha
3. **Colored core** — thin (1.8px) bright line
4. **White-hot tips** — 1px white on wavefront segments (brightness > 0.55)
5. **Sparks** — small rects with velocity, gravity, fade

Per-segment brightness driven by wave-front position with easeOutCubic:
- `wavePos = easeOut(t) * (maxDist + trailDist)`
- `brightness = clamp(1 - (wavePos - dist) / trailDist)`
- Shimmer overlay: `0.8 + 0.2 * sin(age * 0.006)`

### Grid Adherence Spectrum

Effects vary in how tightly they follow grid lines:

- **Precise** — segments snap exactly to grid intersections
- **Loose** — follow grid lines but with perpendicular displacement, drift, wobble
- **Wild** — grid-seeded but overshoot, arc between lines, jitter off-axis

## Effects Lineup

### 1. Electric Lightning (Wild)
Direct port from TS prototype `buildLightning`. Branching random walk from center cell edges in all 4 directions. Segments follow grid but branching is stochastic — arms overshoot, fork unpredictably. Continuation probability decays (0.82x), branch probability ~0.55.
- Duration: 1200ms, trail: 3.0
- Color: cyan/electric blue

### 2. Wave Pulse (Loose)
Grid lines radiating outward from center, but each segment is displaced perpendicular to its direction by a sine wave. The wave travels outward, so lines ripple as the pulse passes. Amplitude ~0.3 cells, frequency based on distance.
- Duration: 1500ms, trail: 4.0
- Color: teal/aqua

### 3. Ghost Flicker (Loose)
Grid segments within a radius around center appear and disappear randomly. Each segment has its own random phase — they strobe independently, creating a flickering, haunted presence. Some segments overshoot their grid position slightly for an unstable feel. Opacity pulses per-segment.
- Duration: 2000ms (longer to let the flicker play out), trail: N/A (no wave front — random visibility)
- Color: pale violet/white

### 4. Digital Cascade (Loose)
Vertical grid lines light up top-to-bottom in a waterfall pattern. At random grid intersections, horizontal branches fire off left or right. Segments have a slight random vertical offset (not perfectly grid-snapped). Evokes Matrix digital rain but on a grid.
- Duration: 1800ms, trail: 5.0
- Color: green/lime

### 5. Spiral Trace (Precise)
Ported from TS `buildSpiral`. Clockwise spiral expanding from center cell along exact grid lines. Clean, geometric, satisfying. Segments light up sequentially along the spiral path.
- Duration: 1800ms, trail: 4.0
- Color: gold/amber

### 6. Circuit Trace (Precise)
Right-angle paths that branch from center and terminate at grid intersections. At branch points and endpoints, intersections glow brighter (node highlight). Paths follow grid lines exactly. PCB/circuit board aesthetic — deliberate, structured.
- Duration: 1400ms, trail: 2.5
- Color: warm orange

### 7. Shockwave Ring (Precise)
Expanding square ring following grid perimeter at increasing Chebyshev distances from center. Each ring is the 4 edges of the square at that distance. Rings expand outward one after another. Clean, radial, precise.
- Duration: 1000ms, trail: 2.0
- Color: white/silver

### 8. Jitter Burst (Wild)
Ported from TS `buildJitter`. Short erratic zigzag arms from center edges. After the first step, each arm randomly turns perpendicular with 60% probability — chaotic, shaky energy. Arms overshoot grid positions freely.
- Duration: 400ms (fast), trail: 1.0
- Color: red/crimson

### 9. Converging Drain (Wild)
Reverse lightning — segments generate outward but animate *inward*. The wave front starts at the tips and converges toward center. Uses the same branching random walk as Electric Lightning but with reversed animation. Segments can arc off-grid as they rush inward.
- Duration: 1000ms, trail: 2.0
- Color: purple/magenta

### 10. Arc Chain (Wild)
Lightning arcs that jump between random grid intersections near center, chaining from node to node. Each arc is a slight curve (quadratic bezier approximated with 3-4 line segments) rather than a straight grid line. Arcs fire sequentially with slight delays. Tesla coil feel.
- Duration: 1200ms, trail: 1.5
- Color: electric yellow/white

## Button Panel

- VBoxContainer anchored top-left with margin
- One `Button` per effect, labeled with the effect name
- Clicking triggers the effect at center cell, restarting if already playing
- Optional: "All" button that fires all 10 simultaneously for the full visual buffet

## What This Is NOT

- Not a permanent game feature — it's a test bench
- No simulation dependency — no GameState, no Commands
- No persistence — effects are transient, scene is throwaway
- No performance optimization needed — 30x30 grid with a few effects at a time is trivial
