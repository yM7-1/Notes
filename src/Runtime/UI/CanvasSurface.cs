using Godot;
using Notes.Core.Documents;

namespace Notes.UI;

/// <summary>The scaled inner canvas: owns node controls and draws branches.</summary>
public partial class CanvasSurface : Control
{
    private const float NodeHeightHalf = NodeControl.NodeHeight / 2f;

    private BoardCanvas _canvas = null!;
    private NotesBoard? _board;
    private readonly Dictionary<string, NodeControl> _nodes = new(StringComparer.Ordinal);

    public void Setup(BoardCanvas canvas)
    {
        _canvas = canvas;
        MouseFilter = MouseFilterEnum.Ignore;
        FocusMode = FocusModeEnum.None;
    }

    public void SetBoard(NotesBoard? board)
    {
        _board = board;
        foreach (var control in _nodes.Values)
        {
            RemoveChild(control);
            control.QueueFree();
        }
        _nodes.Clear();
        if (board != null)
        {
            foreach (var node in board.Nodes)
            {
                AddNodeControl(node);
            }
        }
        QueueRedraw();
    }

    public void Refresh()
    {
        if (_board == null)
        {
            SetBoard(null);
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in _board.Nodes)
        {
            seen.Add(node.Id);
            if (_nodes.TryGetValue(node.Id, out var control))
            {
                control.UpdateFrom(node);
            }
            else
            {
                AddNodeControl(node);
            }
        }
        foreach (var pair in _nodes.Where(p => !seen.Contains(p.Key)).ToList())
        {
            RemoveChild(pair.Value);
            pair.Value.QueueFree();
            _nodes.Remove(pair.Key);
        }
        QueueRedraw();
    }

    private void AddNodeControl(NotesNode node)
    {
        var control = new NodeControl { Name = "Node_" + node.Id };
        control.Setup(_canvas, node);
        AddChild(control);
        _nodes[node.Id] = control;
    }

    public Vector2 NodeCenter(string nodeId)
    {
        if (_nodes.TryGetValue(nodeId, out var control))
        {
            return control.Position + control.Size / 2f;
        }
        var node = _board?.FindNode(nodeId);
        return node == null
            ? Vector2.Zero
            : new Vector2(node.X + NodeControl.NodeWidth / 2f, node.Y + NodeControl.NodeHeight / 2f);
    }

    public bool TryGetNodeAt(Vector2 localPosition, out string nodeId)
    {
        var children = GetChildren();
        for (var i = children.Count - 1; i >= 0; i--)
        {
            if (children[i] is NodeControl control
                && new Rect2(control.Position, control.Size).HasPoint(localPosition))
            {
                nodeId = control.NodeId;
                return true;
            }
        }
        nodeId = "";
        return false;
    }

    public bool TryGetEdgeNear(Vector2 localPosition, float maxDistance, out string edgeId)
    {
        edgeId = "";
        if (_board == null)
        {
            return false;
        }
        var best = maxDistance;
        foreach (var edge in _board.Edges)
        {
            var closest = Geometry2D.GetClosestPointToSegment(
                localPosition, NodeCenter(edge.From), NodeCenter(edge.To));
            var distance = closest.DistanceTo(localPosition);
            if (distance < best)
            {
                best = distance;
                edgeId = edge.Id;
            }
        }
        return edgeId.Length > 0;
    }

    public override void _Draw()
    {
        if (_board == null)
        {
            return;
        }

        var font = ThemeDB.FallbackFont;
        foreach (var edge in _board.Edges)
        {
            var from = NodeCenter(edge.From);
            var to = NodeCenter(edge.To);
            var target = _board.FindNode(edge.To);
            var color = target == null || target.State == NodeState.None
                ? UiStyle.EdgeDefault
                : UiStyle.StateColor(target.State);

            var direction = (to - from).Normalized();
            var end = to - direction * (NodeHeightHalf + 4f);
            if (end.DistanceTo(from) > 8f)
            {
                DrawArrow(from, end, color);
            }
            if (!string.IsNullOrWhiteSpace(edge.Label))
            {
                var middle = (from + end) / 2f;
                DrawString(font, middle + new Vector2(4, -6), edge.Label,
                    HorizontalAlignment.Left, -1, 11, UiStyle.TextDim);
            }
        }

        if (_canvas.IsLinking && _canvas.LinkFromId is { Length: > 0 } linkFrom)
        {
            var start = NodeCenter(linkFrom);
            var mouse = GetLocalMousePosition();
            DrawLine(start, mouse, UiStyle.Accent, 2f, true);
            DrawCircle(mouse, 4f, UiStyle.Accent);
        }
    }

    private void DrawArrow(Vector2 from, Vector2 to, Color color)
    {
        DrawLine(from, to, color, 2f, true);
        var direction = (to - from).Normalized();
        var perpendicular = new Vector2(-direction.Y, direction.X);
        var basePoint = to - direction * 10f;
        DrawColoredPolygon(new[] { to, basePoint + perpendicular * 5f, basePoint - perpendicular * 5f }, color);
    }
}
