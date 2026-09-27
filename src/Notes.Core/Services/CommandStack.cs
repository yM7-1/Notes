namespace Notes.Core.Services;

/// <summary>
/// Small undo/redo stack. <see cref="Execute"/> applies the command; use
/// <see cref="PushApplied"/> when the change has already been made by the caller
/// (e.g. a finished drag) and only needs to be recorded.
/// </summary>
public sealed class CommandStack
{
    private readonly List<INotesCommand> _done = new();
    private readonly List<INotesCommand> _undone = new();

    private int _limit = 200;

    public int Limit
    {
        get => _limit;
        set
        {
            _limit = Math.Clamp(value, 1, 10000);
            Trim();
        }
    }

    public bool CanUndo => _done.Count > 0;

    public bool CanRedo => _undone.Count > 0;

    public void Execute(INotesCommand command)
    {
        command.Do();
        PushApplied(command);
    }

    public void PushApplied(INotesCommand command)
    {
        _done.Add(command);
        _undone.Clear();
        Trim();
    }

    public bool Undo()
    {
        if (!CanUndo)
        {
            return false;
        }
        var command = _done[^1];
        _done.RemoveAt(_done.Count - 1);
        command.Undo();
        _undone.Add(command);
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo)
        {
            return false;
        }
        var command = _undone[^1];
        _undone.RemoveAt(_undone.Count - 1);
        command.Do();
        _done.Add(command);
        return true;
    }

    public void Clear()
    {
        _done.Clear();
        _undone.Clear();
    }

    private void Trim()
    {
        while (_done.Count > Limit)
        {
            _done.RemoveAt(0);
        }
    }
}
