using Godot;
using Notes.Game;

namespace Notes.UI;

/// <summary>Right-hand card tray: current hand (drag source) + full card search.</summary>
public partial class CardPalette : PanelContainer
{
    private LineEdit _search = null!;
    private Label _handHeader = null!;
    private VBoxContainer _handRows = null!;
    private Label _resultHeader = null!;
    private VBoxContainer _resultRows = null!;
    private double _timer;
    private string _handSignature = "";

    /// <summary>Click-to-add fallback for players who prefer not to drag.</summary>
    public event Action<CardSnapshot>? CardActivated;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(272, 0);
        AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.PanelBg, UiStyle.PanelBorder));

        var root = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        root.AddThemeConstantOverride("separation", 6);
        AddChild(root);

        var title = new Label { Text = ModLocalization.T("palette_title", "Cards") };
        title.AddThemeFontSizeOverride("font_size", 14);
        root.AddChild(title);

        _search = new LineEdit { PlaceholderText = ModLocalization.T("palette_search", "Search all cards…") };
        _search.TextChanged += _ => RefreshResults();
        root.AddChild(_search);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 360),
        };
        root.AddChild(scroll);

        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(list);

        _handHeader = new Label();
        _handHeader.AddThemeColorOverride("font_color", UiStyle.TextDim);
        list.AddChild(_handHeader);

        _handRows = new VBoxContainer();
        _handRows.AddThemeConstantOverride("separation", 2);
        list.AddChild(_handRows);

        _resultHeader = new Label { Text = ModLocalization.T("palette_hint_search", "Type to search all cards") };
        _resultHeader.AddThemeColorOverride("font_color", UiStyle.TextDim);
        list.AddChild(_resultHeader);

        _resultRows = new VBoxContainer();
        _resultRows.AddThemeConstantOverride("separation", 2);
        list.AddChild(_resultRows);

        RefreshHand(force: true);
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree())
        {
            return;
        }
        _timer += delta;
        if (_timer < 0.3)
        {
            return;
        }
        _timer = 0;
        RefreshHand(force: false);
    }

    private void RefreshHand(bool force)
    {
        var snapshots = new List<CardSnapshot>();
        foreach (var card in GameContext.HandCards)
        {
            snapshots.Add(CardCatalog.Snapshot(card));
        }
        var signature = string.Join("|", snapshots.Select(s => s.RefId + (s.Upgraded ? "+" : "") + "#" + s.Cost));
        if (!force && signature == _handSignature)
        {
            return;
        }
        _handSignature = signature;

        _handHeader.Text = ModLocalization.T("palette_hand", "Hand") + (snapshots.Count > 0 ? $" ({snapshots.Count})" : "");
        ClearRows(_handRows);
        if (snapshots.Count == 0)
        {
            var hint = new Label
            {
                Text = ModLocalization.T("palette_no_combat", "Enter combat to drag cards from your hand"),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            hint.AddThemeColorOverride("font_color", UiStyle.TextDim);
            _handRows.AddChild(hint);
        }
        else
        {
            foreach (var snapshot in snapshots)
            {
                _handRows.AddChild(CreateRow(snapshot));
            }
        }
    }

    private void RefreshResults()
    {
        ClearRows(_resultRows);
        var query = _search.Text;
        if (string.IsNullOrWhiteSpace(query))
        {
            _resultHeader.Text = ModLocalization.T("palette_hint_search", "Type to search all cards");
            return;
        }
        var results = CardCatalog.Search(query).ToList();
        _resultHeader.Text = results.Count > 0
            ? ModLocalization.T("palette_title", "Cards") + $" ({results.Count})"
            : ModLocalization.T("palette_no_result", "No matching cards");
        foreach (var snapshot in results)
        {
            _resultRows.AddChild(CreateRow(snapshot));
        }
    }

    private DragCardButton CreateRow(CardSnapshot snapshot)
    {
        var row = new DragCardButton { Name = "CardRow" };
        row.Setup(snapshot);
        row.Pressed += () => CardActivated?.Invoke(snapshot);
        return row;
    }

    private static void ClearRows(Node container)
    {
        foreach (var child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }
}

/// <summary>A palette row that can be dragged onto the board as a card legend.</summary>
public partial class DragCardButton : Button
{
    private CardSnapshot _snapshot;

    public void Setup(CardSnapshot snapshot)
    {
        _snapshot = snapshot;
        Text = $"{snapshot.CostText} · {snapshot.DisplayTitle}";
        TooltipText = snapshot.RefId;
        Alignment = HorizontalAlignment.Left;
        FocusMode = FocusModeEnum.None;
        CustomMinimumSize = new Vector2(0, 26);
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        var preview = new Label { Text = Text };
        SetDragPreview(preview);
        return new Godot.Collections.Dictionary
        {
            { "kind", "card" },
            { "refId", _snapshot.RefId },
            { "title", _snapshot.Title },
            { "cost", _snapshot.Cost },
            { "type", _snapshot.CardType },
            { "rarity", _snapshot.Rarity },
            { "upgraded", _snapshot.Upgraded },
        };
    }
}
