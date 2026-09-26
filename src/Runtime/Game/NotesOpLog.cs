using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using Notes.Core.Documents;
using Notes.Core.Services;
using STS2RitsuLib;

namespace Notes.Game;

/// <summary>
/// Captures the local player's combat operations (card plays, potions, draws,
/// discards, end turn) plus relic triggers (via the RelicModel.Flash patch).
/// Runs for the current combat only; the buffer is persisted with the run save.
/// Attribution rule: an event within a short window after an operation becomes
/// that operation's annotation; otherwise it becomes a standalone op / turn event.
/// </summary>
internal static class NotesOpLog
{
    private const long CauseWindowMs = 450;

    private static readonly List<NotesOpData> Ops = new();
    private static readonly object Gate = new();
    private static string? _causeId;
    private static long _causeAtMs;
    private static NotesOpData? _drawBatch;
    private static NotesOpData? _discardBatch;
    private static int _drawCount;
    private static int _discardCount;
    private static int _turn;
    private static bool _initialized;

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
        RitsuLibFramework.SubscribeLifecycle<CombatStartingEvent>(_ => OnCombatStarting());
        RitsuLibFramework.SubscribeLifecycle<PlayerTurnStartedEvent>(OnPlayerTurnStarted);
        RitsuLibFramework.SubscribeLifecycle<CardPlayedEvent>(OnCardPlayed);
        RitsuLibFramework.SubscribeLifecycle<PotionUsedEvent>(OnPotionUsed);
        RitsuLibFramework.SubscribeLifecycle<CardDrawnEvent>(OnCardDrawn);
        RitsuLibFramework.SubscribeLifecycle<CardDiscardedEvent>(OnCardDiscarded);
        RitsuLibFramework.SubscribeLifecycle<SideTurnEndedEvent>(OnSideTurnEnded);
    }

    public static void Clear()
    {
        lock (Gate)
        {
            Ops.Clear();
            _drawBatch = null;
            _discardBatch = null;
            _drawCount = 0;
            _discardCount = 0;
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

    // ---- event handlers -------------------------------------------------------

    private static void OnCombatStarting()
    {
        try
        {
            Clear();
            _turn = 0;
            NotesRuntime.OnOpsChanged();
        }
        catch (Exception ex)
        {
            Log.Error("[Notes] combat-start capture failed: " + ex);
        }
    }

    private static void OnPlayerTurnStarted(PlayerTurnStartedEvent e)
    {
        try
        {
            if (!ReferenceEquals(e.Player, GameContext.LocalPlayer))
            {
                return;
            }
            _turn = e.Player.PlayerCombatState?.TurnNumber ?? _turn + 1;
            _causeId = null;
            EndBatches();
            NotesRuntime.EnsureActualTurnRegion(_turn);
        }
        catch (Exception ex)
        {
            Log.Error("[Notes] turn-start capture failed: " + ex);
        }
    }

    private static void OnCardPlayed(CardPlayedEvent e)
    {
        try
        {
            var play = e.CardPlay;
            // CardPlay.Player only exists on newer APIs; CardModel.Owner works on all.
            if (!ReferenceEquals(play.Card.Owner, GameContext.LocalPlayer))
            {
                return;
            }
            EndBatches();
            var card = play.Card;
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

    private static void OnPotionUsed(PotionUsedEvent e)
    {
        try
        {
            if (!ReferenceEquals(e.Potion.Owner, GameContext.LocalPlayer))
            {
                return;
            }
            EndBatches();
            var op = AddOp(NotesOpKind.Potion, PotionTitle(e.Potion), e.Potion.Id.ToString());
            SetCause(op);
        }
        catch (Exception ex)
        {
            Log.Error("[Notes] potion capture failed: " + ex);
        }
    }

    private static void OnCardDrawn(CardDrawnEvent e)
    {
        try
        {
            if (!ReferenceEquals(e.Card.Owner, GameContext.LocalPlayer))
            {
                return;
            }
            var name = CardCatalog.TitleOf(e.Card);
            if (TryAnnotateCause(Format("annot_draw_to", "抽到 {0}", name), "card:" + e.Card.Id))
            {
                return;
            }
            lock (Gate)
            {
                _drawBatch ??= NewOp(NotesOpKind.Draw, ModLocalization.T("op_draw", "抽牌"));
                _drawCount++;
                _drawBatch.Meta = string.IsNullOrEmpty(_drawBatch.Meta) ? name : _drawBatch.Meta + ", " + name;
                _drawBatch.Title = ModLocalization.T("op_draw", "抽牌") + "×" + _drawCount;
                NotesRuntime.OnOpsChanged();
            }
        }
        catch (Exception ex)
        {
            Log.Error("[Notes] draw capture failed: " + ex);
        }
    }

    private static void OnCardDiscarded(CardDiscardedEvent e)
    {
        try
        {
            if (!ReferenceEquals(e.Card.Owner, GameContext.LocalPlayer))
            {
                return;
            }
            var name = CardCatalog.TitleOf(e.Card);
            if (TryAnnotateCause(Format("annot_discard", "弃掉 {0}", name), "card:" + e.Card.Id))
            {
                return;
            }
            lock (Gate)
            {
                _discardBatch ??= NewOp(NotesOpKind.Discard, ModLocalization.T("op_discard", "弃牌"));
                _discardCount++;
                _discardBatch.Meta = string.IsNullOrEmpty(_discardBatch.Meta) ? name : _discardBatch.Meta + ", " + name;
                _discardBatch.Title = ModLocalization.T("op_discard", "弃牌") + "×" + _discardCount;
                NotesRuntime.OnOpsChanged();
            }
        }
        catch (Exception ex)
        {
            Log.Error("[Notes] discard capture failed: " + ex);
        }
    }

    private static void OnSideTurnEnded(SideTurnEndedEvent e)
    {
        try
        {
            if (e.Side != CombatSide.Player)
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

    // ---- helpers --------------------------------------------------------------

    private static void EndBatches()
    {
        lock (Gate)
        {
            _drawBatch = null;
            _discardBatch = null;
            _drawCount = 0;
            _discardCount = 0;
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
            foreach (var enemy in creature.CombatState?.Enemies ?? (IReadOnlyList<MegaCrit.Sts2.Core.Entities.Creatures.Creature>)Array.Empty<MegaCrit.Sts2.Core.Entities.Creatures.Creature>())
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

    private static string Format(string key, string fallback, string value) =>
        ModLocalization.T(key, fallback).Replace("{0}", value);
}
