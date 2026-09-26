using Notes.Core.Documents;

namespace Notes.Core.Services;

/// <summary>Pure graph rules over a board's node/edge model. Kept in Core so the
/// cycle guard behind slot drops is unit-testable without Godot.</summary>
public static class NotesGraph
{
    /// <summary>True when <paramref name="candidateId"/> is the anchor itself or
    /// inside its subtree (used to reject slot drops that would create a cycle).
    /// An empty candidate (a fresh root slot) is always accepted.</summary>
    public static bool IsInSubtree(NotesBoard board, string anchorId, string candidateId)
    {
        if (candidateId.Length == 0 || candidateId == anchorId)
        {
            return true;
        }
        var queue = new Queue<string>();
        queue.Enqueue(anchorId);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!seen.Add(current))
            {
                continue;
            }
            foreach (var edge in board.Edges.Where(e => e.From == current))
            {
                if (edge.To == candidateId)
                {
                    return true;
                }
                queue.Enqueue(edge.To);
            }
        }
        return false;
    }
}
