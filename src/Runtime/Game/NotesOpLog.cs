using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
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
/// - CombatHistory.CardPlayStarted / DamageReceived / CardGenerated /
///   MonsterPerformedMove (Harmony)
/// - CardPile.AddInternal for hand/discard/exhaust piles (Harmony)
/// - PotionModel.EnqueueManualUse (Harmony)
/// - RelicModel.Flash (Harmony)
/// Relic triggers, effect draws/discards, exhausted cards and dealt damage
/// within a short window after an operation become that operation's annotation;
/// standalone runs become turn events.
/// </summary>
internal static class NotesOpLog
{
    private const long CauseWindowMs = 450;
    private const long ExhaustWindowMs = 1500;
    private const long DamageCauseWindowMs = 10_000;
    private const int DiagnosticEventLimit = 5;
    private const int ErrorLogLimit = 3;
    private static readonly Dictionary<string, int> _errorCounts = new(StringComparer.Ordinal);

    private static readonly List<NotesOpData> Ops = new();
    private static readonly object Gate = new();
    private static readonly Dictionary<Creature, string> EnemyNames = new(ReferenceEqualityComparer.Instance);
    private static readonly HashSet<CardModel> PendingGenerated = new(ReferenceEqualityComparer.Instance);
    private static string? _causeId;
    private static long _causeAtMs;
    private static NotesOpData? _drawBatch;
    private static int _drawCount;
    private static int _turn;
    private static int _diagnostics;
    private static bool _initialized;
    private static CombatState? _combatState;
    private static Creature? _performingMonster;
    private static List<string>? _endTurnHand;

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

    public static bool Initialized => _initialized;

