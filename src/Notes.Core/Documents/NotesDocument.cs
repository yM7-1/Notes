using Notes.Core.Services;

namespace Notes.Core.Documents;

/// <summary>
/// A notes library: an ordered set of boards plus the active board id. A document
/// is stored either per run (RitsuLib RunSavedDataStore) or globally per profile
/// (RitsuLib ModDataStore).
/// </summary>
public sealed class NotesDocument
{
    public const int CurrentVersion = 1;

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
}
