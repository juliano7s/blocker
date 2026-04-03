
# Controls
 - builder should not have an attack move command

# Visuals

 - Rooting animation:
   - clockwise line spinning
   - diagonal stripes moving bottom right direction when rooting; moving top left direction when uprooting. The stripes get darker when rooting and lighter when uprooting.

 - Idle animation:
   - I want to add an animation to blocks when they are idle:
     - builder: does a normal speed 90 degree revolution with acceleration and deceleration
     - soldier swords: spin with a high speed and high acceleration and decelaration
     - stunner icon: spins constantly

 - Block sprites:
    - swords: should have a pulsing glow; one arm should fall off at every hp lost
    - stunner: icon is a diamond in team color with a pulsing glow
    - jumper: last design is a gradient block with a "lava" ball - team colored - in the middle. The ball gets smaller when jumper loses HP.
    - wall: should have tiny bricks inside
    - warden: same shade as the builder but has a white shield as icon that glows
    - Terrain blocks (neutral): Currently they are only a single color in the grid. They should be drawn like player blocks:
      - SolidWall -> (old Terrain) can't be destroyed
      - CrackedWall -> can be destroyed by a blast ray / stun ray
      - WeakWall -> can be destroyed by two surrounding soldiers or jumper

 - Blueprints:
   - they must be team colored, right now they are always blue. Supply is gray, should be team color too.

# Gameplay
 - a stunned unit should have it's abilities paused while stunned. stunned units in formations should disable that formation while stunned
 - instead of the stunner cooldown making it immobile, let's make the cooldown mobile but with 1/3 of the speed
 - stun ray should penetrate units; it should stop only at walls (and kill the first wall)

# Hud
 - builder does not have an attack move command