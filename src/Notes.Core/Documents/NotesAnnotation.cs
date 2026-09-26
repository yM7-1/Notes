namespace Notes.Core.Documents;

/// <summary>A small label attached to a node or turn region, e.g. "armor triggered"
/// or "draw: Strike, Defend". Auto-captured from the op log.</summary>
public sealed class NotesAnnotation
{
    public string Text { get; set; } = "";

    /// <summary>Optional model id (relic / card), used to avoid duplicates.</summary>
    public string RefId { get; set; } = "";

    /// <summary>Structured payload used by rich annotations (e.g. damage source,
    /// enemy + pile for inserted cards). Parts are separated by U+001F.</summary>
    public string Meta { get; set; } = "";

    /// <summary>Aggregated amount: inserted copies, total damage.</summary>
    public int Count { get; set; } = 1;

    public NotesAnnotation Clone() => new() { Text = Text, RefId = RefId, Meta = Meta, Count = Count };
}
