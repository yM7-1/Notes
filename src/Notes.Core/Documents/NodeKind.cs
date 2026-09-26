namespace Notes.Core.Documents;

/// <summary>Kind of a board node.</summary>
public enum NodeKind
{
    Card = 0,
    Text = 1,
    Draw = 2,
    Discard = 3,
    Potion = 4,
    Relic = 5,
    EndTurn = 6,
}
