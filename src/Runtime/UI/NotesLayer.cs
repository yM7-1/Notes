using Godot;
using Notes.Game;

namespace Notes.UI;

/// <summary>Root canvas layer: draggable notes handle (+ quick record buttons)
/// and the notes window. The handle can be collapsed into a small arrow on
/// either screen edge; its position / state is remembered.</summary>
public partial class NotesLayer : CanvasLayer
{
    private const float HandleWidth = 68f;
    private const float HandleHeight = 40f;
    private const float ArrowWidth = 24f;
    private const float ArrowHeight = 46f;
    private const float EdgeMargin = 2f;

    private VBoxContainer _box = null!;
    private DragHandle _toggle = null!;
    private HBoxContainer _quickRow = null!;
    private Button _collapseButton = null!;
    private NotesWindow _window = null!;
    private bool _positionApplied;
    private bool _handleDragged;
    private bool _handleStateApplied;
    private bool _collapsed;
    private int _side; // 0 = right edge, 1 = left edge

    public override void _Ready()
    {
        Layer = 90;

        var viewport = GetViewport().GetVisibleRect().Size;
        var defaultPosition = new Vector2(viewport.X - HandleWidth - 14f, viewport.Y / 2f - HandleHeight / 2f);
        var position = NotesRuntime.TryGetButtonPosition(out var savedX, out var savedY)
            ? new Vector2(savedX, savedY)
            : defaultPosition;

        _box = new VBoxContainer { Name = "NotesHandleBox", MouseFilter = Control.MouseFilterEnum.Ignore };
        _box.AddThemeConstantOverride("separation", 4);
        _box.Position = ClampToViewport(position, viewport, new Vector2(HandleWidth, HandleHeight));
        AddChild(_box);

        var handleRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        handleRow.AddThemeConstantOverride("separation", 2);
        _box.AddChild(handleRow);

        _toggle = new DragHandle { Name = "NotesHandle" };
        _toggle.Setup(ModLocalization.T("toggle_button", "Notes"));
        // Panel does not derive its minimum size from children: without this the
        // container would collapse the handle to zero height (unclickable).
        _toggle.CustomMinimumSize = new Vector2(HandleWidth, HandleHeight);
        _toggle.Size = new Vector2(HandleWidth, HandleHeight);
        _toggle.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        _toggle.TooltipText = ModLocalization.T("window_title", "Notes") + " (F8)";
        _toggle.Activated += OnHandleActivated;
        _toggle.DragMoved += delta => _box.Position = ClampBox(_box.Position + delta);
        _toggle.DragFinished += OnHandleDragFinished;
        handleRow.AddChild(_toggle);

        _collapseButton = new Button
        {
            Text = "−",
            TooltipText = ModLocalization.T("handle_collapse_tip", "Collapse the notes handle"),
        };
        UiStyle.StyleButton(_collapseButton, fontSize: 12);
        _collapseButton.Pressed += Collapse;
        _collapseButton.CustomMinimumSize = new Vector2(22, HandleHeight);
        _collapseButton.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        handleRow.AddChild(_collapseButton);

        _quickRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _quickRow.AddThemeConstantOverride("separation", 4);
        _box.AddChild(_quickRow);
        var recordTurn = new Button
        {
            Text = ModLocalization.T("quick_record_turn", "Record turn"),
            TooltipText = ModLocalization.T("quick_record_turn_tip", "Record this turn into the current world line"),
        };
        UiStyle.StyleButton(recordTurn, accent: true, fontSize: 11);
        recordTurn.Pressed += () => NotesRuntime.Import(currentTurnOnly: true);
        _quickRow.AddChild(recordTurn);
        var recordCombat = new Button
        {
            Text = ModLocalization.T("quick_copy_line", "Copy → new line"),
            TooltipText = ModLocalization.T("quick_copy_line_tip",
                "把当前世界线复制成可交互的世界线画板（录入/更改/预测）"),
        };
        UiStyle.StyleButton(recordCombat, fontSize: 11);
        recordCombat.Pressed += () => NotesRuntime.CopyCurrentToNewLine();
        _quickRow.AddChild(recordCombat);

        _window = new NotesWindow { Name = "NotesWindow" };
        AddChild(_window);
        _window.Hide();
        UpdateHandleStyle();

        if (NotesRuntime.TryGetHandleState(out var collapsed, out var side))
        {
            _handleStateApplied = true;
            if (collapsed)
            {
                _side = side;
                Collapse(save: false);
            }
        }
    }

    public override void _Process(double delta)
    {
        NotesRuntime.Tick(delta);
        if (!_positionApplied && !_handleDragged && NotesRuntime.GlobalLoaded)
        {
            _positionApplied = true;
            if (!_collapsed && NotesRuntime.TryGetButtonPosition(out var savedX, out var savedY))
            {
                _box.Position = ClampBox(new Vector2(savedX, savedY));
            }
        }
        if (!_handleStateApplied && NotesRuntime.GlobalLoaded)
        {
            _handleStateApplied = true;
            if (NotesRuntime.TryGetHandleState(out var collapsed, out var side) && collapsed && !_collapsed)
            {
                _side = side;
                Collapse(save: false);
            }
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.F8)
        {
            Toggle();
            GetViewport().SetInputAsHandled();
        }
    }

