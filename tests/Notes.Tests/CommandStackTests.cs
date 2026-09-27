using Notes.Core.Documents;
using Notes.Core.Services;
using Xunit;

namespace Notes.Tests;

public class CommandStackTests
{
    private static (NotesDocument Doc, NotesBoard Board, CommandStack Stack) NewBoard()
    {
        var document = new NotesDocument();
        var board = document.EnsureActiveBoard();
        return (document, board, new CommandStack());
    }

    private static NotesNode Node(string id) => new() { Id = id, Title = "n" };

    [Fact]
    public void AddNode_UndoRedo_RoundTrips()
    {
        var (doc, board, stack) = NewBoard();

        stack.Execute(new AddNodeCommand(doc, board.Id, Node("n1")));
        Assert.Single(board.Nodes);

        Assert.True(stack.Undo());
        Assert.Empty(board.Nodes);

        Assert.True(stack.Redo());
        Assert.Single(board.Nodes);
    }

    [Fact]
    public void RemoveNode_Undo_RestoresNodeAndEdges()
    {
        var (doc, board, stack) = NewBoard();
        doc.AddNode(board.Id, Node("a"));
        doc.AddNode(board.Id, Node("b"));
        doc.AddEdge(board.Id, new NotesEdge { Id = "e1", From = "a", To = "b" });

        stack.Execute(new RemoveNodeCommand(doc, board.Id, "a"));
        Assert.Single(board.Nodes);
        Assert.Empty(board.Edges);

        stack.Undo();
        Assert.NotNull(board.FindNode("a"));
        Assert.NotNull(board.FindEdge("e1"));
    }

    [Fact]
    public void MoveNode_UndoRestoresPosition()
    {
        var (doc, board, stack) = NewBoard();
        doc.AddNode(board.Id, Node("n1"));
        board.FindNode("n1")!.X = 10;
        board.FindNode("n1")!.Y = 20;

        stack.Execute(new MoveNodeCommand(doc, board.Id, "n1", 10, 20, 100, 50));
        Assert.Equal(100, board.FindNode("n1")!.X);

        stack.Undo();
        Assert.Equal(10, board.FindNode("n1")!.X);
        Assert.Equal(20, board.FindNode("n1")!.Y);
    }

    [Fact]
    public void UpdateNode_UndoRestoresStateAndNote()
    {
        var (doc, board, stack) = NewBoard();
        var node = Node("n1");
        doc.AddNode(board.Id, node);
        var before = node.Clone();
        var after = node.Clone();
        after.State = NodeState.Tried;
        after.Note = "played first";

        stack.Execute(new UpdateNodeCommand(doc, board.Id, "n1", before, after));
        Assert.Equal(NodeState.Tried, board.FindNode("n1")!.State);

        stack.Undo();
        Assert.Equal(NodeState.None, board.FindNode("n1")!.State);
        Assert.Equal("", board.FindNode("n1")!.Note);
    }

    [Fact]
    public void RemoveBoard_UndoRestoresIndexAndActivation()
    {
        var (doc, _, stack) = NewBoard();
        var second = doc.CreateBoard("two");
        var third = doc.CreateBoard("three");
        Assert.Equal(third.Id, doc.ActiveBoardId);

        stack.Execute(new RemoveBoardCommand(doc, third.Id));
        Assert.Null(doc.FindBoard(third.Id));

        stack.Undo();
        Assert.NotNull(doc.FindBoard(third.Id));
        Assert.Equal(third.Id, doc.ActiveBoardId);
        Assert.Equal(2, doc.Boards.IndexOf(doc.FindBoard(third.Id)!));
        Assert.NotNull(doc.FindBoard(second.Id));
    }

    [Fact]
    public void PushApplied_DoesNotReapply()
    {
        var (doc, board, stack) = NewBoard();
        var node = Node("n1");
        doc.AddNode(board.Id, node);

        stack.PushApplied(new MoveNodeCommand(doc, board.Id, "n1", 0, 0, 42, 42));
        Assert.Equal(0, board.FindNode("n1")!.X);

        stack.Undo();
        Assert.Equal(0, board.FindNode("n1")!.X);
    }

    [Fact]
    public void AddBoard_WorldLineCreation_UndoRedoKeepsBoardAndActivation()
    {
        var document = new NotesDocument();
        var overview = document.EnsureActiveBoard("Overview");
        var stack = new CommandStack();

        var board = document.BuildWorldLineBoard("World line 1");
        var line = board.WorldLines.Single();
        board.TurnRegions.Add(new NotesTurnRegion { Id = "r1", WorldLineId = line.Id, TurnNumber = 1 });

        stack.Execute(new AddBoardCommand(document, board));
        Assert.Equal(2, document.Boards.Count);
        Assert.Equal(board.Id, document.ActiveBoardId);

        stack.Undo();
        Assert.Single(document.Boards);
        Assert.Equal(overview.Id, document.ActiveBoardId);

        stack.Redo();
        Assert.Equal(2, document.Boards.Count);
        Assert.Same(board, document.FindBoard(board.Id));
        Assert.Single(document.FindBoard(board.Id)!.TurnRegions);
    }

    [Fact]
    public void Stack_TrimsAtLimit_AndClearsRedoOnNewCommand()
    {
        var (doc, board, stack) = NewBoard();
        var limited = new CommandStack { Limit = 2 };
        limited.Execute(new AddNodeCommand(doc, board.Id, Node("a")));
        limited.Execute(new AddNodeCommand(doc, board.Id, Node("b")));
        limited.Execute(new AddNodeCommand(doc, board.Id, Node("c")));

        limited.Undo();
        limited.Undo();
        Assert.False(limited.CanUndo);

        limited.Redo();
        Assert.True(limited.CanRedo);
        limited.Execute(new AddNodeCommand(doc, board.Id, Node("d")));
        Assert.False(limited.CanRedo);
    }
}
