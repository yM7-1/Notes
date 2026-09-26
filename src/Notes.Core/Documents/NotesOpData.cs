namespace Notes.Core.Documents;

/// <summary>Kind of a captured operation / event.</summary>
public enum NotesOpKind
{
    Card = 0,
    Potion = 1,
    Relic = 2,
    Draw = 3,
    Discard = 4,
    EndTurn = 5,
    TurnEvent = 6,
}

/// <summary>One captured combat operation, persisted with the run so notes can
/// be built from history even after a save/load.</summary>
public sealed class NotesOpData
{
    public string Id { get; set; } = "";

    public NotesOpKind Kind { get; set; }

    public int Turn { get; set; }

    public long UnixMs { get; set; }

    public string Title { get; set; } = "";

    public string RefId { get; set; } = "";

    public int Cost { get; set; } = -1;

    public int CardType { get; set; } = -1;

    public int Rarity { get; set; } = -1;

    public bool Upgraded { get; set; }

    /// <summary>Extra payload (e.g. drawn card names for a grouped draw op).</summary>
    public string Meta { get; set; } = "";

    public List<NotesAnnotation> Annotations { get; set; } = new();

    public NotesOpData Clone() => new()
    {
        Id = Id,
        Kind = Kind,
        Turn = Turn,
        UnixMs = UnixMs,
        Title = Title,
        RefId = RefId,
        Cost = Cost,
        CardType = CardType,
        Rarity = Rarity,
        Upgraded = Upgraded,
        Meta = Meta,
        Annotations = Annotations.Select(a => a.Clone()).ToList(),
    };
}
