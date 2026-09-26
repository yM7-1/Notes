namespace Notes.Core.Documents;

/// <summary>Per-run payload persisted with the save (RitsuLib RunSavedDataStore).</summary>
public sealed class NotesRunData
{
    public NotesDocument Document { get; set; } = new();

    /// <summary>Captured operation log of the current combat (M2).</summary>
    public List<NotesOpData> Ops { get; set; } = new();
}
