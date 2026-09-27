using Notes.Core.Documents;
using Notes.Core.Services;
using Xunit;

namespace Notes.Tests;

public class NotesLayoutTests
{
    [Fact]
    public void ArrangeFreeNodes_UsesGridAndWraps()
    {
        var board = new NotesBoard { Id = "b", Name = "free" };
        for (var i = 0; i < 5; i++)
        {
            board.Nodes.Add(new NotesNode { Id = "n" + i, Title = "n" + i, X = 999, Y = 999 });
        }

        var count = NotesLayout.ArrangeFreeNodes(board);

        Assert.Equal(5, count);
        var first = board.Nodes.Single(n => n.Id == "n0");
        Assert.Equal(NotesLayout.ColumnStartX, first.X);
        Assert.Equal(NotesLayout.RowStartY, first.Y);
        var fifth = board.Nodes.Single(n => n.Id == "n4");
        Assert.Equal(NotesLayout.ColumnStartX + NotesLayout.NodeWidth + 40f, fifth.X);
        Assert.Equal(NotesLayout.RowStartY, fifth.Y);
    }

    [Fact]
    public void ArrangeFreeNodes_LeavesStructuredNodes_AndAvoidsRegions()
    {
        var board = new NotesBoard { Id = "b", Name = "mixed" };
        var line = new NotesWorldLine { Id = "w", Name = "w" };
        board.WorldLines.Add(line);
        board.TurnRegions.Add(new NotesTurnRegion
        {
            Id = "r",
            WorldLineId = "w",
            TurnNumber = 1,
            X = 60,
            Y = 70,
            Width = 400,
            Height = 200,
        });
        board.Nodes.Add(new NotesNode { Id = "s", Title = "in region", RegionId = "r", X = 100, Y = 100 });
        board.Nodes.Add(new NotesNode { Id = "f", Title = "free", X = 0, Y = 0 });

        NotesLayout.ArrangeFreeNodes(board);

        Assert.Equal(100, board.Nodes.Single(n => n.Id == "s").X);
        Assert.Equal(60 + 400 + NotesLayout.ColumnGap, board.Nodes.Single(n => n.Id == "f").X);
    }

    [Fact]
    public void ArrangeFreeNodes_NoFreeNodes_ReturnsZero()
    {
        var board = new NotesBoard { Id = "b", Name = "empty" };

        Assert.Equal(0, NotesLayout.ArrangeFreeNodes(board));
    }
}
