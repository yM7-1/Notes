using Godot;
using Notes.Core.Documents;

namespace Notes.UI;

/// <summary>Shared colors and small style helpers for the notes UI.</summary>
internal static class UiStyle
{
    public static readonly Color WindowBg = Color.FromHtml("16181d");
    public static readonly Color PanelBg = Color.FromHtml("1f222a");
    public static readonly Color NodeBg = Color.FromHtml("20232b");
    public static readonly Color NodeBgText = Color.FromHtml("23262e");
    public static readonly Color PanelBorder = Color.FromHtml("3a3f4b");
    public static readonly Color TextMain = Color.FromHtml("e6e8ee");
    public static readonly Color TextDim = Color.FromHtml("9aa0ab");
    public static readonly Color Accent = Color.FromHtml("f0c674");
    public static readonly Color EdgeDefault = Color.FromHtml("6b7280");

    public static Color TypeColor(int cardType) => cardType switch
    {
        1 => Color.FromHtml("d95f5f"),
        2 => Color.FromHtml("57a773"),
        3 => Color.FromHtml("5b8def"),
        4 => Color.FromHtml("8a8f98"),
        5 => Color.FromHtml("9b6bd3"),
        6 => Color.FromHtml("c8a24a"),
        _ => Color.FromHtml("8a8f98"),
    };

    public static Color StateColor(NodeState state) => state switch
    {
        NodeState.Tried => Color.FromHtml("7f8c8d"),
        NodeState.Speculated => Color.FromHtml("e0a030"),
        NodeState.Confirmed => Color.FromHtml("4caf7d"),
        _ => PanelBorder,
    };

    public static Color RarityColor(int rarity) => rarity switch
    {
        3 => Color.FromHtml("5b8def"),
        4 => Color.FromHtml("f0c674"),
        5 => Color.FromHtml("e08a4a"),
        6 => Color.FromHtml("57a773"),
        7 => Color.FromHtml("8a8f98"),
        8 => Color.FromHtml("9b6bd3"),
        _ => Color.FromHtml("6b7280"),
    };

    public static StyleBoxFlat Box(Color background, Color border, int radius = 6, int borderWidth = 1)
    {
        var style = new StyleBoxFlat { BgColor = background, BorderColor = border };
        style.SetBorderWidthAll(borderWidth);
        style.SetCornerRadiusAll(radius);
        style.ContentMarginLeft = 6;
        style.ContentMarginRight = 6;
        style.ContentMarginTop = 4;
        style.ContentMarginBottom = 4;
        return style;
    }

    public static string Ellipsize(string text, Font font, int fontSize, float maxWidth)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }
        if (font.GetStringSize(text, HorizontalAlignment.Left, -1, fontSize).X <= maxWidth)
        {
            return text;
        }
        var ellipsis = "…";
        var trimmed = text;
        while (trimmed.Length > 1)
        {
            trimmed = trimmed[..^1];
            if (font.GetStringSize(trimmed + ellipsis, HorizontalAlignment.Left, -1, fontSize).X <= maxWidth)
            {
                return trimmed + ellipsis;
            }
        }
        return ellipsis;
    }
}
