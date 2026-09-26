namespace Notes.Core.Documents;

/// <summary>One mind-map canvas: nodes + branches + view transform.</summary>
public sealed class NotesBoard
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>Free canvas vs. run overview vs. one world line.</summary>
    public BoardKind Kind { get; set; }

    /// <summary>1-based world-line number for <see cref="BoardKind.WorldLine"/>
    /// boards (the board is named "世界线N" / "World line N").</summary>
    public int Ordinal { get; set; }

    /// <summary>The auto-recorded current world line is view-only.</summary>
    public bool IsReadOnly => Kind == BoardKind.Current;

    public List<NotesNode> Nodes { get; set; } = new();

    public List<NotesEdge> Edges { get; set; } = new();

    /// <summary>Structured mode (M2): parallel world lines + per-turn regions.</summary>
    public List<NotesWorldLine> WorldLines { get; set; } = new();

    public List<NotesTurnRegion> TurnRegions { get; set; } = new();

    /// <summary>View pan (canvas-local pixels) and zoom (0.5 - 2.0).</summary>
    public float PanX { get; set; }

    public float PanY { get; set; }

    public float Zoom { get; set; } = 1f;

    public long CreatedAtUnix { get; set; }

    public NotesNode? FindNode(string nodeId) =>
        Nodes.FirstOrDefault(n => string.Equals(n.Id, nodeId, StringComparison.Ordinal));

    public NotesEdge? FindEdge(string edgeId) =>
        Edges.FirstOrDefault(e => string.Equals(e.Id, edgeId, StringComparison.Ordinal));

    public IEnumerable<NotesEdge> EdgesOf(string nodeId) =>
        Edges.Where(e => e.From == nodeId || e.To == nodeId);

    public NotesWorldLine? FindWorldLine(string worldLineId) =>
        WorldLines.FirstOrDefault(w => string.Equals(w.Id, worldLineId, StringComparison.Ordinal));

    public NotesTurnRegion? FindRegion(string regionId) =>
        TurnRegions.FirstOrDefault(r => string.Equals(r.Id, regionId, StringComparison.Ordinal));

    public IEnumerable<NotesTurnRegion> RegionsOf(string worldLineId) =>
        TurnRegions.Where(r => r.WorldLineId == worldLineId).OrderBy(r => r.TurnNumber);

    public IEnumerable<NotesNode> NodesOfRegion(string regionId) =>
        Nodes.Where(n => n.RegionId == regionId);

    public IEnumerable<NotesEdge> EdgesOfRegion(string regionId)
    {
        var ids = NodesOfRegion(regionId).Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        return Edges.Where(e => ids.Contains(e.From) && ids.Contains(e.To));
    }
}
