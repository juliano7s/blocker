
# Controls
 - queuing commands with shift not always work: if a block is rooting or uprooting, the next shift commands are not queued.
   - Also, if a block is rooting/uprooting any normal command on top of that should be queued, similar to the wall command.

# Camera


# Visuals
 - Stun rays:
   - Instead of being a wave through the cells, we could take the grid line effect idea and make it an electrical charge that follows the gridlines for the ray range.

 - Blueprints:

 - Towers: they have no formation visuals (outline + diamond). These need to also be added to the PlayerPalette

 - Shaders: Only one is being applied. Directional bloom is a bit weird, generates some permanent artifacts. Screen distortion is too intense.

# Gameplay
 - a stunned unit should have it's abilities paused while stunned. stunned units in formations should disable that formation (no spawn; no ray shooting) while stunned
 - stun ray should penetrate units - it currently doesn't; it should stop only at walls (and kill the first encountered wall)
 - pathfinding: units are preferring to go in a large L path instead of "diagonally" (zigzagging)

# Hud
 - In the command card, icons are wrapping too early, there is still room
 - multiple selected units should show the unit icons, right now they are just different colors
 - blueprints have generic icons, they should either show the unit they produce or a miniature version of the formation - supplies and towers
 - some commands are wrong in the card (jumper jumps with F and can't root; uproot is F and not U)
 - we should improve the contrast of the icons and font, increase the font a bit too. Minimap doesn't need a title "Minimap"