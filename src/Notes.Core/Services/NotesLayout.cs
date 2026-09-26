using Notes.Core.Documents;

namespace Notes.Core.Services;

/// <summary>
/// Deterministic auto-layout for structured mode: world line columns, stacked
/// turn regions, and branch chains inside a region. Structured nodes get their
/// X/Y written here; free canvas nodes keep their manual positions.
/// </summary>
public static class NotesLayout
{
    public const float NodeWidth = 184f;
    public const float NodeHeight = 62f;
    public const float DepthStep = 238f;
    public const float BranchGap = 20f;
    public const float RegionPadding = 24f;
    public const float RegionHeader = 40f;
    public const float RegionGap = 38f;
    public const float ColumnGap = 120f;
    public const float ColumnStartX = 60f;
    public const float RowStartY = 70f;
    public const float MinRegionWidth = 330f;
    public const float MinRegionHeight = 156f;
    public const int RootParallelSlots = 2;
    public const float OverviewCardWidth = 340f;
    public const float OverviewCardHeight = 132f;
    public const float OverviewCardGapX = 24f;
    public const float OverviewCardGapY = 18f;
    public const int OverviewColumns = 3;
    public const float BoundaryChipHeight = 18f;
    public const float BoundaryChipGap = 3f;
    public const float BoundaryStripPadding = 8f;

    /// <summary>Height reserved below the nodes for boundary annotations so the
    /// chips never overlap the node area (and are not covered by node controls).</summary>
    public static float BoundaryAreaHeight(NotesTurnRegion region)
    {
        var count = region.TurnEvents.Count(e => NotesAnnotation.IsBoundaryRef(e.RefId));
        return count == 0 ? 0f : BoundaryStripPadding + count * (BoundaryChipHeight + BoundaryChipGap);
    }

    public static void Apply(NotesBoard board)
    {
        var columnX = ColumnStartX;
        foreach (var line in board.WorldLines)
        {
            var regions = board.RegionsOf(line.Id).ToList();
            var sizes = new List<(float Width, float Height)>(regions.Count);
            foreach (var region in regions)
            {
                LayoutRegionNodes(board, region, 0f, 0f);
                sizes.Add(MeasureRegion(board, region));
            }

            var columnWidth = sizes.Count == 0
                ? MinRegionWidth
                : MathF.Max(MinRegionWidth, sizes.Max(s => s.Width));
            var y = RowStartY;
            for (var i = 0; i < regions.Count; i++)
            {
                var region = regions[i];
                region.X = columnX;
                region.Y = y;
                region.Width = columnWidth;
                region.Height = sizes[i].Height;
                LayoutRegionNodes(board, region, region.X, region.Y);
                y += region.Height + RegionGap;
            }
            columnX += columnWidth + ColumnGap;
        }
    }

    private static (float Width, float Height) MeasureRegion(NotesBoard board, NotesTurnRegion region)
    {
        var nodes = board.NodesOfRegion(region.Id).ToList();
        if (nodes.Count == 0)
        {
            return (MinRegionWidth, MinRegionHeight + BoundaryAreaHeight(region));
        }
        var maxX = nodes.Max(n => n.X) + NodeWidth + RegionPadding;
        var maxY = nodes.Max(n => n.Y) + NodeHeight + RegionPadding + BoundaryAreaHeight(region);
        return (MathF.Max(MinRegionWidth, maxX), MathF.Max(MinRegionHeight, maxY));
    }

    private static void LayoutRegionNodes(NotesBoard board, NotesTurnRegion region, float originX, float originY)
    {
        var nodes = board.NodesOfRegion(region.Id).ToList();
        if (nodes.Count == 0)
        {
            return;
        }

        var nodeById = nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var edges = board.EdgesOfRegion(region.Id).ToList();
        var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var hasParent = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            if (!children.TryGetValue(edge.From, out var list))
            {
                list = new List<string>();
                children[edge.From] = list;
            }
            list.Add(edge.To);
            hasParent.Add(edge.To);
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var roots = nodes.Where(n => !hasParent.Contains(n.Id)).ToList();
        var cursor = originY + RegionHeader + RegionPadding;

        foreach (var root in roots)
        {
            if (!visited.Add(root.Id))
            {
                continue;
            }
            LayoutSubtree(root.Id, 0, ref cursor, originX, nodeById, children, visited);
            cursor += BranchGap;
        }

        // Cycles / detached leftovers: park them in a fallback column.
        foreach (var node in nodes.Where(n => !visited.Contains(n.Id)))
        {
            node.X = originX + RegionPadding;
            node.Y = cursor;
            cursor += NodeHeight + BranchGap;
            visited.Add(node.Id);
        }
    }

