using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace Notes.Game;

/// <summary>Display snapshot of a card, taken when a node is created. Keeps the
/// legend readable even if the underlying model changes with game updates.</summary>
public readonly record struct CardSnapshot(
    string RefId,
    string Title,
    int Cost,
    int CardType,
    int Rarity,
    bool Upgraded)
{
    public string DisplayTitle => Upgraded ? Title + "+" : Title;

    public string CostText => Cost < 0 ? "X" : Cost.ToString();
}

/// <summary>Read-only card lookups against the game content database.</summary>
internal static class CardCatalog
{
    private static List<CardModel>? _all;

    public static IReadOnlyList<CardModel> All
    {
        get
        {
            if (_all != null)
            {
                return _all;
            }
            var list = new List<CardModel>();
            try
            {
                foreach (var card in ModelDb.AllCards)
                {
                    if (card != null)
                    {
                        list.Add(card);
                    }
                }
            }
            catch (Exception ex)
            {
                MegaCrit.Sts2.Core.Logging.Log.Error("[Notes] card catalog build failed: " + ex);
            }
            list.Sort((a, b) => string.Compare(TitleOf(a), TitleOf(b), StringComparison.CurrentCultureIgnoreCase));
            _all = list;
            return _all;
        }
    }


    public static CardSnapshot Snapshot(CardModel card) => new(
        RefId: card.Id.ToString(),
        Title: TitleOf(card),
        Cost: ResolveCost(card),
        CardType: (int)card.Type,
        Rarity: (int)card.Rarity,
        Upgraded: card.IsUpgraded);

    /// <summary>Canonical cost; -1 = X / unknown.</summary>
    public static int CostOf(CardModel card) => ResolveCost(card);

    public static string TitleOf(CardModel card)
    {
        try
        {
            var title = card.Title;
            if (!string.IsNullOrWhiteSpace(title))
            {
                return title;
            }
        }
        catch
        {
            // fall through to the entry id
        }
        return card.Id.Entry ?? "";
    }

    private static int ResolveCost(CardModel card)
    {
        try
        {
            var cost = card.EnergyCost;
            if (cost.CostsX)
            {
                return -1;
            }
            return cost.Canonical;
        }
        catch
        {
            return -1;
        }
    }
}
