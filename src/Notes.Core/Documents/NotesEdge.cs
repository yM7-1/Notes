namespace Notes.Core.Documents;

/// <summary>A directed branch between two nodes (parent -> child).</summary>
public sealed class NotesEdge
{
    public string Id { get; set; } = "";

    public string From { get; set; } = "";

    public string To { get; set; } = "";

    /// <summary>Optional condition label, e.g. "若抽到攻击牌".</summary>
    public string Label { get; set; } = "";

    public NotesEdge Clone() => new()
    {
        Id = Id,
        From = From,
        To = To,
        Label = Label,
    };
}
