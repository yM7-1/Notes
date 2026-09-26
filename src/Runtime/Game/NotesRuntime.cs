using MegaCrit.Sts2.Core.Combat;
using Notes.Core.Documents;
using Notes.Core.Services;

namespace Notes.Game;

internal enum NotesLibrary
{
    Run,
    Global,
}

internal enum NotesSelectionKind
{
    None,
    Node,
    Region,
    WorldLine,
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

    /// <summary>Raised when the captured operation log changes.</summary>
    public static event Action? OpsChanged;

    /// <summary>Raised when the inspected node / region / world line changes.</summary>
    public static event Action? SelectionChanged;

    public static NotesSelectionKind SelectionKind { get; private set; } = NotesSelectionKind.None;

    public static string SelectionId { get; private set; } = "";

    /// <summary>Node under the mouse; the inspector previews it without changing
    /// the selection.</summary>
    public static string HoverNodeId { get; private set; } = "";

    public static void SetHoverNode(string nodeId)
    {
        if (HoverNodeId == nodeId)
        {
            return;
        }
        HoverNodeId = nodeId;
        SelectionChanged?.Invoke();
    }

    public static void ClearHoverNode(string nodeId)
    {
        if (HoverNodeId != nodeId)
        {
            return;
        }
        HoverNodeId = "";
        SelectionChanged?.Invoke();
    }

    /// <summary>Short feedback for the last import action (shown in the status bar).</summary>
    public static string LastImportMessage { get; private set; } = "";

    public static void SelectNode(string nodeId) => SetSelection(NotesSelectionKind.Node, nodeId);

    public static void SelectRegion(string regionId) => SetSelection(NotesSelectionKind.Region, regionId);

    public static void SelectWorldLine(string worldLineId) => SetSelection(NotesSelectionKind.WorldLine, worldLineId);

    public static void ClearSelection() => SetSelection(NotesSelectionKind.None, "");

    private static void SetSelection(NotesSelectionKind kind, string id)
    {
        if (kind == SelectionKind && id == SelectionId)
        {
            return;
        }
        SelectionKind = kind;
        SelectionId = id;
        SelectionChanged?.Invoke();
    }

    private static bool _globalLoaded;
    private static float _retryTimer;
    private static bool _runDirty;
    private static bool _globalDirty;
    private static double _saveTimer;
    private static string _combatKey = "";

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
        HoverNodeId = "";

        var run = GameContext.CurrentRun;
        if (run != null)
        {
            RunDocument = NotesPersistence.LoadRun(run, out var ops, out _combatKey);
            RunDocument.EnsureOverviewBoard(OverviewBoardName());
            RunDocument.EnsureCurrentBoard(CurrentBoardName());
            NotesOpLog.Load(ops);
            Library = NotesLibrary.Run;
            RefreshCurrentBoard();
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
        HoverNodeId = "";
        Commands.Clear();
        Library = library;
        Raise();
    }

    public static void SetActiveBoard(string boardId)
    {
        HoverNodeId = "";
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
        var board = ActiveDocument.FindBoard(boardId);
        if (board == null || board.Kind is BoardKind.Overview or BoardKind.Current)
        {
            return; // system boards cannot be deleted
        }
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
        try
        {
            NotesLayout.Apply(ActiveBoard);
        }
        catch
        {
            // layout is best-effort; never block the UI
        }
        MarkDirty();
        FlushRunIfPossible();
        Changed?.Invoke();
    }

    /// <summary>Operation log changed (captured or cleared); rebuild the
    /// read-only current world line and persist quietly.</summary>
    public static void OnOpsChanged()
    {
        MarkDirty();
        RefreshCurrentBoard();
        FlushRunIfPossible();
        OpsChanged?.Invoke();
    }

