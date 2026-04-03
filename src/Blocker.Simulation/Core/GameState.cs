using Blocker.Simulation.Blocks;
using Blocker.Simulation.Commands;
using Blocker.Simulation.Systems;

namespace Blocker.Simulation.Core;

public class GameState
{
    public Grid Grid { get; }
    public List<Block> Blocks { get; } = [];
    public List<Player> Players { get; } = [];
    public List<Nest> Nests { get; } = [];
    public List<Formation> Formations { get; } = [];
    public List<Tower> Towers { get; } = [];
    public List<Ray> Rays { get; } = [];
    public List<PushWave> PushWaves { get; } = [];
    public List<VisualEvent> VisualEvents { get; } = [];
    public int TickNumber { get; private set; }

    public GameState(Grid grid)
    {
        Grid = grid;
    }

    public Block? GetBlockAt(GridPos pos)
    {
        if (!Grid.InBounds(pos)) return null;
        var blockId = Grid[pos].BlockId;
        return blockId.HasValue ? GetBlock(blockId.Value) : null;
    }

    public Block? GetBlock(int id) => Blocks.Find(b => b.Id == id);

    public int GetPopulation(int playerId) =>
        Blocks.Where(b => b.PlayerId == playerId).Sum(b => b.PopCost);

    public Block AddBlock(BlockType type, int playerId, GridPos pos)
    {
        var block = new Block
        {
            Type = type,
            PlayerId = playerId,
            Pos = pos,
            PrevPos = pos,
            Hp = type switch
            {
                BlockType.Soldier => Constants.SoldierMaxHp,
                BlockType.Jumper => Constants.JumperMaxHp,
                _ => 0
            }
        };
        Blocks.Add(block);
        Grid[pos].BlockId = block.Id;
        return block;
    }

    public void RemoveBlock(Block block)
    {
        if (Grid.InBounds(block.Pos) && Grid[block.Pos].BlockId == block.Id)
            Grid[block.Pos].BlockId = null;
        Blocks.Remove(block);
    }

    /// <summary>
    /// Move a block from its current cell to a new cell. Updates grid occupancy.
    /// Returns false if the target cell is occupied or out of bounds.
    /// </summary>
    public bool TryMoveBlock(Block block, GridPos newPos)
    {
        if (!Grid.InBounds(newPos)) return false;
        var targetCell = Grid[newPos];
        if (targetCell.BlockId.HasValue) return false;
        if (!targetCell.IsPassable) return false;

        // Vacate old cell
        if (Grid.InBounds(block.Pos) && Grid[block.Pos].BlockId == block.Id)
            Grid[block.Pos].BlockId = null;

        block.PrevPos = block.Pos;
        block.Pos = newPos;
        targetCell.BlockId = block.Id;

        VisualEvents.Add(new VisualEvent(VisualEventType.BlockMoved, newPos, block.PlayerId, BlockId: block.Id));
        return true;
    }

    /// <summary>
    /// Process commands from all players. Validates ownership and preconditions.
    /// </summary>
    public void ProcessCommands(List<Command> commands)
    {
        foreach (var cmd in commands)
        {
            foreach (var blockId in cmd.BlockIds)
            {
                var block = GetBlock(blockId);
                if (block == null || block.PlayerId != cmd.PlayerId) continue;

                var queued = new QueuedCommand(cmd.Type, cmd.TargetPos, cmd.Direction);

                if (cmd.Queue)
                {
                    // Shift+action: append to queue
                    block.CommandQueue.Enqueue(queued);
                }
                else
                {
                    // Immediate: clear queue and execute
                    block.CommandQueue.Clear();
                    ExecuteCommand(block, queued);
                }
            }
        }

        // Process queued commands for blocks that are idle
        foreach (var block in Blocks)
        {
            if (block.CommandQueue.Count == 0) continue;
            if (!IsBlockIdle(block)) continue;

            var next = block.CommandQueue.Peek();
            if (TryExecuteCommand(block, next))
                block.CommandQueue.Dequeue();
        }
    }

