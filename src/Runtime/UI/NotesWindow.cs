using Godot;
using Notes.Core.Documents;
using Notes.Core.Services;
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
    private InspectorPanel _inspector = null!;
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
        header.AddChild(MakeButton(ModLocalization.T("world_line_new", "+ World line"), () => NotesRuntime.NewWorldLine()));
        header.AddChild(MakeButton(ModLocalization.T("add_text", "+ Text"), AddTextHere));
        header.AddChild(MakeButton(ModLocalization.T("import_turn", "Record turn"), () => NotesRuntime.Import(currentTurnOnly: true)));
        header.AddChild(MakeButton(ModLocalization.T("import_all", "Record combat"), () => NotesRuntime.Import(currentTurnOnly: false)));
        header.AddChild(MakeButton(ModLocalization.T("ops_clear", "Clear log"), () => NotesRuntime.ClearOps()));

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

        var right = new VBoxContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(292, 0),
        };
        right.AddThemeConstantOverride("separation", 6);
        right.AddChild(_palette);
        _inspector = new InspectorPanel();
        right.AddChild(_inspector);
        body.AddChild(right);

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
        NotesRuntime.OpsChanged += RefreshHeader;
        NotesRuntime.Changed += RefreshInspector;
        NotesRuntime.SelectionChanged += RefreshInspector;
        RefreshHeader();
        RefreshInspector();
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
            var import = NotesRuntime.LastImportMessage;
            _status.Text = combat
                + "  ·  " + ModLocalization.T("status_ops", "Ops") + " " + NotesRuntime.OpsCount
                + (import.Length > 0 ? "  ·  " + import : "")
                + "  ·  " + hint;
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

    private static Button MakeButton(string text, Action onPressed)
    {
        var button = new Button { Text = text };
        UiStyle.StyleButton(button);
        button.Pressed += onPressed;
        return button;
    }
}
