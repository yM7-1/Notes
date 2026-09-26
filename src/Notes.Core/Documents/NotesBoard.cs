namespace Notes.Core.Documents;

/// <summary>One mind-map canvas: nodes + branches + view transform.</summary>
public sealed class NotesBoard
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public List<NotesNode> Nodes { get; set; } = new();

    public List<NotesEdge> Edges { get; set; } = new();

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
}
