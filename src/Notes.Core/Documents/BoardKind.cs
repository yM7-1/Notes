namespace Notes.Core.Documents;

/// <summary>What a board represents. Free boards are plain canvases (global
/// library); run boards are either the world-line overview or a single world
/// line (one board per world line).</summary>
public enum BoardKind
{
    Free = 0,
    Overview = 1,
    WorldLine = 2,

    /// <summary>Read-only board auto-recorded from the live operation log; a
    /// snapshot of it can be copied into an interactive world-line board.</summary>
    Current = 3,

    /// <summary>Post-combat recap board generated at the end of a fight and
    /// carried over across combats of the same run (editable).</summary>
    Summary = 4,
}
