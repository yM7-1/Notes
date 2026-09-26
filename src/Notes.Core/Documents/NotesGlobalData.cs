namespace Notes.Core.Documents;

/// <summary>Global (per-profile) payload persisted across runs (RitsuLib ModDataStore).</summary>
public sealed class NotesGlobalData
{
    public NotesDocument Document { get; set; } = new();

    /// <summary>Notes toggle button position in screen pixels; -1 = not set yet.</summary>
    public float ButtonX { get; set; } = -1f;

    public float ButtonY { get; set; } = -1f;
}
