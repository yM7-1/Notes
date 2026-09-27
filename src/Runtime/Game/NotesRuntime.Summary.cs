using Notes.Core.Documents;
using Notes.Core.Services;

namespace Notes.Game;

/// <summary>NotesRuntime partial: post-combat recap board generation.</summary>
internal static partial class NotesRuntime
{
    /// <summary>Builds the post-combat recap board once the combat actually
    /// ended (no CombatEnded event exists, so this is detected in Tick).</summary>
    private static void ExpireCombatSummary()
    {
        if (!_combatSummaryPending || GameContext.InCombat)
        {
            return;
        }
        _combatSummaryPending = false;
        GenerateCombatSummary();
    }
    /// <summary>Recap board: totals + one line per turn, built from the captured
    /// op log and the current world line's turn events. Purely local.</summary>
    private static void GenerateCombatSummary()
    {
        if (!RunActive)
        {
            return;
        }
        var ops = NotesOpLog.Entries.ToList();
        if (ops.Count == 0)
        {
            return;
        }
        var current = RunDocument.EnsureCurrentBoard(CurrentBoardName());
        var turns = ops.Where(o => o.Turn > 0).Select(o => o.Turn).Distinct().OrderBy(t => t).ToList();
        var name = _combatSummaryName.Length > 0
            ? _combatSummaryName
            : ModLocalization.T("summary_default_name", "战斗");
        var floor = _combatSummaryFloor > 0
            ? "  ·  " + Format("summary_floor", "第 {0} 层", _combatSummaryFloor)
            : "";

        var board = new NotesBoard
        {
            Id = IdFactory.NewBoardId(),
            Name = ModLocalization.T("summary_board_name", "复盘") + " · " + name,
            Kind = BoardKind.Summary,
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };

        var cards = ops.Count(o => o.Kind == NotesOpKind.Card);
        var potions = ops.Count(o => o.Kind == NotesOpKind.Potion);
        var damage = 0;
        var kills = 0;
        foreach (var op in ops)
        {
            foreach (var annotation in op.Annotations)
            {
                if (!AnnotationProtocol.IsDamage(annotation.RefId))
                {
                    continue;
                }
                damage += annotation.Count;
                if (annotation.Meta.EndsWith(AnnotationProtocol.Separator + "1", StringComparison.Ordinal))
                {
                    kills++;
                }
            }
        }
        var losses = current.TurnRegions.Sum(r => r.TurnEvents
            .Where(a => AnnotationProtocol.IsLoss(a.RefId))
            .Sum(a => a.Count));

        var y = 0f;
        board.Nodes.Add(TextNode(
            Format("summary_title", "战斗复盘：{0}", name + floor), 0, ref y));
        board.Nodes.Add(TextNode(
            Format("summary_stats", "回合 {0} · 出牌 {1} · 伤害 {2} · 战损 {3} · 击杀 {4} · 药水 {5}",
                turns.Count, cards, damage, losses, kills, potions), 0, ref y));
        foreach (var turn in turns)
        {
            var plays = ops.Count(o => o.Turn == turn && o.Kind == NotesOpKind.Card);
            var dealt = ops.Where(o => o.Turn == turn).Sum(o => o.Annotations
                .Where(a => AnnotationProtocol.IsDamage(a.RefId))
                .Sum(a => a.Count));
            var lost = current.TurnRegions.FirstOrDefault(r => r.TurnNumber == turn)?.TurnEvents
                .Where(a => AnnotationProtocol.IsLoss(a.RefId))
                .Sum(a => a.Count) ?? 0;
            board.Nodes.Add(TextNode(
                Format("summary_turn", "第{0}回合：出牌 {1} · 伤害 {2} · 战损 {3}", turn, plays, dealt, lost), 0, ref y));
        }
        for (var i = 1; i < board.Nodes.Count; i++)
        {
            board.Edges.Add(new NotesEdge
            {
                Id = IdFactory.NewEdgeId(),
                From = board.Nodes[i - 1].Id,
                To = board.Nodes[i].Id,
            });
        }

        RunDocument.Boards.Add(board);
        RunDocument.ActiveBoardId = board.Id;
        MegaCrit.Sts2.Core.Logging.Log.Info($"[Notes] combat recap board created: {board.Name}");
        Raise();
        SetImportMessage(ModLocalization.T("summary_created", "已生成战斗复盘") + ": " + board.Name);
    }
    private static NotesNode TextNode(string title, float x, ref float y)
    {
        var node = new NotesNode
        {
            Id = IdFactory.NewNodeId(),
            Kind = NodeKind.Text,
            Title = title,
            Cost = -1,
            CardType = -1,
            Rarity = -1,
            X = x,
            Y = y,
        };
        y += 78f;
        return node;
    }
    private static string Format(string key, string fallback, params object[] args)
    {
        var text = ModLocalization.T(key, fallback);
        for (var i = 0; i < args.Length; i++)
        {
            text = text.Replace("{" + i + "}", args[i]?.ToString() ?? "");
        }
        return text;
    }
}
