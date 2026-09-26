using Notes.Core.Documents;
using Notes.Core.Serialization;
using Notes.Core.Services;
using Xunit;

namespace Notes.Tests;

public class StructuredModeTests
{
    private static (NotesDocument Doc, NotesBoard Board, NotesWorldLine Line) NewStructured()
    {
        var document = new NotesDocument();
        var board = document.CreateBoard("combat");
        var line = document.EnsureActualWorldLine(board.Id);
        return (document, board, line);
    }

    [Fact]
    public void EnsureTurnRegion_IsIdempotent()
    {
        var (doc, board, line) = NewStructured();
        var first = doc.EnsureTurnRegion(board.Id, line.Id, 1);
        var second = doc.EnsureTurnRegion(board.Id, line.Id, 1);

        Assert.Same(first, second);
        Assert.Single(board.TurnRegions);
    }

    [Fact]
    public void RemoveTurnRegion_CascadesStructuredNodesAndEdges()
    {
        var (doc, board, line) = NewStructured();
        var region = doc.EnsureTurnRegion(board.Id, line.Id, 1);
        var a = new NotesNode { Id = "a", RegionId = region.Id };
        var b = new NotesNode { Id = "b", RegionId = region.Id };
        doc.AddNode(board.Id, a);
        doc.AddNode(board.Id, b);
        doc.AddEdge(board.Id, new NotesEdge { Id = "e", From = "a", To = "b" });

        Assert.True(doc.RemoveTurnRegion(board.Id, region.Id));
        Assert.Empty(board.Nodes);
        Assert.Empty(board.Edges);
        Assert.Empty(board.TurnRegions);
    }

    [Fact]
    public void Layout_StacksTurnsDownAndChainsRight()
    {
        var (doc, board, line) = NewStructured();
        var t1 = doc.EnsureTurnRegion(board.Id, line.Id, 1);
        var t2 = doc.EnsureTurnRegion(board.Id, line.Id, 2);
        var root = new NotesNode { Id = "root", RegionId = t1.Id };
        var child = new NotesNode { Id = "child", RegionId = t1.Id };
        doc.AddNode(board.Id, root);
        doc.AddNode(board.Id, child);
        doc.AddEdge(board.Id, new NotesEdge { Id = "e", From = "root", To = "child" });

        NotesLayout.Apply(board);

        Assert.True(t2.Y > t1.Y + t1.Height);
        Assert.True(child.X > root.X + 100);
        Assert.True(root.X >= t1.X);
        Assert.True(root.Y >= t1.Y + NotesLayout.RegionHeader);
    }

    [Fact]
    public void FreeSlots_EmptyRegionThenRootThenChild()
    {
        var (doc, board, line) = NewStructured();
        var region = doc.EnsureTurnRegion(board.Id, line.Id, 1);

        var empty = NotesLayout.FreeSlots(board, region);
        Assert.Single(empty);
        Assert.Equal("", empty[0].ParentId);

        var root = new NotesNode { Id = "root", RegionId = region.Id };
        doc.AddNode(board.Id, root);
        NotesLayout.Apply(board);
        Assert.Equal(NotesLayout.RootParallelSlots, NotesLayout.FreeSlots(board, region).Count);

        var child = new NotesNode { Id = "child", RegionId = region.Id };
        doc.AddNode(board.Id, child);
        doc.AddEdge(board.Id, new NotesEdge { Id = "e", From = "root", To = "child" });
        NotesLayout.Apply(board);
        var slots = NotesLayout.FreeSlots(board, region);
        Assert.Equal(NotesLayout.RootParallelSlots - 1 + 1, slots.Count);
    }

    [Fact]
    public void Importer_ChainsOpsAndIsIdempotent()
    {
        var (doc, board, line) = NewStructured();
        var region = doc.EnsureTurnRegion(board.Id, line.Id, 1);
        var ops = new List<NotesOpData>
        {
            new() { Id = "op1", Kind = NotesOpKind.Card, Turn = 1, UnixMs = 1, Title = "Strike" },
            new() { Id = "op2", Kind = NotesOpKind.Potion, Turn = 1, UnixMs = 2, Title = "Fire Potion" },
            new() { Id = "op3", Kind = NotesOpKind.EndTurn, Turn = 1, UnixMs = 3, Title = "End turn" },
        };

        var plan = NotesImporter.Plan(board, region, ops);
        Assert.Equal(3, plan.Nodes.Count);
        Assert.Equal(2, plan.Edges.Count);
        foreach (var node in plan.Nodes)
        {
            doc.AddNode(board.Id, node);
        }
        foreach (var edge in plan.Edges)
        {
            doc.AddEdge(board.Id, edge);
        }

        var again = NotesImporter.Plan(board, region, ops);
        Assert.Empty(again.Nodes);
        Assert.Empty(again.Edges);
    }

    [Fact]
    public void Json_MigratesV1DocumentToStructured()
    {
        const string v1 = """
        {
          "Version": 1,
          "ActiveBoardId": "b1",
          "Boards": [
            {
              "Id": "b1",
              "Name": "old",
              "Nodes": [ { "Id": "n1", "Title": "old node", "RegionId": "missing" } ],
              "Edges": [],
              "Zoom": 1
            }
          ]
        }
        """;

        var document = NotesJson.Deserialize(v1);

        Assert.Equal(NotesDocument.CurrentVersion, document.Version);
        var board = document.ActiveBoard!;
        Assert.Empty(board.WorldLines);
        Assert.Empty(board.TurnRegions);
        Assert.Equal("", board.Nodes[0].RegionId);
        Assert.NotNull(board.Nodes[0].Annotations);
    }

    [Fact]
    public void Json_RepairsRegionWithMissingWorldLine()
    {
        var document = new NotesDocument();
        var board = document.CreateBoard("b");
        board.TurnRegions.Add(new NotesTurnRegion { Id = "r1", WorldLineId = "ghost", TurnNumber = 1 });

        var normalized = NotesJson.Normalize(document);

        var line = Assert.Single(normalized.Boards[0].WorldLines);
        Assert.Equal(line.Id, normalized.Boards[0].TurnRegions[0].WorldLineId);
    }
}
