using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using Notes.Game;

namespace Notes.Patches;

/// <summary>Relics flash when they trigger; capture that as an annotation source
/// (RitsuLib has no relic-trigger lifecycle event).</summary>
[HarmonyPatch(typeof(RelicModel), nameof(RelicModel.Flash), new[] { typeof(IEnumerable<Creature>) })]
internal static class RelicFlashPatch
{
    [HarmonyPostfix]
    private static void Postfix(RelicModel __instance) => NotesOpLog.OnRelicFlash(__instance);
}
