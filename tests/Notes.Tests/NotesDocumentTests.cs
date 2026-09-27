using Notes.Core.Documents;
using Notes.Core.Services;
using Xunit;

namespace Notes.Tests;

public class NotesDocumentTests
{
    private static NotesNode Node(string id, float x = 0, float y = 0) => new()
    {
        Id = id,
        Kind = NodeKind.Card,
        RefId = "cards.strike_ironclad",
        Title = "Strike",
        Cost = 1,
        X = x,
        Y = y,
    };

    [Fact]
    public void EnsureActiveBoard_CreatesBoardAndActivatesIt()
    {
        var document = new NotesDocument();
        var board = document.EnsureActiveBoard();

        Assert.Single(document.Boards);
        Assert.Equal(board.Id, document.ActiveBoardId);
        Assert.Same(board, document.ActiveBoard);
    }

    [Fact]
    public void AddNode_RejectsDuplicateIds()
    {
        var document = new NotesDocument();
        var board = document.EnsureActiveBoard();

        Assert.True(document.AddNode(board.Id, Node("n1")));
        Assert.False(document.AddNode(board.Id, Node("n1")));
        Assert.Single(board.Nodes);
    }

    [Fact]
    public void RemoveNode_CascadesAttachedEdges()
    {
        var document = new NotesDocument();
        var board = document.EnsureActiveBoard();
        document.AddNode(board.Id, Node("a"));
        document.AddNode(board.Id, Node("b"));
        document.AddNode(board.Id, Node("c"));
        document.AddEdge(board.Id, new NotesEdge { Id = "e1", From = "a", To = "b" });
        document.AddEdge(board.Id, new NotesEdge { Id = "e2", From = "b", To = "c" });

        Assert.True(document.RemoveNode(board.Id, "b"));

        Assert.Null(board.FindNode("b"));
        Assert.Empty(board.Edges);
    }

    [Fact]
    public void AddEdge_RejectsSelfLoopDuplicateAndMissingNodes()
    {
        var document = new NotesDocument();
        var board = document.EnsureActiveBoard();
        document.AddNode(board.Id, Node("a"));
        document.AddNode(board.Id, Node("b"));

        Assert.False(document.AddEdge(board.Id, new NotesEdge { Id = "e1", From = "a", To = "a" }));
        Assert.False(document.AddEdge(board.Id, new NotesEdge { Id = "e2", From = "a", To = "ghost" }));
        Assert.True(document.AddEdge(board.Id, new NotesEdge { Id = "e3", From = "a", To = "b" }));
        Assert.False(document.AddEdge(board.Id, new NotesEdge { Id = "e4", From = "a", To = "b" }));
        Assert.Single(board.Edges);
    }

    [Fact]
    public void RemoveBoard_PicksFallbackActiveBoard()
    {
        var document = new NotesDocument();
        var first = document.CreateBoard("one");
        var second = document.CreateBoard("two");
        Assert.Equal(second.Id, document.ActiveBoardId);

        Assert.True(document.RemoveBoard(second.Id));
        Assert.Equal(first.Id, document.ActiveBoardId);
        Assert.Single(document.Boards);
    }

    [Fact]
    public void ResetForNewCombat_KeepsRecaps_AndEnsuresSystemBoards()
    {
        var document = new NotesDocument();
        document.EnsureOverviewBoard("Overview");
        document.EnsureCurrentBoard("Current");
        document.CreateWorldLineBoard("World line 1");
        var recap = document.CreateBoard("Recap");
        recap.Kind = BoardKind.Summary;

        document.ResetForNewCombat("Overview", "Current");

        Assert.Equal(3, document.Boards.Count);
        Assert.Contains(document.Boards, b => b.Kind == BoardKind.Summary && b.Name == "Recap");
        Assert.Contains(document.Boards, b => b.Kind == BoardKind.Overview);
        Assert.Contains(document.Boards, b => b.Kind == BoardKind.Current);
        Assert.Equal(recap.Id, document.ActiveBoardId);
    }
}