    private static float LayoutSubtree(
        string nodeId,
        int depth,
        ref float cursor,
        float originX,
        Dictionary<string, NotesNode> nodeById,
        Dictionary<string, List<string>> children,
        HashSet<string> visited)
    {
        var node = nodeById[nodeId];
        node.X = originX + RegionPadding + depth * DepthStep;
        var kids = children.TryGetValue(nodeId, out var list)
            ? list.Where(kid => nodeById.ContainsKey(kid) && visited.Add(kid)).ToList()
            : new List<string>();

        if (kids.Count == 0)
        {
            node.Y = cursor;
            cursor += NodeHeight;
            return node.Y;
        }

        float first = 0f;
        float last = 0f;
        for (var i = 0; i < kids.Count; i++)
        {
            var childY = LayoutSubtree(kids[i], depth + 1, ref cursor, originX, nodeById, children, visited);
            if (i == 0)
            {
                first = childY;
            }
            last = childY;
            if (i < kids.Count - 1)
            {
                cursor += BranchGap;
            }
        }
        node.Y = (first + last) / 2f;
        return node.Y;
    }

    /// <summary>Overview-board cards: the read-only current line first, then one
    /// card per interactive world-line board, in a grid.</summary>
    public static List<OverviewCard> OverviewCards(NotesDocument document)
    {
        var boards = new List<NotesBoard>();
        var current = document.Boards.FirstOrDefault(b => b.Kind == BoardKind.Current);
        if (current != null)
        {
            boards.Add(current);
        }
        boards.AddRange(document.WorldLineBoards);

        var cards = new List<OverviewCard>();
        var index = 0;
        foreach (var board in boards)
        {
            var column = index % OverviewColumns;
            var row = index / OverviewColumns;
            cards.Add(new OverviewCard(
                board.Id,
                ColumnStartX + column * (OverviewCardWidth + OverviewCardGapX),
                RowStartY + row * (OverviewCardHeight + OverviewCardGapY),
                OverviewCardWidth,
                OverviewCardHeight));
            index++;
        }
        return cards;
    }

    public static bool IsRoot(NotesBoard board, NotesNode node)
    {
        if (node.RegionId.Length == 0)
        {
            return false;
        }
        return !board.EdgesOfRegion(node.RegionId).Any(e => e.To == node.Id);
    }

    /// <summary>Effective number of next-step slots for a node: an explicit
    /// count wins, otherwise roots offer a parallel pair and later steps one.</summary>
    public static int EffectiveSlotCount(NotesBoard board, NotesNode node) =>
        node.NextSlotCount > 0
            ? node.NextSlotCount
            : (IsRoot(board, node) ? RootParallelSlots : 1);

    /// <summary>Free next-step slot positions for a region while the player is
    /// dragging an unplaced legend. Empty regions offer one root slot.</summary>
    public static List<NotesSlot> FreeSlots(NotesBoard board, NotesTurnRegion region)
    {
        var slots = new List<NotesSlot>();
        var nodes = board.NodesOfRegion(region.Id).ToList();
        if (nodes.Count == 0)
        {
            slots.Add(new NotesSlot("", region.X + RegionPadding, region.Y + RegionHeader + RegionPadding));
            return slots;
        }

        var childCount = board.EdgesOfRegion(region.Id)
            .GroupBy(e => e.From)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            var desired = EffectiveSlotCount(board, node);
            var existing = childCount.TryGetValue(node.Id, out var count) ? count : 0;
            var free = Math.Max(0, desired - existing);
            for (var i = 0; i < free; i++)
            {
                slots.Add(new NotesSlot(
                    node.Id,
                    node.X + DepthStep,
                    node.Y + (existing + i) * (NodeHeight + BranchGap)));
            }
        }
        return slots;
    }
}

public readonly record struct NotesSlot(string ParentId, float X, float Y);

/// <summary>Clickable world-line summary card on the overview board.</summary>
public readonly record struct OverviewCard(string BoardId, float X, float Y, float Width, float Height);
