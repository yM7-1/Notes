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

    public NotesBoard CreateBoard(string? name = null)
    {
        var board = new NotesBoard
        {
            Id = IdFactory.NewBoardId(),
            Name = string.IsNullOrWhiteSpace(name) ? $"Board {Boards.Count + 1}" : name.Trim(),
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        Boards.Add(board);
        ActiveBoardId = board.Id;
        return board;
    }

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

    /// <summary>First world line = the actual line; created on demand.</summary>
    public NotesWorldLine EnsureActualWorldLine(string boardId)
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
            Name = "Actual",
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        board.WorldLines.Add(line);
        return line;
    }

    public NotesWorldLine AddWorldLine(string boardId, string? name = null)
    {
        var board = FindBoard(boardId) ?? throw new InvalidOperationException("board not found");
        var line = new NotesWorldLine
        {
            Id = IdFactory.NewWorldLineId(),
            Name = string.IsNullOrWhiteSpace(name) ? $"World {board.WorldLines.Count + 1}" : name.Trim(),
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        board.WorldLines.Add(line);
        return line;
    }

    public bool InsertWorldLine(string boardId, int index, NotesWorldLine line)
    {
        var board = FindBoard(boardId);
        if (board == null || board.FindWorldLine(line.Id) != null)
        {
            return false;
        }
        board.WorldLines.Insert(Math.Clamp(index, 0, board.WorldLines.Count), line);
        return true;
    }

    /// <summary>Removes a world line with its regions, structured nodes and edges.</summary>
    public bool RemoveWorldLine(string boardId, string worldLineId)
    {
        var board = FindBoard(boardId);
        var line = board?.FindWorldLine(worldLineId);
        if (board == null || line == null)
        {
            return false;
        }
        foreach (var region in board.RegionsOf(worldLineId).ToList())
        {
            RemoveTurnRegion(boardId, region.Id);
        }
        board.WorldLines.Remove(line);
        return true;
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