    /// <summary>Run-scoped data is cheap to write (in-memory bag), so flush it
    /// immediately: a later save/quit-to-menu then always contains our notes.
    /// The dirty flag is only cleared once the write actually succeeded.</summary>
    private static void FlushRunIfPossible()
    {
        if (!_runDirty)
        {
            return;
        }
        var run = GameContext.CurrentRun;
        if (run == null)
        {
            return;
        }
        if (NotesPersistence.SaveRun(run, RunDocument, NotesOpLog.Entries, _combatKey))
        {
            _runDirty = false;
        }
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

    /// <summary>Remembers where the player parked the notes window.</summary>
    public static void SaveWindowPosition(float x, float y)
    {
        if (NotesPersistence.TryGetGlobalData(out var data))
        {
            data.WindowX = x;
            data.WindowY = y;
            NotesPersistence.SaveGlobalNow();
        }
    }

    public static bool TryGetWindowPosition(out float x, out float y)
    {
        if (NotesPersistence.TryGetGlobalData(out var data) && data.WindowX >= 0f && data.WindowY >= 0f)
        {
            x = data.WindowX;
            y = data.WindowY;
            return true;
        }
        x = 0f;
        y = 0f;
        return false;
    }

    /// <summary>Remembers the notes window size.</summary>
    public static void SaveWindowSize(float width, float height)
    {
        if (NotesPersistence.TryGetGlobalData(out var data))
        {
            data.WindowW = width;
            data.WindowH = height;
            NotesPersistence.SaveGlobalNow();
        }
    }

    public static bool TryGetWindowSize(out float width, out float height)
    {
        if (NotesPersistence.TryGetGlobalData(out var data) && data.WindowW > 0f && data.WindowH > 0f)
        {
            width = data.WindowW;
            height = data.WindowH;
            return true;
        }
        width = 0f;
        height = 0f;
        return false;
    }

    /// <summary>Remembers the collapsed state / edge of the notes handle.</summary>
    public static void SaveHandleState(bool collapsed, int side)
    {
        if (NotesPersistence.TryGetGlobalData(out var data))
        {
            data.HandleCollapsed = collapsed;
            data.HandleSide = side;
            NotesPersistence.SaveGlobalNow();
        }
    }

    public static bool TryGetHandleState(out bool collapsed, out int side)
    {
        if (NotesPersistence.TryGetGlobalData(out var data))
        {
            collapsed = data.HandleCollapsed;
            side = data.HandleSide;
            return true;
        }
        collapsed = false;
        side = 0;
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
            if (run == null)
            {
                // Nothing left to write to; the document is discarded with the run.
                _runDirty = false;
            }
            else if (NotesPersistence.SaveRun(run, RunDocument, NotesOpLog.Entries, _combatKey))
            {
                _runDirty = false;
            }
        }
        if (_globalDirty && NotesPersistence.SaveGlobal(GlobalDocument))
        {
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
        if (RunActive)
        {
            // The op log always belongs to the run, whatever library is shown.
            _runDirty = true;
        }
    }

    // ---- structured mode / op log --------------------------------------------

    public static int CurrentTurn => GameContext.Combat?.TurnNumber ?? 0;

    public static int OpsCount => NotesOpLog.Count;

    public static NotesTurnRegion? FindActualRegion(int turn)
    {
        if (turn <= 0 || !RunActive)
        {
            return null;
        }
        var board = CurrentBoard();
        var line = board?.WorldLines.FirstOrDefault();
        return line == null || board == null
            ? null
            : board.TurnRegions.FirstOrDefault(r => r.WorldLineId == line.Id && r.TurnNumber == turn);
    }

    /// <summary>Auto-creates the current turn's region of the actual world line
    /// (regions now appear automatically from turn 1, no opt-in needed).</summary>
    public static void EnsureActualTurnRegion(int turn)
    {
        if (turn <= 0 || !RunActive)
        {
            return;
        }
        var document = RunDocument;
        var board = CurrentBoard()!;
        var line = document.EnsureActualWorldLine(board.Id, board.Name);
        document.EnsureTurnRegion(board.Id, line.Id, turn);
        Raise();
    }

    /// <summary>Stores the end-of-turn state snapshot on the actual turn region.</summary>
    public static void UpdateActualRegionSnapshot(int turn, string snapshot, int hp, int maxHp)
    {
        if (turn <= 0 || !RunActive)
        {
            return;
        }
        var document = RunDocument;
        var board = CurrentBoard()!;
        var line = document.EnsureActualWorldLine(board.Id, board.Name);
        var region = document.EnsureTurnRegion(board.Id, line.Id, turn);
        region.Snapshot = snapshot;
        region.Hp = hp;
        region.MaxHp = maxHp;
        Raise();
    }

    public static bool HasRelicNode(int turn, string refId)
    {
        var region = FindActualRegion(turn);
        var board = CurrentBoard();
        if (region == null || board == null)
        {
            return false;
        }
        return board.NodesOfRegion(region.Id)
            .Any(n => n.Kind == NodeKind.Relic && n.RefId == refId);
    }

    public static void AddTurnEvent(int turn, NotesAnnotation annotation)
    {
        if (turn <= 0 || !RunActive)
        {
            return;
        }
        var document = RunDocument;
        var board = CurrentBoard()!;
        var line = document.EnsureActualWorldLine(board.Id, board.Name);
        var region = document.EnsureTurnRegion(board.Id, line.Id, turn);
        if (!region.TurnEvents.Any(a => a.RefId == annotation.RefId && a.Text == annotation.Text))
        {
            region.TurnEvents.Add(annotation.Clone());
        }
        Raise();
    }

    /// <summary>Adds or merges an auto-captured turn event. Exhaust / discard
    /// annotations merge names into one chip; insertion annotations count
    /// copies.</summary>
    public static void MergeTurnEvent(int turn, NotesAnnotation annotation)
    {
        if (turn <= 0 || !RunActive)
        {
            return;
        }
        var document = RunDocument;
        var board = CurrentBoard()!;
        var line = document.EnsureActualWorldLine(board.Id, board.Name);
        var region = document.EnsureTurnRegion(board.Id, line.Id, turn);
        var existing = region.TurnEvents.FirstOrDefault(a => a.RefId == annotation.RefId);
        if (existing == null)
        {
            region.TurnEvents.Add(annotation.Clone());
        }
        else if (annotation.RefId is "exhaust:" or "discard:")
        {
            if (!existing.Text.Contains(annotation.Text, StringComparison.Ordinal))
            {
                existing.Text += "、" + annotation.Text;
            }
        }
        else
        {
            existing.Count += annotation.Count;
        }
        Raise();
    }

    // ---- boards / world lines ------------------------------------------------

    public static string WorldLineBoardName(int ordinal) =>
        ModLocalization.T("board_world_line", "世界线") + ordinal;

    public static string OverviewBoardName() =>
        ModLocalization.T("board_overview", "自由总览");

    public static string CurrentBoardName() =>
        ModLocalization.T("board_current", "当前世界线");

    /// <summary>Read-only board auto-recorded from the live op log; created on demand.</summary>
    public static NotesBoard? CurrentBoard()
    {
        if (!RunActive)
        {
            return null;
        }
        return RunDocument.EnsureCurrentBoard(CurrentBoardName());
    }

    /// <summary>Rebuilds the read-only current world line from the captured
    /// operations (called on every capture, save-load and combat start).</summary>
    public static void RefreshCurrentBoard()
    {
        if (!RunActive)
        {
            return;
        }
        var document = RunDocument;
        var board = document.EnsureCurrentBoard(CurrentBoardName());
        var line = document.EnsureActualWorldLine(board.Id, board.Name);
        var ops = NotesOpLog.Entries;
        var turns = ops.Where(o => o.Turn > 0).Select(o => o.Turn).Distinct().ToList();
        var current = CurrentTurn;
        if (current > 0)
        {
            turns.Add(current);
        }
        if (turns.Count == 0)
        {
            turns.Add(1);
        }
        foreach (var turn in turns.Distinct().OrderBy(t => t))
        {
            var region = document.EnsureTurnRegion(board.Id, line.Id, turn);
            NotesImporter.Apply(document, board, region, ops);
        }
        // Turns that no longer exist in the log (e.g. the log was cleared) must
        // not keep stale auto-recorded nodes.
        foreach (var region in board.TurnRegions.Where(r => !turns.Contains(r.TurnNumber)).ToList())
        {
            foreach (var node in board.NodesOfRegion(region.Id)
                .Where(n => n.SourceOpId.Length > 0)
                .ToList())
            {
                document.RemoveNode(board.Id, node.Id);
            }
        }
        Raise();
    }

    /// <summary>Called when a new combat shows up: boards reset to the default
    /// pair — the free overview plus the read-only current world line. The same
    /// combat (save-load / re-entry) keeps its notes.</summary>
    public static void OnCombatStarted(CombatState state)
    {
        if (!RunActive)
        {
            return;
        }
        var key = CombatKeyOf(state);
        if (key == _combatKey)
        {
            return;
        }
        _combatKey = key;
        RunDocument = new NotesDocument();
        RunDocument.EnsureOverviewBoard(OverviewBoardName());
        RunDocument.EnsureCurrentBoard(CurrentBoardName());
        Commands.Clear();
        ClearSelection();
        _runDirty = true;
        FlushSave();
        Raise();
    }

    private static string CombatKeyOf(CombatState state)
    {
        try
        {
            var run = GameContext.CurrentRun;
            return (run?.CurrentActIndex ?? 0) + ":" + (run?.TotalFloor ?? 0)
                + ":" + (state.Encounter?.Id.Entry ?? "");
        }
        catch
        {
            return "";
        }
    }

    public static void NewWorldLine()
    {
        var document = ActiveDocument;
        var ordinal = document.NextWorldLineOrdinal();
        var board = document.CreateWorldLineBoard(WorldLineBoardName(ordinal));
        var line = document.EnsureActualWorldLine(board.Id, board.Name);
        // A brand-new line starts empty at turn 1; keep the current turn visible too.
        document.EnsureTurnRegion(board.Id, line.Id, 1);
        var turn = CurrentTurn;
        if (turn > 1)
        {
            document.EnsureTurnRegion(board.Id, line.Id, turn);
        }
        Raise();
    }

    /// <summary>Records the current turn's operations into the selected
    /// interactive world-line board, overwriting that turn's imported chain.
    /// On the read-only current line the turn is copied into a fresh
    /// interactive line first, so "record this turn" always lands somewhere
    /// editable.</summary>
    public static void Import(bool currentTurnOnly)
    {
        if (!RunActive)
        {
            LastImportMessage = ModLocalization.T("import_empty", "没有可录入的操作");
            Raise();
            return;
        }
        if (Library != NotesLibrary.Run)
        {
            SetLibrary(NotesLibrary.Run);
        }
        var ops = CollectOps();
        if (ops.Count == 0)
        {
            LastImportMessage = ModLocalization.T("import_empty", "没有可录入的操作");
            Raise();
            return;
        }
        var board = ActiveDocument == RunDocument ? ActiveBoard : null;
        if (board?.Kind == BoardKind.Current)
        {
            CopyCurrentToNewLine();
            board = ActiveBoard;
            LastImportMessage = "";
        }
        if (board == null || board.Kind != BoardKind.WorldLine)
        {
            LastImportMessage = ModLocalization.T("import_readonly",
                "请先「复制→新世界线」或「+ 世界线」创建可交互画板再录入");
            Raise();
            return;
        }
        var document = RunDocument;
        var line = document.EnsureActualWorldLine(board.Id, board.Name);
        var turn = CurrentTurn > 0
            ? CurrentTurn
            : ops.Where(o => o.Turn > 0).Select(o => o.Turn).DefaultIfEmpty(0).Max();
        var turns = turn > 0 ? new List<int> { turn } : new List<int>();
        SelectWorldLine(line.Id);

        var commands = new List<INotesCommand>();
        foreach (var importedTurn in turns)
        {
            var existingRegion = board.TurnRegions.FirstOrDefault(r =>
                r.WorldLineId == line.Id && r.TurnNumber == importedTurn);
            if (existingRegion == null)
            {
                commands.Add(new AddTurnRegionCommand(document, board.Id, line.Id, importedTurn));
            }
            var region = document.EnsureTurnRegion(board.Id, line.Id, importedTurn);
            commands.AddRange(NotesImporter.BuildCommands(document, board, region, ops, replaceImported: true));
            var last = ops
                .Where(o => o.Turn == importedTurn && o.Kind != NotesOpKind.TurnEvent)
                .OrderBy(o => o.UnixMs)
                .LastOrDefault();
            if (last != null)
            {
                commands.Add(new UpdateTurnRegionCommand(
                    document, board.Id, region.Id,
                    region.Snapshot, region.Hp, region.MaxHp,
                    last.Snapshot, last.Hp, last.MaxHp));
            }
        }
        if (commands.Count > 0)
        {
            Commands.Execute(new CompositeCommand(commands, "Import"));
        }
        LastImportMessage = commands.Count > 0
            ? ModLocalization.T("import_done", "已录入") + " " + commands.Count + " → " + board.Name
            : ModLocalization.T("import_empty", "没有可录入的操作");
        MegaCrit.Sts2.Core.Logging.Log.Info(
            $"[Notes] import: board={board.Name} turns={string.Join(",", turns)} ops={ops.Count} commands={commands.Count}");
        Raise();
    }

    /// <summary>Copies the read-only current world line into a new interactive
    /// world-line board so the player can record, edit and speculate.</summary>
    public static void CopyCurrentToNewLine()
    {
        if (!RunActive)
        {
            LastImportMessage = ModLocalization.T("import_empty", "没有可录入的操作");
            Raise();
            return;
        }
        var document = RunDocument;
        RefreshCurrentBoard();
        var current = document.EnsureCurrentBoard(CurrentBoardName());
        var ordinal = document.NextWorldLineOrdinal();
        var copy = document.DuplicateWorldLineBoard(current.Id, WorldLineBoardName(ordinal));
        if (copy == null)
        {
            return;
        }
        if (Library != NotesLibrary.Run)
        {
            SetLibrary(NotesLibrary.Run);
        }
        SetActiveBoard(copy.Id);
        LastImportMessage = ModLocalization.T("copy_done", "已复制到") + " " + copy.Name;
        MegaCrit.Sts2.Core.Logging.Log.Info($"[Notes] copy current line -> {copy.Name}");
        Raise();
    }

    /// <summary>In-memory ops merged with the ones persisted in the run file.</summary>
    private static List<NotesOpData> CollectOps()
    {
        var ops = NotesOpLog.Entries.ToList();
        if (RunActive)
        {
            var persisted = NotesPersistence.LoadOpsForRun();
            if (persisted.Count > 0)
            {
                var ids = ops.Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
                foreach (var op in persisted.Where(op => ids.Add(op.Id)))
                {
                    ops.Add(op);
                }
                ops = ops.OrderBy(o => o.UnixMs).ToList();
            }
        }
        return ops;
    }

    public static void ClearOps()
    {
        NotesOpLog.Clear();
        // The read-only current line is derived from the op log: drop the
        // captured chips / end-of-turn state with it, otherwise the board keeps
        // showing data whose ops no longer exist.
        var board = CurrentBoard();
        if (board != null)
        {
            foreach (var region in board.TurnRegions)
            {
                region.TurnEvents.Clear();
                region.Snapshot = "";
                region.Hp = -1;
                region.MaxHp = -1;
            }
        }
        MarkDirty();
        RefreshCurrentBoard();
        OpsChanged?.Invoke();
        Raise();
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
