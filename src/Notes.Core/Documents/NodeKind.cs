namespace Notes.Core.Documents;

/// <summary>Kind of a board node. Card nodes carry a snapshot of a card; text
/// nodes are free-form notes (events, relics, plans).</summary>
public enum NodeKind
{
    Card = 0,
    Text = 1,
}
