using Notes.Core.Documents;
using Notes.Core.Serialization;
using Xunit;

namespace Notes.Tests;

public class NotesJsonTests
{
    [Fact]
    public void SerializeDeserialize_RoundTripsEverything()
    {
        var document = new NotesDocument();
        var board = document.CreateBoard("World lines");
        document.AddNode(board.Id, new NotesNode
        {
            Id = "n1",
            Kind = NodeKind.Card,
            RefId = "cards.strike_ironclad",
            Title = "Strike",
            Cost = 1,
            CardType = 1,
            Rarity = 2,
            Upgraded = true,
            State = NodeState.Tried,
            Note = "first play",
            X = 12.5f,
            Y = -3f,
        });
        document.AddNode(board.Id, new NotesNode { Id = "n2", Kind = NodeKind.Text, Title = "If I draw Bash" });
        document.AddEdge(board.Id, new NotesEdge { Id = "e1", From = "n1", To = "n2", Label = "若抽到" });

        var json = NotesJson.Serialize(document);
        var loaded = NotesJson.Deserialize(json);

        var loadedBoard = loaded.ActiveBoard!;
        Assert.Equal("World lines", loadedBoard.Name);
        Assert.Equal(2, loadedBoard.Nodes.Count);
        var node = loadedBoard.FindNode("n1")!;
        Assert.Equal("cards.strike_ironclad", node.RefId);
        Assert.Equal(NodeState.Tried, node.State);
        Assert.True(node.Upgraded);
        Assert.Equal(12.5f, node.X);
        Assert.Equal("若抽到", loadedBoard.FindEdge("e1")!.Label);
    }

    [Fact]
    public void Deserialize_Garbage_ReturnsFreshDocument()
    {
        var document = NotesJson.Deserialize("{ not json ]");

        Assert.Empty(document.Boards);
        Assert.Equal(NotesDocument.CurrentVersion, document.Version);
    }

    [Fact]
    public void Normalize_RepairsIdsDanglingEdgesAndActiveBoard()
    {
        var document = new NotesDocument
        {
            ActiveBoardId = "missing",
            Boards =
            {
                new NotesBoard
                {
                    Id = "",
                    Zoom = 99f,
                    Nodes = { new NotesNode { Id = "", Title = null! } },
                    Edges =
                    {
                        new NotesEdge { Id = "", From = "ghost", To = "ghost" },
                    },
                },
            },
        };

        var normalized = NotesJson.Normalize(document);

        var board = Assert.Single(normalized.Boards);
        Assert.False(string.IsNullOrWhiteSpace(board.Id));
        Assert.Equal(3f, board.Zoom);
        Assert.False(string.IsNullOrWhiteSpace(board.Nodes[0].Id));
        Assert.Empty(board.Edges);
        Assert.Equal(board.Id, normalized.ActiveBoardId);
    }

    [Fact]
    public void Serialize_IsStable_ForSameInput()
    {
        var document = new NotesDocument();
        var board = document.CreateBoard("b");
        document.AddNode(board.Id, new NotesNode { Id = "n1", Title = "t" });

        var first = NotesJson.Serialize(document);
        var second = NotesJson.Serialize(NotesJson.Deserialize(first));

        Assert.Equal(first, second);
    }

    [Fact]
    public void Normalize_DedupesBoardIds_AndKeepsEveryBoardReachable()
    {
        var document = new NotesDocument
        {
            ActiveBoardId = "dup",
            Boards =
            {
                new NotesBoard { Id = "dup", Name = "first" },
                new NotesBoard { Id = "dup", Name = "second" },
            },
        };

        var normalized = NotesJson.Normalize(document);

        Assert.Equal(2, normalized.Boards.Count);
        Assert.NotEqual(normalized.Boards[0].Id, normalized.Boards[1].Id);
        Assert.Equal("first", normalized.FindBoard(normalized.Boards[0].Id)!.Name);
        Assert.Equal("second", normalized.FindBoard(normalized.Boards[1].Id)!.Name);
        Assert.Equal(normalized.Boards[0].Id, normalized.ActiveBoardId);
    }

    [Fact]
    public void Normalize_DuplicateEntityIds_KeepReferencesResolvable()
    {
        var document = new NotesDocument
        {
            Boards =
            {
                new NotesBoard
                {
                    Id = "b1",
                    WorldLines =
                    {
                        new NotesWorldLine { Id = "w", Name = "A" },
                        new NotesWorldLine { Id = "w", Name = "B" },
                    },
                    TurnRegions =
                    {
                        new NotesTurnRegion { Id = "r", WorldLineId = "w", TurnNumber = 1 },
                        new NotesTurnRegion { Id = "r", WorldLineId = "w", TurnNumber = 2 },
                    },
                    Nodes =
                    {
                        new NotesNode { Id = "n", Title = "n1", RegionId = "r" },
                        new NotesNode { Id = "n", Title = "n2", RegionId = "r" },
                    },
                    Edges =
                    {
                        new NotesEdge { Id = "e", From = "n", To = "n" },
                    },
                },
            },
        };

        var normalized = NotesJson.Normalize(document);
        var board = normalized.Boards[0];

        // ids unique per scope, first holder keeps its id
        Assert.Equal(2, board.WorldLines.Select(l => l.Id).Distinct().Count());
        Assert.Equal(2, board.TurnRegions.Select(r => r.Id).Distinct().Count());
        Assert.Equal(2, board.Nodes.Select(n => n.Id).Distinct().Count());
        Assert.Equal("w", board.WorldLines[0].Id);
        Assert.Equal("r", board.TurnRegions[0].Id);
        Assert.Equal("n", board.Nodes[0].Id);

        // references resolve (first-wins) and nothing dangles
        Assert.All(board.TurnRegions, r => Assert.NotNull(board.FindWorldLine(r.WorldLineId)));
        Assert.All(board.Nodes, n => Assert.NotNull(board.FindRegion(n.RegionId)));
        Assert.Empty(board.Edges); // the duplicate self-loop is dropped

        var once = NotesJson.Serialize(normalized);
        var twice = NotesJson.Serialize(NotesJson.Deserialize(once));
        Assert.Equal(once, twice);
    }
}
