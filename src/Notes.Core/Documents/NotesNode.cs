namespace Notes.Core.Documents;

/// <summary>A single node on a notes board: either a card legend or a text note.
/// Card nodes keep a display snapshot (title/cost/type/rarity/upgrade) so the
/// legend survives game updates and stays readable even if the card model is gone.</summary>
public sealed class NotesNode
{
    public string Id { get; set; } = "";

    public NodeKind Kind { get; set; }

    /// <summary>Card reference, e.g. "cards.strike_ironclad" (empty for text nodes).</summary>
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

    /// <summary>Optional custom accent color, "#RRGGBB"; empty = derived from type/state.</summary>
    public string ColorHex { get; set; } = "";

    public NodeState State { get; set; }

    public float X { get; set; }

    public float Y { get; set; }

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
    };

    /// <summary>Copies every field except <see cref="Id"/> and (optionally) the
    /// position. Used by undo-able edits.</summary>
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
        if (includePosition)
        {
            X = other.X;
            Y = other.Y;
        }
    }
}
