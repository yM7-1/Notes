namespace Notes.Core.Documents;

/// <summary>A single node on a notes board: a card legend, an action legend
/// (draw/discard/potion/relic/end turn) or a text note. Card nodes keep a
/// display snapshot so the legend survives game updates.</summary>
public sealed class NotesNode
{
    public string Id { get; set; } = "";

    public NodeKind Kind { get; set; }

    /// <summary>Card (or potion/relic) reference, e.g. "cards.strike_ironclad".</summary>
    public string RefId { get; set; } = "";

    /// <summary>Display title snapshot (or the free text for text nodes).</summary>
    public string Title { get; set; } = "";

    /// <summary>Energy cost snapshot; -1 = X cost / unknown / not a card.</summary>
    public int Cost { get; set; } = -1;

    /// <summary>CardType snapshot as int (see game enum); -1 = unknown.</summary>
    public int CardType { get; set; } = -1;

    /// <summary>CardRarity snapshot as int; -1 = unknown.</summary>
    public int Rarity { get; set; } = -1;

    public bool Upgraded { get; set; }

    /// <summary>Free-form note attached to the node.</summary>
    public string Note { get; set; } = "";

    /// <summary>Optional custom accent color, "#RRGGBB"; empty = derived.</summary>
    public string ColorHex { get; set; } = "";

    public NodeState State { get; set; }

    public float X { get; set; }

    public float Y { get; set; }

    /// <summary>Turn region this node belongs to; empty = free canvas node.
    /// Structured nodes are positioned by the layout engine.</summary>
    public string RegionId { get; set; } = "";

    /// <summary>How many parallel next-step slots this node offers; 0 = auto
    /// (roots offer 2, other nodes offer 1).</summary>
    public int NextSlotCount { get; set; }

    /// <summary>Op-log id this node was imported from; keeps imports idempotent.</summary>
    public string SourceOpId { get; set; } = "";

    /// <summary>Extra payload, e.g. card names for a compressed draw node.</summary>
    public string Meta { get; set; } = "";

    /// <summary>Auto-captured labels (relic triggers, effect draws/discards).</summary>
    public List<NotesAnnotation> Annotations { get; set; } = new();

    public NotesNode Clone() => new()
    {
        Id = Id,
        Kind = Kind,
        RefId = RefId,
        Title = Title,
        Cost = Cost,
        CardType = CardType,
        Rarity = Rarity,
        Upgraded = Upgraded,
        Note = Note,
        ColorHex = ColorHex,
        State = State,
        X = X,
        Y = Y,
        RegionId = RegionId,
        NextSlotCount = NextSlotCount,
        SourceOpId = SourceOpId,
        Meta = Meta,
        Annotations = Annotations.Select(a => a.Clone()).ToList(),
    };

    /// <summary>Copies every field except <see cref="Id"/> and (optionally) the
    /// position and region. Used by undo-able edits.</summary>
    public void CopyFrom(NotesNode other, bool includePosition = true)
    {
        Kind = other.Kind;
        RefId = other.RefId;
        Title = other.Title;
        Cost = other.Cost;
        CardType = other.CardType;
        Rarity = other.Rarity;
        Upgraded = other.Upgraded;
        Note = other.Note;
        ColorHex = other.ColorHex;
        State = other.State;
        NextSlotCount = other.NextSlotCount;
        Meta = other.Meta;
        Annotations = other.Annotations.Select(a => a.Clone()).ToList();
        if (includePosition)
        {
            X = other.X;
            Y = other.Y;
            RegionId = other.RegionId;
        }
    }
}
