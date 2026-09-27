using Godot;
using Notes.Core.Documents;
using Notes.Game;

namespace Notes.UI;

/// <summary>NotesWindow partial: the Ctrl+K command palette. One input filters
/// both commands and note hits, so every feature has a single discoverable
/// entry point without crowding the toolbar.</summary>
public partial class NotesWindow
{
    private const int CommandNoteLimit = 8;

    private PopupPanel _commandPopup = null!;
    private LineEdit _commandInput = null!;
    private ItemList _commandRows = null!;
    private readonly List<(string Label, Action Run)> _commandHits = new();

    private void BuildCommands()
    {
        _commandPopup = new PopupPanel { Name = "CommandPopup" };
        _commandPopup.AddThemeStyleboxOverride("panel",
            UiStyle.Box(UiStyle.PanelBg, UiStyle.PanelBorder, 8, 1, shadow: true));

        var panel = new VBoxContainer { CustomMinimumSize = new Vector2(440, 340) };
        panel.AddThemeConstantOverride("separation", 6);
        _commandPopup.AddChild(panel);

        _commandInput = new LineEdit
        {
            PlaceholderText = ModLocalization.T("command_placeholder", "输入命令或搜索笔记（Ctrl+K）"),
        };
        UiStyle.StyleInput(_commandInput);
        _commandInput.TextChanged += _ => RunCommands();
        _commandInput.TextSubmitted += _ => ActivateCommand(0);
        panel.AddChild(_commandInput);

        _commandRows = new ItemList
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 260),
        };
        _commandRows.ItemActivated += ActivateCommand;
        panel.AddChild(_commandRows);

        AddChild(_commandPopup);
    }

    private void OpenCommands()
    {
        _commandInput.Text = "";
        _commandRows.Clear();
        _commandHits.Clear();
        _commandPopup.PopupCentered(new Vector2I(460, 380));
        _commandInput.GrabFocus();
        RunCommands();
    }

    private void RunCommands()
    {
        _commandRows.Clear();
        _commandHits.Clear();
        var query = _commandInput.Text?.Trim() ?? "";
        foreach (var (label, run) in Commands())
        {
            if (query.Length == 0 || label.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            {
                _commandHits.Add((label, run));
                _commandRows.AddItem(label);
            }
        }
        foreach (var (label, run) in NoteHits(query))
        {
            _commandHits.Add((label, run));
            _commandRows.AddItem(label);
        }
    }

    private IEnumerable<(string Label, Action Run)> Commands()
    {
        yield return (ModLocalization.T("copy_current_line", "复制世界线"), () => NotesRuntime.CopyCurrentToNewLine());
        yield return (ModLocalization.T("import_turn", "记本回合"), () => NotesRuntime.Import(currentTurnOnly: true));
        yield return (ModLocalization.T("board_new", "新建画板"), () => NotesRuntime.NewBoard());
        yield return (ModLocalization.T("world_line_new", "新建世界线"), () => NotesRuntime.NewWorldLine());
        yield return (ModLocalization.T("add_text", "添加文本"), AddTextHere);
        yield return (ModLocalization.T("undo", "撤销"), () =>
        {
            NotesRuntime.Commands.Undo();
            NotesRuntime.Raise();
        });
        yield return (ModLocalization.T("redo", "重做"), () =>
        {
            NotesRuntime.Commands.Redo();
            NotesRuntime.Raise();
        });
        yield return (ModLocalization.T("notes_compare", "对比"), () => OpenCompare());
        yield return (ModLocalization.T("arrange_button", "整理"), () => NotesRuntime.ArrangeBoard());
        yield return (ModLocalization.T("export_button", "导出"), ExportBoard);
        yield return (ModLocalization.T("settings_button", "设置"), OpenSettings);
        yield return (ModLocalization.T("notes_find", "搜索笔记"), OpenSearch);
        yield return (ModLocalization.T("help_title", "帮助"), OpenHelp);
        yield return (ModLocalization.T("reset_view", "复位视图"), () => _canvas.ResetView());
        yield return (ModLocalization.T("ops_clear", "清空记录"), () => NotesRuntime.ClearOps());
    }

    private IEnumerable<(string Label, Action Run)> NoteHits(string query)
    {
        if (query.Length == 0)
        {
            yield break;
        }
        var document = NotesRuntime.ActiveDocument;
        var count = 0;
        foreach (var board in document.Boards)
        {
            foreach (var node in board.Nodes)
            {
                if (!node.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                    && !node.Note.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                {
                    continue;
                }
                var nodeId = node.Id;
                var boardId = board.Id;
                yield return (ModLocalization.T("command_note_prefix", "笔记") + ": " + board.Name + " · " + node.Title,
                    () =>
                    {
                        NotesRuntime.SetActiveBoard(boardId);
                        NotesRuntime.SelectNode(nodeId);
                        _canvas.FocusNode(nodeId);
                    });
                if (++count >= CommandNoteLimit)
                {
                    yield break;
                }
            }
        }
    }

    private void ActivateCommand(long index)
    {
        if (index < 0 || index >= _commandHits.Count)
        {
            return;
        }
        var run = _commandHits[(int)index].Run;
        _commandPopup.Hide();
        run();
    }
}
