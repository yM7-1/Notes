using Notes.Core.Documents;

namespace Notes.Core.Services;

public sealed class NotesImportPlan
{
    public List<NotesNode> Nodes { get; } = new();

    public List<NotesEdge> Edges { get; } = new();
}

/// <summary>
/// Turns captured operations into structured nodes + chain edges for one turn
/// region. Idempotent: ops already present (by <see cref="NotesNode.SourceOpId"/>)
/// are skipped, and new nodes chain after the region's current tail.
/// </summary>
public static class NotesImporter
{
    public static NotesImportPlan Plan(
        NotesBoard board,
        NotesTurnRegion region,
        IReadOnlyList<NotesOpData> ops,
        bool replaceImported = false)
    {
        var plan = new NotesImportPlan();
        var existingOpIds = replaceImported
            ? new HashSet<string>(StringComparer.Ordinal)
            : board.NodesOfRegion(region.Id)
                .Select(n => n.SourceOpId)
                .Where(id => id.Length > 0)
                .ToHashSet(StringComparer.Ordinal);

        var turnOps = ops
            .Where(o => o.Turn == region.TurnNumber
                && o.Kind != NotesOpKind.TurnEvent
                && o.Kind != NotesOpKind.Exhaust // exhausts live in op annotations / turn events
                && o.Kind != NotesOpKind.Discard) // discards live in op annotations / turn events
            .OrderBy(o => o.UnixMs)
            .ToList();

        var tail = replaceImported
            ? FindManualTailId(board, region)
            : FindTailId(board, region, turnOps, existingOpIds);
        foreach (var op in turnOps)
        {
            if (existingOpIds.Contains(op.Id))
            {
                continue;
            }
            var node = new NotesNode
            {
                Id = IdFactory.NewNodeId(),
                Kind = MapKind(op.Kind),
                Title = op.Title,
                RefId = op.RefId,
                Cost = op.Cost,
                CardType = op.CardType,
                Rarity = op.Rarity,
                Upgraded = op.Upgraded,
                Meta = op.Meta,
                Snapshot = op.Snapshot,
                Hp = op.Hp,
                MaxHp = op.MaxHp,
                OrderMs = op.UnixMs,
                SourceOpId = op.Id,
                RegionId = region.Id,
                State = NodeState.Tried,
                Annotations = op.Annotations.Select(a => a.Clone()).ToList(),
            };
            plan.Nodes.Add(node);
            if (tail != null)
            {
                plan.Edges.Add(new NotesEdge
                {
                    Id = IdFactory.NewEdgeId(),
                    From = tail,
                    To = node.Id,
                });
            }
            tail = node.Id;
        }

        return plan;
    }

    /// <summary>Commands that materialize a turn import. With
    /// <paramref name="replaceImported"/> the previously imported nodes of the
    /// region are removed first, so "record this turn" overwrites instead of
    /// appending; manually placed nodes survive.</summary>
    public static List<INotesCommand> BuildCommands(
        NotesDocument document,
        NotesBoard board,
        NotesTurnRegion region,
        IReadOnlyList<NotesOpData> ops,
        bool replaceImported)
    {
        var commands = new List<INotesCommand>();
        var plan = Plan(board, region, ops, replaceImported);
        if (replaceImported)
        {
            commands.AddRange(board.NodesOfRegion(region.Id)
                .Where(n => n.SourceOpId.Length > 0)
                .Select(n => (INotesCommand)new RemoveNodeCommand(document, board.Id, n.Id)));
        }
        commands.AddRange(plan.Nodes.Select(n => (INotesCommand)new AddNodeCommand(document, board.Id, n)));
        commands.AddRange(plan.Edges.Select(e => (INotesCommand)new AddEdgeCommand(document, board.Id, e)));
        return commands;
    }

