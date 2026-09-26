using System.Text.Json;
using System.Text.Json.Serialization;
using Notes.Core.Documents;
using Notes.Core.Services;

namespace Notes.Core.Serialization;

/// <summary>Version-tolerant JSON for exporting / normalizing a notes document.
/// (Per-run and global storage go through RitsuLib stores, which serialize the
/// objects themselves; this codec backs import/export and repair.)</summary>
public static class NotesJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(NotesDocument document) =>
        JsonSerializer.Serialize(Normalize(document), Options);

    /// <summary>Never throws; returns a fresh document on unreadable input.</summary>
    public static NotesDocument Deserialize(string json)
    {
        try
        {
            var document = JsonSerializer.Deserialize<NotesDocument>(json, Options);
            return Normalize(document);
        }
        catch (JsonException)
        {
            return new NotesDocument();
        }
    }

    /// <summary>Repairs ids, nulls, zoom range and dangling references in place.</summary>
    public static NotesDocument Normalize(NotesDocument? document)
    {
        if (document == null)
        {
            return new NotesDocument();
        }

        document.Version = Math.Max(document.Version, NotesDocument.CurrentVersion);
        document.Boards ??= new List<NotesBoard>();

        foreach (var board in document.Boards)
        {
            board.Id = string.IsNullOrWhiteSpace(board.Id) ? IdFactory.NewBoardId() : board.Id;
            board.Name = board.Name?.Trim() ?? "";
            board.Nodes ??= new List<NotesNode>();
            board.Edges ??= new List<NotesEdge>();
            board.Zoom = Math.Clamp(board.Zoom <= 0f ? 1f : board.Zoom, 0.5f, 2f);

            var seenNodes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in board.Nodes)
            {
                node.Id = string.IsNullOrWhiteSpace(node.Id) ? IdFactory.NewNodeId() : node.Id;
                while (!seenNodes.Add(node.Id))
                {
                    node.Id = IdFactory.NewNodeId();
                }
                node.Title ??= "";
                node.Note ??= "";
                node.RefId ??= "";
                node.ColorHex ??= "";
            }

            var seenEdges = new HashSet<string>(StringComparer.Ordinal);
            board.Edges.RemoveAll(edge =>
            {
                edge.Id = string.IsNullOrWhiteSpace(edge.Id) ? IdFactory.NewEdgeId() : edge.Id;
                if (!seenEdges.Add(edge.Id))
                {
                    edge.Id = IdFactory.NewEdgeId();
                    seenEdges.Add(edge.Id);
                }
                edge.Label ??= "";
                return edge.From == edge.To
                    || board.FindNode(edge.From) == null
                    || board.FindNode(edge.To) == null;
            });
        }

        if (document.FindBoard(document.ActiveBoardId) == null)
        {
            document.ActiveBoardId = document.Boards.Count > 0 ? document.Boards[0].Id : "";
        }

        return document;
    }
}
