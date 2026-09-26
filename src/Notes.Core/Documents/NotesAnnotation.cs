namespace Notes.Core.Documents;

/// <summary>A small label attached to a node or turn region, e.g. "臂甲 triggered"
/// or "draw: Strike, Defend". Auto-captured from the op log.</summary>
public sealed class NotesAnnotation
{
    public string Text { get; set; } = "";

    /// <summary>Optional model id (relic / card), used to avoid duplicates.</summary>
    public string RefId { get; set; } = "";

    public NotesAnnotation Clone() => new() { Text = Text, RefId = RefId };
}
