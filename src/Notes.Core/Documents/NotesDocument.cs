using Notes.Core.Services;

namespace Notes.Core.Documents;

/// <summary>
/// A notes library: an ordered set of boards plus the active board id. A document
/// is stored either per run (RitsuLib RunSavedDataStore) or globally per profile
/// (RitsuLib ModDataStore).
/// </summary>
public sealed class NotesDocument
{
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;

    public string ActiveBoardId { get; set; } = "";

    public List<NotesBoard> Boards { get; set; } = new();

    public NotesBoard? ActiveBoard
    {
        get
        {
            if (Boards.Count == 0)
            {
                return null;
            }
            var match = FindBoard(ActiveBoardId);
            return match ?? Boards[0];
        }
    }

    public NotesBoard? FindBoard(string boardId) =>
        Boards.FirstOrDefault(b => string.Equals(b.Id, boardId, StringComparison.Ordinal));

    /// <summary>Returns the active board, creating (and activating) a new one if
    /// the document is empty.</summary>
    public NotesBoard EnsureActiveBoard(string? name = null)
    {
        var board = ActiveBoard;
        if (board != null)
        {
            return board;
        }
        board = CreateBoard(name);
        return board;
    }

    public NotesBoard CreateBoard(string? name = null, BoardKind kind = BoardKind.Free, int ordinal = 0)
    {
        var board = new NotesBoard
        {
            Id = IdFactory.NewBoardId(),
            Name = string.IsNullOrWhiteSpace(name) ? $"Board {Boards.Count + 1}" : name.Trim(),
            Kind = kind,
            Ordinal = ordinal,
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        Boards.Add(board);
        ActiveBoardId = board.Id;
        return board;
    }

    /// <summary>All world-line boards of this document, ordered by their number.</summary>
    public IEnumerable<NotesBoard> WorldLineBoards =>
        Boards.Where(b => b.Kind == BoardKind.WorldLine).OrderBy(b => b.Ordinal);

    /// <summary>The default "free overview" board (one per run document).</summary>
    public NotesBoard EnsureOverviewBoard(string? name = null)
    {
        var existing = Boards.FirstOrDefault(b => b.Kind == BoardKind.Overview);
        if (existing != null)
        {
            return existing;
        }
        return CreateBoard(name ?? "Overview", BoardKind.Overview);
    }

    /// <summary>The read-only auto-recorded current world line (one per document).</summary>
    public NotesBoard EnsureCurrentBoard(string name)
    {
        var existing = Boards.FirstOrDefault(b => b.Kind == BoardKind.Current);
        if (existing != null)
        {
            return existing;
        }
        return CreateBoard(name, BoardKind.Current);
    }

    /// <summary>Deep-copies a board (regions, nodes, edges, annotations) into a
    /// new interactive world-line board with fresh ids; positions and captured
    /// snapshots are preserved. The copy becomes the active board.</summary>
    public NotesBoard? DuplicateWorldLineBoard(string sourceBoardId, string newName)
    {
        var source = FindBoard(sourceBoardId);
        if (source == null)
        {
            return null;
        }
        var target = CreateWorldLineBoard(newName);
        var sourceLine = source.WorldLines.FirstOrDefault();
        var targetLine = EnsureActualWorldLine(target.Id, newName);

        var regionMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var region in source.TurnRegions.OrderBy(r => r.TurnNumber))
        {
            if (sourceLine != null && region.WorldLineId != sourceLine.Id)
            {
                continue;
            }
            var clone = new NotesTurnRegion
            {
                Id = IdFactory.NewRegionId(),
                WorldLineId = targetLine.Id,
                TurnNumber = region.TurnNumber,
                TurnEvents = region.TurnEvents.Select(a => a.Clone()).ToList(),
                Snapshot = region.Snapshot,
                Hp = region.Hp,
                MaxHp = region.MaxHp,
            };
            target.TurnRegions.Add(clone);
            regionMap[region.Id] = clone.Id;
        }

        var nodeMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in source.Nodes)
        {
            var mappedRegion = "";
            if (node.RegionId.Length > 0)
            {
                if (!regionMap.TryGetValue(node.RegionId, out mappedRegion!))
                {
                    continue; // belongs to another line of the source board
                }
            }
            var clone = node.Clone();
            clone.Id = IdFactory.NewNodeId();
            clone.RegionId = mappedRegion;
            target.Nodes.Add(clone);
            nodeMap[node.Id] = clone.Id;
        }
        foreach (var edge in source.Edges)
        {
            if (nodeMap.TryGetValue(edge.From, out var from) && nodeMap.TryGetValue(edge.To, out var to))
            {
                target.Edges.Add(new NotesEdge
                {
                    Id = IdFactory.NewEdgeId(),
                    From = from,
                    To = to,
                    Label = edge.Label,
                });
            }
        }
        return target;
    }

    public int NextWorldLineOrdinal()
    {
        var max = 0;
        foreach (var board in WorldLineBoards)
        {
            max = Math.Max(max, board.Ordinal);
        }
        return max + 1;
    }

    /// <summary>Creates (and activates) the board that holds one world line.</summary>
    public NotesBoard CreateWorldLineBoard(string name)
    {
        return CreateBoard(name, BoardKind.WorldLine, NextWorldLineOrdinal());
    }

    /// <summary>The board that holds the actual world line (lowest ordinal).</summary>
    public NotesBoard? ActualWorldLineBoard =>
        WorldLineBoards.FirstOrDefault();

    public bool RemoveBoard(string boardId)
    {
        var board = FindBoard(boardId);
        if (board == null)
        {
            return false;
        }
        Boards.Remove(board);
        if (ActiveBoardId == boardId)
        {
            ActiveBoardId = Boards.Count > 0 ? Boards[0].Id : "";
        }
        return true;
    }

    public bool AddNode(string boardId, NotesNode node)
    {
        var board = FindBoard(boardId);
        if (board == null || string.IsNullOrEmpty(node.Id) || board.FindNode(node.Id) != null)
        {
            return false;
        }
        board.Nodes.Add(node);
        return true;
    }

    /// <summary>Removes a node and every branch attached to it.</summary>
    public bool RemoveNode(string boardId, string nodeId)
    {
        var board = FindBoard(boardId);
        var node = board?.FindNode(nodeId);
        if (board == null || node == null)
        {
            return false;
        }
        board.Nodes.Remove(node);
        board.Edges.RemoveAll(e => e.From == nodeId || e.To == nodeId);
        return true;
    }

    public bool AddEdge(string boardId, NotesEdge edge)
    {
        var board = FindBoard(boardId);
        if (board == null || string.IsNullOrEmpty(edge.Id) || edge.From == edge.To)
        {
            return false;
        }
        if (board.FindNode(edge.From) == null || board.FindNode(edge.To) == null)
        {
            return false;
        }
        if (board.FindEdge(edge.Id) != null)
        {
            return false;
        }
        if (board.Edges.Any(e => e.From == edge.From && e.To == edge.To))
        {
            return false;
        }
        board.Edges.Add(edge);
        return true;
    }

    public bool RemoveEdge(string boardId, string edgeId)
    {
        var board = FindBoard(boardId);
        var edge = board?.FindEdge(edgeId);
        if (board == null || edge == null)
        {
            return false;
        }
        board.Edges.Remove(edge);
        return true;
    }

    /// <summary>Removes a board and re-inserts it at the captured index (undo).</summary>
    public bool InsertBoard(int index, NotesBoard board)
    {
        if (FindBoard(board.Id) != null)
        {
            return false;
        }
        Boards.Insert(Math.Clamp(index, 0, Boards.Count), board);
        return true;
    }

    // ---- structured mode (world lines / turn regions) ------------------------

    /// <summary>First world line of a board (one line per board in run mode);
    /// created on demand.</summary>
    public NotesWorldLine EnsureActualWorldLine(string boardId, string? name = null)
    {
        var board = FindBoard(boardId) ?? throw new InvalidOperationException("board not found");
        var existing = board.WorldLines.FirstOrDefault();
        if (existing != null)
        {
            return existing;
        }
        var line = new NotesWorldLine
        {
            Id = IdFactory.NewWorldLineId(),
            Name = string.IsNullOrWhiteSpace(name) ? "Actual" : name.Trim(),
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        board.WorldLines.Add(line);
        return line;
    }

    public NotesTurnRegion EnsureTurnRegion(string boardId, string worldLineId, int turnNumber)
    {
        var board = FindBoard(boardId) ?? throw new InvalidOperationException("board not found");
        var existing = board.TurnRegions.FirstOrDefault(r =>
            r.WorldLineId == worldLineId && r.TurnNumber == turnNumber);
        if (existing != null)
        {
            return existing;
        }
        var region = new NotesTurnRegion
        {
            Id = IdFactory.NewRegionId(),
            WorldLineId = worldLineId,
            TurnNumber = turnNumber,
        };
        board.TurnRegions.Add(region);
        return region;
    }

    public bool InsertTurnRegion(string boardId, int index, NotesTurnRegion region)
    {
        var board = FindBoard(boardId);
        if (board == null || board.FindRegion(region.Id) != null)
        {
            return false;
        }
        board.TurnRegions.Insert(Math.Clamp(index, 0, board.TurnRegions.Count), region);
        return true;
    }

    public bool RemoveTurnRegion(string boardId, string regionId)
    {
        var board = FindBoard(boardId);
        var region = board?.FindRegion(regionId);
        if (board == null || region == null)
        {
            return false;
        }
        foreach (var node in board.NodesOfRegion(regionId).ToList())
        {
            board.Edges.RemoveAll(e => e.From == node.Id || e.To == node.Id);
            board.Nodes.Remove(node);
        }
        board.TurnRegions.Remove(region);
        return true;
    }

    public bool SetNodeRegion(string boardId, string nodeId, string regionId)
    {
        var node = FindBoard(boardId)?.FindNode(nodeId);
        if (node == null)
        {
            return false;
        }
        node.RegionId = regionId;
        return true;
    }
}
