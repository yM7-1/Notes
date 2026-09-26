using Godot;
using Notes.Game;

namespace Notes.UI;

/// <summary>Root canvas layer: draggable notes handle + the notes window.
/// The handle defaults to the middle-right edge and remembers where it was parked.</summary>
public partial class NotesLayer : CanvasLayer
{
    private const float HandleWidth = 68f;
    private const float HandleHeight = 40f;

    private DragHandle _toggle = null!;
    private NotesWindow _window = null!;
    private bool _positionApplied;
    private bool _handleDragged;

    public override void _Ready()
    {
        Layer = 90;

        var viewport = GetViewport().GetVisibleRect().Size;
        var defaultPosition = new Vector2(viewport.X - HandleWidth - 14f, viewport.Y / 2f - HandleHeight / 2f);
        var position = NotesRuntime.TryGetButtonPosition(out var savedX, out var savedY)
            ? new Vector2(savedX, savedY)
            : defaultPosition;
        position = ClampToViewport(position, viewport);

        _toggle = new DragHandle { Name = "NotesHandle" };
        _toggle.Setup(ModLocalization.T("toggle_button", "Notes"));
        _toggle.Size = new Vector2(HandleWidth, HandleHeight);
        _toggle.Position = position;
        _toggle.TooltipText = ModLocalization.T("window_title", "Notes") + " (F8)";
        _toggle.Activated += Toggle;
        _toggle.DragFinished += () =>
        {
            _handleDragged = true;
            NotesRuntime.SaveButtonPosition(_toggle.Position.X, _toggle.Position.Y);
        };
        AddChild(_toggle);

        _window = new NotesWindow { Name = "NotesWindow" };
        AddChild(_window);
        _window.Hide();
        UpdateHandleStyle();
    }

    public override void _Process(double delta)
    {
        NotesRuntime.Tick(delta);
        if (!_positionApplied && !_handleDragged && NotesRuntime.GlobalLoaded)
        {
            _positionApplied = true;
            if (NotesRuntime.TryGetButtonPosition(out var savedX, out var savedY))
            {
                _toggle.Position = ClampToViewport(
                    new Vector2(savedX, savedY), GetViewport().GetVisibleRect().Size);
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

    private void Toggle()
    {
        _window.Visible = !_window.Visible;
        if (_window.Visible)
        {
            _window.OnShown();
        }
        UpdateHandleStyle();
    }

    private void UpdateHandleStyle() => UiStyle.StyleHandle(_toggle, _window != null && _window.Visible);

    private static Vector2 ClampToViewport(Vector2 position, Vector2 viewport)
    {
        return new Vector2(
            Mathf.Clamp(position.X, 0f, Mathf.Max(0f, viewport.X - HandleWidth)),
            Mathf.Clamp(position.Y, 0f, Mathf.Max(0f, viewport.Y - HandleHeight)));
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

    public void Setup(string text)
    {
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.None;
        _label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _label.SetAnchorsPreset(LayoutPreset.FullRect);
        _label.AddThemeFontSizeOverride("font_size", 14);
        AddChild(_label);
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
                var viewport = GetViewportRect().Size;
                target.X = Mathf.Clamp(target.X, 0f, Mathf.Max(0f, viewport.X - Size.X));
                target.Y = Mathf.Clamp(target.Y, 0f, Mathf.Max(0f, viewport.Y - Size.Y));
                GlobalPosition = target;
                AcceptEvent();
            }
        }
    }
}
