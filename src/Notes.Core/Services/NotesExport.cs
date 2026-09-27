using System.Text;
using Notes.Core.Documents;

namespace Notes.Core.Services;

/// <summary>Localizable labels for <see cref="NotesExport.ToMarkdown"/>.</summary>
public sealed record NotesExportLabels(string Turn, string Hp, string FreeNodes, string Note);

/// <summary>Markdown export of one board (pure; unit-tested).</summary>
public static class NotesExport
{
    public static string ToMarkdown(NotesBoard board, NotesExportLabels labels)
    {
        var sb = new StringBuilder();
        sb.Append("# ").Append(board.Name).Append('\n').Append('\n');
        foreach (var line in board.WorldLines)
        {
            sb.Append("## ").Append(line.Name).Append('\n').Append('\n');
            foreach (var region in board.RegionsOf(line.Id))
            {
                sb.Append("### ").Append(labels.Turn).Append(' ').Append(region.TurnNumber);
                if (region.Hp >= 0)
                {
                    sb.Append(" · ").Append(labels.Hp).Append(' ').Append(region.Hp);
                }
                sb.Append('\n');
                foreach (var annotation in region.TurnEvents)
                {
                    sb.Append("- ").Append(annotation.Text).Append('\n');
                }
                foreach (var node in board.NodesOfRegion(region.Id).OrderBy(n => n.OrderMs))
                {
                    AppendNode(sb, node, labels);
                }
                sb.Append('\n');
            }
        }
        var free = board.Nodes
            .Where(n => n.RegionId.Length == 0)
            .OrderBy(n => n.Y)
            .ThenBy(n => n.X)
            .ToList();
        if (free.Count > 0)
        {
            sb.Append("## ").Append(labels.FreeNodes).Append('\n').Append('\n');
            foreach (var node in free)
            {
                AppendNode(sb, node, labels);
            }
        }
        return sb.ToString();
    }

    private static void AppendNode(StringBuilder sb, NotesNode node, NotesExportLabels labels)
    {
        sb.Append("- ");
        sb.Append(StateMark(node.State));
        sb.Append(node.Title).Append(node.Upgraded ? "+" : "");
        if (node.Kind == NodeKind.Card && node.Cost >= 0)
        {
            sb.Append(" (").Append(node.Cost).Append(')');
        }
        sb.Append('\n');
        if (node.Note.Length > 0)
        {
            sb.Append("  > ").Append(labels.Note).Append(": ")
                .Append(node.Note.Replace('\n', ' ')).Append('\n');
        }
    }

    private static string StateMark(NodeState state) => state switch
    {
        NodeState.Tried => "[x] ",
        NodeState.Speculated => "[?] ",
        NodeState.Confirmed => "[*] ",
        _ => "",
    };
}
