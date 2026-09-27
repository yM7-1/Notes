using Godot;
using MegaCrit.Sts2.Core.Models;

namespace Notes.UI;

/// <summary>Lazy, cached card portraits for node thumbnails. Reading the game's
/// portrait atlas is best-effort: any failure falls back to the plain badge.</summary>
internal static class CardArt
{
    private static readonly Dictionary<string, CardModel?> Models = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Texture2D?> Portraits = new(StringComparer.Ordinal);

    public static Texture2D? Portrait(string? refId)
    {
        if (string.IsNullOrEmpty(refId))
        {
            return null;
        }
        if (Portraits.TryGetValue(refId, out var cached))
        {
            return cached;
        }
        Texture2D? texture = null;
        try
        {
            if (!Models.TryGetValue(refId, out var model))
            {
                model = FindModel(refId);
                Models[refId] = model;
            }
            if (model is { HasPortrait: true })
            {
                texture = model.Portrait;
            }
        }
        catch (Exception ex)
        {
            MegaCrit.Sts2.Core.Logging.Log.Error("[Notes] card portrait load failed: " + ex);
        }
        Portraits[refId] = texture;
        return texture;
    }

    /// <summary>Reload hook for a fresh card catalog (attributes never change
    /// between game content updates, so this is only used by tests/debug).</summary>
    public static void ClearCache()
    {
        Models.Clear();
        Portraits.Clear();
    }

    private static CardModel? FindModel(string refId)
    {
        foreach (var card in Game.CardCatalog.All)
        {
            try
            {
                if (card.Id.ToString() == refId)
                {
                    return card;
                }
            }
            catch
            {
                // card without a stable id: skip
            }
        }
        return null;
    }
}
