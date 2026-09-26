namespace Notes.Core.Documents;

/// <summary>Global (per-profile) payload persisted across runs (RitsuLib ModDataStore).</summary>
public sealed class NotesGlobalData
{
    public NotesDocument Document { get; set; } = new();

    /// <summary>Notes toggle button position in screen pixels; -1 = not set yet.</summary>
    public float ButtonX { get; set; } = -1f;

    public float ButtonY { get; set; } = -1f;

    /// <summary>Notes window size; -1 = default.</summary>
    public float WindowW { get; set; } = -1f;

    public float WindowH { get; set; } = -1f;

    /// <summary>Notes window position; -1 = default.</summary>
    public float WindowX { get; set; } = -1f;

    public float WindowY { get; set; } = -1f;

    /// <summary>Handle collapsed into a small edge arrow.</summary>
    public bool HandleCollapsed { get; set; }

    /// <summary>0 = right edge, 1 = left edge (used while collapsed).</summary>
    public int HandleSide { get; set; }
}
