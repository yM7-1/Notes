using Notes.Core.Documents;

namespace Notes.Core.Services;

public sealed class NotesWorldLineStats
{
    public int TurnCount { get; set; }

    public int NodeCount { get; set; }

    /// <summary>Leaf count (paths through the line, at least 1).</summary>
    public int BranchCount { get; set; }

    /// <summary>Leaves that reached an end-of-turn node (the line survived the turn).</summary>
    public int SurvivingBranches { get; set; }

    public int TriedCount { get; set; }

    public int SpeculatedCount { get; set; }

    public int ConfirmedCount { get; set; }

    public int PotionCount { get; set; }

    public List<string> Potions { get; set; } = new();

    /// <summary>Total HP lost along the captured steps (healing not credited).</summary>
    public int DamageTaken { get; set; }

    public int HpMin { get; set; } = -1;

    public int HpMax { get; set; } = -1;
}

/// <summary>Aggregates a world line for the overview panel.</summary>
public static class NotesStats
{
    public static NotesWorldLineStats Compute(NotesBoard board, string worldLineId)
    {
        var stats = new NotesWorldLineStats();
        var regions = board.RegionsOf(worldLineId).ToList();
        stats.TurnCount = regions.Count;

        var nodes = regions.SelectMany(region => board.NodesOfRegion(region.Id)).ToList();
        stats.NodeCount = nodes.Count;
        foreach (var node in nodes)
        {
            switch (node.State)
            {
                case NodeState.Tried:
                    stats.TriedCount++;
                    break;
                case NodeState.Speculated:
                    stats.SpeculatedCount++;
                    break;
                case NodeState.Confirmed:
                    stats.ConfirmedCount++;
                    break;
            }
            if (node.Kind == NodeKind.Potion && node.Title.Length > 0
                && !stats.Potions.Contains(node.Title))
            {
                stats.Potions.Add(node.Title);
            }
        }
        stats.PotionCount = stats.Potions.Count;

        var nodeIds = nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        var hasChild = new HashSet<string>(StringComparer.Ordinal);
        // Count across the whole line: edges may cross turn regions.
        foreach (var edge in board.Edges)
        {
            if (nodeIds.Contains(edge.From) && nodeIds.Contains(edge.To))
            {
                hasChild.Add(edge.From);
            }
        }
        var leaves = nodes.Where(n => !hasChild.Contains(n.Id)).ToList();
        stats.BranchCount = Math.Max(1, leaves.Count);
        stats.SurvivingBranches = leaves.Count(n => n.Kind == NodeKind.EndTurn);

        // Prefer the end-of-turn HP series of the regions: it captures damage
        // between turns (enemy turn) and does not depend on nodes being imported.
        var regionHp = regions
            .Where(r => r.Hp > 0)
            .OrderBy(r => r.TurnNumber)
            .Select(r => r.Hp)
            .ToList();
        if (regionHp.Count >= 2)
        {
            var regionPrevious = -1;
            foreach (var hp in regionHp)
            {
                if (regionPrevious > 0 && hp < regionPrevious)
                {
                    stats.DamageTaken += regionPrevious - hp;
                }
                regionPrevious = hp;
                stats.HpMin = stats.HpMin < 0 ? hp : Math.Min(stats.HpMin, hp);
                stats.HpMax = stats.HpMax < 0 ? hp : Math.Max(stats.HpMax, hp);
            }
            return stats;
        }

        var ordered = nodes
            .Where(n => n.Hp > 0)
            .OrderBy(n => n.OrderMs)
            .ToList();
        var previous = -1;
        foreach (var node in ordered)
        {
            if (previous > 0 && node.Hp < previous)
            {
                stats.DamageTaken += previous - node.Hp;
            }
            previous = node.Hp;
            stats.HpMin = stats.HpMin < 0 ? node.Hp : Math.Min(stats.HpMin, node.Hp);
            stats.HpMax = stats.HpMax < 0 ? node.Hp : Math.Max(stats.HpMax, node.Hp);
        }

        return stats;
    }
}
