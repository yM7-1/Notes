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

    /// <summary>The first-run "how it works" strip was dismissed.</summary>
    public bool OnboardingSeen { get; set; }

    /// <summary>Capture combat operations automatically (the read-only current
    /// world line is derived from them). Off = a purely manual notebook.</summary>
    public bool AutoRecordOps { get; set; } = true;

    /// <summary>Status-bar feedback lifetime in seconds; 0 keeps it until the
    /// next message replaces it.</summary>
    public double MessageLifetimeSeconds { get; set; } = 8;

    /// <summary>Undo stack depth.</summary>
    public int UndoLimit { get; set; } = 200;

    /// <summary>Library shown when a run starts: 0 = run, 1 = global.</summary>
    public int DefaultLibrary { get; set; }

    /// <summary>Maximum rows the card codex search returns.</summary>
    public int CodexLimit { get; set; } = 120;

    /// <summary>Quick actions shown by the floating handle (bit mask:
    /// 1 = record turn, 2 = copy line, 4 = open settings).</summary>
    public int QuickActionsMask { get; set; } = 3;
}
