namespace Notes.Core.Documents;

/// <summary>A parallel world line (column). The first world line is the actual
/// line: auto-imported combat history lands there; the others are speculation.</summary>
public sealed class NotesWorldLine
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public long CreatedAtUnix { get; set; }
}
