using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Godot;

namespace Blocker.Game.Input;

/// <summary>
/// Manages the current selection state and control groups.
/// Separated from SelectionManager to keep the core logic clean.
/// </summary>
public class SelectionState
{
    private readonly List<Block> _selectedBlocks = [];
    private readonly Dictionary<int, List<int>> _controlGroups = new();

    public IReadOnlyList<Block> SelectedBlocks => _selectedBlocks;

    public IReadOnlyDictionary<int, IReadOnlyList<int>> ControlGroups =>
        _controlGroups.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<int>)kvp.Value.AsReadOnly());

    public void Clear() => _selectedBlocks.Clear();

    public void Select(Block block, bool additive = false)
    {
        if (!additive) _selectedBlocks.Clear();

        if (_selectedBlocks.Contains(block))
            _selectedBlocks.Remove(block);
        else
            _selectedBlocks.Add(block);
    }

    public void SelectOnly(Block block)
    {
        _selectedBlocks.Clear();
        _selectedBlocks.Add(block);
    }

    public void Deselect(int blockId)
    {
        _selectedBlocks.RemoveAll(b => b.Id == blockId);
    }

    public void SelectAll(IEnumerable<Block> blocks, bool additive = false)
    {
        if (!additive) _selectedBlocks.Clear();
        foreach (var b in blocks)
        {
            if (!_selectedBlocks.Contains(b))
                _selectedBlocks.Add(b);
        }
    }

    public void RemoveDeadBlocks(GameState gameState)
    {
        _selectedBlocks.RemoveAll(b => !gameState.Blocks.Contains(b));
    }

    public void AssignGroup(int index)
    {
        if (_selectedBlocks.Count > 0)
        {
            _controlGroups[index] = _selectedBlocks.Select(b => b.Id).ToList();
            GD.Print($"Assigned {_selectedBlocks.Count} blocks to group {index}");
        }
    }

    public void SelectGroup(int index, GameState gameState, int controllingPlayer)
    {
        if (!_controlGroups.TryGetValue(index, out var ids)) return;

        _selectedBlocks.Clear();
        foreach (var id in ids)
        {
            var block = gameState.Blocks.FirstOrDefault(b => b.Id == id);
            if (block != null && block.PlayerId == controllingPlayer)
                _selectedBlocks.Add(block);
        }

        // Clean up dead block IDs from the group record
        _controlGroups[index] = _selectedBlocks.Select(b => b.Id).ToList();
        GD.Print($"Selected group {index}: {_selectedBlocks.Count} blocks");
    }
}
