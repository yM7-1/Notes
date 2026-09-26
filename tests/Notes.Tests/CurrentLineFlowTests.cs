using Notes.Core.Documents;
using Notes.Core.Services;
using Xunit;

namespace Notes.Tests;

/// <summary>End-to-end flows of the v0.6 board model: the read-only current
/// line, copying it into an interactive world line, and overwrite-imports.</summary>
public class CurrentLineFlowTests
{
    private static NotesDocument NewRunDocument()
    {
        var document = new NotesDocument();
        document.EnsureOverviewBoard("Overview");
        document.EnsureCurrentBoard("Current");
        return document;
    }

    [Fact]
    public void NewBoard_HasSlotsAndLayout()
    {
        var document = NewRunDocument();
        var ordinal = document.NextWorldLineOrdinal();
        var board = document.CreateWorldLineBoard("World line " + ordinal);
        var line = document.EnsureActualWorldLine(board.Id, board.Name);
        document.EnsureTurnRegion(board.Id, line.Id, 1);
        document.EnsureTurnRegion(board.Id, line.Id, 2);
        NotesLayout.Apply(board);

        var region = board.RegionsOf(line.Id).First();
        Assert.True(region.Width > 0 && region.Height > 0);
        var slots = NotesLayout.FreeSlots(board, region);
        Assert.Single(slots);
        Assert.Equal(region.X + NotesLayout.RegionPadding, slots[0].X);
    }

    [Fact]
    public void CopyThenImport_ReplacesTheChain()
    {
        var document = NewRunDocument();
        var current = document.EnsureCurrentBoard("Current");
        var line = document.EnsureActualWorldLine(current.Id, current.Name);
        var region = document.EnsureTurnRegion(current.Id, line.Id, 1);
        var ops = new List<NotesOpData>
        {
            new() { Id = "op1", Kind = NotesOpKind.Card, Turn = 1, UnixMs = 1, Title = "Strike" },
            new() { Id = "op2", Kind = NotesOpKind.Card, Turn = 1, UnixMs = 2, Title = "Defend" },
        };
        NotesImporter.Apply(document, current, region, ops);

        var copy = document.DuplicateWorldLineBoard(current.Id, "World line 1")!;
        var copyRegion = copy.TurnRegions.Single();
        Assert.Equal(2, copy.NodesOfRegion(copyRegion.Id).Count());
        Assert.All(copy.NodesOfRegion(copyRegion.Id), n => Assert.False(string.IsNullOrEmpty(n.SourceOpId)));

        ops.Add(new NotesOpData { Id = "op3", Kind = NotesOpKind.Card, Turn = 1, UnixMs = 3, Title = "Bash" });
        var commands = NotesImporter.BuildCommands(document, copy, copyRegion, ops, replaceImported: true);
        foreach (var command in commands)
        {
            command.Do();
        }

        Assert.Equal(3, copy.NodesOfRegion(copyRegion.Id).Count());
        Assert.Equal(3, copy.NodesOfRegion(copyRegion.Id).Count(n => !string.IsNullOrEmpty(n.SourceOpId)));
    }

    [Fact]
    public void Plan_Replace_IgnoresManualTail()
    {
        var document = NewRunDocument();
        var board = document.CreateWorldLineBoard("World line 1");
        var line = document.EnsureActualWorldLine(board.Id, board.Name);
        var region = document.EnsureTurnRegion(board.Id, line.Id, 1);
        document.AddNode(board.Id, new NotesNode { Id = "manual", RegionId = region.Id, Title = "idea" });
        var ops = new List<NotesOpData>
        {
            new() { Id = "op1", Kind = NotesOpKind.Card, Turn = 1, UnixMs = 1, Title = "Strike" },
            new() { Id = "op2", Kind = NotesOpKind.EndTurn, Turn = 1, UnixMs = 2, Title = "End turn" },
        };

        var plan = NotesImporter.Plan(board, region, ops, replaceImported: true);

        Assert.Equal(2, plan.Nodes.Count);
        Assert.DoesNotContain(plan.Edges, e => e.From == "manual");
        Assert.Single(plan.Edges);
    }
}
