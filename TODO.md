
# Controls
 - builder should not have an attack move command

# Visuals

 - Rays:
   - They now have a better cell wave pattern, but it's not flowing well.
     - From the stunner and towers:
       - rays come out in the rows to the direction of the mouse.
       - each ray comes out from the very next adjacent cell of the unit, this causes the middle row to be one cell forward than the other two
     - From explosions:
       - rays come out of the center in all directions, uniformly, respecting the range of the ray

 - Idle animation:
   - I want to add an animation to blocks when they are idle, and only when idle:
     - builder: does a normal speed, 90 degree revolution, with acceleration and deceleration
     - soldier swords: spin with a high speed and high acceleration and decelaration
     - stunner icon: spins constantly

 - Block sprites:
    - swords: should have a pulsing glow; one arm should fall off at every hp lost (right now the soldier is losing two arms when 2 hp is lost and not 1 by hp)
    - jumper: is some kinda of "lava" ball. The ball gets smaller when jumper loses HP. When the jumper moves, it leaves a ghost behind that fades. When he jumps, the ghost is a fast blur in the jumped cells.
    - warden: same shade as the builder but has a white shield as icon that glows
    - Terrain blocks (neutral): Currently they are only a single color in the grid. They should be drawn like player blocks:
      - SolidWall -> (old Terrain) can't be destroyed -> let's make it with the same sprite as the player wall but with a neutral gray color and some obsidian stripes
      - CrackedWall -> can be destroyed by a blast ray / stun ray -> for now, let's have the same sprite as the SolidWall but a slight lighter color
      - WeakWall -> can be destroyed by two surrounding soldiers or jumper -> for now, let's have the same sprite as the CrackedWall but a slight lighter color

 - Blueprints:
   - they must be team colored, right now they are always blue. Supply is gray, should be team color too.

 - Towers: the have no formation visuals (outline + diamond). These need to also be added to the PlayerPalette

 - Shaders: Only one is being applied. Directional bloom is a bit weird, generates some permanent artifacts. Screen distortion is too intense.

# Gameplay
 - a stunned unit should have it's abilities paused while stunned. stunned units in formations should disable that formation while stunned
 - instead of the stunner cooldown making it immobile, let's make the cooldown mobile but with 1/3 of the speed
 - stun ray should penetrate units; it should stop only at walls (and kill the first wall)

# Hud
 - builder does not have an attack move command