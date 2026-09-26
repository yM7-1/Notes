using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using Notes.Core.Documents;
using Notes.Core.Services;

namespace Notes.Game;

/// <summary>
/// Captures the local player's combat operations.
/// Hook sources (all synchronous / game-provided, independent of RitsuLib
/// lifecycle patches which did not deliver combat events in testing):
/// - CombatManager.CombatBegan / TurnStarted / TurnEnded (game events)
/// - CombatHistory.CardPlayStarted (Harmony)
/// - CardPile.AddInternal for hand/discard piles (Harmony)
/// - PotionModel.EnqueueManualUse (Harmony)
/// - RelicModel.Flash (Harmony)
/// Relic triggers and effect draws/discards within a short window after an
/// operation become that operation's annotation; standalone runs become ops.
/// </summary>
internal static class NotesOpLog
{
    private const long CauseWindowMs = 450;
    private const int DiagnosticEventLimit = 5;

    private static readonly List<NotesOpData> Ops = new();
    private static readonly object Gate = new();
    private static string? _causeId;
    private static long _causeAtMs;
    private static NotesOpData? _drawBatch;
    private static NotesOpData? _discardBatch;
    private static NotesOpData? _exhaustBatch;
    private static int _drawCount;
    private static int _discardCount;
    private static int _exhaustCount;
    private static int _turn;
    private static int _diagnostics;
    private static bool _initialized;
    private static CombatState? _combatState;

    public static IReadOnlyList<NotesOpData> Entries
    {
        get
        {
            lock (Gate)
            {
                return Ops.Select(o => o.Clone()).ToList();
            }
        }
    }

