using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace Notes.Game;

/// <summary>Read-only access to the current run / combat. Never mutates state.
/// The run state is tracked through <see cref="RunManager.RunStarted"/>, which
/// fires for both new runs and loaded saves.</summary>
internal static class GameContext
{
    public static RunState? CurrentRun { get; set; }

    public static Player? LocalPlayer
    {
        get
        {
            var run = CurrentRun;
            return run is { Players.Count: > 0 } ? run.Players[0] : null;
        }
    }

    public static PlayerCombatState? Combat => LocalPlayer?.PlayerCombatState;

    public static bool InCombat => Combat != null;

    public static IReadOnlyList<CardModel> HandCards =>
        Combat?.Hand.Cards ?? (IReadOnlyList<CardModel>)Array.Empty<CardModel>();
}
