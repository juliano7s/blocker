namespace Blocker.Game.Editor;

public class EditorActionStack
{
    private readonly List<EditorAction> _undoStack = [];
    private readonly List<EditorAction> _redoStack = [];
    private const int MaxDepth = 200;

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public void Push(EditorAction action)
    {
        _undoStack.Add(action);
        _redoStack.Clear();
        if (_undoStack.Count > MaxDepth)
            _undoStack.RemoveAt(0);
    }

    public EditorAction? Undo()
    {
        if (_undoStack.Count == 0) return null;
        var action = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        _redoStack.Add(action);
        return action;
    }

    public EditorAction? Redo()
    {
        if (_redoStack.Count == 0) return null;
        var action = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);
        _undoStack.Add(action);
        return action;
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }
}