    /// <summary>Rebuilds one turn region in place (no command stack), keeping the
    /// ids of nodes that are already there (stable UI while it refreshes). Used
    /// by the read-only current-world-line board, derived from the op log.</summary>
    public static int Apply(
        NotesDocument document,
        NotesBoard board,
        NotesTurnRegion region,
        IReadOnlyList<NotesOpData> ops)
    {
        var existingByOp = board.NodesOfRegion(region.Id)
            .Where(n => n.SourceOpId.Length > 0)
            .ToDictionary(n => n.SourceOpId, StringComparer.Ordinal);
        var plan = Plan(board, region, ops, replaceImported: true);
        var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var planOps = new HashSet<string>(StringComparer.Ordinal);
        var changed = 0;
        foreach (var node in plan.Nodes)
        {
            planOps.Add(node.SourceOpId);
            if (existingByOp.TryGetValue(node.SourceOpId, out var existing))
            {
                existing.CopyFrom(node, includePosition: false);
                idMap[node.Id] = existing.Id;
            }
            else
            {
                if (document.AddNode(board.Id, node))
                {
                    changed++;
                }
                idMap[node.Id] = node.Id;
            }
        }
        foreach (var stale in existingByOp.Values.Where(n => !planOps.Contains(n.SourceOpId)).ToList())
        {
            document.RemoveNode(board.Id, stale.Id);
            changed++;
        }

        var importedIds = existingByOp.Values.Select(n => n.Id)
            .Concat(idMap.Values)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var edge in board.EdgesOfRegion(region.Id)
            .Where(e => importedIds.Contains(e.From) || importedIds.Contains(e.To))
            .ToList())
        {
            document.RemoveEdge(board.Id, edge.Id);
        }
        foreach (var edge in plan.Edges)
        {
            var from = idMap.TryGetValue(edge.From, out var mappedFrom) ? mappedFrom : edge.From;
            var to = idMap.TryGetValue(edge.To, out var mappedTo) ? mappedTo : edge.To;
            document.AddEdge(board.Id, new NotesEdge { Id = IdFactory.NewEdgeId(), From = from, To = to });
        }

        var last = ops
            .Where(o => o.Turn == region.TurnNumber
                && o.Kind != NotesOpKind.TurnEvent
                && o.Kind != NotesOpKind.Exhaust
                && o.Kind != NotesOpKind.Discard)
            .OrderBy(o => o.UnixMs)
            .LastOrDefault();
        if (last != null)
        {
            region.Snapshot = last.Snapshot;
            region.Hp = last.Hp;
            region.MaxHp = last.MaxHp;
        }
        return changed;
    }

    /// <summary>Tail among manually placed nodes only (used when imported nodes
    /// are about to be replaced).</summary>
    private static string? FindManualTailId(NotesBoard board, NotesTurnRegion region)
    {
        var manual = board.NodesOfRegion(region.Id)
            .Where(n => n.SourceOpId.Length == 0)
            .ToList();
        if (manual.Count == 0)
        {
            return null;
        }
        var manualIds = manual.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        var hasChild = board.EdgesOfRegion(region.Id)
            .Where(e => manualIds.Contains(e.From) && manualIds.Contains(e.To))
            .Select(e => e.From)
            .ToHashSet(StringComparer.Ordinal);
        return manual.FirstOrDefault(n => !hasChild.Contains(n.Id))?.Id;
    }

    private static string? FindTailId(
        NotesBoard board,
        NotesTurnRegion region,
        IReadOnlyList<NotesOpData> turnOps,
        HashSet<string> existingOpIds)
    {
        var nodes = board.NodesOfRegion(region.Id).ToList();
        if (nodes.Count == 0)
        {
            return null;
        }
        var hasChild = board.EdgesOfRegion(region.Id).Select(e => e.From).ToHashSet(StringComparer.Ordinal);
        var tails = nodes.Where(n => !hasChild.Contains(n.Id)).ToList();
        if (tails.Count == 0)
        {
            return null;
        }
        // Prefer the tail that was imported last (latest op order).
        var order = turnOps.Select((op, index) => (op.Id, index)).ToDictionary(p => p.Id, p => p.index, StringComparer.Ordinal);
        return tails
            .OrderByDescending(t => t.SourceOpId.Length > 0 && order.TryGetValue(t.SourceOpId, out var index) ? index : -1)
            .First()
            .Id;
    }

    public static NodeKind MapKind(NotesOpKind kind) => kind switch
    {
        NotesOpKind.Card => NodeKind.Card,
        NotesOpKind.Potion => NodeKind.Potion,
        NotesOpKind.Relic => NodeKind.Relic,
        NotesOpKind.Draw => NodeKind.Draw,
        NotesOpKind.Discard => NodeKind.Discard,
        NotesOpKind.EndTurn => NodeKind.EndTurn,
        NotesOpKind.Exhaust => NodeKind.Exhaust,
        _ => NodeKind.Text,
    };
}