    private void ExecuteCommand(Block block, QueuedCommand cmd)
    {
        switch (cmd.Type)
        {
            case CommandType.Move:
                if (cmd.TargetPos.HasValue && block.IsMobile)
                {
                    block.MoveTarget = cmd.TargetPos.Value;
                    block.IsAttackMoving = false;
                }
                break;

            case CommandType.AttackMove:
                if (cmd.TargetPos.HasValue && block.IsMobile)
                {
                    block.MoveTarget = cmd.TargetPos.Value;
                    block.IsAttackMoving = true;
                }
                break;

            case CommandType.Root:
                RootingSystem.ToggleRoot(block);
                // Auto-chain: if W was queued during rooting, it stays in queue
                break;

            case CommandType.ConvertToWall:
                if (block.IsFullyRooted)
                    RootingSystem.ConvertToWall(this, block);
                else if (block.State == BlockState.Rooting)
                {
                    // Queue the convert — will fire when fully rooted
                    block.CommandQueue.Enqueue(cmd);
                }
                break;

            case CommandType.FireStunRay:
                if (cmd.Direction.HasValue)
                    StunSystem.FireStunRay(this, block, cmd.Direction.Value);
                break;

            case CommandType.SelfDestruct:
                StunSystem.SelfDestruct(this, block);
                break;

            case CommandType.CreateTower:
                TowerSystem.CreateTower(this, block);
                break;

            case CommandType.TogglePush:
                PushSystem.TogglePush(block, cmd.Direction);
                break;

            case CommandType.MagnetPull:
                WardenSystem.MagnetPull(this, block);
                break;

            case CommandType.Jump:
                if (cmd.Direction.HasValue)
                    JumperSystem.Jump(this, block, cmd.Direction.Value);
                break;
        }
    }

    /// <summary>
    /// Try to execute a queued command. Returns true if it was consumed (executed or invalid).
    /// </summary>
    private bool TryExecuteCommand(Block block, QueuedCommand cmd)
    {
        switch (cmd.Type)
        {
            case CommandType.Move:
                if (!block.IsMobile) return false; // Wait until mobile
                if (cmd.TargetPos.HasValue)
                {
                    block.MoveTarget = cmd.TargetPos.Value;
                    block.IsAttackMoving = false;
                }
                return true;

            case CommandType.AttackMove:
                if (!block.IsMobile) return false;
                if (cmd.TargetPos.HasValue)
                {
                    block.MoveTarget = cmd.TargetPos.Value;
                    block.IsAttackMoving = true;
                }
                return true;

            case CommandType.Root:
                RootingSystem.ToggleRoot(block);
                return true;

            case CommandType.ConvertToWall:
                if (block.IsFullyRooted)
                {
                    RootingSystem.ConvertToWall(this, block);
                    return true;
                }
                return false; // Wait until rooted

            case CommandType.FireStunRay:
                if (block.IsOnCooldown) return false;
                if (cmd.Direction.HasValue)
                    StunSystem.FireStunRay(this, block, cmd.Direction.Value);
                return true;

            case CommandType.SelfDestruct:
                return StunSystem.SelfDestruct(this, block);

            case CommandType.CreateTower:
                return TowerSystem.CreateTower(this, block);

            case CommandType.TogglePush:
                return PushSystem.TogglePush(block, cmd.Direction);

            case CommandType.MagnetPull:
                return WardenSystem.MagnetPull(this, block);

            case CommandType.Jump:
                if (block.IsOnCooldown && !block.HasCombo) return false;
                if (cmd.Direction.HasValue)
                    return JumperSystem.Jump(this, block, cmd.Direction.Value);
                return true;

            default:
                return true; // Unknown — consume
        }
    }

