namespace Notes.Core.Documents;

/// <summary>Per-run payload persisted with the save (RitsuLib RunSavedDataStore).</summary>
public sealed class NotesRunData
{
    public NotesDocument Document { get; set; } = new();
}
