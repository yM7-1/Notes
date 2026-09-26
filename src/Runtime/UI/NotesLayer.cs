using Godot;
using Notes.Game;

namespace Notes.UI;

/// <summary>Root canvas layer: floating toggle button + the notes window.</summary>
public partial class NotesLayer : CanvasLayer
{
    private Button _toggle = null!;
    private NotesWindow _window = null!;

    public override void _Ready()
    {
        Layer = 90;

        _toggle = new Button
        {
            Text = ModLocalization.T("toggle_button", "Notes"),
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = ModLocalization.T("window_title", "Notes") + " (F8)",
        };
        _toggle.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _toggle.OffsetLeft = -76;
        _toggle.OffsetTop = 8;
        _toggle.OffsetRight = -12;
        _toggle.OffsetBottom = 38;
        _toggle.Pressed += Toggle;
        AddChild(_toggle);

        _window = new NotesWindow { Name = "NotesWindow" };
        AddChild(_window);
        _window.Hide();
    }

    public override void _Process(double delta)
    {
        NotesRuntime.Tick(delta);
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
    }
}
