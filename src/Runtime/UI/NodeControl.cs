using Godot;
using Notes.Core.Documents;
using Notes.Game;

namespace Notes.UI;

/// <summary>A draggable legend on the board: card, action (draw/discard/potion/
/// relic/end turn) or text note. Structured nodes (inside a turn region) are
/// positioned by the layout engine and can be re-slotted by dragging.</summary>
public partial class NodeControl : Control
{
    public const float NodeWidth = 184f;
    public const float NodeHeight = 62f;
    private static readonly Vector2 PortOffset = new(NodeWidth - 11f, 11f);

    private BoardCanvas _canvas = null!;
    private NotesNode _node = null!;
    private StyleBoxFlat _box = null!;
    private StyleBoxFlat _bar = null!;
    private StyleBoxFlat _badge = null!;
    private StyleBoxFlat _chip = null!;
    private bool _hover;
    private bool _hoverPort;
    private bool _dragging;
    private bool _moved;
    private Vector2 _grabOffset;
    private Vector2 _startNodePosition;

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
        MouseDefaultCursorShape = CursorShape.Move;

        _box = new StyleBoxFlat();
        _bar = new StyleBoxFlat();
        _bar.SetCornerRadiusAll(2);
        _bar.SetBorderWidthAll(0);
        _badge = new StyleBoxFlat();
        _badge.SetCornerRadiusAll(5);
        _badge.SetBorderWidthAll(1);
        _chip = new StyleBoxFlat();
        _chip.SetCornerRadiusAll(4);
        _chip.SetBorderWidthAll(1);

