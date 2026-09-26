using HarmonyLib;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Entities.Cards;
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

/// <summary>Draw / discard capture (synchronous pile hook).</summary>
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
