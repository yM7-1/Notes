using Godot;
using Notes.Core.Documents;
using Notes.Core.Services;
using Notes.Game;

namespace Notes.UI;

/// <summary>NotesWindow partial: the world-line compare popup. Two lines of the
/// active run are aligned turn by turn; the first divergence is highlighted.</summary>
public partial class NotesWindow
{
    private PopupPanel _comparePopup = null!;
    private OptionButton _compareLeft = null!;
    private OptionButton _compareRight = null!;
    private ItemList _compareRows = null!;
    private Label _compareSummary = null!;
    private bool _compareUpdating;
    private readonly List<(string BoardId, string LineId)> _compareLines = new();

    private void BuildCompare()
    {
        _comparePopup = new PopupPanel { Name = "ComparePopup" };
        _comparePopup.AddThemeStyleboxOverride("panel",
            UiStyle.Box(UiStyle.PanelBg, UiStyle.PanelBorder, 8, 1, shadow: true));

        var panel = new VBoxContainer { CustomMinimumSize = new Vector2(460, 380) };
        panel.AddThemeConstantOverride("separation", 8);
        _comparePopup.AddChild(panel);

        var title = new Label { Text = ModLocalization.T("compare_title", "世界线对比") };
        title.AddThemeFontSizeOverride("font_size", 15);
        title.AddThemeColorOverride("font_color", UiStyle.Accent);
        panel.AddChild(title);

        var pickers = new HBoxContainer();
        pickers.AddThemeConstantOverride("separation", 6);
        panel.AddChild(pickers);
        _compareLeft = MakeComparePicker(pickers, ModLocalization.T("compare_left", "左"));
        _compareRight = MakeComparePicker(pickers, ModLocalization.T("compare_right", "右"));

        _compareRows = new ItemList
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 260),
        };
        panel.AddChild(_compareRows);

        _compareSummary = new Label();
        _compareSummary.AddThemeFontSizeOverride("font_size", 11);
        _compareSummary.AddThemeColorOverride("font_color", UiStyle.TextDim);
        panel.AddChild(_compareSummary);

        AddChild(_comparePopup);
    }

    private OptionButton MakeComparePicker(HBoxContainer parent, string label)
    {
        var text = new Label { Text = label };
        text.AddThemeFontSizeOverride("font_size", 11);
        parent.AddChild(text);
        var picker = new OptionButton { FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        UiStyle.StyleOptionButton(picker, fontSize: 11);
        picker.ItemSelected += _ =>
        {
            if (!_compareUpdating)
            {
                RebuildCompare();
            }
        };
        parent.AddChild(picker);
        return picker;
    }

    private void OpenCompare()
    {
        _compareLines.Clear();
        var document = NotesRuntime.ActiveDocument;
        foreach (var board in document.Boards)
        {
            if (board.WorldLines.Count == 0)
            {
                continue;
            }
            _compareLines.Add((board.Id, board.WorldLines[0].Id));
        }
        if (_compareLines.Count < 2)
        {
            NotesRuntime.SetImportMessage(ModLocalization.T("compare_need_two",
                "至少需要两条世界线（复制→新世界线）才能对比"));
            return;
        }
        _compareUpdating = true;
        try
        {
            _compareLeft.Clear();
            _compareRight.Clear();
            foreach (var (boardId, _) in _compareLines)
            {
                var name = document.FindBoard(boardId)?.Name ?? "";
                _compareLeft.AddItem(name);
                _compareRight.AddItem(name);
            }
            var activeIndex = _compareLines.FindIndex(l => l.BoardId == NotesRuntime.ActiveBoard.Id);
            _compareLeft.Selected = 0;
            _compareRight.Selected = activeIndex > 0 ? activeIndex : 1;
            if (_compareRight.Selected == _compareLeft.Selected)
            {
                _compareRight.Selected = 1;
            }
        }
        finally
        {
            _compareUpdating = false;
        }
        RebuildCompare();
        _comparePopup.PopupCentered(new Vector2I(480, 420));
    }

    private void RebuildCompare()
    {
        _compareRows.Clear();
        var leftIndex = (int)_compareLeft.Selected;
        var rightIndex = (int)_compareRight.Selected;
        if (leftIndex < 0 || rightIndex < 0
            || leftIndex >= _compareLines.Count || rightIndex >= _compareLines.Count)
        {
            return;
        }
        var document = NotesRuntime.ActiveDocument;
        var left = document.FindBoard(_compareLines[leftIndex].BoardId);
        var right = document.FindBoard(_compareLines[rightIndex].BoardId);
        if (left == null || right == null)
        {
            return;
        }
        var comparison = NotesCompare.Compare(
            left, _compareLines[leftIndex].LineId,
            right, _compareLines[rightIndex].LineId);
        var diffMark = ModLocalization.T("compare_diff_mark", "分叉");
        foreach (var turn in comparison.Turns)
        {
            var text = "T" + turn.Turn
                + "   " + Cell(turn.LeftNodes, turn.LeftHp, turn.LeftDamage)
                + "  ⇄  " + Cell(turn.RightNodes, turn.RightHp, turn.RightDamage)
                + (turn.Diverges ? "   ← " + diffMark : "");
            _compareRows.AddItem(text);
        }
        _compareSummary.Text = (comparison.FirstDivergingTurn == 0
            ? ModLocalization.T("compare_same", "两条世界线完全一致")
            : ModLocalization.T("compare_first_diff", "首个分叉：第 {0} 回合")
                .Replace("{0}", comparison.FirstDivergingTurn.ToString()))
            + "  ·  " + ModLocalization.T("compare_turns", "回合数")
            + " " + comparison.LeftTurnCount + " / " + comparison.RightTurnCount;
    }

    private static string Cell(int nodes, int hp, int damage)
    {
        var hpText = hp >= 0 ? hp.ToString() : "-";
        return ModLocalization.T("compare_cell", "{0}节点 · HP{1} · 伤害{2}")
            .Replace("{0}", nodes.ToString())
            .Replace("{1}", hpText)
            .Replace("{2}", damage.ToString());
    }
}
