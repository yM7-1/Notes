namespace Notes.Core.Documents;

/// <summary>One turn's region inside a world line. Operations of that turn live
/// here; positions and size are computed by the layout engine.</summary>
public sealed class NotesTurnRegion
{
    public string Id { get; set; } = "";

    public string WorldLineId { get; set; } = "";

    public int TurnNumber { get; set; }

    public float X { get; set; }

    public float Y { get; set; }

    public float Width { get; set; }

    public float Height { get; set; }

    /// <summary>Auto-captured events with no owning operation (turn start relic
    /// triggers, shuffles, ...).</summary>
    public List<NotesAnnotation> TurnEvents { get; set; } = new();

    /// <summary>Player state at the end of this turn (inspector text).</summary>
    public string Snapshot { get; set; } = "";

    public int Hp { get; set; } = -1;

    public int MaxHp { get; set; } = -1;

    public NotesTurnRegion Clone() => new()
    {
        Id = Id,
        WorldLineId = WorldLineId,
        TurnNumber = TurnNumber,
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
        TurnEvents = TurnEvents.Select(a => a.Clone()).ToList(),
        Snapshot = Snapshot,
        Hp = Hp,
        MaxHp = MaxHp,
    };
}
