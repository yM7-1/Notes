namespace Notes.Core.Documents;

/// <summary>Global (per-profile) payload persisted across runs (RitsuLib ModDataStore).</summary>
public sealed class NotesGlobalData
{
    public NotesDocument Document { get; set; } = new();
}