    private static bool IsBlockIdle(Block block)
    {
        // A block is idle when it has no active task
        if (block.MoveTarget.HasValue) return false;
        if (block.State == BlockState.Rooting) return false;
        if (block.State == BlockState.Uprooting) return false;
        return true;
    }

    /// <summary>
    /// Advance simulation by one tick.
    /// Follows tick resolution order from game bible Section 14.
    /// </summary>
    public void Tick(List<Command>? commands = null)
    {
        VisualEvents.Clear();

        // Step 2: Formations — root/uproot progress, nest detection, supply formations
        RootingSystem.Tick(this);
        NestSystem.DetectNests(this);
        FormationSystem.DetectFormations(this);

        // Steps 3-4: Towers — fire stun/blast rays
        TowerSystem.Tick(this);

        // Step 5: Stun — advance rays, apply effects, decay cooldowns
        StunSystem.Tick(this);

        // Step 6: Variant cooldowns — TODO

        // Steps 7-8: Push fire + wave advance
        PushSystem.Tick(this);

        // Step 9: Command queues — process player commands
        if (commands != null)
            ProcessCommands(commands);

        // Step 10: Warden ZoC — update effective move intervals
        WardenSystem.UpdateZoC(this);

        // Step 11: Snap prevPos for interpolation
        foreach (var block in Blocks)
            block.PrevPos = block.Pos;

        // Step 12: Movement — per-type intervals (uses EffectiveMoveInterval for ZoC slow)
        foreach (var block in Blocks)
        {
            if (block.IsImmobile) continue;
            if (block.IsStunned) continue;
            if (block.IsOnCooldown && block.Type != BlockType.Jumper) continue;
            if (block.IsOnCooldown && block.Type == BlockType.Jumper && !block.HasCombo) continue;
            if (!block.MoveTarget.HasValue) continue;
            if (block.Pos == block.MoveTarget.Value)
            {
                block.MoveTarget = null;
                block.IsAttackMoving = false;
                continue;
            }

            // Attack-move: pause when adjacent to enemies
            if (block.IsAttackMoving)
            {
                bool adjacentEnemy = false;
                foreach (var offset in GridPos.OrthogonalOffsets)
                {
                    var neighbor = block.Pos + offset;
                    var other = GetBlockAt(neighbor);
                    if (other != null && other.PlayerId != block.PlayerId && other.Type != BlockType.Wall)
                    {
                        adjacentEnemy = true;
                        break;
                    }
                }
                if (adjacentEnemy) continue; // Stay in place, let combat resolve
            }

            // Jumper: consuming move target clears combo
            if (block.Type == BlockType.Jumper && block.HasCombo)
                JumperSystem.ConsumeCombo(block);

            // Check if this tick is a move tick — uses EffectiveMoveInterval (ZoC-aware)
            if (TickNumber % block.EffectiveMoveInterval != 0) continue;

            // A* pathfinding toward target
            var target = block.MoveTarget.Value;
            var nextStep = PathfindingSystem.GetNextStep(this, block.Pos, target);

            if (nextStep.HasValue)
            {
                if (TryMoveBlock(block, nextStep.Value))
                    block.StuckTicks = 0;
                else
                    block.StuckTicks++;
            }
            else
            {
                block.StuckTicks++;
            }

            // Give up if stuck too long
            if (block.StuckTicks >= Constants.MoveGiveUpTicks)
            {
                block.MoveTarget = null;
                block.IsAttackMoving = false;
                block.StuckTicks = 0;
                continue;
            }

            // Clear target if we've arrived
            if (block.Pos == target)
                block.MoveTarget = null;
        }

        // Step 13: Combat — surrounding kills + soldier adjacency kills
        CombatSystem.Tick(this);

        // Step 14: Spawning — nest timers and unit production
        NestSystem.TickSpawning(this);
        // Step 15: Death effects — TODO

        // Step 16: Elimination check
        EliminationSystem.Tick(this);

        TickNumber++;
    }
}
