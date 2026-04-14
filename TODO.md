

# Chat, Alerts and Info
 - right above the bottom hud, center aligned

# Controls
 - blueprint commands should also auto-queue when rooting/uprooting or immobile cooldown

# Sounds
 - We need to review which sounds should play only for the players units. For example spawn sounds.


# Visuals
 - Blueprints:

 - Towers: they have no formation visuals (outline + diamond). These need to also be added to the PlayerPalette

 - Shaders: Only one is being applied. Directional bloom is a bit weird, generates some permanent artifacts. Screen distortion is too intense.

# Gameplay

## Nugget Blocks
 - Mineable blocks that builders can mine
 - Sparkling, shining, diamond-like blocks
 - Right clicking a nugget with a builder selected makes the builder stand adjacent to it and start mining. After a time the nugget is freed. More adjacent builders mine nuggets faster.
 - Blast rays destroy nuggets (free or not)
 - Once mined, nugget blocks are freed and can move:
   - Automatically go to the nearest "refining" nest?
   - If refined on soldier/jumper/stunner/warden nests, they are turned into those units
   - Can heal soldiers and jumpers
   - Can turn a line of walls into resistant walls with more hp
 - Nugget blocks can be captured by builders
 - Show the count of owned nuggets in the top bar

## Gates
 - Walls can be turned into gates, gates "lower" and allow units to pass. Player controls the lowering

## Current Issues
 - a stunned unit should have it's abilities paused while stunned. stunned units in formations should disable that formation (no spawn; no ray shooting) while stunned
 - stun ray should penetrate units - it currently doesn't; it should stop only at walls (and kill the first encountered wall)
 - pathfinding: units are preferring to go in a large L path instead of "diagonally" (zigzagging)

# Hud
 - The whole bottom bar is a bit too large. We can probably split the minimap to one side and make the info panel and the command card to the right
 - Commands should be clickable, all buttons should have a hover highlight and mouse pointer if they are enabled
 - multiple selected units should show the unit icons, right now they are just different colors
 - blueprints have generic icons, they should a miniature version of the formation
 - some commands are wrong in the card (jumper jumps with F and can't root; uproot is F and not U)
 - we should improve the contrast of the icons and font, increase the font a bit too. Minimap doesn't need a title "Minimap"
 - 1-9 -> control groups
   - Ctrl-# -> create group with selected units
   - Shift-# -> add selected unit to group
   - single tap # -> select units from group
   - double tap # -> move camera to the majority of units in the group
 - toggle spawns shortcuts: (also shown in the top bar with the icon of each unit)
   - Alt+Q: toggle spawn builder
   - Alt+W: toggle spawn soldier
   - Alt+E: toggle spawn stunner
   - Alt+R: toggle spawn warden
   - Alt+T: toggle spawn jumper