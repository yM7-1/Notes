using Godot;
using Notes.Core.Documents;

namespace Notes.UI;

/// <summary>The scaled inner canvas: owns node controls, draws the dot grid and
/// mind-map style curved branches.</summary>
public partial class CanvasSurface : Control
{
    private const float GridSpacing = 48f;
    private const float EdgeOffset = 34f;
    private const int CurveSegments = 22;

    private BoardCanvas _canvas = null!;
    private NotesBoard? _board;
    private readonly Dictionary<string, NodeControl> _nodes = new(StringComparer.Ordinal);
    private StyleBoxFlat _labelBox = new();

    public void Setup(BoardCanvas canvas)
    {
        _canvas = canvas;
        MouseFilter = MouseFilterEnum.Ignore;
        FocusMode = FocusModeEnum.None;
        _labelBox.SetCornerRadiusAll(4);
        _labelBox.SetBorderWidthAll(1);
        _labelBox.ContentMarginLeft = 5;
        _labelBox.ContentMarginRight = 5;
        _labelBox.ContentMarginTop = 1;
        _labelBox.ContentMarginBottom = 1;
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
            var points = CurvePoints(edge);
            for (var i = 0; i + 1 < points.Count; i++)
            {
                var closest = Geometry2D.GetClosestPointToSegment(localPosition, points[i], points[i + 1]);
                var distance = closest.DistanceTo(localPosition);
                if (distance < best)
                {
                    best = distance;
                    edgeId = edge.Id;
                }
            }
        }
        return edgeId.Length > 0;
    }

    public override void _Draw()
    {
        DrawGrid();
        if (_board == null)
        {
            return;
        }

        foreach (var edge in _board.Edges)
        {
            DrawEdge(edge);
        }

        if (_canvas.ActiveLinkSource is { Length: > 0 } source
            && _nodes.TryGetValue(source, out var sourceControl))
        {
            var highlight = new Rect2(
                sourceControl.Position - new Vector2(3, 3),
                sourceControl.Size + new Vector2(6, 6));
            DrawRect(highlight, UiStyle.Accent, false, 2f);
        }

        if (_canvas.IsLinking && _canvas.ActiveLinkSource is { Length: > 0 } pendingFrom)
        {
            var start = NodeCenter(pendingFrom);
            var mouse = GetLocalMousePosition();
            DrawDashedPolyline(new List<Vector2> { start, mouse }, UiStyle.Accent, 2f, 9f, 6f);
            DrawCircle(mouse, 4f, UiStyle.Accent);
        }
    }

    private void DrawGrid()
    {
        var zoom = Scale.X <= 0.01f ? 1f : Scale.X;
        var topLeft = -Position / zoom;
        var visible = new Rect2(topLeft, _canvas.Size / zoom);
        var startX = MathF.Floor(visible.Position.X / GridSpacing) * GridSpacing;
        var startY = MathF.Floor(visible.Position.Y / GridSpacing) * GridSpacing;
        var columns = (int)(visible.Size.X / GridSpacing) + 2;
        var rows = (int)(visible.Size.Y / GridSpacing) + 2;
        if (columns <= 0 || rows <= 0 || (long)columns * rows > 12000)
        {
            return;
        }
        for (var i = 0; i < columns; i++)
        {
            for (var j = 0; j < rows; j++)
            {
                var point = new Vector2(startX + i * GridSpacing, startY + j * GridSpacing);
                DrawCircle(point, 1.2f, UiStyle.GridDot);
            }
        }
    }

    private void DrawEdge(NotesEdge edge)
    {
        if (_board == null)
        {
            return;
        }
        var target = _board.FindNode(edge.To);
        var color = target == null || target.State == NodeState.None
            ? UiStyle.EdgeDefault
            : UiStyle.StateColor(target.State);
        var dashed = target?.State == NodeState.Speculated;

        var points = CurvePoints(edge);
        if (dashed)
        {
            DrawDashedPolyline(points, color, 2.2f, 9f, 6f);
        }
        else
        {
            DrawPolyline(points.ToArray(), color, 2.2f, true);
        }

        var endDirection = (points[^1] - points[^2]).Normalized();
        DrawArrowHead(points[^1], endDirection, color);

        if (!string.IsNullOrWhiteSpace(edge.Label))
        {
            var font = ThemeDB.FallbackFont;
            var middle = points[points.Count / 2];
            var labelSize = font.GetStringSize(edge.Label, HorizontalAlignment.Left, -1, 11);
            var box = new Rect2(
                middle + new Vector2(6, -labelSize.Y - 4),
                new Vector2(labelSize.X + 10, labelSize.Y + 4));
            _labelBox.BgColor = Color.FromHtml("171a20e6");
            _labelBox.BorderColor = UiStyle.PanelBorder;
            DrawStyleBox(_labelBox, box);
            DrawString(font, new Vector2(box.Position.X + 5, box.Position.Y + labelSize.Y - 1),
                edge.Label, HorizontalAlignment.Left, -1, 11, UiStyle.TextDim);
        }
    }

    private List<Vector2> CurvePoints(NotesEdge edge)
    {
        var from = NodeCenter(edge.From);
        var to = NodeCenter(edge.To);
        var dx = (to.X - from.X) * 0.45f;
        var c1 = from + new Vector2(dx, 0);
        var c2 = to - new Vector2(dx, 0);

        var startDirection = (c1 - from).Normalized();
        if (startDirection == Vector2.Zero)
        {
            startDirection = (to - from).Normalized();
        }
        var endDirection = (to - c2).Normalized();
        if (endDirection == Vector2.Zero)
        {
            endDirection = (to - from).Normalized();
        }

        var start = from + startDirection * EdgeOffset;
        var end = to - endDirection * EdgeOffset;

        var points = new List<Vector2>(CurveSegments + 1);
        for (var i = 0; i <= CurveSegments; i++)
        {
            var t = i / (float)CurveSegments;
            points.Add(Cubic(start, c1, c2, end, t));
        }
        return points;
    }

    private static Vector2 Cubic(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        var u = 1f - t;
        return u * u * u * p0
            + 3f * u * u * t * p1
            + 3f * u * t * t * p2
            + t * t * t * p3;
    }

    private void DrawArrowHead(Vector2 tip, Vector2 direction, Color color)
    {
        if (direction == Vector2.Zero)
        {
            return;
        }
        var perpendicular = new Vector2(-direction.Y, direction.X);
        var basePoint = tip - direction * 11f;
        DrawColoredPolygon(new[] { tip, basePoint + perpendicular * 5.5f, basePoint - perpendicular * 5.5f }, color);
    }

    private void DrawDashedPolyline(List<Vector2> points, Color color, float width, float dash, float gap)
    {
        var drawing = true;
        var remaining = dash;
        for (var i = 0; i + 1 < points.Count; i++)
        {
            var a = points[i];
            var b = points[i + 1];
            var segment = b - a;
            var length = segment.Length();
            if (length <= 0.001f)
            {
                continue;
            }
            var direction = segment / length;
            var position = 0f;
            while (position < length)
            {
                var step = MathF.Min(remaining, length - position);
                if (drawing)
                {
                    DrawLine(a + direction * position, a + direction * (position + step), color, width, true);
                }
                position += step;
                remaining -= step;
                if (remaining <= 0.001f)
                {
                    drawing = !drawing;
                    remaining = drawing ? dash : gap;
                }
            }
        }
    }
}
