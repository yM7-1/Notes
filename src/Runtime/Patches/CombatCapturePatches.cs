using HarmonyLib;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using Notes.Game;

namespace Notes.Patches;

/// <summary>Card play capture (synchronous history hook).</summary>
[HarmonyPatch(typeof(CombatHistory), nameof(CombatHistory.CardPlayStarted))]
internal static class CardPlayStartedPatch
{
    [HarmonyPostfix]
    private static void Postfix(CardPlay cardPlay) => NotesOpLog.OnCardPlayed(cardPlay);
}

/// <summary>Draw / discard / exhaust capture (synchronous pile hook).</summary>
[HarmonyPatch(typeof(CardPile), nameof(CardPile.AddInternal))]
internal static class CardPileAddPatch
{
    [HarmonyPostfix]
    private static void Postfix(CardPile __instance, CardModel card) =>
        NotesOpLog.OnCardAdded(__instance.Type, card);
}

/// <summary>Manual potion use capture.</summary>
[HarmonyPatch(typeof(PotionModel), nameof(PotionModel.EnqueueManualUse))]
internal static class PotionUsePatch
{
    [HarmonyPrefix]
    private static void Prefix(PotionModel __instance) => NotesOpLog.OnPotionQueued(__instance);
}

/// <summary>Damage dealt by the local player: target / amount / kill.</summary>
[HarmonyPatch(typeof(CombatHistory), nameof(CombatHistory.DamageReceived))]
internal static class DamageReceivedPatch
{
    [HarmonyPostfix]
    private static void Postfix(Creature receiver, Creature? dealer, DamageResult result, CardModel? cardSource) =>
        NotesOpLog.OnDamage(receiver, dealer, result, cardSource);
}

/// <summary>Cards generated with no player creator (enemy insertions).</summary>
[HarmonyPatch(typeof(CombatHistory), nameof(CombatHistory.CardGenerated))]
internal static class CardGeneratedPatch
{
    [HarmonyPostfix]
    private static void Postfix(CardModel card, Player? creator) => NotesOpLog.OnCardGenerated(card, creator);
}

/// <summary>Monster move start: remember the acting monster for insert attribution.</summary>
[HarmonyPatch(typeof(MonsterModel), nameof(MonsterModel.PerformMove))]
internal static class MonsterPerformMovePatch
{
    [HarmonyPrefix]
    private static void Prefix(MonsterModel __instance) => NotesOpLog.OnMonsterMoveStart(__instance);
}

/// <summary>Monster move end (history entry fires after the move completed).</summary>
[HarmonyPatch(typeof(CombatHistory), nameof(CombatHistory.MonsterPerformedMove))]
internal static class MonsterPerformedMovePatch
{
    [HarmonyPostfix]
    private static void Postfix(MonsterModel monster) => NotesOpLog.OnMonsterMoveEnd(monster);
}
