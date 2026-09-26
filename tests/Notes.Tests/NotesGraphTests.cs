using Notes.Core.Documents;
using Notes.Core.Services;
using Xunit;

namespace Notes.Tests;

public class NotesGraphTests
{
    private static (NotesDocument Doc, NotesBoard Board) NewBoard()
    {
        var document = new NotesDocument();
        var board = document.CreateBoard("combat");
        return (document, board);
    }

    [Fact]
    public void IsInSubtree_AcceptsEmptyRootSlotAndSelf()
    {
        var (_, board) = NewBoard();
        Assert.True(NotesGraph.IsInSubtree(board, "a", ""));
        Assert.True(NotesGraph.IsInSubtree(board, "a", "a"));
    }

    [Fact]
    public void IsInSubtree_DetectsDescendants()
    {
        var (doc, board) = NewBoard();
        doc.AddNode(board.Id, new NotesNode { Id = "a" });
        doc.AddNode(board.Id, new NotesNode { Id = "b" });
        doc.AddNode(board.Id, new NotesNode { Id = "c" });
        doc.AddEdge(board.Id, new NotesEdge { Id = "e1", From = "a", To = "b" });
        doc.AddEdge(board.Id, new NotesEdge { Id = "e2", From = "b", To = "c" });

        Assert.True(NotesGraph.IsInSubtree(board, "a", "b"));
        Assert.True(NotesGraph.IsInSubtree(board, "a", "c"));
        Assert.False(NotesGraph.IsInSubtree(board, "c", "a"));
        Assert.False(NotesGraph.IsInSubtree(board, "b", "a"));
    }

    [Fact]
    public void IsInSubtree_TerminatesOnCycles()
    {
        var (doc, board) = NewBoard();
        doc.AddNode(board.Id, new NotesNode { Id = "a" });
        doc.AddNode(board.Id, new NotesNode { Id = "b" });
        doc.AddEdge(board.Id, new NotesEdge { Id = "e1", From = "a", To = "b" });
        doc.AddEdge(board.Id, new NotesEdge { Id = "e2", From = "b", To = "a" });

        Assert.True(NotesGraph.IsInSubtree(board, "a", "b"));
        Assert.False(NotesGraph.IsInSubtree(board, "a", "zz"));
    }
}
