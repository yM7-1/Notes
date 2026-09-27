using Notes.Core.Documents;
using Notes.Core.Services;
using Xunit;

namespace Notes.Tests;

public class NotesCompareTests
{
    private static (NotesBoard Board, string LineId) Line(string name)
    {
        var board = new NotesBoard { Id = name + "-board", Name = name };
        var line = new NotesWorldLine { Id = name + "-line", Name = name };
        board.WorldLines.Add(line);
        return (board, line.Id);
    }

    private static void AddTurn(NotesBoard board, string lineId, int turn, int hp, params string[] titles)
    {
        var region = new NotesTurnRegion { Id = $"{board.Id}-r{turn}", WorldLineId = lineId, TurnNumber = turn, Hp = hp };
        board.TurnRegions.Add(region);
        var index = 0;
        foreach (var title in titles)
        {
            board.Nodes.Add(new NotesNode
            {
                Id = $"{board.Id}-n{turn}-{index}",
                Kind = Notes.Core.Documents.NodeKind.Card,
                Title = title,
                RegionId = region.Id,
                OrderMs = index,
            });
            index++;
        }
    }

    [Fact]
    public void Compare_IdenticalLines_ReportsNoDivergence()
    {
        var (left, leftLine) = Line("left");
        var (right, rightLine) = Line("right");
        AddTurn(left, leftLine, 1, 70, "Strike", "Defend");
        AddTurn(right, rightLine, 1, 70, "Strike", "Defend");

        var comparison = NotesCompare.Compare(left, leftLine, right, rightLine);

        Assert.Equal(0, comparison.FirstDivergingTurn);
        var turn = Assert.Single(comparison.Turns);
        Assert.False(turn.Diverges);
        Assert.Equal(2, turn.LeftNodes);
        Assert.Equal(70, turn.LeftHp);
    }

    [Fact]
    public void Compare_ReportsFirstDivergingTurn()
    {
        var (left, leftLine) = Line("left");
        var (right, rightLine) = Line("right");
        AddTurn(left, leftLine, 1, 70, "Strike", "Defend");
        AddTurn(right, rightLine, 1, 70, "Strike", "Defend");
        AddTurn(left, leftLine, 2, 60, "Strike", "Defend");
        AddTurn(right, rightLine, 2, 50, "Strike", "Bash");
        AddTurn(left, leftLine, 3, 55, "Defend");
        AddTurn(right, rightLine, 3, 45, "Defend");

        var comparison = NotesCompare.Compare(left, leftLine, right, rightLine);

        Assert.Equal(2, comparison.FirstDivergingTurn);
        Assert.True(comparison.Turns.Single(t => t.Turn == 2).Diverges);
        Assert.False(comparison.Turns.Single(t => t.Turn == 3).Diverges);
    }

    [Fact]
    public void Compare_ExtraTurnOnOneSide_Diverges()
    {
        var (left, leftLine) = Line("left");
        var (right, rightLine) = Line("right");
        AddTurn(left, leftLine, 1, 70, "Strike");
        AddTurn(right, rightLine, 1, 70, "Strike");
        AddTurn(right, rightLine, 2, 62, "Defend");

        var comparison = NotesCompare.Compare(left, leftLine, right, rightLine);

        Assert.Equal(2, comparison.FirstDivergingTurn);
        Assert.Equal(0, comparison.Turns.Single(t => t.Turn == 2).LeftNodes);
        Assert.Equal(1, comparison.Turns.Single(t => t.Turn == 2).RightNodes);
    }

    [Fact]
    public void TurnDelta_ComparesAgainstPreviousTurn()
    {
        var (board, line) = Line("l");
        AddTurn(board, line, 1, 70, "Strike");
        AddTurn(board, line, 2, 58, "Strike", "Defend");

        var delta = NotesCompare.TurnDelta(board, line, 2)!;

        Assert.True(delta.HasPrevious);
        Assert.Equal(-12, delta.HpDelta);
        Assert.Equal(1, delta.NodeDelta);
        Assert.False(NotesCompare.TurnDelta(board, line, 1)!.HasPrevious);
        Assert.Null(NotesCompare.TurnDelta(board, line, 9));
    }
}
