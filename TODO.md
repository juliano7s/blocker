
# Controls
 - builder should not have an attack move command

# Visuals
 - Stun rays:
   - Instead of being a wave through the cells, we could take the grid line effect idea and make it an electrical charge that follows the gridlines for the ray range.

 - Rays:
   - They now have a better cell wave pattern, but it's not flowing well.
     - From the stunner and towers:
       - rays come out in the rows to the direction of the mouse.
       - each ray comes out from the very next adjacent cell of the unit, this causes the middle row to be one cell forward than the other two
     - From explosions:
       - rays come out of the center in all directions, uniformly, respecting the range of the ray

 - Blueprints:
   - they must be team colored, right now they are always blue. Supply is gray, should be team color too.

 - Towers: they have no formation visuals (outline + diamond). These need to also be added to the PlayerPalette

 - Shaders: Only one is being applied. Directional bloom is a bit weird, generates some permanent artifacts. Screen distortion is too intense.

# Gameplay
 - a stunned unit should have it's abilities paused while stunned. stunned units in formations should disable that formation (no spawn; no ray shooting) while stunned
 - instead of the stunner cooldown making it immobile, let's make the cooldown mobile but with 1/3 of the speed
 - stun ray should penetrate units - it currently doesn't; it should stop only at walls (and kill the first encountered wall)
 - formation detection: Formations are not being detect at game start (walls in an L pattern don't become supplies for ex). Soldier nests are wrongly just requiring adjacent walls. They should only form when it has this shape:
  [w][b]
  [b]
  [w][b]
 - pathfinding: units are preferring to go in a large L path instead of "diagonally" (zigzagging)

# Hud
 - builder does not have an attack move command, remove it