using Godot;
using Notes.Core.Documents;
using Notes.Core.Services;
using Notes.Game;

namespace Notes.UI;

/// <summary>The scaled inner canvas: owns node controls, draws the dot grid,
/// world-line columns, turn region frames, slot markers and curved branches.</summary>
public partial class CanvasSurface : Control
{
    private const float GridSpacing = 48f;
    private const float EdgeOffset = 34f;
    private const int CurveSegments = 22;

    private static readonly Color[] WorldLineColors =
    {
        Color.FromHtml("f0c674"),
        Color.FromHtml("6a94e8"),
        Color.FromHtml("5cb87f"),
        Color.FromHtml("b06ad6"),
        Color.FromHtml("e06c5f"),
    };

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

    public void RefreshNodeDraw()
    {
        foreach (var control in _nodes.Values)
        {
            control.QueueRedraw();
        }
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

        DrawWorldLinesAndRegions();

        foreach (var edge in _board.Edges)
        {
            DrawEdge(edge);
        }

        if (_canvas.SlotHint)
        {
            DrawSlots();
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
                DrawCircle(new Vector2(startX + i * GridSpacing, startY + j * GridSpacing), 1.2f, UiStyle.GridDot);
            }
        }
    }

    private void DrawWorldLinesAndRegions()
    {
        var font = ThemeDB.FallbackFont;
        for (var lineIndex = 0; lineIndex < _board!.WorldLines.Count; lineIndex++)
        {
            var line = _board.WorldLines[lineIndex];
            var color = WorldLineColors[lineIndex % WorldLineColors.Length];
            var regions = _board.RegionsOf(line.Id).ToList();
            var headerX = NotesLayout.ColumnStartX + 4;
            var headerY = NotesLayout.RowStartY - 46;
            if (regions.Count > 0)
            {
                headerX = regions[0].X + 2;
                headerY = regions[0].Y - 34;
            }
            var label = ModLocalization.T("world_line_label", "世界线") + " " + (lineIndex + 1);
            if (lineIndex == 0)
            {
                label += " · " + ModLocalization.T("world_line_actual", "实际");
            }
            label += " · " + line.Name;
            DrawString(font, new Vector2(headerX, headerY), label, HorizontalAlignment.Left, -1, 14, color);
            if (NotesRuntime.SelectionKind == NotesSelectionKind.WorldLine
                && NotesRuntime.SelectionId == line.Id)
            {
                var size = font.GetStringSize(label, HorizontalAlignment.Left, -1, 14);
                DrawLine(new Vector2(headerX, headerY + 4), new Vector2(headerX + size.X, headerY + 4),
                    UiStyle.Accent, 2f, true);
            }

            for (var i = 0; i < regions.Count; i++)
            {
                var region = regions[i];
                DrawRegion(region, color, font);
                if (i + 1 < regions.Count)
                {
                    var next = regions[i + 1];
                    var from = new Vector2(region.X + 26, region.Y + region.Height + 4);
                    var to = new Vector2(next.X + 26, next.Y - 6);
                    if (to.Y > from.Y)
                    {
                        DrawDashedPolyline(new List<Vector2> { from, to }, color.Lerp(UiStyle.PanelBorder, 0.4f), 2f, 7f, 5f);
                        DrawArrowHead(to, new Vector2(0, 1), color.Lerp(UiStyle.PanelBorder, 0.4f));
                    }
                }
            }
        }
    }

    private void DrawRegion(NotesTurnRegion region, Color color, Font font)
    {
        var rect = new Rect2(region.X, region.Y, region.Width, region.Height);
        var selected = NotesRuntime.SelectionKind == NotesSelectionKind.Region
            && NotesRuntime.SelectionId == region.Id;
        DrawRect(rect, Color.FromHtml("1b1f27cc"), true);
        DrawDashedRect(rect, selected ? UiStyle.Accent : color.Lerp(UiStyle.PanelBorder, 0.45f), selected ? 2.5f : 1.5f);

        var title = ModLocalization.T("region_turn", "回合") + " " + region.TurnNumber;
        DrawString(font, new Vector2(region.X + 12, region.Y + 26), title, HorizontalAlignment.Left, -1, 13, color);

        if (region.TurnEvents.Count > 0)
        {
            var text = string.Join(" · ", region.TurnEvents.Take(3).Select(e => e.Text));
            var size = font.GetStringSize(text, HorizontalAlignment.Left, -1, 10);
            var box = new Rect2(region.X + region.Width - size.X - 24, region.Y + 10, size.X + 12, 18);
            _labelBox.BgColor = UiStyle.BadgeBg;
            _labelBox.BorderColor = UiStyle.KindColor(NodeKind.Relic, -1);
            DrawStyleBox(_labelBox, box);
            DrawString(font, box.Position + new Vector2(6, 13), text, HorizontalAlignment.Left, -1, 10,
                UiStyle.KindColor(NodeKind.Relic, -1));
        }
    }

    private void DrawSlots()
    {
        if (_board == null)
        {
            return;
        }
        var font = ThemeDB.FallbackFont;
        var mouse = GetLocalMousePosition();
        foreach (var region in _board.TurnRegions)
        {
            foreach (var slot in NotesLayout.FreeSlots(_board, region))
            {
                var center = new Vector2(
                    slot.X + NodeControl.NodeWidth / 2f,
                    slot.Y + NodeControl.NodeHeight / 2f);
                var hovered = center.DistanceTo(mouse) < 70f;
                var color = hovered ? UiStyle.Accent : UiStyle.Accent.Lerp(UiStyle.PanelBorder, 0.55f);
                DrawCircle(center, hovered ? 20f : 16f, Color.FromHtml("171a20aa"));
                DrawCircle(center, hovered ? 20f : 16f, color, false, 2f);
                DrawString(font, center + new Vector2(-5, 5), "+", HorizontalAlignment.Left, -1, 16, color);
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

    private void DrawDashedRect(Rect2 rect, Color color, float width)
    {
        var points = new List<Vector2>
        {
            rect.Position,
            rect.Position + new Vector2(rect.Size.X, 0),
            rect.Position + rect.Size,
            rect.Position + new Vector2(0, rect.Size.Y),
            rect.Position,
        };
        DrawDashedPolyline(points, color, width, 10f, 7f);
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
