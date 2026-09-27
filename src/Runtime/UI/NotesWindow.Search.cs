using Godot;
using Notes.Core.Documents;
using Notes.Game;

namespace Notes.UI;

/// <summary>NotesWindow partial: cross-board node search (toolbar "Find" / Ctrl+F).
/// Selecting a hit switches to its board, selects the node and centers the view.</summary>
public partial class NotesWindow
{
    private const int SearchResultLimit = 60;

    private PopupPanel _searchPopup = null!;
    private LineEdit _searchInput = null!;
    private ItemList _searchResults = null!;
    private Label _searchHint = null!;
    private readonly List<SearchHit> _searchHits = new();

    private readonly record struct SearchHit(string BoardId, string NodeId);

    private void BuildSearch()
    {
        _searchPopup = new PopupPanel { Name = "SearchPopup" };
        _searchPopup.AddThemeStyleboxOverride("panel",
            UiStyle.Box(UiStyle.PanelBg, UiStyle.PanelBorder, 8, 1, shadow: true));

        var panel = new VBoxContainer { CustomMinimumSize = new Vector2(420, 360) };
        panel.AddThemeConstantOverride("separation", 6);
        _searchPopup.AddChild(panel);

        _searchInput = new LineEdit
        {
            PlaceholderText = ModLocalization.T("notes_search_placeholder", "搜索笔记：标题 / 备注 / 卡 id"),
        };
        UiStyle.StyleInput(_searchInput);
        _searchInput.TextChanged += _ => RunSearch();
        _searchInput.TextSubmitted += _ => ActivateFirstResult();
        panel.AddChild(_searchInput);

        _searchResults = new ItemList
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 280),
        };
        _searchResults.ItemActivated += ActivateResult;
        panel.AddChild(_searchResults);

        _searchHint = new Label();
        _searchHint.AddThemeFontSizeOverride("font_size", 10);
        _searchHint.AddThemeColorOverride("font_color", UiStyle.TextDim);
        panel.AddChild(_searchHint);

        AddChild(_searchPopup);
    }

    private void OpenSearch()
    {
        _searchInput.Text = "";
        _searchResults.Clear();
        _searchHits.Clear();
        _searchPopup.PopupCentered(new Vector2I(440, 400));
        _searchInput.GrabFocus();
        _searchInput.SelectAll();
        RunSearch();
    }

    private void RunSearch()
    {
        _searchHits.Clear();
        _searchResults.Clear();
        var query = _searchInput.Text?.Trim() ?? "";
        if (query.Length == 0)
        {
            _searchHint.Text = ModLocalization.T("notes_search_empty", "输入关键词搜索当前库的节点");
            return;
        }
        var activeBoardId = NotesRuntime.ActiveBoard.Id;
        foreach (var board in NotesRuntime.ActiveDocument.Boards)
        {
            foreach (var node in board.Nodes)
            {
                if (!Matches(node, query))
                {
                    continue;
                }
                var prefix = board.Id == activeBoardId ? "" : board.Name + " · ";
                _searchResults.AddItem(prefix + TitleOf(node));
                _searchHits.Add(new SearchHit(board.Id, node.Id));
                if (_searchHits.Count >= SearchResultLimit)
                {
                    break;
                }
            }
            if (_searchHits.Count >= SearchResultLimit)
            {
                break;
            }
        }
        _searchHint.Text = _searchHits.Count == 0
            ? ModLocalization.T("notes_search_none", "没有匹配的节点")
            : ModLocalization.T("notes_search_count", "匹配") + ": " + _searchHits.Count
                + "  ·  " + ModLocalization.T("notes_search_enter", "Enter 打开第一条");
    }

    private static bool Matches(NotesNode node, string query) =>
        node.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || node.Note.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || node.RefId.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static string TitleOf(NotesNode node) =>
        node.Title.Length > 0 ? node.Title : ModLocalization.T("op_text", "Text");

    private void ActivateFirstResult()
    {
        if (_searchHits.Count > 0)
        {
            ActivateResult(0);
        }
    }

    private void ActivateResult(long index)
    {
        if (index < 0 || index >= _searchHits.Count)
        {
            return;
        }
        var hit = _searchHits[(int)index];
        NotesRuntime.SetActiveBoard(hit.BoardId);
        NotesRuntime.SelectNode(hit.NodeId);
        _canvas.FocusNode(hit.NodeId);
        _searchPopup.Hide();
    }
}
