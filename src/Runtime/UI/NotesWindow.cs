using Godot;
using Notes.Core.Documents;
using Notes.Core.Services;
using Notes.Game;

namespace Notes.UI;

/// <summary>The notes panel: header (library / board / actions) + canvas + card tray.
/// Freely resizable via the bottom-right grip; size is remembered.</summary>
public partial class NotesWindow : Control
{
    private const float MinWindowWidth = 680f;
    private const float MinWindowHeight = 520f;

    private OptionButton _boardPicker = null!;
    private Button _libraryButton = null!;
    private Button _linkButton = null!;
    private Button _undoButton = null!;
    private Button _redoButton = null!;
    private Button _textButton = null!;
    private Button _deleteBoardButton = null!;
    private Label _status = null!;
    private BoardCanvas _canvas = null!;
    private CardPalette _palette = null!;
    private InspectorPanel _inspector = null!;
    private ConfirmationDialog _deleteConfirm = null!;
    private Panel _grip = null!;
    private bool _updating;
    private bool _resizing;
    private bool _sizeApplied;
    private bool _windowDragging;
    private Vector2 _dragOffset;
    private Vector2 _resizeStart;
    private Vector2 _resizeOrigin;

    public override void _Process(double delta)
    {
        if (!_sizeApplied && NotesRuntime.GlobalLoaded)
        {
            _sizeApplied = true;
            if (NotesRuntime.TryGetWindowSize(out var savedW, out var savedH))
            {
                Size = new Vector2(
                    Mathf.Max(savedW, MinWindowWidth),
                    Mathf.Max(savedH, MinWindowHeight));
            }
            Position = ClampPosition(Position);
            SetProcess(false);
        }
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(MinWindowWidth, MinWindowHeight);
        Size = NotesRuntime.TryGetWindowSize(out var savedW, out var savedH)
            ? new Vector2(Mathf.Max(savedW, MinWindowWidth), Mathf.Max(savedH, MinWindowHeight))
            : new Vector2(1000, 640);
        _sizeApplied = NotesRuntime.GlobalLoaded;
        Position = NotesRuntime.TryGetWindowPosition(out var posX, out var posY)
            ? new Vector2(posX, posY)
            : new Vector2(48, 40);
        Position = ClampPosition(Position);
        if (_sizeApplied)
        {
            SetProcess(false);
        }
        ClipContents = true;

        var background = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        background.SetAnchorsPreset(LayoutPreset.FullRect);
        background.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.WindowBg, UiStyle.PanelBorder, 10, 2, shadow: true));
        AddChild(background);

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "margin_left", "margin_top", "margin_right", "margin_bottom" })
        {
            margin.AddThemeConstantOverride(side, 10);
        }
        AddChild(margin);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 8);
        margin.AddChild(root);

        // Row 1: title (drag handle), library switch, board picker, close.
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 6);
        header.GuiInput += OnDragAreaInput;
        root.AddChild(header);

        var title = new Label
        {
            Text = ModLocalization.T("window_title", "Notes"),
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = CursorShape.Drag,
            TooltipText = ModLocalization.T("window_drag_tip", "拖动移动窗口"),
        };
        title.AddThemeFontSizeOverride("font_size", 16);
        title.AddThemeColorOverride("font_color", UiStyle.Accent);
        title.GuiInput += OnDragAreaInput;
        header.AddChild(title);

        _libraryButton = MakeButton("", OnLibraryPressed,
            ModLocalization.T("library_switch_tip", "Switch library"));
        header.AddChild(_libraryButton);

        _boardPicker = new OptionButton
        {
            FocusMode = FocusModeEnum.None,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _boardPicker.ItemSelected += OnBoardSelected;
        header.AddChild(_boardPicker);

        header.AddChild(MakeButton(ModLocalization.T("close", "Close"), Hide));

        // Row 2: actions in a flow container so they wrap instead of clipping
        // when the window is narrow.
        var actions = new HFlowContainer();
        actions.AddThemeConstantOverride("h_separation", 6);
        actions.AddThemeConstantOverride("v_separation", 6);
        root.AddChild(actions);

        actions.AddChild(MakeButton(ModLocalization.T("board_new", "+ Board"),
            () => NotesRuntime.NewBoard(), ModLocalization.T("board_new_tip", "新建自由画板")));
        _deleteBoardButton = MakeButton(ModLocalization.T("board_delete", "Del Board"), ShowDeleteBoard,
            ModLocalization.T("board_delete_tip", "删除当前画板（系统画板不可删）"));
        actions.AddChild(_deleteBoardButton);
        actions.AddChild(MakeButton(ModLocalization.T("world_line_new", "+ World line"),
            () => NotesRuntime.NewWorldLine(), ModLocalization.T("world_line_new_tip", "新建可交互的世界线画板")));
        _textButton = MakeButton(ModLocalization.T("add_text", "+ Text"), AddTextHere,
            ModLocalization.T("add_text_tip", "在视图中心添加文字节点"));
        actions.AddChild(_textButton);
        actions.AddChild(MakeButton(ModLocalization.T("import_turn", "Record turn"),
            () => NotesRuntime.Import(currentTurnOnly: true), ModLocalization.T("quick_record_turn_tip", "把本回合操作录入当前世界线（覆盖该回合）")));
        actions.AddChild(MakeButton(ModLocalization.T("copy_current_line", "Copy → new line"),
            () => NotesRuntime.CopyCurrentToNewLine(), ModLocalization.T("quick_copy_line_tip", "把当前世界线复制成可交互的世界线画板")));
        actions.AddChild(MakeButton(ModLocalization.T("ops_clear", "Clear log"),
            () => NotesRuntime.ClearOps(), ModLocalization.T("ops_clear_tip", "清空本局操作记录")));

        _linkButton = new Button
        {
            Text = ModLocalization.T("link_mode", "Link"),
            ToggleMode = true,
            TooltipText = ModLocalization.T("link_mode_tip", "Link mode"),
        };
        UiStyle.StyleButton(_linkButton, accent: true);
        _linkButton.Toggled += OnLinkToggled;
        actions.AddChild(_linkButton);

        _undoButton = MakeButton(ModLocalization.T("undo", "Undo"), () =>
        {
            NotesRuntime.Commands.Undo();
            NotesRuntime.Raise();
        }, ModLocalization.T("undo_tip", "撤销 (Ctrl+Z)"));
        actions.AddChild(_undoButton);

        _redoButton = MakeButton(ModLocalization.T("redo", "Redo"), () =>
        {
            NotesRuntime.Commands.Redo();
            NotesRuntime.Raise();
        }, ModLocalization.T("redo_tip", "重做 (Ctrl+Y)"));
        actions.AddChild(_redoButton);

        actions.AddChild(MakeButton(ModLocalization.T("reset_view", "Reset"),
            () => _canvas.ResetView(), ModLocalization.T("reset_view_tip", "重置缩放与平移")));

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 8);
        root.AddChild(body);

        _canvas = new BoardCanvas
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(320, 240),
        };
        _canvas.SetBackdrop(UiStyle.CanvasBg);
        body.AddChild(_canvas);

        _palette = new CardPalette { SizeFlagsVertical = SizeFlags.ExpandFill };
        _palette.CardActivated += snapshot =>
            _canvas.AddCardNode(snapshot, _canvas.ViewCenterInBoardCoords()
                - new Vector2(NodeControl.NodeWidth / 2f, NodeControl.NodeHeight / 2f));

        var right = new VBoxContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(240, 0),
        };
        right.AddThemeConstantOverride("separation", 6);
        right.AddChild(_palette);
        _inspector = new InspectorPanel();
        right.AddChild(_inspector);
        body.AddChild(right);

        _status = new Label
        {
            Text = ModLocalization.T("status_hint", ""),
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            ClipText = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _status.AddThemeFontSizeOverride("font_size", 11);
        _status.AddThemeColorOverride("font_color", UiStyle.TextDim);
        root.AddChild(_status);

        _grip = new Panel
        {
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = CursorShape.Fdiagsize,
            TooltipText = ModLocalization.T("window_resize_tip", "Drag to resize"),
        };
        _grip.SetAnchorsPreset(LayoutPreset.BottomRight);
        _grip.OffsetLeft = -20;
        _grip.OffsetTop = -20;
        _grip.OffsetRight = -3;
        _grip.OffsetBottom = -3;
        _grip.AddThemeStyleboxOverride("panel",
            UiStyle.Box(Color.FromHtml("2a303b"), UiStyle.PanelBorder, 4, 1));
        _grip.GuiInput += OnGripInput;
        AddChild(_grip);

        _deleteConfirm = new ConfirmationDialog
        {
            Title = ModLocalization.T("board_delete", "Del Board"),
            DialogText = ModLocalization.T("delete_board_text", "Delete the current board?"),
        };
        UiStyle.StyleDialog(_deleteConfirm);
        _deleteConfirm.Confirmed += () =>
        {
            var board = NotesRuntime.ActiveBoard;
            NotesRuntime.DeleteBoard(board.Id);
        };
        AddChild(_deleteConfirm);

        NotesRuntime.Changed += RefreshHeader;
        NotesRuntime.OpsChanged += RefreshHeader;
        NotesRuntime.Changed += RefreshInspector;
        NotesRuntime.SelectionChanged += RefreshInspector;
        RefreshHeader();
        RefreshInspector();
    }

    /// <summary>Drag the window by its title / empty header space; the position
    /// is clamped to the viewport and remembered.</summary>
    private void OnDragAreaInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton button && button.ButtonIndex == MouseButton.Left)
        {
            if (button.Pressed)
            {
                _windowDragging = true;
                _dragOffset = GetGlobalMousePosition() - Position;
            }
            else if (_windowDragging)
            {
                _windowDragging = false;
                NotesRuntime.SaveWindowPosition(Position.X, Position.Y);
            }
            AcceptEvent();
            return;
        }
        if (@event is InputEventMouseMotion && _windowDragging)
        {
            Position = ClampPosition(GetGlobalMousePosition() - _dragOffset);
            AcceptEvent();
        }
    }

    private Vector2 ClampPosition(Vector2 position)
    {
        var viewport = GetViewportRect().Size;
        return new Vector2(
            Mathf.Clamp(position.X, 0f, Mathf.Max(0f, viewport.X - Size.X)),
            Mathf.Clamp(position.Y, 0f, Mathf.Max(0f, viewport.Y - Size.Y)));
    }

    private void OnGripInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton button && button.ButtonIndex == MouseButton.Left)
        {
            _resizing = button.Pressed;
            if (button.Pressed)
            {
                _resizeStart = GetGlobalMousePosition();
                _resizeOrigin = Size;
            }
            else
            {
                _sizeApplied = true;
                SetProcess(false);
                NotesRuntime.SaveWindowSize(Size.X, Size.Y);
            }
            AcceptEvent();
            return;
        }
        if (@event is InputEventMouseMotion && _resizing)
        {
            var delta = GetGlobalMousePosition() - _resizeStart;
            var viewport = GetViewportRect().Size;
            var target = new Vector2(
                Mathf.Clamp(_resizeOrigin.X + delta.X, MinWindowWidth,
                    Mathf.Max(MinWindowWidth, viewport.X - Position.X - 8f)),
                Mathf.Clamp(_resizeOrigin.Y + delta.Y, MinWindowHeight,
                    Mathf.Max(MinWindowHeight, viewport.Y - Position.Y - 8f)));
            Size = target;
            AcceptEvent();
        }
    }

    public override void _ExitTree()
    {
        NotesRuntime.Changed -= RefreshHeader;
        NotesRuntime.OpsChanged -= RefreshHeader;
        NotesRuntime.Changed -= RefreshInspector;
        NotesRuntime.SelectionChanged -= RefreshInspector;
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
        if (key.Keycode == Key.Escape)
        {
            if (_canvas.LinkMode)
            {
                _linkButton.ButtonPressed = false;
            }
            else
            {
                Hide();
            }
            GetViewport().SetInputAsHandled();
            return;
        }
        var ctrl = key.CtrlPressed || key.MetaPressed;
        if (ctrl && key.Keycode == Key.Z)
        {
            if (NotesRuntime.ActiveBoard.IsReadOnly)
            {
                return;
            }
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
            if (NotesRuntime.ActiveBoard.IsReadOnly)
            {
                return;
            }
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

            var readOnly = active?.IsReadOnly ?? false;
            var systemBoard = active?.Kind is BoardKind.Overview or BoardKind.Current;
            _undoButton.Disabled = readOnly || !NotesRuntime.Commands.CanUndo;
            _redoButton.Disabled = readOnly || !NotesRuntime.Commands.CanRedo;
            _textButton.Disabled = readOnly;
            _deleteBoardButton.Disabled = systemBoard;
            _linkButton.Disabled = readOnly;
            if (readOnly && _linkButton.ButtonPressed)
            {
                _linkButton.ButtonPressed = false;
            }

            var combat = GameContext.InCombat
                ? ModLocalization.T("status_combat", "In combat")
                : ModLocalization.T("status_no_combat", "Not in combat");
            string hint;
            if (readOnly)
            {
                hint = ModLocalization.T("status_readonly",
                    "当前世界线为自动记录（只读）：用「复制→新世界线」创建可交互画板");
            }
            else if (_canvas.LinkMode)
            {
                hint = _canvas.IsLinking
                    ? ModLocalization.T("status_link_source", "Pick a target to finish the branch (Esc to exit)")
                    : ModLocalization.T("status_link_hint", "Link mode: click the source node, then the target");
            }
            else
            {
                hint = ModLocalization.T("status_hint", "");
            }
            var import = NotesRuntime.LastImportMessage;
            _status.Text = combat
                + "  ·  " + ModLocalization.T("status_ops", "Ops") + " " + NotesRuntime.OpsCount
                + (import.Length > 0 ? "  ·  " + import : "")
                + "  ·  " + hint;
            _status.TooltipText = _status.Text;
        }
        finally
        {
            _updating = false;
        }
    }

    private void RefreshInspector()
    {
        if (_inspector == null)
        {
            return;
        }
        try
        {
            var board = NotesRuntime.ActiveBoard;
            if (NotesRuntime.HoverNodeId.Length > 0 && board.FindNode(NotesRuntime.HoverNodeId) is { } hoverNode)
            {
                ShowNodeDetail(hoverNode);
                return;
            }
            switch (NotesRuntime.SelectionKind)
            {
                case NotesSelectionKind.Node:
                    if (board.FindNode(NotesRuntime.SelectionId) is { } node)
                    {
                        ShowNodeDetail(node);
                    }
                    break;

                case NotesSelectionKind.Region:
                {
                    var region = board.FindRegion(NotesRuntime.SelectionId);
                    if (region == null)
                    {
                        return;
                    }
                    var line = board.FindWorldLine(region.WorldLineId);
                    var lines = new List<string>
                    {
                        ModLocalization.T("region_turn", "回合") + " " + region.TurnNumber
                            + (line != null ? "  ·  " + line.Name : ""),
                        ModLocalization.T("inspector_ops", "Ops") + ": " + board.NodesOfRegion(region.Id).Count(),
                    };
                    if (region.TurnEvents.Count > 0)
                    {
                        lines.Add(ModLocalization.T("inspector_turn_events", "Turn events") + ":");
                        lines.AddRange(region.TurnEvents.Select(e => "• " + UiStyle.AnnotationText(e)));
                    }
                    lines.Add("");
                    lines.Add(ModLocalization.T("inspector_state_end", "State at end of turn") + ":");
                    lines.Add(region.Snapshot.Length > 0
                        ? region.Snapshot
                        : ModLocalization.T("inspector_no_snapshot", "(no snapshot — import this turn first)"));
                    _inspector.Show(ModLocalization.T("inspector_region", "Turn region"), string.Join("\n", lines));
                    break;
                }

                case NotesSelectionKind.WorldLine:
                {
                    var line = board.FindWorldLine(NotesRuntime.SelectionId);
                    if (line == null)
                    {
                        return;
                    }
                    var stats = NotesStats.Compute(board, line.Id);
                    var lines = new List<string>
                    {
                        ModLocalization.T("inspector_turns", "Turns") + ": " + stats.TurnCount
                            + "   " + ModLocalization.T("inspector_nodes", "Nodes") + ": " + stats.NodeCount,
                        ModLocalization.T("inspector_branches", "Branches") + ": " + stats.BranchCount
                            + "   " + ModLocalization.T("inspector_surviving", "Survived turn end") + ": " + stats.SurvivingBranches,
                        ModLocalization.T("inspector_marks", "Marks") + ": ✔" + stats.TriedCount
                            + " ?" + stats.SpeculatedCount + " ★" + stats.ConfirmedCount,
                        ModLocalization.T("inspector_potions", "Potions used") + ": " + stats.PotionCount
                            + (stats.Potions.Count > 0 ? " (" + string.Join(", ", stats.Potions) + ")" : ""),
                        ModLocalization.T("inspector_damage", "Damage taken") + ": " + stats.DamageTaken,
                    };
                    if (stats.HpMin >= 0)
                    {
                        lines.Add(ModLocalization.T("inspector_hp_range", "HP range") + ": " + stats.HpMin + " ~ " + stats.HpMax);
                    }
                    if (board.WorldLines.Count > 0 && board.WorldLines[0].Id == line.Id
                        && GameContext.LocalPlayer is { } player)
                    {
                        lines.Add(ModLocalization.T("inspector_live", "Live HP") + ": "
                            + player.Creature.CurrentHp + "/" + player.Creature.MaxHp);
                    }
                    _inspector.Show(ModLocalization.T("inspector_world_line", "World line overview"), string.Join("\n", lines));
                    break;
                }

                default:
                    _inspector.Show(ModLocalization.T("inspector_title", "Detail"),
                        ModLocalization.T("inspector_none", "Select a step, turn region or world line"));
                    break;
            }
        }
        catch
        {
            // inspector is informational only
        }
    }

    /// <summary>Step detail: annotations first (e.g. 「A」、「B」被消耗), then the
    /// captured state snapshot and the user note.</summary>
    private void ShowNodeDetail(NotesNode node)
    {
        var lines = new List<string>
        {
            KindLabel(node.Kind) + " · " + node.Title + (node.Upgraded ? "+" : "")
                + (node.State != NodeState.None ? "   " + StateLabel(node.State) : ""),
        };
        if (node.Kind == NodeKind.Card && node.Cost >= 0)
        {
            lines.Add(ModLocalization.T("inspector_cost", "Cost") + ": " + node.Cost);
        }
        foreach (var annotation in node.Annotations)
        {
            lines.Add("▶ " + UiStyle.AnnotationText(annotation));
        }
        if (node.Meta.Length > 0 && node.Kind is NodeKind.Draw or NodeKind.Discard)
        {
            lines.Add("▶ " + ModLocalization.T("inspector_cards", "牌") + ": " + node.Meta);
        }
        if (node.Snapshot.Length > 0)
        {
            lines.Add("");
            lines.Add(ModLocalization.T("inspector_state", "State at this step") + ":");
            lines.Add(node.Snapshot);
        }
        if (!string.IsNullOrWhiteSpace(node.Note))
        {
            lines.Add("");
            lines.Add(node.Note);
        }
        _inspector.Show(ModLocalization.T("inspector_node", "Step detail"), string.Join("\n", lines));
    }

    private static string KindLabel(NodeKind kind) => kind switch
    {
        NodeKind.Card => ModLocalization.T("palette_tab_hand", "Card"),
        NodeKind.Potion => ModLocalization.T("op_potion", "Potion"),
        NodeKind.Relic => ModLocalization.T("op_relic", "Relic"),
        NodeKind.Draw => ModLocalization.T("op_draw", "Draw"),
        NodeKind.Discard => ModLocalization.T("op_discard", "Discard"),
        NodeKind.EndTurn => ModLocalization.T("op_end_turn", "End turn"),
        _ => ModLocalization.T("op_text", "Text"),
    };

    private static string StateLabel(NodeState state) => state switch
    {
        NodeState.Tried => ModLocalization.T("node_state_tried", "Tried"),
        NodeState.Speculated => ModLocalization.T("node_state_speculated", "Speculated"),
        NodeState.Confirmed => ModLocalization.T("node_state_confirmed", "Confirmed"),
        _ => "",
    };

    private void OnLibraryPressed()
    {        NotesRuntime.FlushSave();
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

    private static Button MakeButton(string text, Action onPressed, string? tooltip = null)
    {
        var button = new Button { Text = text };
        UiStyle.StyleButton(button);
        if (!string.IsNullOrEmpty(tooltip))
        {
            button.TooltipText = tooltip;
        }
        button.Pressed += onPressed;
        return button;
    }
}
