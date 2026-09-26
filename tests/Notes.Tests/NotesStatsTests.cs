using Notes.Core.Documents;
using Notes.Core.Services;
using Xunit;

namespace Notes.Tests;

public class NotesStatsTests
{
    [Fact]
    public void Compute_AggregatesBranchesPotionsAndDamage()
    {
        var document = new NotesDocument();
        var board = document.CreateBoard("combat");
        var line = document.EnsureActualWorldLine(board.Id);
        var t1 = document.EnsureTurnRegion(board.Id, line.Id, 1);
        var t2 = document.EnsureTurnRegion(board.Id, line.Id, 2);

        NotesNode Node(string id, string region, NotesOpKind kind, string title, int hp, long ms,
            NodeState state = NodeState.None) => new()
        {
            Id = id,
            RegionId = region,
            Kind = NotesImporter.MapKind(kind),
            Title = title,
            Hp = hp,
            MaxHp = 80,
            OrderMs = ms,
            State = state,
        };

        board.Nodes.Add(Node("a", t1.Id, NotesOpKind.Card, "Strike", 80, 1, NodeState.Tried));
        board.Nodes.Add(Node("b", t1.Id, NotesOpKind.Potion, "Fire Potion", 74, 2));
        board.Nodes.Add(Node("c", t1.Id, NotesOpKind.EndTurn, "End turn", 70, 3));
        board.Nodes.Add(Node("d", t2.Id, NotesOpKind.Card, "Defend", 66, 4, NodeState.Speculated));
        board.Nodes.Add(Node("e", t2.Id, NotesOpKind.EndTurn, "End turn", 61, 5));
        // second branch in turn 2 (leaf without end turn)
        board.Nodes.Add(Node("f", t2.Id, NotesOpKind.Card, "Bash", 58, 6));

        board.Edges.Add(new NotesEdge { Id = "e1", From = "a", To = "b" });
        board.Edges.Add(new NotesEdge { Id = "e2", From = "b", To = "c" });
        board.Edges.Add(new NotesEdge { Id = "e3", From = "c", To = "d" });
        board.Edges.Add(new NotesEdge { Id = "e4", From = "d", To = "e" });
        board.Edges.Add(new NotesEdge { Id = "e5", From = "c", To = "f" });

        var stats = NotesStats.Compute(board, line.Id);

        Assert.Equal(2, stats.TurnCount);
        Assert.Equal(6, stats.NodeCount);
        Assert.Equal(2, stats.BranchCount);          // leaves: e, f
        Assert.Equal(1, stats.SurvivingBranches);    // e is an end-turn node
        Assert.Equal(1, stats.TriedCount);
        Assert.Equal(1, stats.SpeculatedCount);
        Assert.Single(stats.Potions);
        Assert.Equal("Fire Potion", stats.Potions[0]);
        Assert.Equal(80 - 58, stats.DamageTaken);    // all HP drops along the ops
        Assert.Equal(58, stats.HpMin);
        Assert.Equal(80, stats.HpMax);
    }
}