    /// <summary>True when the player turned automatic capture off (settings).</summary>
    private static bool CaptureOff => !NotesRuntime.AutoRecord;

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }
        try
        {
            var manager = CombatManager.Instance;
            manager.TurnStarted += OnTurnStarted;
            manager.TurnEnded += OnTurnEnded;
            manager.PlayerEndedTurn += OnPlayerEndedTurn;
            _initialized = true;
            Log.Info("[Notes] op capture attached (CombatManager events + Harmony pile/card/potion hooks)");
        }
        catch (Exception ex)
        {
            // Marked attached only on success; callers retry later.
            Log.Error("[Notes] op capture attach failed (will retry): " + ex);
        }
    }

    public static void Clear()
    {
        lock (Gate)
        {
            Ops.Clear();
            _drawBatch = null;
            _drawCount = 0;
            _causeId = null;
            EnemyNames.Clear();
            PendingGenerated.Clear();
            _performingMonster = null;
            _endTurnHand = null;
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
        _errorCounts.Clear();
        // Number same-named enemies left-to-right up front so the numbering is
        // stable even after some of them die.
        lock (Gate)
        {
            foreach (var enemy in state.Enemies)
            {
                EnsureEnemyName(enemy);
            }
        }
        NotesRuntime.OnCombatStarted(state);
        Log.Info("[Notes] new combat detected; op log reset");
    }

    /// <summary>Display name of an enemy. When several enemies share a name they
    /// are numbered left-to-right ("扭动虫1", "扭动虫2", ...); the numbering is
    /// frozen per creature so later deaths never renumber the survivors.</summary>
    private static string EnsureEnemyName(Creature creature)
    {
        if (EnemyNames.TryGetValue(creature, out var existing))
        {
            return existing;
        }
        var baseName = creature.Name;
        var enemies = creature.CombatState?.Enemies ?? (IReadOnlyList<Creature>)Array.Empty<Creature>();
        var sameName = enemies.Count(e => string.Equals(e.Name, baseName, StringComparison.Ordinal));
        var display = baseName;
        if (sameName > 1)
        {
            var index = 1;
            foreach (var enemy in enemies)
            {
                if (ReferenceEquals(enemy, creature))
                {
                    break;
                }
                if (string.Equals(enemy.Name, baseName, StringComparison.Ordinal))
                {
                    index++;
                }
            }
            display = baseName + index;
            while (EnemyNames.Any(kv => kv.Key != creature && kv.Value == display))
            {
                index++;
                display = baseName + index;
            }
        }
        EnemyNames[creature] = display;
        return display;
    }

    private static void OnTurnStarted(CombatState state)
    {
        try
        {
            EnsureCombat(state);
            lock (Gate)
            {
                _performingMonster = null; // a move never spans a turn boundary
            }
            if (state.CurrentSide != CombatSide.Player)
            {
                return;
            }
            _turn = GameContext.Combat?.TurnNumber ?? state.RoundNumber;
            _causeId = null;
            _endTurnHand = null;
            EndBatches();
            if (!CaptureOff)
            {
                NotesRuntime.EnsureActualTurnRegion(_turn);
            }
        }
        catch (Exception ex)
        {
            LogCaptureError("turn-start capture", ex);
        }
    }

    private static void OnTurnEnded(CombatState state)
    {
        try
        {
            EnsureCombat(state);
            if (CaptureOff || state.CurrentSide != CombatSide.Player)
            {
                return;
            }
            lock (Gate)
            {
                _performingMonster = null;
            }
            EndBatches();
            List<string>? hand;
            lock (Gate)
            {
                hand = _endTurnHand;
                _endTurnHand = null;
            }
            var op = AddOp(NotesOpKind.EndTurn, ModLocalization.T("op_end_turn", "结束回合"),
                handOverride: hand);
            _causeId = null;
            NotesRuntime.UpdateActualRegionSnapshot(_turn, op.Snapshot, op.Hp, op.MaxHp);
        }
        catch (Exception ex)
        {
            LogCaptureError("turn-end capture", ex);
        }
    }

    /// <summary>CombatManager.PlayerEndedTurn (fires when the player requests the
    /// end of turn, before the hand is discarded): cache the hand so the
    /// end-turn step can show the hand it was played from.</summary>
    private static void OnPlayerEndedTurn(Player player, bool canBackOut)
    {
        try
        {
            if (CaptureOff || !ReferenceEquals(player, GameContext.LocalPlayer))
            {
                return;
            }
            var hand = GameContext.HandCards.Select(CardCatalog.TitleOf).ToList();
            lock (Gate)
            {
                _endTurnHand = hand;
            }
        }
        catch (Exception ex)
        {
            LogCaptureError("end-turn hand capture", ex);
        }
    }

    /// <summary>CardPlayStarted hook (per repetition; repeated plays counted once).</summary>
    public static void OnCardPlayed(CardPlay play)
    {
        try
        {
            if (CaptureOff || (play.PlayCount > 1 && play.PlayIndex > 0))
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
                card.IsUpgraded,
                extraHandCard: CardCatalog.TitleOf(card));
            SetCause(op);
        }
        catch (Exception ex)
        {
            LogCaptureError("card-play capture", ex);
        }
    }

    /// <summary>CardPile.AddInternal hook: hand = draw, discard = discard.</summary>
    public static void OnCardAdded(PileType pileType, CardModel card)
    {
        try
        {
            if (CaptureOff || card == null || !ReferenceEquals(card.Owner, GameContext.LocalPlayer))
            {
                return;
            }
            if (TryHandleEnemyInsert(pileType, card))
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
            LogCaptureError("pile capture", ex);
        }
    }

    /// <summary>PotionModel.EnqueueManualUse hook.</summary>
    public static void OnPotionQueued(PotionModel potion)
    {
        try
        {
            if (CaptureOff || !ReferenceEquals(potion.Owner, GameContext.LocalPlayer))
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
            LogCaptureError("potion capture", ex);
        }
    }

    /// <summary>Called from the RelicModel.Flash patch.</summary>
    public static void OnRelicFlash(RelicModel relic)
    {
        try
        {
            if (CaptureOff || !ReferenceEquals(relic.Owner, GameContext.LocalPlayer))
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
            if (TryAnnotateCause(text, AnnotationProtocol.RelicRef(refId)))
            {
                return;
            }
            NotesRuntime.AddTurnEvent(_turn, new NotesAnnotation { Text = text, RefId = AnnotationProtocol.RelicRef(refId) });
        }
        catch (Exception ex)
        {
            LogCaptureError("relic capture", ex);
        }
    }

    // ---- shared handling ------------------------------------------------------

    /// <summary>CombatHistory.DamageReceived: records who the player's operation
    /// hit and for how much ("「打击」对 火炬头 造成 6 点伤害并击杀"), and how much
    /// the player lost during the enemy turn ("战损 12").</summary>
    public static void OnDamage(Creature receiver, Creature? dealer, DamageResult result, CardModel? cardSource)
    {
        try
        {
            var player = GameContext.LocalPlayer;
            if (CaptureOff || player == null || receiver == null)
            {
                return;
            }
            if (ReferenceEquals(receiver, player.Creature))
            {
                RecordLoss(result);
                return;
            }
            if (dealer == null || !ReferenceEquals(dealer, player.Creature))
            {
                return;
            }
            if (receiver.IsPlayer || receiver.Side == CombatSide.Player)
            {
                return;
            }
            var op = FindDamageOp(cardSource);
            if (op == null)
            {
                return;
            }
            var killed = result.WasTargetKilled;
            lock (Gate)
            {
                var targetName = EnsureEnemyName(receiver);
                var refId = AnnotationProtocol.DamageRef(targetName);
                var existing = op.Annotations.FirstOrDefault(a => a.RefId == refId);
                if (existing == null)
                {
                    op.Annotations.Add(new NotesAnnotation
                    {
                        RefId = refId,
                        Text = targetName,
                        Meta = AnnotationProtocol.Join(op.Title, killed ? "1" : "0"),
                        Count = result.TotalDamage,
                    });
                }
                else
                {
                    existing.Count += result.TotalDamage;
                    var wasKilled = existing.Meta.EndsWith(
                        AnnotationProtocol.Separator + "1", StringComparison.Ordinal);
                    existing.Meta = AnnotationProtocol.Join(op.Title, killed || wasKilled ? "1" : "0");
                }
            }
            NotesRuntime.OnOpsChanged();
        }
        catch (Exception ex)
        {
            LogCaptureError("damage capture", ex);
        }
    }

    /// <summary>HP actually lost while the enemy side is acting: accumulated into
    /// one "战损 N" boundary annotation of the player turn that just ended.</summary>
    private static void RecordLoss(DamageResult result)
    {
        var amount = result.UnblockedDamage;
        if (amount <= 0)
        {
            return;
        }
        var side = GameContext.LocalPlayer?.Creature?.CombatState?.CurrentSide;
        if (side != CombatSide.Enemy)
        {
            return;
        }
        NotesRuntime.MergeTurnEvent(_turn, new NotesAnnotation
        {
            RefId = AnnotationProtocol.Loss,
            Count = amount,
        });
    }

    /// <summary>CombatHistory.CardGenerated: cards generated with no player
    /// creator (monster moves / monster powers) are remembered; the following
    /// pile add becomes an "enemy inserted" boundary annotation.</summary>
    public static void OnCardGenerated(CardModel card, Player? creator)
    {
        if (CaptureOff)
        {
            return;
        }
        try
        {
            if (card == null || creator != null)
            {
                return;
            }
            if (!ReferenceEquals(card.Owner, GameContext.LocalPlayer))
            {
                return;
            }
            lock (Gate)
            {
                PendingGenerated.Add(card);
            }
        }
        catch (Exception ex)
        {
            LogCaptureError("generated-card capture", ex);
        }
    }

    /// <summary>MonsterModel.PerformMove prefix: remembers the acting monster so
    /// cards it inserts during the move can be attributed to it. Guarded like
    /// every other capture hook: an exception here would abort the enemy move.</summary>
    public static void OnMonsterMoveStart(MonsterModel monster)
    {
        if (CaptureOff)
        {
            return;
        }
        try
        {
            lock (Gate)
            {
                _performingMonster = monster.Creature;
            }
        }
        catch (Exception ex)
        {
            LogCaptureError("monster move start capture", ex);
        }
    }

    /// <summary>CombatHistory.MonsterPerformedMove (fired after the move): ends
    /// the attribution window and freezes the monster's display name.</summary>
    public static void OnMonsterMoveEnd(MonsterModel monster)
    {
        if (CaptureOff)
        {
            return;
        }
        try
        {
            lock (Gate)
            {
                _performingMonster = null;
                if (monster.Creature != null)
                {
                    EnsureEnemyName(monster.Creature);
                }
            }
        }
        catch (Exception ex)
        {
            LogCaptureError("monster move end capture", ex);
        }
    }

    private static void HandleDraw(CardModel card)
    {
        var name = CardCatalog.TitleOf(card);
        if (TryAnnotateCause(Format("annot_draw_to", "抽到 {0}", name), AnnotationProtocol.Card + card.Id))
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
        // Discards never become their own legend: they merge into the causing
        // operation's explanation (like exhausts). Standalone discards (end of
        // turn hand, enemy forced discards) land on the turn boundary.
        if (TryMergeDiscardCause(name))
        {
            return;
        }
        NotesRuntime.MergeTurnEvent(_turn, new NotesAnnotation
        {
            RefId = AnnotationProtocol.Discard,
            Text = name,
            Count = 1,
        });
    }

    private static void HandleExhaust(CardModel card)
    {
        var name = CardCatalog.TitleOf(card);
        // Exhausts never become their own legend: they merge into the causing
        // operation's explanation (the played card itself included). Standalone
        // exhausts (ethereal at end of turn, ...) land on the turn region.
        if (TryMergeExhaustCause(name))
        {
            return;
        }
        NotesRuntime.MergeTurnEvent(_turn, new NotesAnnotation
        {
            RefId = AnnotationProtocol.Exhaust,
            Text = name,
            Count = 1,
        });
    }

    /// <summary>Enemy-inserted cards (creator == null at CardGenerated) are
    /// recorded on the turn boundary: 【敌人】将【卡*N】加入到【牌堆】.</summary>
    private static bool TryHandleEnemyInsert(PileType pileType, CardModel card)
    {
        lock (Gate)
        {
            if (!PendingGenerated.Remove(card))
            {
                return false;
            }
        }
        if (pileType is not (PileType.Draw or PileType.Discard or PileType.Hand or PileType.Exhaust))
        {
            return true; // consumed, but not a tracked combat pile
        }
        var pileKey = pileType switch
        {
            PileType.Draw => "pile_draw",
            PileType.Hand => "pile_hand",
            PileType.Exhaust => "pile_exhaust",
            _ => "pile_discard",
        };
        string enemy;
        lock (Gate)
        {
            enemy = _performingMonster != null
                ? EnsureEnemyName(_performingMonster)
                : ModLocalization.T("enemy_generic", "敌人");
        }
        NotesRuntime.MergeTurnEvent(_turn, new NotesAnnotation
        {
            RefId = AnnotationProtocol.InsertRef(enemy, pileKey, card.Id.ToString()),
            Text = CardCatalog.TitleOf(card),
            Meta = AnnotationProtocol.Join(enemy, pileKey),
            Count = 1,
        });
        return true;
    }

    private static void EndBatches()
    {
        lock (Gate)
        {
            _drawBatch = null;
            _drawCount = 0;
        }
    }

    private static NotesOpData AddOp(
        NotesOpKind kind,
        string title,
        string refId = "",
        int cost = -1,
        int cardType = -1,
        int rarity = -1,
        bool upgraded = false,
        string? extraHandCard = null,
        IReadOnlyList<string>? handOverride = null)
    {
        NotesOpData op;
        lock (Gate)
        {
            op = NewOp(kind, title, extraHandCard, handOverride);
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

    private static NotesOpData NewOp(
        NotesOpKind kind,
        string title,
        string? extraHandCard = null,
        IReadOnlyList<string>? handOverride = null)
    {
        var op = new NotesOpData
        {
            Id = IdFactory.NewOpId(),
            Kind = kind,
            Turn = _turn,
            UnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Title = title,
        };
        var snapshot = BuildSnapshot(extraHandCard, handOverride);
        op.Snapshot = snapshot.Text;
        op.Hp = snapshot.Hp;
        op.MaxHp = snapshot.MaxHp;
        return op;
    }

    /// <summary>Player + enemy state right now, for the inspector panel. The hand
    /// line shows the hand as it was when the operation started (the played card
    /// is re-added; the end-turn step uses the pre-discard hand).</summary>
    private static (string Text, int Hp, int MaxHp) BuildSnapshot(
        string? extraHandCard = null,
        IReadOnlyList<string>? handOverride = null)
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
            var hand = handOverride != null
                ? handOverride.ToList()
                : GameContext.HandCards.Select(CardCatalog.TitleOf).ToList();
            if (extraHandCard != null && !hand.Contains(extraHandCard, StringComparer.Ordinal))
            {
                hand.Insert(0, extraHandCard);
            }
            parts.Add(ModLocalization.T("snap_hand", "手牌") + ": "
                + (hand.Count > 0 ? string.Join(", ", hand) : ModLocalization.T("snap_none", "none")));
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
                    enemies.Add(EnsureEnemyName(enemy) + " " + enemy.CurrentHp + "/" + enemy.MaxHp);
                }
            }
            if (enemies.Count > 0)
            {
                parts.Add(ModLocalization.T("snap_enemies", "Enemies") + ": " + string.Join("; ", enemies));
            }
            return (string.Join("\n", parts), creature.CurrentHp, creature.MaxHp);
        }
        catch (Exception ex)
        {
            LogCaptureError("snapshot build", ex);
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
    /// ("A、B"); the UI renders it as 「A」、「B」被消耗. The played card itself is
    /// included, so e.g. playing 净化 records 「净化」、「进阶之灾」… 被消耗.</summary>
    private static bool TryMergeExhaustCause(string name)
    {
        if (_causeId == null || Environment.TickCount64 - _causeAtMs > ExhaustWindowMs)
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
            var existing = cause.Annotations.FirstOrDefault(a => a.RefId == AnnotationProtocol.Exhaust);
            if (existing == null)
            {
                cause.Annotations.Add(new NotesAnnotation { RefId = AnnotationProtocol.Exhaust, Text = name, Meta = cause.Title });
            }
            else
            {
                if (existing.Meta.Length == 0)
                {
                    existing.Meta = cause.Title;
                }
                if (!existing.Text.Contains(name, StringComparison.Ordinal))
                {
                    existing.Text += "、" + name;
                }
            }
        }
        NotesRuntime.OnOpsChanged();
        return true;
    }

    /// <summary>Merges discarded card names into one raw-name annotation
    /// ("A、B"); the UI renders it as 「A」、「B」被弃置.</summary>
    private static bool TryMergeDiscardCause(string name)
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
            var existing = cause.Annotations.FirstOrDefault(a => a.RefId == AnnotationProtocol.Discard);
            if (existing == null)
            {
                cause.Annotations.Add(new NotesAnnotation { RefId = AnnotationProtocol.Discard, Text = name, Meta = cause.Title });
            }
            else
            {
                if (existing.Meta.Length == 0)
                {
                    existing.Meta = cause.Title;
                }
                if (!existing.Text.Contains(name, StringComparison.Ordinal))
                {
                    existing.Text += "、" + name;
                }
            }
        }
        NotesRuntime.OnOpsChanged();
        return true;
    }

    /// <summary>The operation a damage instance belongs to: matched by card
    /// source, else the current cause (card / potion) within a wide window
    /// (potion damage resolves after the throw animation).</summary>
    private static NotesOpData? FindDamageOp(CardModel? cardSource)
    {
        lock (Gate)
        {
            if (cardSource != null)
            {
                var refId = cardSource.Id.ToString();
                for (var i = Ops.Count - 1; i >= 0; i--)
                {
                    var op = Ops[i];
                    if (op.Turn == _turn && op.Kind == NotesOpKind.Card && op.RefId == refId)
                    {
                        return op;
                    }
                }
            }
            if (_causeId == null || Environment.TickCount64 - _causeAtMs > DamageCauseWindowMs)
            {
                return null;
            }
            var cause = Ops.FirstOrDefault(o => o.Id == _causeId);
            return cause is { Kind: NotesOpKind.Card or NotesOpKind.Potion } ? cause : null;
        }
    }

    /// <summary>True when the moving card is the very card that was just played
    /// (its own move to the discard pile must not become a "discarded" chip).</summary>
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

    /// <summary>Rate-limited error log for the hot capture paths: a broken
    /// capture must not spam the log once per card/hit. Counters reset per combat.</summary>
    private static void LogCaptureError(string site, Exception ex)
    {
        var count = _errorCounts.TryGetValue(site, out var current) ? current : 0;
        _errorCounts[site] = count + 1;
        if (count < ErrorLogLimit)
        {
            Log.Error($"[Notes] {site} failed: " + ex);
            if (count + 1 == ErrorLogLimit)
            {
                Log.Error($"[Notes] {site}: further errors suppressed for this combat");
            }
        }
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
