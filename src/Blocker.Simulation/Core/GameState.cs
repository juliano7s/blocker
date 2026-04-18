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

    private int _nextBlockId = 1;
    private int _nextNestId = 1;
    private int _nextTowerId = 1;
    private int _nextFormationId = 1;
    private int _nextRayId = 1;
    private int _nextPushWaveId = 1;

    public int NextNestId() => _nextNestId++;
    public int NextTowerId() => _nextTowerId++;
    public int NextFormationId() => _nextFormationId++;
    public int NextRayId() => _nextRayId++;
    public int NextPushWaveId() => _nextPushWaveId++;

    // O(1) block lookup by ID — maintained by AddBlock/RemoveBlock
    private readonly Dictionary<int, Block> _blockById = new();

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

    public Block? GetBlock(int id) => _blockById.GetValueOrDefault(id);

    public int GetPopulation(int playerId) =>
        Blocks.Where(b => b.PlayerId == playerId).Sum(b => b.PopCost);

    /// <summary>
    /// Look up which team a player belongs to. Returns the playerId itself if no
    /// matching player exists, so callers don't crash on stale references — and so
    /// FFA (where teamId == playerId) keeps working even before MapLoader has set
    /// teams up.
    /// </summary>
    public int GetTeamFor(int playerId)
    {
        for (int i = 0; i < Players.Count; i++)
            if (Players[i].Id == playerId) return Players[i].TeamId;
        return playerId;
    }

    /// <summary>True iff the two players are on different teams (i.e. hostile).</summary>
    public bool AreEnemies(int playerIdA, int playerIdB) =>
        playerIdA != playerIdB && GetTeamFor(playerIdA) != GetTeamFor(playerIdB);

    /// <summary>True iff two blocks are on different teams.</summary>
    public bool AreEnemies(Block a, Block b) => AreEnemies(a.PlayerId, b.PlayerId);

    /// <summary>True iff two blocks are on the same team (allies or self).</summary>
    public bool AreAllies(Block a, Block b) => !AreEnemies(a, b);

    public Block AddBlock(BlockType type, int playerId, GridPos pos)
    {
        var block = new Block
        {
            Id = _nextBlockId++,
            Type = type,
            PlayerId = playerId,
            Pos = pos,
            PrevPos = pos,
            State = type == BlockType.Wall ? BlockState.Rooted : BlockState.Mobile,
            RootProgress = type == BlockType.Wall ? Constants.RootTicks : 0,
            Hp = type switch
            {
                BlockType.Soldier => Constants.SoldierMaxHp,
                BlockType.Jumper => Constants.JumperMaxHp,
                _ => 0
            }
        };
        Blocks.Add(block);
        _blockById[block.Id] = block;
        Grid[pos].BlockId = block.Id;
        return block;
    }

    public void RemoveBlock(Block block)
    {
        if (Grid.InBounds(block.Pos) && Grid[block.Pos].BlockId == block.Id)
            Grid[block.Pos].BlockId = null;

        // Swap-with-last + remove-at-end: O(1) instead of O(n) array shift
        int index = Blocks.IndexOf(block);
        if (index >= 0)
        {
            int last = Blocks.Count - 1;
            if (index < last)
                Blocks[index] = Blocks[last];
            Blocks.RemoveAt(last);
        }

        _blockById.Remove(block.Id);
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
            // Player-level commands bypass the per-block loop. Surrender carries
            // an empty BlockIds list — it acts on the Player record, not on units.
            if (cmd.Type == CommandType.Surrender)
            {
                var player = Players.FirstOrDefault(p => p.Id == cmd.PlayerId);
                if (player != null && !player.IsEliminated)
                {
                    player.IsEliminated = true;
                    VisualEvents.Add(new VisualEvent(
                        VisualEventType.PlayerEliminated, new GridPos(0, 0), player.Id));
                }
                continue;
            }

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

            foreach (var blockId in cmd.BlockIds)
            {
                var block = GetBlock(blockId);
                if (block == null || block.PlayerId != cmd.PlayerId) continue;

                // Emit visual/audio event at receipt time so the player gets
                // feedback even when the command is auto-queued.
                EmitCommandReceiptEvent(block, cmd);

                var queued = new QueuedCommand(cmd.Type, cmd.TargetPos, cmd.Direction);

                if (cmd.Queue)
                {
                    // Shift+action: append to queue
                    block.CommandQueue.Enqueue(queued);
                }
                else
                {
                    // Immediate: clear queue
                    block.CommandQueue.Clear();

                    // Root on rooting/uprooting block = immediate toggle (cancel)
                    bool isRootingOrUprooting = block.State is BlockState.Rooting or BlockState.Uprooting;
                    if (cmd.Type == CommandType.Root && isRootingOrUprooting)
                    {
                        ExecuteCommand(block, queued);
                    }
                    else if (IsAutoQueueBusy(block))
                    {
                        // Auto-queue: "do this next" when busy (rooting/uprooting/immobile cooldown)
                        block.CommandQueue.Enqueue(queued);
                    }
                    else
                    {
                        ExecuteCommand(block, queued);
                    }
                }
            }
        }

        // Process queued commands for blocks that are idle.
        // Snapshot IDs: TryExecuteCommand may mutate Blocks (e.g. Jump kills
        // enemies, SelfDestruct removes the actor), which would invalidate a
        // direct foreach.
        var blockIds = new List<int>(Blocks.Count);
        foreach (var b in Blocks) blockIds.Add(b.Id);
        foreach (var id in blockIds)
        {
            var block = GetBlock(id);
            if (block == null) continue;
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
                ExplosionSystem.SelfDestruct(this, block);
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
                {
                    var (dir, dist) = ResolveJumpDirAndRange(block.Pos, cmd.Direction, cmd.TargetPos);
                    if (dir.HasValue)
                        JumperSystem.Jump(this, block, dir.Value, dist);
                }
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
                if (!block.IsMobile) return true; // Drop — rooted block can't move
                if (cmd.TargetPos.HasValue)
                {
                    block.MoveTarget = cmd.TargetPos.Value;
                    block.IsAttackMoving = false;
                }
                return true;

            case CommandType.AttackMove:
                if (!block.IsMobile) return true; // Drop — rooted block can't attack-move
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
                return ExplosionSystem.SelfDestruct(this, block);

            case CommandType.CreateTower:
                return TowerSystem.CreateTower(this, block);

            case CommandType.TogglePush:
                return PushSystem.TogglePush(block, cmd.Direction);

            case CommandType.MagnetPull:
                return WardenSystem.MagnetPull(this, block);

            case CommandType.Jump:
                if (block.IsOnCooldown && !block.HasCombo) return false;
                {
                    var (dir, dist) = ResolveJumpDirAndRange(block.Pos, cmd.Direction, cmd.TargetPos);
                    if (dir.HasValue)
                        return JumperSystem.Jump(this, block, dir.Value, dist);
                }
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
        if (block.IsOnCooldown && !block.MobileCooldown) return false; // Immobile cooldown
        return true;
    }

    /// <summary>
    /// Whether a block is in a state where immediate commands should be auto-queued
    /// rather than executed (or dropped). This is narrower than IsBlockBusy: having
    /// a MoveTarget is NOT auto-queue-busy because immediate commands should just
    /// replace the current target.
    /// </summary>
    private static bool IsAutoQueueBusy(Block block)
    {
        if (block.State == BlockState.Rooting) return true;
        if (block.State == BlockState.Uprooting) return true;
        if (block.IsOnCooldown && !block.MobileCooldown) return true; // Immobile cooldown
        return false;
    }

    /// <summary>
    /// Emit visual/audio event at command receipt time based on the block's
    /// current state and the command type. Gives player feedback even when
    /// the command is auto-queued.
    /// </summary>
    private void EmitCommandReceiptEvent(Block block, Command cmd)
    {
        switch (cmd.Type)
        {
            case CommandType.Move:
            case CommandType.AttackMove:
                if (cmd.TargetPos.HasValue && block.IsMobile)
                {
                    VisualEvents.Add(new VisualEvent(VisualEventType.CommandMoveIssued,
                        block.Pos, block.PlayerId,
                        Direction: DirectionFromDelta(block.Pos, cmd.TargetPos.Value),
                        BlockId: block.Id));
                }
                break;

            case CommandType.Root:
                // Determine what state the root will transition to
                var rootEvtType = block.State switch
                {
                    BlockState.Mobile => VisualEventType.CommandRootIssued,
                    BlockState.Rooting => VisualEventType.CommandUprootIssued,    // Cancel rooting
                    BlockState.Rooted => VisualEventType.CommandUprootIssued,
                    BlockState.Uprooting => VisualEventType.CommandRootIssued,    // Cancel uprooting
                    _ => (VisualEventType?)null
                };
                if (rootEvtType.HasValue)
                    VisualEvents.Add(new VisualEvent(rootEvtType.Value, block.Pos, block.PlayerId, BlockId: block.Id));
                break;

            case CommandType.ConvertToWall:
                VisualEvents.Add(new VisualEvent(VisualEventType.CommandWallIssued,
                    block.Pos, block.PlayerId, BlockId: block.Id));
                break;
        }
    }

    /// <summary>
    /// Compute the cardinal direction from one position toward another.
    /// Returns the dominant axis direction.
    /// </summary>
    private static Direction DirectionFromDelta(GridPos from, GridPos to)
    {
        int dx = to.X - from.X;
        int dy = to.Y - from.Y;
        if (Math.Abs(dx) >= Math.Abs(dy))
            return dx >= 0 ? Direction.Right : Direction.Left;
        return dy >= 0 ? Direction.Down : Direction.Up;
    }

    /// <summary>
    /// Compute cell distance from a target position projected onto a cardinal direction.
    /// </summary>
    private static int? CellDistance(GridPos from, Direction dir, GridPos? target)
    {
        if (!target.HasValue) return null;
        var offset = dir.ToOffset();
        return offset.X != 0
            ? Math.Abs(target.Value.X - from.X)
            : Math.Abs(target.Value.Y - from.Y);
    }

    /// <summary>
    /// Resolve (direction, range) for a Jump command at execution time.
    /// If a TargetPos is provided, the direction is re-derived from the block's
    /// CURRENT position toward the target — this is critical for queued jumps
    /// where the jumper may have moved (or been pushed) after the click, which
    /// would otherwise leave the stored direction pointing away from the target.
    /// If the jumper is already at the target, no direction is returned.
    /// </summary>
    private static (Direction? dir, int? range) ResolveJumpDirAndRange(
        GridPos from, Direction? storedDir, GridPos? target)
    {
        if (target.HasValue)
        {
            if (target.Value == from) return (null, null);
            var dir = DirectionFromDelta(from, target.Value);
            return (dir, CellDistance(from, dir, target));
        }
        if (storedDir.HasValue) return (storedDir, null);
        return (null, null);
    }

    /// <summary>
    /// Explicitly clear visual events. Should be called by the renderer once per frame
    /// to ensure events accumulated across multiple ticks are processed together.
    /// </summary>
    public void ClearVisualEvents() => VisualEvents.Clear();

    /// <summary>
    /// Advance simulation by one tick.
    /// Follows tick resolution order from game bible Section 14.
    /// </summary>
    public void Tick(List<Command>? commands = null)
    {
        // Step 1: Clear push and jump flags from previous tick
        foreach (var block in Blocks)
        {
            block.WasPushedThisTick = false;
            block.WasJumpedThisTick = false;
        }

        // Step 2: Formations — root/uproot progress, nest detection, supply formations
        RootingSystem.Tick(this);
        NestSystem.DetectNests(this);
        FormationSystem.DetectFormations(this);

        // Steps 3-4: Towers — fire stun/blast rays
        TowerSystem.Tick(this);

        // Step 5: Stun — advance rays, apply effects, decay cooldowns
        StunSystem.Tick(this);

        // Step 6: Decay ability cooldowns (Jumper jump, Stunner fire, Warden pull)
        foreach (var block in Blocks)
        {
            if (block.Cooldown > 0)
            {
                block.Cooldown--;
                if (block.Cooldown == 0)
                    block.MobileCooldown = false;
            }
        }

        // Steps 7-8: Push fire + wave advance
        PushSystem.Tick(this);

        // Step 9: Command queues — process player commands
        if (commands != null)
            ProcessCommands(commands);

        // Step 10: Warden ZoC — update effective move intervals
        WardenSystem.UpdateZoC(this);

        // Step 11: Snap prevPos for interpolation (except pushed or jumped blocks)
        foreach (var block in Blocks)
        {
            if (!block.WasPushedThisTick && !block.WasJumpedThisTick)
                block.PrevPos = block.Pos;
        }

        // Step 12: Movement — per-type intervals (uses EffectiveMoveInterval for ZoC slow)
        foreach (var block in Blocks)
        {
            if (block.IsImmobile) continue;
            if (block.IsStunned) continue;
            if (block.IsOnCooldown && block.Type != BlockType.Jumper && block.Type != BlockType.Stunner) continue;
            if (block.IsOnCooldown && block.Type == BlockType.Jumper && !block.HasCombo && !block.MobileCooldown) continue;
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
                    if (other != null && AreEnemies(other, block) && other.Type != BlockType.Wall)
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

            // Check if this tick is a move tick — uses EffectiveMoveInterval (ZoC + stunner cooldown aware)
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

        // Step 15: Death effects — Handled by Godot layer (VisualEvents -> EffectManager)
        
        // Step 16: Elimination check
        EliminationSystem.Tick(this);

        TickNumber++;
    }
}