        MouseEntered += () =>
        {
            _hover = true;
            QueueRedraw();
        };
        MouseExited += () =>
        {
            _hover = false;
            _hoverPort = false;
            QueueRedraw();
        };

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
        if (!string.IsNullOrWhiteSpace(_node.Meta))
        {
            text += "\n" + _node.Meta;
        }
        if (_node.Annotations.Count > 0)
        {
            text += "\n" + string.Join("\n", _node.Annotations.Select(a => "• " + a.Text));
        }
        if (!string.IsNullOrWhiteSpace(_node.Note))
        {
            text += "\n" + _node.Note;
        }
        TooltipText = text;
    }

    private bool IsInPort(Vector2 localPosition) => localPosition.DistanceTo(PortOffset) <= 13f;

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        var kindColor = UiStyle.KindColor(_node.Kind, _node.CardType);
        var stateColor = UiStyle.StateColor(_node.State);
        var selected = NotesRuntime.SelectionKind == NotesSelectionKind.Node
            && NotesRuntime.SelectionId == _node.Id;

        var border = selected
            ? UiStyle.Accent
            : _dragging || _hover
                ? UiStyle.Accent
                : _node.State == NodeState.None
                    ? UiStyle.PanelBorder.Lerp(kindColor, 0.4f)
                    : stateColor;
        var background = _node.Kind == NodeKind.Text ? UiStyle.NodeBgText : UiStyle.NodeBg;
        if (_hover || _dragging)
        {
            background = background.Lightened(0.05f);
        }

        _box.BgColor = background;
        _box.BorderColor = border;
        _box.SetBorderWidthAll(selected ? 3 : _node.State == NodeState.None && !_hover ? 1 : 2);
        _box.SetCornerRadiusAll(9);
        DrawStyleBox(_box, new Rect2(Vector2.Zero, Size));
        if (selected)
        {
            DrawRect(new Rect2(-3, -3, Size.X + 6, Size.Y + 6), UiStyle.Accent, false, 1f);
        }

        _bar.BgColor = kindColor;
        DrawStyleBox(_bar, new Rect2(8, 10, 4, Size.Y - 20));

        var textX = 18f;
        var glyph = UiStyle.KindGlyph(_node.Kind);
        if (_node.Kind == NodeKind.Card)
        {
            textX = 48f;
            var costCenter = new Vector2(30, Size.Y / 2f);
            DrawCircle(costCenter, 12.5f, UiStyle.BadgeBg);
            DrawCircle(costCenter, 12.5f, UiStyle.RarityColor(_node.Rarity), false, 1.6f);
            var costText = _node.Cost < 0 ? "X" : _node.Cost.ToString();
            var costSize = font.GetStringSize(costText, HorizontalAlignment.Left, -1, 13);
            DrawString(font, costCenter + new Vector2(-costSize.X / 2f, costSize.Y / 2f - 3f), costText,
                HorizontalAlignment.Left, -1, 13, UiStyle.TextMain);
        }
        else if (glyph.Length > 0)
        {
            textX = 48f;
            var badgeCenter = new Vector2(30, Size.Y / 2f);
            DrawCircle(badgeCenter, 12.5f, UiStyle.BadgeBg);
            DrawCircle(badgeCenter, 12.5f, kindColor, false, 1.6f);
            var glyphSize = font.GetStringSize(glyph, HorizontalAlignment.Left, -1, 13);
            DrawString(font, badgeCenter + new Vector2(-glyphSize.X / 2f, glyphSize.Y / 2f - 3f), glyph,
                HorizontalAlignment.Left, -1, 13, kindColor);
        }

        var hasNote = !string.IsNullOrWhiteSpace(_node.Note);
        var maxWidth = NodeWidth - textX - (_node.State == NodeState.None ? 16f : 34f);
        var title = UiStyle.Ellipsize(_node.Title + (_node.Upgraded ? "+" : ""), font, 13, maxWidth);
        DrawString(font, new Vector2(textX, hasNote ? 27 : Size.Y / 2f + 5f), title,
            HorizontalAlignment.Left, -1, 13, UiStyle.TextMain);
        if (hasNote)
        {
            var snippet = UiStyle.Ellipsize(_node.Note.Replace('\n', ' '), font, 10, maxWidth);
            DrawString(font, new Vector2(textX, 46), snippet, HorizontalAlignment.Left, -1, 10, UiStyle.TextDim);
        }

        if (_node.State != NodeState.None)
        {
            var stateGlyph = _node.State switch
            {
                NodeState.Tried => "✔",
                NodeState.Speculated => "?",
                NodeState.Confirmed => "★",
                _ => "",
            };
            var badge = new Rect2(Size.X - 26, 7, 19, 17);
            _badge.BgColor = UiStyle.BadgeBg;
            _badge.BorderColor = stateColor;
            DrawStyleBox(_badge, badge);
            var glyphSize = font.GetStringSize(stateGlyph, HorizontalAlignment.Left, -1, 11);
            DrawString(font, badge.Position + new Vector2((badge.Size.X - glyphSize.X) / 2f, badge.Size.Y - 4f),
                stateGlyph, HorizontalAlignment.Left, -1, 11, stateColor);
        }

        // Auto-captured annotations as chips under the node.
        var chipY = Size.Y + 4f;
        foreach (var annotation in _node.Annotations.Take(3))
        {
            var text = UiStyle.Ellipsize(annotation.Text, font, 10, 176f);
            var textSize = font.GetStringSize(text, HorizontalAlignment.Left, -1, 10);
            var chip = new Rect2(6, chipY, textSize.X + 12, 16);
            var isRelic = annotation.RefId.StartsWith("relic:", StringComparison.Ordinal);
            _chip.BgColor = UiStyle.BadgeBg;
            _chip.BorderColor = isRelic ? UiStyle.KindColor(NodeKind.Relic, -1) : UiStyle.PanelBorder;
            DrawStyleBox(_chip, chip);
            DrawString(font, chip.Position + new Vector2(6, 12), text,
                HorizontalAlignment.Left, -1, 10, isRelic ? UiStyle.KindColor(NodeKind.Relic, -1) : UiStyle.TextDim);
            chipY += 18f;
        }

        DrawCircle(PortOffset, _hoverPort ? 8f : 6f, UiStyle.BadgeBg);
        DrawCircle(PortOffset, _hoverPort ? 6.5f : 5f, _hoverPort ? UiStyle.Accent : kindColor);
    }

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
                if (_canvas.LinkMode)
                {
                    _canvas.LinkClick(_node.Id);
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
                _grabOffset = GetGlobalMousePosition() - GlobalPosition;
                _startNodePosition = new Vector2(_node.X, _node.Y);
                if (_node.RegionId.Length > 0)
                {
                    _canvas.BeginStructuredDrag(_node.Id);
                }
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
                    if (_node.RegionId.Length > 0)
                    {
                        _canvas.EndStructuredDrag(_node.Id, Position);
                    }
                    else if (_moved)
                    {
                        _canvas.CommitNodeMove(_node.Id, _startNodePosition, Position);
                    }
                    else
                    {
                        NotesRuntime.SelectNode(_node.Id);
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
        else if (@event is InputEventMouseMotion motion)
        {
            if (_dragging)
            {
                var target = GetGlobalMousePosition() - _grabOffset;
                if (!_moved && target.DistanceTo(GlobalPosition) > 3f)
                {
                    _moved = true;
                }
                if (_moved)
                {
                    GlobalPosition = target;
                    if (_node.RegionId.Length == 0)
                    {
                        _node.X = Position.X;
                        _node.Y = Position.Y;
                    }
                    _canvas.OnNodeMoved();
                }
                AcceptEvent();
            }
            else
            {
                var overPort = IsInPort(motion.Position);
                if (overPort != _hoverPort)
                {
                    _hoverPort = overPort;
                    QueueRedraw();
                }
                MouseDefaultCursorShape = overPort ? CursorShape.PointingHand : CursorShape.Move;
                if (_canvas.IsLinking)
                {
                    _canvas.OnNodeMoved();
                }
            }
        }
    }
}
