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

    /// <summary>Raised when the hovered node changes; hover only affects the
    /// inspector and the hovered control itself, so it is a separate, cheap event.</summary>
    public static event Action? HoverChanged;

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
        HoverChanged?.Invoke();
    }

    public static void ClearHoverNode(string nodeId)
    {
        if (HoverNodeId != nodeId)
        {
            return;
        }
        HoverNodeId = "";
        HoverChanged?.Invoke();
    }

    /// <summary>Short feedback for the last import action (shown in the status
    /// bar). Expires by itself so a stale result does not linger forever.</summary>
    public static string LastImportMessage { get; private set; } = "";

    private static double _importMessageAge;

    /// <summary>Shows a status-bar message and restarts its expiry timer.</summary>
    public static void SetImportMessage(string message)
    {
        LastImportMessage = message;
        _importMessageAge = 0;
    }

    private static double _importMessageLifetime = 8.0;
    private static int _codexLimit = 120;

    /// <summary>Capture combat operations automatically (settings mirror).</summary>
    public static bool AutoRecord { get; private set; } = true;

    /// <summary>Status-bar feedback lifetime; 0 keeps it until replaced.</summary>
    public static double MessageLifetime => _importMessageLifetime;

    /// <summary>Maximum rows the codex search returns.</summary>
    public static int CodexLimit => _codexLimit;

    /// <summary>Which quick actions the floating handle shows (bit mask:
    /// 1 = record turn, 2 = copy line, 4 = open settings).</summary>
    public static int QuickActionsMask => _quickActionsMask;

    private static int _quickActionsMask = 3;

    /// <summary>Applies the persisted global settings to the runtime.</summary>
    public static void ApplySettings(NotesGlobalData data)
    {
        AutoRecord = data.AutoRecordOps;
        _importMessageLifetime = Math.Max(0, data.MessageLifetimeSeconds);
        _codexLimit = Math.Clamp(data.CodexLimit, 10, 500);
        _quickActionsMask = data.QuickActionsMask;
        Commands.Limit = Math.Clamp(data.UndoLimit, 10, 2000);
    }

    /// <summary>Mutates + persists one setting and applies it immediately.</summary>
    public static void UpdateSettings(Action<NotesGlobalData> mutate)
    {
        if (!NotesPersistence.TryGetGlobalData(out var data))
        {
            return;
        }
        mutate(data);
        NotesPersistence.SaveGlobalNow();
        ApplySettings(data);
        Changed?.Invoke();
    }

    public static void SelectNode(string nodeId) => SetSelection(NotesSelectionKind.Node, nodeId);

    public static void SelectRegion(string regionId) => SetSelection(NotesSelectionKind.Region, regionId);

    public static void SelectWorldLine(string worldLineId) => SetSelection(NotesSelectionKind.WorldLine, worldLineId);

    public static void ClearSelection() => SetSelection(NotesSelectionKind.None, "");

    /// <summary>Applies a state mark to one node through the command stack
    /// (shared by the context menu and the keyboard shortcuts).</summary>
    public static void SetNodeState(string nodeId, NodeState state) =>
        SetNodesState(new[] { nodeId }, state);

    /// <summary>Batch version: all ids become one undo step.</summary>
    public static void SetNodesState(IReadOnlyCollection<string> nodeIds, NodeState state)
    {
        var board = ActiveBoard;
        if (board.IsReadOnly)
        {
            return;
        }
        var commands = new List<INotesCommand>();
        foreach (var nodeId in nodeIds)
        {
            if (board.FindNode(nodeId) is not { } node)
            {
                continue;
            }
            var before = node.Clone();
            var after = node.Clone();
            after.State = state;
            commands.Add(new UpdateNodeCommand(ActiveDocument, board.Id, nodeId, before, after));
        }
        if (commands.Count == 0)
        {
            return;
        }
        Commands.Execute(new CompositeCommand(commands, "SetState"));
        Raise();
    }

    /// <summary>Removes one node (and its branches) through the command stack.</summary>
    public static void DeleteNode(string nodeId) => DeleteNodes(new[] { nodeId });

    /// <summary>Batch version: all removals are one undo step.</summary>
    public static void DeleteNodes(IReadOnlyCollection<string> nodeIds)
    {
        var board = ActiveBoard;
        if (board.IsReadOnly)
        {
            return;
        }
        var commands = new List<INotesCommand>();
        foreach (var nodeId in nodeIds)
        {
            if (board.FindNode(nodeId) != null)
            {
                commands.Add(new RemoveNodeCommand(ActiveDocument, board.Id, nodeId));
            }
        }
        if (commands.Count == 0)
        {
            return;
        }
        Commands.Execute(new CompositeCommand(commands, "DeleteNodes"));
        ClearSelection();
        Raise();
    }

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
    private static bool _globalStoreMissingLogged;
    private static float _retryTimer;
    private static float _globalCheckTimer;
    private static float _opLogRetryTimer;
    private static bool _layoutErrorLogged;
    private static bool _runDirty;
    private static bool _globalDirty;
    private static bool _opsDirty;
    private static double _saveTimer;
    private static string _combatKey = "";
    private static bool _combatSummaryPending;
    private static string _combatSummaryName = "";
    private static int _combatSummaryFloor;

    public static bool RunActive => GameContext.CurrentRun != null;

    public static bool GlobalLoaded => _globalLoaded;

    public static NotesDocument ActiveDocument =>
        Library == NotesLibrary.Run && RunActive ? RunDocument : GlobalDocument;

    public static NotesBoard ActiveBoard
    {
        get
        {
            var document = ActiveDocument;
            return document.ActiveBoard ?? document.EnsureActiveBoard(
                ModLocalization.T("board_new_name", "画板") + " " + (document.Boards.Count + 1));
        }
    }

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
            RunDocument = NotesPersistence.LoadRun(run, out var ops, out _combatKey, out var loadWarning);
            if (loadWarning.Length > 0)
            {
                SetImportMessage(loadWarning);
            }
            RunDocument.EnsureOverviewBoard(OverviewBoardName());
            RunDocument.EnsureCurrentBoard(CurrentBoardName());
            NotesOpLog.Load(ops);
            Library = NotesPersistence.TryGetGlobalData(out var settings) && settings.DefaultLibrary == 1
                ? NotesLibrary.Global
                : NotesLibrary.Run;
            RefreshCurrentBoard();
        }
        else
        {
            RunDocument = new NotesDocument();
            _combatSummaryPending = false;
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
        if (ActiveDocument.ActiveBoardId == boardId)
        {
            return;
        }
        HoverNodeId = "";
        ActiveDocument.ActiveBoardId = boardId;
        // The undo stack is per visible board: undoing an edit that belongs to
        // a board the player is no longer looking at would be invisible damage.
        Commands.Clear();
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

    /// <summary>Tidies the free nodes of the active board into a grid (undo-able).</summary>
    public static void ArrangeBoard()
    {
        var board = ActiveBoard;
        if (board.IsReadOnly)
        {
            return;
        }
        var before = board.Nodes
            .Where(n => n.RegionId.Length == 0)
            .Select(n => (n.Id, n.X, n.Y))
            .ToList();
        if (before.Count == 0)
        {
            SetImportMessage(ModLocalization.T("arrange_empty", "没有可整理的自由节点"));
            Raise();
            return;
        }
        var document = ActiveDocument;
        NotesLayout.ArrangeFreeNodes(board);
        var commands = new List<INotesCommand>();
        foreach (var (id, x, y) in before)
        {
            if (board.FindNode(id) is not { } node)
            {
                continue;
            }
            commands.Add(new MoveNodeCommand(document, board.Id, id, x, y, node.X, node.Y));
        }
        Commands.Execute(new CompositeCommand(commands, "Arrange"));
        SetImportMessage(ModLocalization.T("arrange_done", "已整理") + " " + before.Count + " "
            + ModLocalization.T("arrange_nodes", "个节点"));
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
            _layoutErrorLogged = false;
        }
        catch (Exception ex)
        {
            // layout is best-effort; never block the UI, but do not hide it either
            if (!_layoutErrorLogged)
            {
                _layoutErrorLogged = true;
                MegaCrit.Sts2.Core.Logging.Log.Error(
                    "[Notes] layout failed (further errors suppressed): " + ex);
            }
        }
        MarkDirty();
        FlushRunIfPossible();
        Changed?.Invoke();
    }

    /// <summary>Operation log changed (captured or cleared). Capture callbacks
    /// fire inside game code and can burst (a 5-card draw), so the board
    /// rebuild + save are coalesced into the next Tick instead of running
    /// once per op inside the callback.</summary>
    public static void OnOpsChanged()
    {
        MarkDirty();
        _opsDirty = true;
        OpsChanged?.Invoke();
    }

    /// <summary>Coalesced capture work: rebuild the read-only current line and
    /// flush the run file at most once per frame.</summary>
    private static void FlushOpsIfDirty()
    {
        if (!_opsDirty)
        {
            return;
        }
        _opsDirty = false;
        RefreshCurrentBoard();
        FlushRunIfPossible();
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

    /// <summary>True once the player dismissed the first-run guide.</summary>
    public static bool OnboardingSeen =>
        NotesPersistence.TryGetGlobalData(out var data) && data.OnboardingSeen;

    public static void MarkOnboardingSeen()
    {
        if (NotesPersistence.TryGetGlobalData(out var data))
        {
            data.OnboardingSeen = true;
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
        ExpireImportMessage(delta);
        ExpireCombatSummary();
        FlushOpsIfDirty();
        if (!NotesOpLog.Initialized)
        {
            _opLogRetryTimer += (float)delta;
            if (_opLogRetryTimer >= 2f)
            {
                _opLogRetryTimer = 0f;
                NotesOpLog.Initialize();
            }
        }
        if (Library == NotesLibrary.Global && !_globalLoaded)
        {
            _retryTimer += (float)delta;
            if (_retryTimer >= 2f)
            {
                _retryTimer = 0f;
                EnsureGlobalLoaded();
            }
        }
        else if (_globalLoaded)
        {
            // Cheap identity check: RitsuLib's cache reloads after a profile
            // switch, and we must not keep serving the old profile's boards.
            _globalCheckTimer += (float)delta;
            if (_globalCheckTimer >= 1f)
            {
                _globalCheckTimer = 0f;
                EnsureGlobalFresh();
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

    /// <summary>Drops the status-bar feedback once it has been visible long
    /// enough; the UI refreshes through <see cref="Changed"/>.</summary>
    private static void ExpireImportMessage(double delta)
    {
        if (LastImportMessage.Length == 0 || _importMessageLifetime <= 0)
        {
            return;
        }
        _importMessageAge += delta;
        if (_importMessageAge < _importMessageLifetime)
        {
            return;
        }
        _importMessageAge = 0;
        LastImportMessage = "";
        Changed?.Invoke();
    }

    /// <summary>Builds the post-combat recap board once the combat actually
    /// ended (no CombatEnded event exists, so this is detected in Tick).</summary>
    private static void ExpireCombatSummary()
    {
        if (!_combatSummaryPending || GameContext.InCombat)
        {
            return;
        }
        _combatSummaryPending = false;
        GenerateCombatSummary();
    }

    /// <summary>Recap board: totals + one line per turn, built from the captured
    /// op log and the current world line's turn events. Purely local.</summary>
    private static void GenerateCombatSummary()
    {
        if (!RunActive)
        {
            return;
        }
        var ops = NotesOpLog.Entries.ToList();
        if (ops.Count == 0)
        {
            return;
        }
        var current = RunDocument.EnsureCurrentBoard(CurrentBoardName());
        var turns = ops.Where(o => o.Turn > 0).Select(o => o.Turn).Distinct().OrderBy(t => t).ToList();
        var name = _combatSummaryName.Length > 0
            ? _combatSummaryName
            : ModLocalization.T("summary_default_name", "战斗");
        var floor = _combatSummaryFloor > 0
            ? "  ·  " + Format("summary_floor", "第 {0} 层", _combatSummaryFloor)
            : "";

        var board = new NotesBoard
        {
            Id = IdFactory.NewBoardId(),
            Name = ModLocalization.T("summary_board_name", "复盘") + " · " + name,
            Kind = BoardKind.Summary,
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };

        var cards = ops.Count(o => o.Kind == NotesOpKind.Card);
        var potions = ops.Count(o => o.Kind == NotesOpKind.Potion);
        var damage = 0;
        var kills = 0;
        foreach (var op in ops)
        {
            foreach (var annotation in op.Annotations)
            {
                if (!AnnotationProtocol.IsDamage(annotation.RefId))
                {
                    continue;
                }
                damage += annotation.Count;
                if (annotation.Meta.EndsWith(AnnotationProtocol.Separator + "1", StringComparison.Ordinal))
                {
                    kills++;
                }
            }
        }
        var losses = current.TurnRegions.Sum(r => r.TurnEvents
            .Where(a => AnnotationProtocol.IsLoss(a.RefId))
            .Sum(a => a.Count));

        var y = 0f;
        board.Nodes.Add(TextNode(
            Format("summary_title", "战斗复盘：{0}", name + floor), 0, ref y));
        board.Nodes.Add(TextNode(
            Format("summary_stats", "回合 {0} · 出牌 {1} · 伤害 {2} · 战损 {3} · 击杀 {4} · 药水 {5}",
                turns.Count, cards, damage, losses, kills, potions), 0, ref y));
        foreach (var turn in turns)
        {
            var plays = ops.Count(o => o.Turn == turn && o.Kind == NotesOpKind.Card);
            var dealt = ops.Where(o => o.Turn == turn).Sum(o => o.Annotations
                .Where(a => AnnotationProtocol.IsDamage(a.RefId))
                .Sum(a => a.Count));
            var lost = current.TurnRegions.FirstOrDefault(r => r.TurnNumber == turn)?.TurnEvents
                .Where(a => AnnotationProtocol.IsLoss(a.RefId))
                .Sum(a => a.Count) ?? 0;
            board.Nodes.Add(TextNode(
                Format("summary_turn", "第{0}回合：出牌 {1} · 伤害 {2} · 战损 {3}", turn, plays, dealt, lost), 0, ref y));
        }
        for (var i = 1; i < board.Nodes.Count; i++)
        {
            board.Edges.Add(new NotesEdge
            {
                Id = IdFactory.NewEdgeId(),
                From = board.Nodes[i - 1].Id,
                To = board.Nodes[i].Id,
            });
        }

        RunDocument.Boards.Add(board);
        RunDocument.ActiveBoardId = board.Id;
        MegaCrit.Sts2.Core.Logging.Log.Info($"[Notes] combat recap board created: {board.Name}");
        Raise();
        SetImportMessage(ModLocalization.T("summary_created", "已生成战斗复盘") + ": " + board.Name);
    }

    private static NotesNode TextNode(string title, float x, ref float y)
    {
        var node = new NotesNode
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
        y += 78f;
        return node;
    }

    private static string Format(string key, string fallback, params object[] args)
    {
        var text = ModLocalization.T(key, fallback);
        for (var i = 0; i < args.Length; i++)
        {
            text = text.Replace("{" + i + "}", args[i]?.ToString() ?? "");
        }
        return text;
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
        else if (AnnotationProtocol.IsExhaust(annotation.RefId) || AnnotationProtocol.IsDiscard(annotation.RefId))
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
    /// combat (save-load / re-entry) keeps its notes. Recap boards generated at
    /// the end of previous combats are carried over.</summary>
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
        _combatSummaryName = state.Encounter?.Id.Entry ?? "";
        _combatSummaryFloor = GameContext.CurrentRun?.TotalFloor ?? 0;
        _combatSummaryPending = true;
        var recaps = RunDocument.Boards.Where(b => b.Kind == BoardKind.Summary).ToList();
        RunDocument = new NotesDocument();
        foreach (var recap in recaps)
        {
            RunDocument.Boards.Add(recap);
        }
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

    /// <summary>Creates a new interactive world line. Runs through the command
    /// stack, so Ctrl+Z removes it again.</summary>
    public static void NewWorldLine()
    {
        var document = ActiveDocument;
        var ordinal = document.NextWorldLineOrdinal();
        var board = document.BuildWorldLineBoard(WorldLineBoardName(ordinal));
        var line = board.WorldLines[0];
        // A brand-new line starts empty at turn 1; keep the current turn visible too.
        board.TurnRegions.Add(new NotesTurnRegion
        {
            Id = IdFactory.NewRegionId(),
            WorldLineId = line.Id,
            TurnNumber = 1,
        });
        var turn = CurrentTurn;
        if (turn > 1)
        {
            board.TurnRegions.Add(new NotesTurnRegion
            {
                Id = IdFactory.NewRegionId(),
                WorldLineId = line.Id,
                TurnNumber = turn,
            });
        }
        Commands.Execute(new AddBoardCommand(document, board));
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
            SetImportMessage(ModLocalization.T("import_empty", "没有可录入的操作"));
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
            SetImportMessage(ModLocalization.T("import_empty", "没有可录入的操作"));
            Raise();
            return;
        }
        var board = ActiveDocument == RunDocument ? ActiveBoard : null;
        if (board?.Kind == BoardKind.Current)
        {
            CopyCurrentToNewLine();
            board = ActiveBoard;
            SetImportMessage("");
        }
        if (board == null || board.Kind != BoardKind.WorldLine)
        {
            SetImportMessage(ModLocalization.T("import_readonly",
                "请先「复制→新世界线」或「+ 世界线」创建可交互画板再录入"));
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
        SetImportMessage(commands.Count > 0
            ? ModLocalization.T("import_done", "已录入") + " " + commands.Count + " → " + board.Name
            : ModLocalization.T("import_empty", "没有可录入的操作"));
        MegaCrit.Sts2.Core.Logging.Log.Info(
            $"[Notes] import: board={board.Name} turns={string.Join(",", turns)} ops={ops.Count} commands={commands.Count}");
        Raise();
    }

    /// <summary>Copies the read-only current world line into a new interactive
    /// world-line board so the player can record, edit and speculate. Runs
    /// through the command stack, so Ctrl+Z removes the copy again.</summary>
    public static void CopyCurrentToNewLine()
    {
        if (!RunActive)
        {
            SetImportMessage(ModLocalization.T("copy_no_run", "没有进行中的游戏，暂时无法复制世界线"));
            Raise();
            return;
        }
        var document = RunDocument;
        RefreshCurrentBoard();
        var current = document.EnsureCurrentBoard(CurrentBoardName());
        var ordinal = document.NextWorldLineOrdinal();
        var copy = document.BuildWorldLineCopy(current.Id, WorldLineBoardName(ordinal));
        if (copy == null)
        {
            return;
        }
        if (Library != NotesLibrary.Run)
        {
            SetLibrary(NotesLibrary.Run);
        }
        Commands.Execute(new AddBoardCommand(document, copy));
        SetImportMessage(ModLocalization.T("copy_done", "已复制到") + " " + copy.Name);
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
            _globalStoreMissingLogged = false;
            if (NotesPersistence.TryGetGlobalData(out var settings))
            {
                ApplySettings(settings);
            }
        }
        else if (!_globalStoreMissingLogged)
        {
            _globalStoreMissingLogged = true;
            MegaCrit.Sts2.Core.Logging.Log.Info(
                "[Notes] global store not ready yet; retrying in the background");
        }
    }

    /// <summary>Picks up a global document reload (profile switch) instead of
    /// keeping the previous profile's boards in memory.</summary>
    private static void EnsureGlobalFresh()
    {
        try
        {
            if (NotesPersistence.TryLoadGlobal(out var document)
                && !ReferenceEquals(document, GlobalDocument))
            {
                GlobalDocument = document;
                if (NotesPersistence.TryGetGlobalData(out var settings))
                {
                    ApplySettings(settings);
                }
                MegaCrit.Sts2.Core.Logging.Log.Info("[Notes] global library reloaded (profile changed)");
                if (Library == NotesLibrary.Global)
                {
                    Commands.Clear();
                    Raise();
                }
            }
        }
        catch
        {
            // best effort only
        }
    }
}
