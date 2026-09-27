using Notes.Core.Documents;

namespace Notes.Core.Services;

/// <summary>One turn compared across two world lines.</summary>
public sealed class NotesTurnDiff
{
    public int Turn { get; init; }

    public int LeftNodes { get; set; }

    public int RightNodes { get; set; }

    public int LeftHp { get; set; } = -1;

    public int RightHp { get; set; } = -1;

    public int LeftDamage { get; set; }

    public int RightDamage { get; set; }

    /// <summary>Node chains (titles, in op order) differ for this turn.</summary>
    public bool Diverges { get; set; }
}

/// <summary>Turn-aligned comparison of two world lines, with the first turn
/// where their chains diverge.</summary>
public sealed class NotesLineComparison
{
    public List<NotesTurnDiff> Turns { get; } = new();

    /// <summary>0 when the two lines never diverge.</summary>
    public int FirstDivergingTurn { get; set; }

    public int LeftTurnCount { get; set; }

    public int RightTurnCount { get; set; }
}

/// <summary>Pure comparison helpers (unit-tested; used by the compare dialog).</summary>
public static class NotesCompare
{
    public static NotesLineComparison Compare(
        NotesBoard left, string leftLineId, NotesBoard right, string rightLineId)
    {
        var comparison = new NotesLineComparison();
        var leftRegions = left.RegionsOf(leftLineId).ToDictionary(r => r.TurnNumber);
        var rightRegions = right.RegionsOf(rightLineId).ToDictionary(r => r.TurnNumber);
        comparison.LeftTurnCount = leftRegions.Count;
        comparison.RightTurnCount = rightRegions.Count;

        var turns = leftRegions.Keys.Concat(rightRegions.Keys).Distinct().OrderBy(t => t);
        foreach (var turn in turns)
        {
            leftRegions.TryGetValue(turn, out var leftRegion);
            rightRegions.TryGetValue(turn, out var rightRegion);
            var leftTitles = Titles(left, leftRegion);
            var rightTitles = Titles(right, rightRegion);
            var diff = new NotesTurnDiff
            {
                Turn = turn,
                LeftNodes = leftTitles.Count,
                RightNodes = rightTitles.Count,
                LeftHp = leftRegion?.Hp ?? -1,
                RightHp = rightRegion?.Hp ?? -1,
                LeftDamage = Damage(left, leftRegion),
                RightDamage = Damage(right, rightRegion),
                Diverges = !leftTitles.SequenceEqual(rightTitles),
            };
            if (diff.Diverges && comparison.FirstDivergingTurn == 0)
            {
                comparison.FirstDivergingTurn = turn;
            }
            comparison.Turns.Add(diff);
        }
        return comparison;
    }

    private static List<string> Titles(NotesBoard board, NotesTurnRegion? region) =>
        region == null
            ? new List<string>()
            : board.NodesOfRegion(region.Id)
                .OrderBy(n => n.OrderMs)
                .ThenBy(n => n.Title, StringComparer.Ordinal)
                .Select(n => n.Title)
                .ToList();

    private static int Damage(NotesBoard board, NotesTurnRegion? region) =>
        region == null
            ? 0
            : board.NodesOfRegion(region.Id).Sum(n => n.Annotations
                .Where(a => AnnotationProtocol.IsDamage(a.RefId))
                .Sum(a => a.Count));

    /// <summary>Change of one turn compared with the previous turn of the same
    /// line (HP / node count / damage dealt). Null when the turn is missing.</summary>
    public static NotesTurnDelta? TurnDelta(NotesBoard board, string lineId, int turn)
    {
        var regions = board.RegionsOf(lineId).ToList();
        if (regions.FirstOrDefault(r => r.TurnNumber == turn) is not { } current)
        {
            return null;
        }
        var previous = regions.LastOrDefault(r => r.TurnNumber < turn);
        var damage = Damage(board, current);
        if (previous == null)
        {
            return new NotesTurnDelta
            {
                Turn = turn,
                HasPrevious = false,
                NodeDelta = board.NodesOfRegion(current.Id).Count(),
                DamageDelta = damage,
                HpDelta = null,
            };
        }
        return new NotesTurnDelta
        {
            Turn = turn,
            HasPrevious = true,
            HpDelta = current.Hp >= 0 && previous.Hp >= 0 ? current.Hp - previous.Hp : null,
            NodeDelta = board.NodesOfRegion(current.Id).Count() - board.NodesOfRegion(previous.Id).Count(),
            DamageDelta = damage - Damage(board, previous),
        };
    }
}

/// <summary>Diff of one turn against the previous turn of the same world line.</summary>
public sealed class NotesTurnDelta
{
    public int Turn { get; init; }

    public bool HasPrevious { get; set; }

    /// <summary>Null when either side has no HP snapshot.</summary>
    public int? HpDelta { get; set; }

    public int NodeDelta { get; set; }

    public int DamageDelta { get; set; }
}
