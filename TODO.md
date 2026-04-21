

# Chat, Alerts and Info
 - right above the bottom hud, center aligned

# Controls
 - blueprint commands should also auto-queue when rooting/uprooting or immobile cooldown

# Sounds
 - We need to review which sounds should play only for the players units. For example spawn sounds.

# Gameplay
 - Fog of war and shroud

# Visuals
 - Blueprints:

 - Shaders: Only one is being applied. Directional bloom is a bit weird, generates some permanent artifacts. Screen distortion is too intense.
# Gameplay

## Nugget Blocks
 - Mineable blocks that builders can mine
 - Sparkling, shining, white diamond-like blocks with a golden reflection (nice shader?)
 - Right clicking a nugget with a builder selected makes the builder stand adjacent to it and start mining. After a time the nugget is freed. More adjacent builders mine nuggets faster.
 - Blast rays destroy nuggets (free or not)
 - Once mined, nugget blocks are freed and can move:
   - Automatically go to the nearest "refining" nest?
   - If refined on soldier/jumper/stunner/warden nests, they immediately spawn those units (spawn progress goes to 100%)
   - Can heal soldiers and jumpers to full health (consuming the nugget)
   - Can turn a line of walls into resistant walls with more hp (consuming the nugget)
 - Nugget blocks can be captured by opponent builders
 - Show the count of owned nuggets in the top bar

## Gates
 - Walls can be turned into gates, gates "lower" and allow units to pass. Player controls the lowering

## Current Issues

# Hud
 - 1-9 -> control groups
   - Ctrl-# -> create group with selected units
   - Shift-# -> add selected unit to group
   - single tap # -> select units from group
   - double tap # -> move camera to the majority of units in the group