    public static int Count
    {
        get
        {
            lock (Gate)
            {
                return Ops.Count;
            }
        }
    }

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }
        _initialized = true;

        var manager = CombatManager.Instance;
        manager.TurnStarted += OnTurnStarted;
        manager.TurnEnded += OnTurnEnded;
        Log.Info("[Notes] op capture attached (CombatManager events + Harmony pile/card/potion hooks)");
    }

    public static void Clear()
    {
        lock (Gate)
        {
            Ops.Clear();
            _drawBatch = null;
            _discardBatch = null;
            _exhaustBatch = null;
            _drawCount = 0;
            _discardCount = 0;
            _exhaustCount = 0;
            _causeId = null;
        }
    }

    public static void Load(IEnumerable<NotesOpData> ops)
    {
        lock (Gate)
        {
            Ops.Clear();
            foreach (var op in ops.OrderBy(o => o.UnixMs))
            {
                Ops.Add(op.Clone());
            }
            _drawBatch = null;
            _discardBatch = null;
            _causeId = null;
        }
    }

    // ---- game event handlers --------------------------------------------------

    /// <summary>Resets the log when a new combat state shows up (CombatBegan does
    /// not exist on older game APIs, so detect it from the turn events).</summary>
    private static void EnsureCombat(CombatState state)
    {
        if (ReferenceEquals(_combatState, state))
        {
            return;
        }
        _combatState = state;
        Clear();
        _turn = 0;
        _diagnostics = 0;
        Log.Info("[Notes] new combat detected; op log reset");
    }

    private static void OnTurnStarted(CombatState state)
    {
        try
        {
            EnsureCombat(state);
            if (state.CurrentSide != CombatSide.Player)
            {
                return;
            }
            _turn = GameContext.Combat?.TurnNumber ?? state.RoundNumber;
            _causeId = null;
            EndBatches();
            NotesRuntime.EnsureActualTurnRegion(_turn);
        }
        catch (Exception ex)
        {
            Log.Error("[Notes] turn-start capture failed: " + ex);
        }
    }

    private static void OnTurnEnded(CombatState state)
    {
        try
        {
            EnsureCombat(state);
            if (state.CurrentSide != CombatSide.Player)
            {
                return;
            }
            EndBatches();
            var op = AddOp(NotesOpKind.EndTurn, ModLocalization.T("op_end_turn", "结束回合"));
            _causeId = null;
            NotesRuntime.UpdateActualRegionSnapshot(_turn, op.Snapshot, op.Hp, op.MaxHp);
        }
        catch (Exception ex)
        {
            Log.Error("[Notes] turn-end capture failed: " + ex);
        }
    }

    /// <summary>CardPlayStarted hook (per repetition; repeated plays counted once).</summary>
    public static void OnCardPlayed(CardPlay play)
    {
        try
        {
            if (play.PlayCount > 1 && play.PlayIndex > 0)
            {
                return;
            }
            var card = play.Card;
            if (!ReferenceEquals(card.Owner, GameContext.LocalPlayer))
            {
                return;
            }
            LogDiagnostic($"card play '{CardCatalog.TitleOf(card)}' owner-match=true");
            EndBatches();
            var op = AddOp(
                NotesOpKind.Card,
                CardCatalog.TitleOf(card),
                card.Id.ToString(),
                CardCatalog.Snapshot(card).Cost,
                (int)card.Type,
                (int)card.Rarity,
                card.IsUpgraded);
            SetCause(op);
        }
        catch (Exception ex)
        {
            Log.Error("[Notes] card-play capture failed: " + ex);
        }
    }

    /// <summary>CardPile.AddInternal hook: hand = draw, discard = discard.</summary>
    public static void OnCardAdded(PileType pileType, CardModel card)
    {
        try
        {
            if (card == null || !ReferenceEquals(card.Owner, GameContext.LocalPlayer))
            {
                return;
            }
            if (pileType == PileType.Hand)
            {
                HandleDraw(card);
            }
            else if (pileType == PileType.Discard)
            {
                HandleDiscard(card);
            }
            else if (pileType == PileType.Exhaust)
            {
                HandleExhaust(card);
            }
        }
        catch (Exception ex)
        {
            Log.Error("[Notes] pile capture failed: " + ex);
        }
    }

    /// <summary>PotionModel.EnqueueManualUse hook.</summary>
    public static void OnPotionQueued(PotionModel potion)
    {
        try
        {
            if (!ReferenceEquals(potion.Owner, GameContext.LocalPlayer))
            {
                return;
            }
            LogDiagnostic($"potion '{PotionTitle(potion)}' owner-match=true");
            EndBatches();
            var op = AddOp(NotesOpKind.Potion, PotionTitle(potion), potion.Id.ToString());
            SetCause(op);
        }
        catch (Exception ex)
        {
            Log.Error("[Notes] potion capture failed: " + ex);
        }
    }

    /// <summary>Called from the RelicModel.Flash patch.</summary>
    public static void OnRelicFlash(RelicModel relic)
    {
        try
        {
            if (!ReferenceEquals(relic.Owner, GameContext.LocalPlayer))
            {
                return;
            }
            var refId = relic.Id.ToString();
            if (NotesRuntime.HasRelicNode(_turn, refId))
            {
                return; // player already placed this relic legend by hand
            }
            var title = RelicTitle(relic);
            var text = Format("annot_relic_trigger", "「{0}」触发", title);
            if (TryAnnotateCause(text, "relic:" + refId))
            {
                return;
            }
            NotesRuntime.AddTurnEvent(_turn, new NotesAnnotation { Text = text, RefId = "relic:" + refId });
        }
        catch (Exception ex)
        {
            Log.Error("[Notes] relic capture failed: " + ex);
        }
    }

    // ---- shared handling ------------------------------------------------------

    private static void HandleDraw(CardModel card)
    {
        var name = CardCatalog.TitleOf(card);
        if (TryAnnotateCause(Format("annot_draw_to", "抽到 {0}", name), "card:" + card.Id))
        {
            return;
        }
        lock (Gate)
        {
            if (_drawBatch == null)
            {
                _drawBatch = NewOp(NotesOpKind.Draw, ModLocalization.T("op_draw", "抽牌"));
                Ops.Add(_drawBatch);
            }
            _drawCount++;
            _drawBatch.Meta = string.IsNullOrEmpty(_drawBatch.Meta) ? name : _drawBatch.Meta + ", " + name;
            _drawBatch.Title = ModLocalization.T("op_draw", "抽牌") + "×" + _drawCount;
            NotesRuntime.OnOpsChanged();
        }
    }

    private static void HandleDiscard(CardModel card)
    {
        var name = CardCatalog.TitleOf(card);
        if (IsCauseSelfMove(card.Id.ToString()))
        {
            return; // the played card itself moving to the discard pile
        }
        if (TryAnnotateCause(Format("annot_discard", "弃掉 {0}", name), "card:" + card.Id))
        {
            return;
        }
        lock (Gate)
        {
            if (_discardBatch == null)
            {
                _discardBatch = NewOp(NotesOpKind.Discard, ModLocalization.T("op_discard", "弃牌"));
                Ops.Add(_discardBatch);
            }
            _discardCount++;
            _discardBatch.Meta = string.IsNullOrEmpty(_discardBatch.Meta) ? name : _discardBatch.Meta + ", " + name;
            _discardBatch.Title = ModLocalization.T("op_discard", "弃牌") + "×" + _discardCount;
            NotesRuntime.OnOpsChanged();
        }
    }

    private static void HandleExhaust(CardModel card)
    {
        var name = CardCatalog.TitleOf(card);
        if (IsCauseSelfMove(card.Id.ToString()))
        {
            return; // the played card itself exhausting after play
        }
        // Exhaust effects often hit several cards: merge them into one annotation.
        if (TryMergeExhaustCause(name))
        {
            return;
        }
        lock (Gate)
        {
            if (_exhaustBatch == null)
            {
                _exhaustBatch = NewOp(NotesOpKind.Exhaust, ModLocalization.T("op_exhaust", "消耗"));
                Ops.Add(_exhaustBatch);
            }
            _exhaustCount++;
            _exhaustBatch.Meta = string.IsNullOrEmpty(_exhaustBatch.Meta) ? name : _exhaustBatch.Meta + ", " + name;
            _exhaustBatch.Title = ModLocalization.T("op_exhaust", "消耗") + "×" + _exhaustCount;
            NotesRuntime.OnOpsChanged();
        }
    }

    private static void EndBatches()
    {
        lock (Gate)
        {
            _drawBatch = null;
            _discardBatch = null;
            _exhaustBatch = null;
            _drawCount = 0;
            _discardCount = 0;
            _exhaustCount = 0;
        }
    }

    private static NotesOpData AddOp(
        NotesOpKind kind,
        string title,
        string refId = "",
        int cost = -1,
        int cardType = -1,
        int rarity = -1,
        bool upgraded = false)
    {
        NotesOpData op;
        lock (Gate)
        {
            op = NewOp(kind, title);
            op.RefId = refId;
            op.Cost = cost;
            op.CardType = cardType;
            op.Rarity = rarity;
            op.Upgraded = upgraded;
            Ops.Add(op);
        }
        NotesRuntime.OnOpsChanged();
        return op;
    }

    private static NotesOpData NewOp(NotesOpKind kind, string title)
    {
        var op = new NotesOpData
        {
            Id = IdFactory.NewOpId(),
            Kind = kind,
            Turn = _turn,
            UnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Title = title,
        };
        var snapshot = BuildSnapshot();
        op.Snapshot = snapshot.Text;
        op.Hp = snapshot.Hp;
        op.MaxHp = snapshot.MaxHp;
        return op;
    }

    /// <summary>Player + enemy state right now, for the inspector panel.</summary>
    private static (string Text, int Hp, int MaxHp) BuildSnapshot()
    {
        try
        {
            var player = GameContext.LocalPlayer;
            if (player == null)
            {
                return ("", -1, -1);
            }
            var creature = player.Creature;
            var parts = new List<string>
            {
                ModLocalization.T("snap_hp", "HP") + " " + creature.CurrentHp + "/" + creature.MaxHp
                    + (creature.Block > 0 ? "  " + ModLocalization.T("snap_block", "Block") + " " + creature.Block : ""),
            };
            var combat = player.PlayerCombatState;
            if (combat != null)
            {
                parts.Add(ModLocalization.T("snap_energy", "Energy") + " " + combat.Energy + "/" + combat.MaxEnergy);
            }
            var buffs = creature.Powers
                .Where(p => p.Amount != 0)
                .Take(6)
                .Select(p => PowerName(p) + (Math.Abs(p.Amount) != 1 ? "×" + p.Amount : ""))
                .ToList();
            if (buffs.Count > 0)
            {
                parts.Add(string.Join("  ", buffs));
            }
            var potions = player.Potions.Take(3).Select(PotionTitle).ToList();
            parts.Add(ModLocalization.T("snap_potions", "Potions") + ": "
                + (potions.Count > 0 ? string.Join(", ", potions) : ModLocalization.T("snap_none", "none")));
            var enemies = new List<string>();
            foreach (var enemy in creature.CombatState?.Enemies
                ?? (IReadOnlyList<MegaCrit.Sts2.Core.Entities.Creatures.Creature>)Array.Empty<MegaCrit.Sts2.Core.Entities.Creatures.Creature>())
            {
                if (enemy.IsAlive)
                {
                    enemies.Add(enemy.Name + " " + enemy.CurrentHp + "/" + enemy.MaxHp);
                }
            }
            if (enemies.Count > 0)
            {
                parts.Add(ModLocalization.T("snap_enemies", "Enemies") + ": " + string.Join("; ", enemies));
            }
            return (string.Join("\n", parts), creature.CurrentHp, creature.MaxHp);
        }
        catch
        {
            return ("", -1, -1);
        }
    }

    private static void SetCause(NotesOpData op)
    {
        _causeId = op.Id;
        _causeAtMs = Environment.TickCount64;
    }

    private static bool TryAnnotateCause(string text, string refId)
    {
        if (_causeId == null || Environment.TickCount64 - _causeAtMs > CauseWindowMs)
        {
            return false;
        }
        lock (Gate)
        {
            var cause = Ops.FirstOrDefault(o => o.Id == _causeId);
            if (cause == null)
            {
                return false;
            }
            if (cause.Annotations.Any(a => a.Text == text && a.RefId == refId))
            {
                return true;
            }
            cause.Annotations.Add(new NotesAnnotation { Text = text, RefId = refId });
        }
        NotesRuntime.OnOpsChanged();
        return true;
    }

    /// <summary>Merges exhausted card names into one raw-name annotation
    /// ("A、B"); the UI renders it as 「A」、「B」被消耗.</summary>
    private static bool TryMergeExhaustCause(string name)
    {
        if (_causeId == null || Environment.TickCount64 - _causeAtMs > CauseWindowMs)
        {
            return false;
        }
        lock (Gate)
        {
            var cause = Ops.FirstOrDefault(o => o.Id == _causeId);
            if (cause == null)
            {
                return false;
            }
            var existing = cause.Annotations.FirstOrDefault(a => a.RefId == "exhaust:");
            if (existing == null)
            {
                cause.Annotations.Add(new NotesAnnotation { RefId = "exhaust:", Text = name });
            }
            else if (!existing.Text.Contains(name, StringComparison.Ordinal))
            {
                existing.Text += "、" + name;
            }
        }
        NotesRuntime.OnOpsChanged();
        return true;
    }

    /// <summary>True when the moving card is the very card that was just played
    /// (its own move to discard/exhaust must not become an annotation).</summary>
    private static bool IsCauseSelfMove(string cardModelId)
    {
        if (_causeId == null || Environment.TickCount64 - _causeAtMs > CauseWindowMs)
        {
            return false;
        }
        lock (Gate)
        {
            var cause = Ops.FirstOrDefault(o => o.Id == _causeId);
            return cause != null && cause.Kind == NotesOpKind.Card && cause.RefId == cardModelId;
        }
    }

    private static void LogDiagnostic(string message)
    {
        if (_diagnostics >= DiagnosticEventLimit)
        {
            return;
        }
        _diagnostics++;
        Log.Info("[Notes] capture: " + message);
    }

    private static string RelicTitle(RelicModel relic)
    {
        try
        {
            var title = relic.Title.GetFormattedText();
            if (!string.IsNullOrWhiteSpace(title))
            {
                return title;
            }
        }
        catch
        {
            // fall back to entry id
        }
        return relic.Id.Entry ?? "";
    }

    private static string PotionTitle(PotionModel potion)
    {
        try
        {
            var title = potion.Title.GetFormattedText();
            if (!string.IsNullOrWhiteSpace(title))
            {
                return title;
            }
        }
        catch
        {
            // fall back to entry id
        }
        return potion.Id.Entry ?? "";
    }

    private static string PowerName(PowerModel power)
    {
        try
        {
            var title = power.Title.GetFormattedText();
            if (!string.IsNullOrWhiteSpace(title))
            {
                return title;
            }
        }
        catch
        {
            // fall back to entry id
        }
        return power.Id.Entry ?? "";
    }

    private static string Format(string key, string fallback, string value) =>
        ModLocalization.T(key, fallback).Replace("{0}", value);
}
