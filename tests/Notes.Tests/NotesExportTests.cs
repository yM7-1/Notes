using Notes.Core.Documents;
using Notes.Core.Services;
using Xunit;

namespace Notes.Tests;

public class NotesExportTests
{
    private static readonly NotesExportLabels Labels = new("Turn", "HP", "Free nodes", "Note");

    [Fact]
    public void ToMarkdown_IncludesLinesTurnsNodesAndNotes()
    {
        var board = new NotesBoard { Id = "b", Name = "World lines" };
        var line = new NotesWorldLine { Id = "w", Name = "Actual" };
        board.WorldLines.Add(line);
        var region = new NotesTurnRegion { Id = "r", WorldLineId = "w", TurnNumber = 2, Hp = 58 };
        board.TurnRegions.Add(region);
        board.Nodes.Add(new NotesNode
        {
            Id = "n1",
            Kind = NodeKind.Card,
            Title = "Strike",
            Cost = 1,
            State = NodeState.Tried,
            RegionId = "r",
            Note = "first try",
        });
        board.Nodes.Add(new NotesNode { Id = "n2", Kind = NodeKind.Text, Title = "Idea" });

        var markdown = NotesExport.ToMarkdown(board, Labels);

        Assert.Contains("# World lines", markdown);
        Assert.Contains("## Actual", markdown);
        Assert.Contains("### Turn 2 · HP 58", markdown);
        Assert.Contains("- [x] Strike (1)", markdown);
        Assert.Contains("  > Note: first try", markdown);
        Assert.Contains("## Free nodes", markdown);
        Assert.Contains("- Idea", markdown);
    }

    [Fact]
    public void ToMarkdown_EmptyBoard_HasOnlyTitle()
    {
        var board = new NotesBoard { Id = "b", Name = "Empty" };

        var markdown = NotesExport.ToMarkdown(board, Labels);

        Assert.Equal("# Empty\n\n", markdown);
    }
}
