using Notes.Core.Documents;

namespace Notes.Core.Services;

/// <summary>An undo-able edit on a <see cref="NotesDocument"/>.</summary>
public interface INotesCommand
{
    string Name { get; }

    void Do();

    void Undo();
}

public sealed class AddNodeCommand : INotesCommand
{
    private readonly NotesDocument _doc;
    private readonly string _boardId;
    private readonly NotesNode _node;

    public AddNodeCommand(NotesDocument doc, string boardId, NotesNode node)
    {
        _doc = doc;
        _boardId = boardId;
        _node = node;
    }

    public string Name => "AddNode";

    public void Do() => _doc.AddNode(_boardId, _node);

    public void Undo() => _doc.RemoveNode(_boardId, _node.Id);
}

public sealed class RemoveNodeCommand : INotesCommand
{
    private readonly NotesDocument _doc;
    private readonly string _boardId;
    private readonly string _nodeId;
    private NotesNode? _node;
    private List<NotesEdge> _edges = new();

    public RemoveNodeCommand(NotesDocument doc, string boardId, string nodeId)
    {
        _doc = doc;
        _boardId = boardId;
        _nodeId = nodeId;
    }

    public string Name => "RemoveNode";

    public void Do()
    {
        var board = _doc.FindBoard(_boardId);
        var node = board?.FindNode(_nodeId);
        if (node == null)
        {
            return;
        }
        _node = node;
        _edges = board!.EdgesOf(_nodeId).Select(e => e.Clone()).ToList();
        _doc.RemoveNode(_boardId, _nodeId);
    }

    public void Undo()
    {
        if (_node == null)
        {
            return;
        }
        _doc.AddNode(_boardId, _node);
        foreach (var edge in _edges)
        {
            _doc.AddEdge(_boardId, edge);
        }
    }
}

public sealed class MoveNodeCommand : INotesCommand
{
    private readonly NotesDocument _doc;
    private readonly string _boardId;
    private readonly string _nodeId;
    private readonly float _fromX;
    private readonly float _fromY;
    private readonly float _toX;
    private readonly float _toY;

    public MoveNodeCommand(NotesDocument doc, string boardId, string nodeId,
        float fromX, float fromY, float toX, float toY)
    {
        _doc = doc;
        _boardId = boardId;
        _nodeId = nodeId;
        _fromX = fromX;
        _fromY = fromY;
        _toX = toX;
        _toY = toY;
    }

    public string Name => "MoveNode";

    public void Do()
    {
        var node = _doc.FindBoard(_boardId)?.FindNode(_nodeId);
        if (node != null)
        {
            node.X = _toX;
            node.Y = _toY;
        }
    }

    public void Undo()
    {
        var node = _doc.FindBoard(_boardId)?.FindNode(_nodeId);
        if (node != null)
        {
            node.X = _fromX;
            node.Y = _fromY;
        }
    }
}

/// <summary>Applies an edited clone onto a node; the before snapshot makes it undo-able.</summary>
public sealed class UpdateNodeCommand : INotesCommand
{
    private readonly NotesDocument _doc;
    private readonly string _boardId;
    private readonly string _nodeId;
    private readonly NotesNode _before;
    private readonly NotesNode _after;

    public UpdateNodeCommand(NotesDocument doc, string boardId, string nodeId, NotesNode before, NotesNode after)
    {
        _doc = doc;
        _boardId = boardId;
        _nodeId = nodeId;
        _before = before.Clone();
        _after = after.Clone();
    }

    public string Name => "UpdateNode";

    public void Do() => Apply(_after);

    public void Undo() => Apply(_before);

    private void Apply(NotesNode source)
    {
        var node = _doc.FindBoard(_boardId)?.FindNode(_nodeId);
        node?.CopyFrom(source);
    }
}

public sealed class AddEdgeCommand : INotesCommand
{
    private readonly NotesDocument _doc;
    private readonly string _boardId;
    private readonly NotesEdge _edge;

    public AddEdgeCommand(NotesDocument doc, string boardId, NotesEdge edge)
    {
        _doc = doc;
        _boardId = boardId;
        _edge = edge;
    }

    public string Name => "AddEdge";

    public void Do() => _doc.AddEdge(_boardId, _edge);

    public void Undo() => _doc.RemoveEdge(_boardId, _edge.Id);
}

public sealed class RemoveEdgeCommand : INotesCommand
{
    private readonly NotesDocument _doc;
    private readonly string _boardId;
    private readonly string _edgeId;
    private NotesEdge? _edge;

    public RemoveEdgeCommand(NotesDocument doc, string boardId, string edgeId)
    {
        _doc = doc;
        _boardId = boardId;
        _edgeId = edgeId;
    }

    public string Name => "RemoveEdge";

    public void Do()
    {
        _edge = _doc.FindBoard(_boardId)?.FindEdge(_edgeId)?.Clone();
        _doc.RemoveEdge(_boardId, _edgeId);
    }

    public void Undo()
    {
        if (_edge != null)
        {
            _doc.AddEdge(_boardId, _edge);
        }
    }
}

public sealed class UpdateEdgeCommand : INotesCommand
{
    private readonly NotesDocument _doc;
    private readonly string _boardId;
    private readonly string _edgeId;
    private readonly string _before;
    private readonly string _after;

    public UpdateEdgeCommand(NotesDocument doc, string boardId, string edgeId, string before, string after)
    {
        _doc = doc;
        _boardId = boardId;
        _edgeId = edgeId;
        _before = before;
        _after = after;
    }

    public string Name => "UpdateEdge";

    public void Do() => Apply(_after);

    public void Undo() => Apply(_before);

    private void Apply(string label)
    {
        var edge = _doc.FindBoard(_boardId)?.FindEdge(_edgeId);
        if (edge != null)
        {
            edge.Label = label;
        }
    }
}

public sealed class AddBoardCommand : INotesCommand
{
    private readonly NotesDocument _doc;
    private readonly NotesBoard _board;
    private string _previousActive = "";

    public AddBoardCommand(NotesDocument doc, NotesBoard board)
    {
        _doc = doc;
        _board = board;
    }

    public string Name => "AddBoard";

    public void Do()
    {
        _previousActive = _doc.ActiveBoardId;
        _doc.InsertBoard(_doc.Boards.Count, _board);
        _doc.ActiveBoardId = _board.Id;
    }

    public void Undo()
    {
        _doc.RemoveBoard(_board.Id);
        _doc.ActiveBoardId = _previousActive;
    }
}

public sealed class RemoveBoardCommand : INotesCommand
{
    private readonly NotesDocument _doc;
    private readonly string _boardId;
    private NotesBoard? _board;
    private int _index;
    private string _previousActive = "";

    public RemoveBoardCommand(NotesDocument doc, string boardId)
    {
        _doc = doc;
        _boardId = boardId;
    }

    public string Name => "RemoveBoard";

    public void Do()
    {
        _board = _doc.FindBoard(_boardId);
        _index = _board == null ? -1 : _doc.Boards.IndexOf(_board);
        _previousActive = _doc.ActiveBoardId;
        _doc.RemoveBoard(_boardId);
    }

    public void Undo()
    {
        if (_board == null)
        {
            return;
        }
        _doc.InsertBoard(_index, _board);
        _doc.ActiveBoardId = _previousActive;
    }
}
