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
    public void ClearRegionEventsCommand_ClearsAndRestores()
    {
        var (doc, board, line) = NewStructured();
        var region = doc.EnsureTurnRegion(board.Id, line.Id, 1);
        region.TurnEvents.Add(new NotesAnnotation { RefId = "loss:1", Text = "", Count = 7 });
        var command = new ClearRegionEventsCommand(doc, board.Id, region.Id);

        command.Do();
        Assert.Empty(region.TurnEvents);

        command.Undo();
        var restored = Assert.Single(region.TurnEvents);
        Assert.Equal("loss:1", restored.RefId);
        Assert.Equal(7, restored.Count);
    }

    [Fact]
    public void Apply_ShrinkingOps_RemovesStaleNodeAndItsEdges()
    {
        var (doc, board, line) = NewStructured();
        var region = doc.EnsureTurnRegion(board.Id, line.Id, 1);
        var ops = new List<NotesOpData>
        {
            new() { Id = "op1", Kind = NotesOpKind.Card, Turn = 1, UnixMs = 1, Title = "Strike" },
            new() { Id = "op2", Kind = NotesOpKind.Card, Turn = 1, UnixMs = 2, Title = "Defend" },
            new() { Id = "op3", Kind = NotesOpKind.Card, Turn = 1, UnixMs = 3, Title = "Bash" },
        };
        NotesImporter.Apply(doc, board, region, ops);
        var byOp = board.NodesOfRegion(region.Id).ToDictionary(n => n.SourceOpId, StringComparer.Ordinal);
        var idOf = byOp.ToDictionary(p => p.Key, p => p.Value.Id, StringComparer.Ordinal);
        Assert.Equal(2, board.EdgesOfRegion(region.Id).Count());

        // Drop the middle op: its node and both attached edges disappear,
        // surviving nodes keep their ids (stable UI).
        NotesImporter.Apply(doc, board, region, new List<NotesOpData> { ops[0], ops[2] });

        var remaining = board.NodesOfRegion(region.Id).ToList();
        Assert.Equal(2, remaining.Count);
        Assert.DoesNotContain(remaining, n => n.SourceOpId == "op2");
        Assert.Contains(remaining, n => n.Id == idOf["op1"]);
        Assert.Contains(remaining, n => n.Id == idOf["op3"]);
        Assert.DoesNotContain(board.Edges, e => e.To == idOf["op2"] || e.From == idOf["op2"]);
        Assert.Equal(1, board.EdgesOfRegion(region.Id).Count()); // op1 -> op3
    }

    [Fact]
    public void Apply_ReplacedOpId_RecreatesNodeAndRepointsEdges()
    {
        var (doc, board, line) = NewStructured();
        var region = doc.EnsureTurnRegion(board.Id, line.Id, 1);
        NotesImporter.Apply(doc, board, region, new List<NotesOpData>
        {
            new() { Id = "op1", Kind = NotesOpKind.Card, Turn = 1, UnixMs = 1, Title = "Strike" },
            new() { Id = "op2", Kind = NotesOpKind.Card, Turn = 1, UnixMs = 2, Title = "Defend" },
        });
        var firstId = board.NodesOfRegion(region.Id).Single(n => n.SourceOpId == "op1").Id;

        // The same logical step comes back under a new op id: the edge must be
        // re-pointed to the recreated node instead of dangling.
        NotesImporter.Apply(doc, board, region, new List<NotesOpData>
        {
            new() { Id = "op1", Kind = NotesOpKind.Card, Turn = 1, UnixMs = 1, Title = "Strike" },
            new() { Id = "op2b", Kind = NotesOpKind.Card, Turn = 1, UnixMs = 2, Title = "Defend" },
        });

        var nodes = board.NodesOfRegion(region.Id).ToList();
        Assert.Equal(2, nodes.Count);
        Assert.DoesNotContain(nodes, n => n.SourceOpId == "op2");
        var newId = nodes.Single(n => n.SourceOpId == "op2b").Id;
        Assert.Contains(nodes, n => n.Id == firstId);
        var edge = Assert.Single(board.EdgesOfRegion(region.Id));
        Assert.Equal(firstId, edge.From);
        Assert.Equal(newId, edge.To);
    }

    [Fact]
    public void UpdateTurnRegionCommand_RoundTripsSnapshotAndHp()
    {
        var (doc, board, line) = NewStructured();
        var region = doc.EnsureTurnRegion(board.Id, line.Id, 1);
        region.Snapshot = "old";
        region.Hp = 30;
        region.MaxHp = 70;
        var command = new UpdateTurnRegionCommand(
            doc, board.Id, region.Id, "old", 30, 70, "new", 22, 70);

        command.Do();
        Assert.Equal("new", region.Snapshot);
        Assert.Equal(22, region.Hp);

        command.Undo();
        Assert.Equal("old", region.Snapshot);
        Assert.Equal(30, region.Hp);
        Assert.Equal(70, region.MaxHp);
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
    public void Importer_ReplaceImported_RebuildsTheTurn()
    {
        var (doc, board, line) = NewStructured();
        var region = doc.EnsureTurnRegion(board.Id, line.Id, 1);
        var ops = new List<NotesOpData>
        {
            new() { Id = "op1", Kind = NotesOpKind.Card, Turn = 1, UnixMs = 1, Title = "Strike" },
            new() { Id = "op2", Kind = NotesOpKind.Potion, Turn = 1, UnixMs = 2, Title = "Fire Potion" },
        };
        var first = NotesImporter.Plan(board, region, ops);
        foreach (var node in first.Nodes)
        {
            doc.AddNode(board.Id, node);
        }
        foreach (var edge in first.Edges)
        {
            doc.AddEdge(board.Id, edge);
        }

        // Plain re-import is idempotent...
        Assert.Empty(NotesImporter.Plan(board, region, ops).Nodes);
        // ...but a replace import rebuilds the turn.
        var replace = NotesImporter.Plan(board, region, ops, replaceImported: true);
        Assert.Equal(2, replace.Nodes.Count);
        Assert.Single(replace.Edges);
    }

    [Fact]
    public void Importer_SkipsExhaustAndDiscardOps()
    {
        var (doc, board, line) = NewStructured();
        var region = doc.EnsureTurnRegion(board.Id, line.Id, 1);
        var ops = new List<NotesOpData>
        {
            new() { Id = "op1", Kind = NotesOpKind.Exhaust, Turn = 1, UnixMs = 1, Title = "消耗×2" },
            new() { Id = "op2", Kind = NotesOpKind.Discard, Turn = 1, UnixMs = 2, Title = "弃牌×3" },
        };

        Assert.Empty(NotesImporter.Plan(board, region, ops).Nodes);
    }

    [Fact]
    public void WorldLineBoards_AreNumberedAndOverviewComesFirst()
    {
        var document = new NotesDocument();
        var overview = document.EnsureOverviewBoard("Overview");
        Assert.Equal(BoardKind.Overview, overview.Kind);
        Assert.Same(overview, document.EnsureOverviewBoard("Other"));

        var first = document.CreateWorldLineBoard("World line 1");
        var second = document.CreateWorldLineBoard("World line 2");
        Assert.Equal(1, first.Ordinal);
        Assert.Equal(2, second.Ordinal);
        Assert.Same(first, document.ActualWorldLineBoard);

        var cards = NotesLayout.OverviewCards(document);
        Assert.Equal(2, cards.Count);
        Assert.Equal(first.Id, cards[0].BoardId);
        Assert.Equal(second.Id, cards[1].BoardId);
        Assert.True(cards[1].X > cards[0].X);
    }

    [Fact]
    public void EnsureActualWorldLine_UsesGivenName()
    {
        var document = new NotesDocument();
        var board = document.CreateBoard("combat");
        var line = document.EnsureActualWorldLine(board.Id, board.Name);
        Assert.Equal("combat", line.Name);
        Assert.Same(line, document.EnsureActualWorldLine(board.Id, "ignored"));
    }

    [Fact]
    public void OverviewCards_IncludeCurrentBoardFirst()
    {
        var document = new NotesDocument();
        document.EnsureOverviewBoard("Overview");
        var current = document.EnsureCurrentBoard("Current");
        var worldLine = document.CreateWorldLineBoard("World line 1");

        var cards = NotesLayout.OverviewCards(document);

        Assert.Equal(2, cards.Count);
        Assert.Equal(current.Id, cards[0].BoardId);
        Assert.Equal(worldLine.Id, cards[1].BoardId);
    }

    [Fact]
    public void DuplicateWorldLineBoard_RemapsIdsAndKeepsContent()
    {
        var document = new NotesDocument();
        document.EnsureOverviewBoard("Overview");
        var current = document.EnsureCurrentBoard("Current");
        var line = document.EnsureActualWorldLine(current.Id, current.Name);
        var region = document.EnsureTurnRegion(current.Id, line.Id, 1);
        region.Hp = 61;
        region.TurnEvents.Add(new NotesAnnotation { RefId = "loss:", Count = 12 });
        document.AddNode(current.Id, new NotesNode
        {
            Id = "n1",
            RegionId = region.Id,
            Title = "Strike",
            SourceOpId = "op1",
            Annotations = { new NotesAnnotation { RefId = "exhaust:", Text = "A" } },
        });
        document.AddNode(current.Id, new NotesNode { Id = "n2", RegionId = region.Id, Title = "Defend" });
        document.AddEdge(current.Id, new NotesEdge { Id = "e1", From = "n1", To = "n2", Label = "if" });

        var copy = document.DuplicateWorldLineBoard(current.Id, "World line 1");

        Assert.NotNull(copy);
        Assert.Equal(BoardKind.WorldLine, copy!.Kind);
        Assert.Equal(1, copy.Ordinal);
        Assert.Single(copy.TurnRegions);
        Assert.Equal(2, copy.Nodes.Count);
        Assert.DoesNotContain(copy.Nodes, n => n.Id == "n1" || n.Id == "n2");
        Assert.Single(copy.Edges);
        var copiedEdge = copy.Edges[0];
        Assert.Equal("if", copiedEdge.Label);
        Assert.Contains(copy.Nodes, n => n.Id == copiedEdge.From);
        Assert.Contains(copy.Nodes, n => n.Id == copiedEdge.To);
        var copiedRegion = copy.TurnRegions[0];
        Assert.Equal(61, copiedRegion.Hp);
        Assert.Contains(copiedRegion.TurnEvents, a => a.RefId == "loss:" && a.Count == 12);
        var copiedNode = copy.Nodes.First(n => n.SourceOpId == "op1");
        Assert.Equal("Strike", copiedNode.Title);
        Assert.Equal(copiedRegion.Id, copiedNode.RegionId);
        Assert.Contains(copiedNode.Annotations, a => a.RefId == "exhaust:");
    }

    [Fact]
    public void Importer_Apply_KeepsNodeIdsAndRefreshesContent()
    {
        var (doc, board, line) = NewStructured();
        var region = doc.EnsureTurnRegion(board.Id, line.Id, 1);
        var ops = new List<NotesOpData>
        {
            new() { Id = "op1", Kind = NotesOpKind.Card, Turn = 1, UnixMs = 1, Title = "Strike", Hp = 80 },
        };
        NotesImporter.Apply(doc, board, region, ops);
        var firstId = Assert.Single(board.NodesOfRegion(region.Id)).Id;

        ops[0].Title = "Strike+";
        ops[0].Hp = 74;
        ops[0].Annotations.Add(new NotesAnnotation { RefId = "damage:x", Text = "x", Count = 6 });
        NotesImporter.Apply(doc, board, region, ops);

        var again = Assert.Single(board.NodesOfRegion(region.Id));
        Assert.Equal(firstId, again.Id);
        Assert.Equal("Strike+", again.Title);
        Assert.Equal(74, again.Hp);
        Assert.Single(again.Annotations);
        Assert.Equal(74, region.Hp);
    }

    [Fact]
    public void Layout_ReservesStripForBoundaryAnnotations()
    {
        var (doc, board, line) = NewStructured();
        var region = doc.EnsureTurnRegion(board.Id, line.Id, 1);
        doc.AddNode(board.Id, new NotesNode { Id = "n", RegionId = region.Id });
        NotesLayout.Apply(board);
        var plainHeight = region.Height;

        region.TurnEvents.Add(new NotesAnnotation { RefId = "loss:", Count = 5 });
        region.TurnEvents.Add(new NotesAnnotation { RefId = "insert:x", Text = "A" });
        NotesLayout.Apply(board);

        Assert.True(region.Height > plainHeight);
        Assert.Equal(
            NotesLayout.BoundaryStripPadding + 2 * (NotesLayout.BoundaryChipHeight + NotesLayout.BoundaryChipGap),
            NotesLayout.BoundaryAreaHeight(region), 3);
    }

    [Fact]
    public void ImportCommands_ReplaceOverwritesTheWholeTurn()
    {
        var (doc, board, line) = NewStructured();
        var region = doc.EnsureTurnRegion(board.Id, line.Id, 1);
        var manual = new NotesNode { Id = "manual", RegionId = region.Id, Title = "my idea" };
        doc.AddNode(board.Id, manual);

        var ops = new List<NotesOpData>
        {
            new() { Id = "op1", Kind = NotesOpKind.Card, Turn = 1, UnixMs = 1, Title = "Strike" },
            new() { Id = "op2", Kind = NotesOpKind.EndTurn, Turn = 1, UnixMs = 2, Title = "End turn" },
        };
        var first = NotesImporter.BuildCommands(doc, board, region, ops, replaceImported: true);
        foreach (var command in first)
        {
            command.Do();
        }
        // Overwrite rebuilds the turn from the op log: manual nodes are gone too.
        Assert.Equal(2, board.NodesOfRegion(region.Id).Count());
        Assert.DoesNotContain(board.NodesOfRegion(region.Id), n => n.Id == "manual");

        // Re-recording replaces the chain instead of appending duplicates.
        var second = NotesImporter.BuildCommands(doc, board, region, ops, replaceImported: true);
        foreach (var command in second)
        {
            command.Do();
        }
        Assert.Equal(2, board.NodesOfRegion(region.Id).Count());
        Assert.Equal(2, board.NodesOfRegion(region.Id).Count(n => n.SourceOpId.Length > 0));
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
    public void Json_RoundTripsRichAnnotations()
    {
        var document = new NotesDocument();
        var board = document.CreateBoard("b");
        var node = new NotesNode { Id = "n1" };
        node.Annotations.Add(new NotesAnnotation
        {
            RefId = "damage:扭动虫2",
            Text = "扭动虫2",
            Meta = "火焰药水\u001f1",
            Count = 20,
        });
        board.Nodes.Add(node);

        var back = NotesJson.Deserialize(NotesJson.Serialize(document));

        var annotation = Assert.Single(back.ActiveBoard!.Nodes[0].Annotations);
        Assert.Equal("火焰药水\u001f1", annotation.Meta);
        Assert.Equal(20, annotation.Count);
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
