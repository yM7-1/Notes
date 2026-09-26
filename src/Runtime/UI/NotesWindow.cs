using Godot;
using Notes.Game;

namespace Notes.UI;

/// <summary>The notes panel: header (library / board / actions) + canvas + card tray.</summary>
public partial class NotesWindow : PanelContainer
{
    private OptionButton _boardPicker = null!;
    private Button _libraryButton = null!;
    private Button _linkButton = null!;
    private Button _undoButton = null!;
    private Button _redoButton = null!;
    private Label _status = null!;
    private BoardCanvas _canvas = null!;
    private CardPalette _palette = null!;
    private ConfirmationDialog _deleteConfirm = null!;
    private bool _updating;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(1000, 640);
        Position = new Vector2(48, 40);
        AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.WindowBg, UiStyle.PanelBorder, 10, 2));

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 8);
        AddChild(root);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 6);
        root.AddChild(header);

        var title = new Label { Text = ModLocalization.T("window_title", "Notes") };
        title.AddThemeFontSizeOverride("font_size", 16);
        title.AddThemeColorOverride("font_color", UiStyle.Accent);
        header.AddChild(title);

        _libraryButton = MakeButton("", OnLibraryPressed);
        _libraryButton.TooltipText = ModLocalization.T("library_switch_tip", "Switch library");
        header.AddChild(_libraryButton);

        _boardPicker = new OptionButton
        {
            FocusMode = FocusModeEnum.None,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _boardPicker.ItemSelected += OnBoardSelected;
        header.AddChild(_boardPicker);

        header.AddChild(MakeButton(ModLocalization.T("board_new", "+ Board"), () => NotesRuntime.NewBoard()));
        header.AddChild(MakeButton(ModLocalization.T("board_delete", "Del Board"), ShowDeleteBoard));
        header.AddChild(MakeButton(ModLocalization.T("add_text", "+ Text"), AddTextHere));

        _linkButton = new Button
        {
            Text = ModLocalization.T("link_mode", "Link"),
            ToggleMode = true,
            TooltipText = ModLocalization.T("link_mode_tip", "Link mode"),
        };
        UiStyle.StyleButton(_linkButton, accent: true);
        _linkButton.Toggled += OnLinkToggled;
        header.AddChild(_linkButton);

        _undoButton = MakeButton(ModLocalization.T("undo", "Undo"), () =>
        {
            NotesRuntime.Commands.Undo();
            NotesRuntime.Raise();
        });
        header.AddChild(_undoButton);

        _redoButton = MakeButton(ModLocalization.T("redo", "Redo"), () =>
        {
            NotesRuntime.Commands.Redo();
            NotesRuntime.Raise();
        });
        header.AddChild(_redoButton);

        header.AddChild(MakeButton(ModLocalization.T("reset_view", "Reset"), () => _canvas.ResetView()));
        header.AddChild(MakeButton(ModLocalization.T("close", "Close"), Hide));

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 8);
        root.AddChild(body);

        _canvas = new BoardCanvas
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(640, 500),
        };
        _canvas.SetBackdrop(UiStyle.CanvasBg);
        body.AddChild(_canvas);

        _palette = new CardPalette { SizeFlagsVertical = SizeFlags.ExpandFill };
        _palette.CardActivated += snapshot =>
            _canvas.AddCardNode(snapshot, _canvas.ViewCenterInBoardCoords()
                - new Vector2(NodeControl.NodeWidth / 2f, NodeControl.NodeHeight / 2f));
        body.AddChild(_palette);

        _status = new Label { Text = ModLocalization.T("status_hint", "") };
        _status.AddThemeFontSizeOverride("font_size", 11);
        _status.AddThemeColorOverride("font_color", UiStyle.TextDim);
        root.AddChild(_status);

        _deleteConfirm = new ConfirmationDialog
        {
            Title = ModLocalization.T("board_delete", "Del Board"),
            DialogText = ModLocalization.T("delete_board_text", "Delete the current board?"),
        };
        _deleteConfirm.Confirmed += () =>
        {
            var board = NotesRuntime.ActiveBoard;
            NotesRuntime.DeleteBoard(board.Id);
        };
        AddChild(_deleteConfirm);

        NotesRuntime.Changed += RefreshHeader;
        RefreshHeader();
    }

    public override void _ExitTree()
    {
        NotesRuntime.Changed -= RefreshHeader;
    }

    public void OnShown()
    {
        RefreshHeader();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!IsVisibleInTree())
        {
            return;
        }
        if (@event is not InputEventKey key || !key.Pressed || key.Echo)
        {
            return;
        }
        if (key.Keycode == Key.Escape && _canvas.LinkMode)
        {
            _linkButton.ButtonPressed = false;
            GetViewport().SetInputAsHandled();
            return;
        }
        var ctrl = key.CtrlPressed || key.MetaPressed;
        if (ctrl && key.Keycode == Key.Z)
        {
            if (key.ShiftPressed)
            {
                NotesRuntime.Commands.Redo();
            }
            else
            {
                NotesRuntime.Commands.Undo();
            }
            NotesRuntime.Raise();
            GetViewport().SetInputAsHandled();
        }
        else if (ctrl && key.Keycode == Key.Y)
        {
            NotesRuntime.Commands.Redo();
            NotesRuntime.Raise();
            GetViewport().SetInputAsHandled();
        }
    }

    private void RefreshHeader()
    {
        if (_updating)
        {
            return;
        }
        _updating = true;
        try
        {
            var inRun = NotesRuntime.Library == NotesLibrary.Run && NotesRuntime.RunActive;
            _libraryButton.Text = ModLocalization.T(
                inRun ? "library_run" : "library_global",
                inRun ? "Library: Run" : "Library: Global");
            _libraryButton.Disabled = !NotesRuntime.RunActive && !inRun;

            var document = NotesRuntime.ActiveDocument;
            var active = document.ActiveBoard;
            _boardPicker.Clear();
            var selected = 0;
            for (var i = 0; i < document.Boards.Count; i++)
            {
                _boardPicker.AddItem(document.Boards[i].Name);
                if (active != null && document.Boards[i].Id == active.Id)
                {
                    selected = i;
                }
            }
            _boardPicker.Selected = selected;

            _undoButton.Disabled = !NotesRuntime.Commands.CanUndo;
            _redoButton.Disabled = !NotesRuntime.Commands.CanRedo;

            var combat = GameContext.InCombat
                ? ModLocalization.T("status_combat", "In combat")
                : ModLocalization.T("status_no_combat", "Not in combat");
            string hint;
            if (_canvas.LinkMode)
            {
                hint = _canvas.IsLinking
                    ? ModLocalization.T("status_link_source", "Pick a target to finish the branch (Esc to exit)")
                    : ModLocalization.T("status_link_hint", "Link mode: click the source node, then the target");
            }
            else
            {
                hint = ModLocalization.T("status_hint", "");
            }
            _status.Text = combat + "  ·  " + hint;
        }
        finally
        {
            _updating = false;
        }
    }

    private void OnLibraryPressed()
    {
        NotesRuntime.FlushSave();
        NotesRuntime.SetLibrary(NotesRuntime.Library == NotesLibrary.Run
            ? NotesLibrary.Global
            : NotesLibrary.Run);
    }

    private void OnLinkToggled(bool pressed)
    {
        _canvas.SetLinkMode(pressed);
        RefreshHeader();
    }

    private void OnBoardSelected(long index)
    {
        if (_updating)
        {
            return;
        }
        var document = NotesRuntime.ActiveDocument;
        if (index >= 0 && index < document.Boards.Count)
        {
            NotesRuntime.SetActiveBoard(document.Boards[(int)index].Id);
        }
    }

    private void AddTextHere()
    {
        _canvas.AddTextNode(_canvas.ViewCenterInBoardCoords()
            - new Vector2(NodeControl.NodeWidth / 2f, NodeControl.NodeHeight / 2f));
    }

    private void ShowDeleteBoard()
    {
        _deleteConfirm.DialogText = ModLocalization.T("delete_board_text", "Delete the current board?");
        _deleteConfirm.PopupCentered(new Vector2I(380, 140));
    }

    private static Button MakeButton(string text, Action onPressed)
    {
        var button = new Button { Text = text };
        UiStyle.StyleButton(button);
        button.Pressed += onPressed;
        return button;
    }
}
