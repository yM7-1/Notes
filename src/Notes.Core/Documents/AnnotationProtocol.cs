namespace Notes.Core.Documents;

/// <summary>
/// Single source of truth for the <see cref="NotesAnnotation.RefId"/> protocol of
/// auto-captured annotations ("exhaust:", "discard:", "relic:", "damage:",
/// "insert:", "loss:", "card:"). Structured payload parts (enemy / pile / card
/// id, damage source / killed flag) are joined with <see cref="Separator"/>.
/// </summary>
public static class AnnotationProtocol
{
    /// <summary>Unit separator between structured payload parts.</summary>
    public const char Separator = '\u001f';

    public const string Exhaust = "exhaust:";
    public const string Discard = "discard:";
    public const string Relic = "relic:";
    public const string Damage = "damage:";
    public const string Insert = "insert:";
    public const string Loss = "loss:";
    public const string Card = "card:";

    public static string RelicRef(string relicId) => Relic + relicId;

    public static string DamageRef(string targetName) => Damage + targetName;

    public static string InsertRef(string enemy, string pileKey, string cardId) =>
        Insert + Join(enemy, pileKey, cardId);

    public static string Join(params string[] parts) => string.Join(Separator, parts);

    public static string[] Split(string payload) => payload.Split(Separator);

    public static bool IsRelic(string refId) => refId.StartsWith(Relic, StringComparison.Ordinal);

    public static bool IsExhaust(string refId) => refId.StartsWith(Exhaust, StringComparison.Ordinal);

    public static bool IsDiscard(string refId) => refId.StartsWith(Discard, StringComparison.Ordinal);

    public static bool IsDamage(string refId) => refId.StartsWith(Damage, StringComparison.Ordinal);

    public static bool IsInsert(string refId) => refId.StartsWith(Insert, StringComparison.Ordinal);

    public static bool IsLoss(string refId) => refId.StartsWith(Loss, StringComparison.Ordinal);

    public static bool IsCard(string refId) => refId.StartsWith(Card, StringComparison.Ordinal);

    /// <summary>Annotations that belong on the strip between two turn regions:
    /// unattributed exhausts / discards, enemy card insertions, damage taken
    /// during the enemy turn.</summary>
    public static bool IsBoundary(string refId) =>
        IsExhaust(refId) || IsDiscard(refId) || IsInsert(refId) || IsLoss(refId);
}
