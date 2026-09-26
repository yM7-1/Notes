namespace Notes.Core.Documents;

/// <summary>How sure the player is about a world line / branch.</summary>
public enum NodeState
{
    /// <summary>No mark.</summary>
    None = 0,

    /// <summary>Already tried in this run (solid, dimmed).</summary>
    Tried = 1,

    /// <summary>Speculation, not yet tested (dashed).</summary>
    Speculated = 2,

    /// <summary>Confirmed outcome.</summary>
    Confirmed = 3,
}
