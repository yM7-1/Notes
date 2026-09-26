using Godot;
using Notes.Game;

namespace Notes.UI;

/// <summary>Right-hand card tray with three sources: current hand, run deck and
/// the full card codex. Every row can be dragged onto the board as a legend.</summary>
public partial class CardPalette : PanelContainer
{
    private enum Tab
    {
        Hand,
        Deck,
        Codex,
    }

    private Tab _tab = Tab.Hand;
    private Button _handTab = null!;
    private Button _deckTab = null!;
    private Button _codexTab = null!;
    private LineEdit _search = null!;
    private HBoxContainer _filters = null!;
    private OptionButton _costFilter = null!;
    private OptionButton _typeFilter = null!;
    private Label _header = null!;
    private VBoxContainer _rows = null!;
    private readonly HashSet<string> _upgradedKeys = new(StringComparer.Ordinal);
    private double _timer;
    private string _signature = "";

    public event Action<CardSnapshot>? CardActivated;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(292, 0);
        AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.PanelBg, UiStyle.PanelBorder, shadow: true));

        var root = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        root.AddThemeConstantOverride("separation", 6);
        AddChild(root);

        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 4);
        root.AddChild(tabs);
        _handTab = MakeTab(ModLocalization.T("palette_tab_hand", "Hand"), Tab.Hand);
        _deckTab = MakeTab(ModLocalization.T("palette_tab_deck", "Deck"), Tab.Deck);
        _codexTab = MakeTab(ModLocalization.T("palette_tab_codex", "Codex"), Tab.Codex);
        tabs.AddChild(_handTab);
        tabs.AddChild(_deckTab);
        tabs.AddChild(_codexTab);

        _search = new LineEdit { PlaceholderText = ModLocalization.T("palette_search", "Search all cards…") };
        UiStyle.StyleInput(_search);
        _search.TextChanged += _ => Refresh(force: true);
        root.AddChild(_search);

        _filters = new HBoxContainer();
        _filters.AddThemeConstantOverride("separation", 4);
        root.AddChild(_filters);
        _costFilter = new OptionButton { FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _costFilter.AddItem(ModLocalization.T("filter_all", "All"));
        _costFilter.AddItem("0");
        _costFilter.AddItem("1");
        _costFilter.AddItem("2");
        _costFilter.AddItem("3+");
        _costFilter.ItemSelected += _ => Refresh(force: true);
        _filters.AddChild(_costFilter);
        _typeFilter = new OptionButton { FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _typeFilter.AddItem(ModLocalization.T("filter_all", "All"));
        _typeFilter.AddItem(ModLocalization.T("type_attack", "Attack"));
        _typeFilter.AddItem(ModLocalization.T("type_skill", "Skill"));
        _typeFilter.AddItem(ModLocalization.T("type_power", "Power"));
        _typeFilter.AddItem(ModLocalization.T("filter_other", "Other"));
        _typeFilter.ItemSelected += _ => Refresh(force: true);
        _filters.AddChild(_typeFilter);

        _header = new Label();
        _header.AddThemeColorOverride("font_color", UiStyle.TextDim);
        root.AddChild(_header);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 160),
        };
        root.AddChild(scroll);
        _rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", 2);
        scroll.AddChild(_rows);

        SelectTab(Tab.Hand);
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree())
        {
            return;
        }
        _timer += delta;
        if (_timer < 0.35)
        {
            return;
        }
        _timer = 0;
        if (_tab != Tab.Codex)
        {
            Refresh(force: false);
        }
    }

    private Button MakeTab(string text, Tab tab)
    {
        var button = new Button { Text = text, ToggleMode = true, FocusMode = FocusModeEnum.None };
        UiStyle.StyleButton(button, fontSize: 11);
        button.Pressed += () => SelectTab(tab);
        return button;
    }

    private void SelectTab(Tab tab)
    {
        _tab = tab;
        _handTab.ButtonPressed = tab == Tab.Hand;
        _deckTab.ButtonPressed = tab == Tab.Deck;
        _codexTab.ButtonPressed = tab == Tab.Codex;
        _search.Visible = tab == Tab.Codex;
        _filters.Visible = tab == Tab.Codex;
        Refresh(force: true);
    }

    private void Refresh(bool force)
    {
        switch (_tab)
        {
            case Tab.Hand:
                RefreshHand(force);
                break;
            case Tab.Deck:
                RefreshDeck(force);
                break;
            default:
                RefreshCodex();
                break;
        }
    }

    private void RefreshHand(bool force)
    {
        var snapshots = GameContext.HandCards.Select(CardCatalog.Snapshot).ToList();
        var signature = "hand:" + string.Join("|", snapshots.Select(s => s.RefId + (s.Upgraded ? "+" : "") + "#" + s.Cost));
        if (!force && signature == _signature)
        {
            return;
        }
        _signature = signature;
        _header.Text = ModLocalization.T("palette_hand", "Hand") + (snapshots.Count > 0 ? $" ({snapshots.Count})" : "");
        ClearRows();
        if (snapshots.Count == 0)
        {
            AddHint(ModLocalization.T("palette_no_combat", "Enter combat to drag cards from your hand"));
        }
        else
        {
            foreach (var snapshot in snapshots)
            {
                _rows.AddChild(CreateRow(snapshot, speculated: false, count: 0, key: null));
            }
        }
    }

    private void RefreshDeck(bool force)
    {
        var cards = GameContext.LocalPlayer?.Deck.Cards;
        var groups = (cards ?? Array.Empty<MegaCrit.Sts2.Core.Models.CardModel>())
            .Where(c => c != null)
            .GroupBy(c => c.Id.ToString() + (c.IsUpgraded ? "+" : ""))
            .Select(g => new { Snapshot = CardCatalog.Snapshot(g.First()), Count = g.Count() })
            .OrderBy(g => g.Snapshot.Cost)
            .ThenBy(g => g.Snapshot.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var signature = "deck:" + string.Join("|", groups.Select(g => g.Snapshot.RefId + (g.Snapshot.Upgraded ? "+" : "") + "x" + g.Count));
        if (!force && signature == _signature)
        {
            return;
        }
        _signature = signature;
        _header.Text = ModLocalization.T("palette_deck", "Deck") + (groups.Count > 0
            ? $" ({groups.Sum(g => g.Count)})"
            : "");
        ClearRows();
        if (groups.Count == 0)
        {
            AddHint(ModLocalization.T("palette_no_deck", "No deck outside a run"));
            return;
        }
        foreach (var group in groups)
        {
            _rows.AddChild(CreateRow(group.Snapshot, speculated: true, count: group.Count, key: null));
        }
    }

    private void RefreshCodex()
    {
        _header.Text = ModLocalization.T("palette_codex", "Codex");
        ClearRows();
        var query = _search.Text?.Trim() ?? "";
        var cost = _costFilter.Selected;
        var type = _typeFilter.Selected;
        var results = CardCatalog.All
            .Where(card => Matches(card, query, CardCatalog.CostOf(card), type))
            .Take(120)
            .ToList();
        if (results.Count == 0)
        {
            AddHint(ModLocalization.T("palette_no_result", "No matching cards"));
            return;
        }
        foreach (var card in results)
        {
            var key = card.Id.ToString();
            var upgraded = _upgradedKeys.Contains(key);
            var snapshot = CardCatalog.Snapshot(card) with { Upgraded = upgraded };
            _rows.AddChild(CreateRow(snapshot, speculated: true, count: 0, key));
        }
    }

    private static bool Matches(MegaCrit.Sts2.Core.Models.CardModel card, string query, int costFilter, int typeFilter)
    {
        if (costFilter > 0)
        {
            var cost = CardCatalog.CostOf(card);
            if (costFilter == 4)
            {
                if (cost < 3)
                {
                    return false;
                }
            }
            else if (cost != costFilter - 1)
            {
                return false;
            }
        }
        if (typeFilter > 0)
        {
            var type = (int)card.Type;
            if (typeFilter == 4)
            {
                if (type is 1 or 2 or 3)
                {
                    return false;
                }
            }
            else if (type != typeFilter)
            {
                return false;
            }
        }
        if (query.Length == 0)
        {
            return true;
        }
        var title = CardCatalog.TitleOf(card);
        var entry = card.Id.Entry ?? "";
        return title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || entry.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private DragCardButton CreateRow(CardSnapshot snapshot, bool speculated, int count, string? key)
    {
        var row = new DragCardButton { Name = "CardRow" };
        row.Setup(snapshot, speculated, count);
        row.Pressed += () => CardActivated?.Invoke(snapshot);
        if (key != null)
        {
            row.RightClicked += () =>
            {
                if (!_upgradedKeys.Remove(key))
                {
                    _upgradedKeys.Add(key);
                }
                RefreshCodex();
            };
        }
        return row;
    }

    private void AddHint(string text)
    {
        var hint = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        hint.AddThemeColorOverride("font_color", UiStyle.TextDim);
        _rows.AddChild(hint);
    }

    private void ClearRows()
    {
        foreach (var child in _rows.GetChildren())
        {
            _rows.RemoveChild(child);
            child.QueueFree();
        }
    }
}

/// <summary>A palette row that can be dragged onto the board as a legend.</summary>
public partial class DragCardButton : Button
{
    private CardSnapshot _snapshot;
    private bool _speculated;
    private int _count;

    public event Action? RightClicked;

    public void Setup(CardSnapshot snapshot, bool speculated, int count)
    {
        _snapshot = snapshot;
        _speculated = speculated;
        _count = count;
        Text = $"{snapshot.CostText} · {snapshot.DisplayTitle}" + (count > 1 ? $" ×{count}" : "");
        TooltipText = snapshot.RefId + (speculated
            ? "\n" + Game.ModLocalization.T("palette_speculated_tip", "Drag adds a speculated legend")
            : "");
        Alignment = HorizontalAlignment.Left;
        CustomMinimumSize = new Vector2(0, 26);
        UiStyle.StyleRow(this);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton button
            && button.ButtonIndex == MouseButton.Right
            && button.Pressed)
        {
            RightClicked?.Invoke();
            AcceptEvent();
            return;
        }
        base._GuiInput(@event);
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
            { "speculated", _speculated },
        };
    }
}
