using Godot;
using Notes.Core.Documents;

namespace Notes.UI;

/// <summary>A draggable legend on the board: mini card or text note.</summary>
public partial class NodeControl : Control
{
    public const float NodeWidth = 168f;
    public const float NodeHeight = 54f;

    private BoardCanvas _canvas = null!;
    private NotesNode _node = null!;
    private bool _dragging;
    private bool _moved;
    private Vector2 _pressLocal;
    private Vector2 _startPos;

    public string NodeId => _node.Id;

    public void Setup(BoardCanvas canvas, NotesNode node)
    {
        _canvas = canvas;
        _node = node;
        CustomMinimumSize = new Vector2(NodeWidth, NodeHeight);
        Size = new Vector2(NodeWidth, NodeHeight);
        Position = new Vector2(node.X, node.Y);
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.None;
        UpdateTooltip();
    }

    public void UpdateFrom(NotesNode node)
    {
        _node = node;
        Position = new Vector2(node.X, node.Y);
        UpdateTooltip();
        QueueRedraw();
    }

    private void UpdateTooltip()
    {
        var text = _node.Title + (_node.Upgraded ? "+" : "");
        if (!string.IsNullOrWhiteSpace(_node.Note))
        {
            text += "\n" + _node.Note;
        }
        if (_node.Kind == NodeKind.Card && !string.IsNullOrWhiteSpace(_node.RefId))
        {
            text += "\n" + _node.RefId;
        }
        TooltipText = text;
    }

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        var centerY = NodeHeight / 2f;
        var stateColor = UiStyle.StateColor(_node.State);
        var background = _node.Kind == NodeKind.Text ? UiStyle.NodeBgText : UiStyle.NodeBg;
        if (_dragging)
        {
            background = background.Lightened(0.06f);
        }

        DrawRect(new Rect2(0, 0, NodeWidth, NodeHeight), background, true);
        DrawRect(new Rect2(1, 1, NodeWidth - 2, NodeHeight - 2), stateColor, false,
            _node.State == NodeState.None ? 1f : 2.5f);

        var typeColor = _node.Kind == NodeKind.Card ? UiStyle.TypeColor(_node.CardType) : UiStyle.Accent;
        DrawRect(new Rect2(0, 0, 6, NodeHeight), typeColor, true);

        if (_node.Kind == NodeKind.Card)
        {
            var costCenter = new Vector2(24, centerY);
            DrawCircle(costCenter, 12, Color.FromHtml("12141a"));
            DrawCircle(costCenter, 12, UiStyle.RarityColor(_node.Rarity), false, 1.5f);
            var costText = _node.Cost < 0 ? "X" : _node.Cost.ToString();
            var costSize = font.GetStringSize(costText, HorizontalAlignment.Left, -1, 13);
            DrawString(font, costCenter + new Vector2(-costSize.X / 2f, costSize.Y / 2f - 3f), costText,
                HorizontalAlignment.Left, -1, 13, UiStyle.TextMain);
        }

        var textX = _node.Kind == NodeKind.Card ? 44f : 14f;
        var maxWidth = NodeWidth - textX - 34f;
        var title = UiStyle.Ellipsize(_node.Title + (_node.Upgraded ? "+" : ""), font, 13, maxWidth);
        DrawString(font, new Vector2(textX, centerY + 5), title, HorizontalAlignment.Left, -1, 13, UiStyle.TextMain);

        var glyph = _node.State switch
        {
            NodeState.Tried => "✔",
            NodeState.Speculated => "?",
            NodeState.Confirmed => "★",
            _ => "",
        };
        if (glyph.Length > 0)
        {
            DrawString(font, new Vector2(NodeWidth - 18, 18), glyph, HorizontalAlignment.Left, -1, 12, stateColor);
        }

        DrawCircle(new Vector2(NodeWidth - 9, 9), 4.5f, typeColor);
    }

    private bool IsInPort(Vector2 localPosition) =>
        localPosition.DistanceTo(new Vector2(NodeWidth - 9, 9)) <= 12f;

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.Left && button.Pressed)
            {
                if (IsInPort(button.Position))
                {
                    _canvas.BeginLink(_node.Id);
                    AcceptEvent();
                    return;
                }
                if (button.DoubleClick)
                {
                    _canvas.OpenEditor(_node.Id);
                    AcceptEvent();
                    return;
                }
                _dragging = true;
                _moved = false;
                _pressLocal = button.Position;
                _startPos = Position;
                MoveToFront();
                QueueRedraw();
                AcceptEvent();
            }
            else if (button.ButtonIndex == MouseButton.Left && !button.Pressed)
            {
                if (_canvas.IsLinking)
                {
                    _canvas.CompleteLink();
                    AcceptEvent();
                    return;
                }
                if (_dragging)
                {
                    _dragging = false;
                    QueueRedraw();
                    if (_moved)
                    {
                        _canvas.CommitNodeMove(_node.Id, _startPos, Position);
                    }
                    AcceptEvent();
                }
            }
            else if (button.ButtonIndex == MouseButton.Right && button.Pressed)
            {
                _canvas.OpenNodeMenu(_node.Id, GetGlobalMousePosition());
                AcceptEvent();
            }
        }
        else if (@event is InputEventMouseMotion motion && _dragging)
        {
            var delta = motion.Position - _pressLocal;
            if (!_moved && delta.Length() > 3f)
            {
                _moved = true;
            }
            Position = _startPos + delta;
            _node.X = Position.X;
            _node.Y = Position.Y;
            _canvas.OnNodeMoved();
        }
    }
}
