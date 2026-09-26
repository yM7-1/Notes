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
{    private readonly NotesDocument _doc;
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

/// <summary>Updates a turn region's end-of-turn state (snapshot / HP) so an
/// import can be undone back to the previous state.</summary>
public sealed class UpdateTurnRegionCommand : INotesCommand
{
    private readonly NotesDocument _doc;
    private readonly string _boardId;
    private readonly string _regionId;
    private readonly string _beforeSnapshot;
    private readonly string _afterSnapshot;
    private readonly int _beforeHp;
    private readonly int _afterHp;
    private readonly int _beforeMaxHp;
    private readonly int _afterMaxHp;

    public UpdateTurnRegionCommand(
        NotesDocument doc,
        string boardId,
        string regionId,
        string beforeSnapshot,
        int beforeHp,
        int beforeMaxHp,
        string afterSnapshot,
        int afterHp,
        int afterMaxHp)
    {
        _doc = doc;
        _boardId = boardId;
        _regionId = regionId;
        _beforeSnapshot = beforeSnapshot;
        _afterSnapshot = afterSnapshot;
        _beforeHp = beforeHp;
        _afterHp = afterHp;
        _beforeMaxHp = beforeMaxHp;
        _afterMaxHp = afterMaxHp;
    }

    public string Name => "UpdateTurnRegion";

    public void Do() => Apply(_afterSnapshot, _afterHp, _afterMaxHp);

    public void Undo() => Apply(_beforeSnapshot, _beforeHp, _beforeMaxHp);

    private void Apply(string snapshot, int hp, int maxHp)
    {
        var region = _doc.FindBoard(_boardId)?.FindRegion(_regionId);
        if (region == null)
        {
            return;
        }
        region.Snapshot = snapshot;
        region.Hp = hp;
        region.MaxHp = maxHp;
    }
}

/// <summary>Clears a turn region's captured event chips (undo-able).</summary>
public sealed class ClearRegionEventsCommand : INotesCommand
{
    private readonly NotesDocument _doc;
    private readonly string _boardId;
    private readonly string _regionId;
    private List<NotesAnnotation> _events = new();

    public ClearRegionEventsCommand(NotesDocument doc, string boardId, string regionId)
    {
        _doc = doc;
        _boardId = boardId;
        _regionId = regionId;
    }

    public string Name => "ClearRegionEvents";

    public void Do()
    {
        var region = _doc.FindBoard(_boardId)?.FindRegion(_regionId);
        if (region == null)
        {
            return;
        }
        _events = region.TurnEvents.Select(a => a.Clone()).ToList();
        region.TurnEvents.Clear();
    }

    public void Undo()
    {
        var region = _doc.FindBoard(_boardId)?.FindRegion(_regionId);
        if (region == null)
        {
            return;
        }
        region.TurnEvents.Clear();
        region.TurnEvents.AddRange(_events.Select(a => a.Clone()));
    }
}

/// <summary>Runs several commands as one undo step (auto-import batches).</summary>
public sealed class CompositeCommand : INotesCommand
{
    private readonly List<INotesCommand> _commands;

    public CompositeCommand(IEnumerable<INotesCommand> commands, string name = "Composite")
    {
        _commands = commands.ToList();
        Name = name;
    }

    public string Name { get; }

    public void Do()
    {
        foreach (var command in _commands)
        {
            command.Do();
        }
    }

    public void Undo()
    {
        for (var i = _commands.Count - 1; i >= 0; i--)
        {
            _commands[i].Undo();
        }
    }
}

public sealed class AddWorldLineCommand : INotesCommand
{
    private readonly NotesDocument _doc;
    private readonly string _boardId;
    private readonly string? _name;
    private NotesWorldLine? _line;

    public AddWorldLineCommand(NotesDocument doc, string boardId, string? name = null)
    {
        _doc = doc;
        _boardId = boardId;
        _name = name;
    }

    public string Name => "AddWorldLine";

    public void Do() => _line = _doc.AddWorldLine(_boardId, _name);

    public void Undo()
    {
        if (_line != null)
        {
            _doc.RemoveWorldLine(_boardId, _line.Id);
        }
    }
}

public sealed class RemoveWorldLineCommand : INotesCommand
{
    private readonly NotesDocument _doc;
    private readonly string _boardId;
    private readonly string _worldLineId;
    private NotesWorldLine? _line;
    private int _index;
    private readonly List<NotesTurnRegion> _regions = new();
    private readonly List<NotesNode> _nodes = new();
    private readonly List<NotesEdge> _edges = new();

    public RemoveWorldLineCommand(NotesDocument doc, string boardId, string worldLineId)
    {
        _doc = doc;
        _boardId = boardId;
        _worldLineId = worldLineId;
    }

    public string Name => "RemoveWorldLine";

    public void Do()
    {
        var board = _doc.FindBoard(_boardId);
        _line = board?.FindWorldLine(_worldLineId);
        if (board == null || _line == null)
        {
            return;
        }
        _index = board.WorldLines.IndexOf(_line);
        _regions.Clear();
        _nodes.Clear();
        _edges.Clear();
        foreach (var region in board.RegionsOf(_worldLineId))
        {
            _regions.Add(region.Clone());
            foreach (var node in board.NodesOfRegion(region.Id))
            {
                _nodes.Add(node.Clone());
                foreach (var edge in board.EdgesOf(node.Id).Where(e => e.To == node.Id || e.From == node.Id))
                {
                    if (!_edges.Any(x => x.Id == edge.Id))
                    {
                        _edges.Add(edge.Clone());
                    }
                }
            }
        }
        _doc.RemoveWorldLine(_boardId, _worldLineId);
    }

    public void Undo()
    {
        if (_line == null)
        {
            return;
        }
        _doc.InsertWorldLine(_boardId, _index, _line);
        foreach (var region in _regions)
        {
            _doc.InsertTurnRegion(_boardId, _doc.FindBoard(_boardId)!.TurnRegions.Count, region);
        }
        foreach (var node in _nodes)
        {
            _doc.AddNode(_boardId, node);
        }
        foreach (var edge in _edges)
        {
            _doc.AddEdge(_boardId, edge);
        }
    }
}

public sealed class AddTurnRegionCommand : INotesCommand
{
    private readonly NotesDocument _doc;
    private readonly string _boardId;
    private readonly string _worldLineId;
    private readonly int _turnNumber;
    private NotesTurnRegion? _region;

    public AddTurnRegionCommand(NotesDocument doc, string boardId, string worldLineId, int turnNumber)
    {
        _doc = doc;
        _boardId = boardId;
        _worldLineId = worldLineId;
        _turnNumber = turnNumber;
    }

    public string Name => "AddTurnRegion";

    public void Do() => _region = _doc.EnsureTurnRegion(_boardId, _worldLineId, _turnNumber);

    public void Undo()
    {
        if (_region != null)
        {
            _doc.RemoveTurnRegion(_boardId, _region.Id);
        }
    }
}

public sealed class RemoveTurnRegionCommand : INotesCommand
{
    private readonly NotesDocument _doc;
    private readonly string _boardId;
    private readonly string _regionId;
    private NotesTurnRegion? _region;
    private int _index;
    private readonly List<NotesNode> _nodes = new();
    private readonly List<NotesEdge> _edges = new();

    public RemoveTurnRegionCommand(NotesDocument doc, string boardId, string regionId)
    {
        _doc = doc;
        _boardId = boardId;
        _regionId = regionId;
    }

    public string Name => "RemoveTurnRegion";

    public void Do()
    {
        var board = _doc.FindBoard(_boardId);
        _region = board?.FindRegion(_regionId);
        if (board == null || _region == null)
        {
            return;
        }
        _index = board.TurnRegions.IndexOf(_region);
        _nodes.Clear();
        _edges.Clear();
        var nodeIds = board.NodesOfRegion(_regionId).Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var node in board.NodesOfRegion(_regionId))
        {
            _nodes.Add(node.Clone());
        }
        foreach (var edge in board.Edges.Where(e => nodeIds.Contains(e.From) || nodeIds.Contains(e.To)))
        {
            _edges.Add(edge.Clone());
        }
        _doc.RemoveTurnRegion(_boardId, _regionId);
    }

    public void Undo()
    {
        if (_region == null)
        {
            return;
        }
        _doc.InsertTurnRegion(_boardId, _index, _region);
        foreach (var node in _nodes)
        {
            _doc.AddNode(_boardId, node);
        }
        foreach (var edge in _edges)
        {
            _doc.AddEdge(_boardId, edge);
        }
    }
}
