using Notes.Core.Documents;
using Notes.Core.Services;

namespace Notes.Game;

internal enum NotesLibrary
{
    Run,
    Global,
}

/// <summary>
/// In-memory state of the notes mod: the two documents, the active library /
/// board, the undo stack and debounced persistence. All UI talks to this class.
/// </summary>
internal static class NotesRuntime
{
    public static NotesLibrary Library { get; private set; } = NotesLibrary.Global;

    public static NotesDocument RunDocument { get; private set; } = new();

    public static NotesDocument GlobalDocument { get; private set; } = new();

    public static CommandStack Commands { get; } = new();

    /// <summary>Raised after any model change; the UI rebuilds from the model.</summary>
    public static event Action? Changed;

    private static bool _globalLoaded;
    private static float _retryTimer;
    private static bool _runDirty;
    private static bool _globalDirty;
    private static double _saveTimer;

    public static bool RunActive => GameContext.CurrentRun != null;

    public static bool GlobalLoaded => _globalLoaded;

    public static NotesDocument ActiveDocument =>
        Library == NotesLibrary.Run && RunActive ? RunDocument : GlobalDocument;

    public static NotesBoard ActiveBoard => ActiveDocument.EnsureActiveBoard();

    public static void Initialize()
    {
        EnsureGlobalLoaded();
    }

    /// <summary>Called when the run starts, is loaded, or ends.</summary>
    public static void OnRunContextChanged()
    {
        FlushSave();

        var run = GameContext.CurrentRun;
        if (run != null)
        {
            RunDocument = NotesPersistence.LoadRun(run);
            Library = NotesLibrary.Run;
        }
        else
        {
            RunDocument = new NotesDocument();
            if (Library == NotesLibrary.Run)
            {
                Library = NotesLibrary.Global;
            }
        }
        Commands.Clear();
        Raise();
    }

    public static void SetLibrary(NotesLibrary library)
    {
        if (library == NotesLibrary.Run && !RunActive)
        {
            return;
        }
        if (library == Library)
        {
            return;
        }
        Commands.Clear();
        Library = library;
        Raise();
    }

    public static void SetActiveBoard(string boardId)
    {
        ActiveDocument.ActiveBoardId = boardId;
        Raise();
    }

    public static void NewBoard()
    {
        var board = new NotesBoard
        {
            Id = IdFactory.NewBoardId(),
            Name = ModLocalization.T("board_new_name", "Board") + " " + (ActiveDocument.Boards.Count + 1),
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        Commands.Execute(new AddBoardCommand(ActiveDocument, board));
        Raise();
    }

    public static void DeleteBoard(string boardId)
    {
        Commands.Execute(new RemoveBoardCommand(ActiveDocument, boardId));
        Raise();
    }

    public static NotesNode CreateCardNode(CardSnapshot snapshot, float x, float y) => new()
    {
        Id = IdFactory.NewNodeId(),
        Kind = NodeKind.Card,
        RefId = snapshot.RefId,
        Title = snapshot.Title,
        Cost = snapshot.Cost,
        CardType = snapshot.CardType,
        Rarity = snapshot.Rarity,
        Upgraded = snapshot.Upgraded,
        X = x,
        Y = y,
    };

    public static NotesNode CreateTextNode(string title, float x, float y) => new()
    {
        Id = IdFactory.NewNodeId(),
        Kind = NodeKind.Text,
        Title = title,
        Cost = -1,
        CardType = -1,
        Rarity = -1,
        X = x,
        Y = y,
    };

    /// <summary>Any model change goes through here: refreshes the UI and marks
    /// the current library dirty for the debounced save.</summary>
    public static void Raise()
    {
        MarkDirty();
        Changed?.Invoke();
    }

    /// <summary>Marks the active library dirty without rebuilding the UI
    /// (used by pan/zoom, where a full refresh would be wasteful).</summary>
    public static void ScheduleSave() => MarkDirty();

    /// <summary>Remembers where the player parked the notes toggle button.</summary>
    public static void SaveButtonPosition(float x, float y)
    {
        if (NotesPersistence.TryGetGlobalData(out var data))
        {
            data.ButtonX = x;
            data.ButtonY = y;
            NotesPersistence.SaveGlobalNow();
        }
    }

    public static bool TryGetButtonPosition(out float x, out float y)
    {
        if (NotesPersistence.TryGetGlobalData(out var data) && data.ButtonX >= 0f && data.ButtonY >= 0f)
        {
            x = data.ButtonX;
            y = data.ButtonY;
            return true;
        }
        x = 0f;
        y = 0f;
        return false;
    }

    public static void Tick(double delta)
    {
        if (Library == NotesLibrary.Global && !_globalLoaded)
        {
            _retryTimer += (float)delta;
            if (_retryTimer >= 2f)
            {
                _retryTimer = 0f;
                EnsureGlobalLoaded();
            }
        }

        if (!_runDirty && !_globalDirty)
        {
            return;
        }
        _saveTimer += delta;
        if (_saveTimer < 1.5)
        {
            return;
        }
        _saveTimer = 0;
        FlushSave();
    }

    public static void FlushSave()
    {
        if (_runDirty)
        {
            var run = GameContext.CurrentRun;
            if (run != null)
            {
                NotesPersistence.SaveRun(run, RunDocument);
            }
            _runDirty = false;
        }
        if (_globalDirty)
        {
            NotesPersistence.SaveGlobal(GlobalDocument);
            _globalDirty = false;
        }
    }

    private static void MarkDirty()
    {
        if (Library == NotesLibrary.Run && RunActive)
        {
            _runDirty = true;
        }
        else
        {
            _globalDirty = true;
        }
    }

    private static void EnsureGlobalLoaded()
    {
        if (_globalLoaded)
        {
            return;
        }
        if (NotesPersistence.TryLoadGlobal(out var document))
        {
            GlobalDocument = document;
            _globalLoaded = true;
        }
    }
}