    private void OnHandleActivated()
    {
        if (_collapsed)
        {
            Expand();
            return;
        }
        Toggle();
    }

    private void Toggle()
    {
        _window.Visible = !_window.Visible;
        if (_window.Visible)
        {
            _window.OnShown();
        }
        else
        {
            NotesRuntime.FlushSave();
        }
        UpdateHandleStyle();
    }

    private void OnHandleDragFinished()
    {
        _handleDragged = true;
        if (_collapsed)
        {
            _side = NearestSide(_box);
            ApplyCollapsedPosition();
        }
        else
        {
            _box.Position = ClampBox(_box.Position);
            NotesRuntime.SaveButtonPosition(_box.Position.X, _box.Position.Y);
        }
    }

    private void Collapse() => Collapse(save: true);

    private void Collapse(bool save)
    {
        _collapsed = true;
        _side = NearestSide(_box);
        _quickRow.Visible = false;
        _collapseButton.Visible = false;
        _toggle.SetText(_side == 1 ? "▶" : "◀");
        _toggle.CustomMinimumSize = new Vector2(ArrowWidth, ArrowHeight);
        _toggle.Size = new Vector2(ArrowWidth, ArrowHeight);
        ApplyCollapsedPosition();
        if (save)
        {
            NotesRuntime.SaveHandleState(true, _side);
        }
    }

    private void Expand()
    {
        _collapsed = false;
        _quickRow.Visible = true;
        _collapseButton.Visible = true;
        _toggle.SetText(ModLocalization.T("toggle_button", "Notes"));
        _toggle.CustomMinimumSize = new Vector2(HandleWidth, HandleHeight);
        _toggle.Size = new Vector2(HandleWidth, HandleHeight);
        if (NotesRuntime.TryGetButtonPosition(out var savedX, out var savedY))
        {
            _box.Position = ClampBox(new Vector2(savedX, savedY));
        }
        else
        {
            _box.Position = ClampBox(_box.Position);
        }
        NotesRuntime.SaveHandleState(false, _side);
    }

    private void ApplyCollapsedPosition()
    {
        var viewport = GetViewport().GetVisibleRect().Size;
        var y = Mathf.Clamp(_box.Position.Y, EdgeMargin, Mathf.Max(EdgeMargin, viewport.Y - ArrowHeight - EdgeMargin));
        _box.Position = _side == 1
            ? new Vector2(EdgeMargin, y)
            : new Vector2(Mathf.Max(EdgeMargin, viewport.X - ArrowWidth - EdgeMargin), y);
    }

    private static int NearestSide(Control box)
    {
        var viewport = box.GetViewportRect().Size;
        var center = box.Position.X + box.Size.X / 2f;
        return center < viewport.X / 2f ? 1 : 0;
    }

    private Vector2 ClampBox(Vector2 position)
    {
        var viewport = GetViewport().GetVisibleRect().Size;
        return ClampToViewport(position, viewport, _box.Size);
    }

    private void UpdateHandleStyle() => UiStyle.StyleHandle(_toggle, _window != null && _window.Visible);

    private static Vector2 ClampToViewport(Vector2 position, Vector2 viewport, Vector2 size)
    {
        return new Vector2(
            Mathf.Clamp(position.X, 0f, Mathf.Max(0f, viewport.X - size.X)),
            Mathf.Clamp(position.Y, 0f, Mathf.Max(0f, viewport.Y - size.Y)));
    }
}

/// <summary>
/// A small always-on-top handle that can be clicked (open the notes) or dragged
/// (park it anywhere on screen). Implemented on Panel so the built-in button
/// press handling cannot swallow the drag gesture.
/// </summary>
public partial class DragHandle : Panel
{
    private const float DragThreshold = 5f;

    private Label _label = null!;
    private bool _pressed;
    private bool _dragged;
    private Vector2 _grabOffset;

    public event Action? Activated;

    public event Action? DragFinished;

    /// <summary>Emitted while dragging with the desired position delta.</summary>
    public event Action<Vector2>? DragMoved;

    public void Setup(string text)
    {
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.None;
        _label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _label.SetAnchorsPreset(LayoutPreset.FullRect);
        _label.AddThemeFontSizeOverride("font_size", 14);
        AddChild(_label);
    }

    public void SetText(string text)
    {
        if (_label != null)
        {
            _label.Text = text;
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton button && button.ButtonIndex == MouseButton.Left)
        {
            if (button.Pressed)
            {
                _pressed = true;
                _dragged = false;
                _grabOffset = GetGlobalMousePosition() - GlobalPosition;
            }
            else if (_pressed)
            {
                _pressed = false;
                if (_dragged)
                {
                    DragFinished?.Invoke();
                }
                else
                {
                    Activated?.Invoke();
                }
                AcceptEvent();
            }
            return;
        }

        if (@event is InputEventMouseMotion && _pressed)
        {
            var target = GetGlobalMousePosition() - _grabOffset;
            if (!_dragged && target.DistanceTo(GlobalPosition) > DragThreshold)
            {
                _dragged = true;
            }
            if (_dragged)
            {
                DragMoved?.Invoke(target - GlobalPosition);
                AcceptEvent();
            }
        }
    }
}
