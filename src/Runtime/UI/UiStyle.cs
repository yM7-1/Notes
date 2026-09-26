using Godot;
using Notes.Core.Documents;
using Notes.Game;

namespace Notes.UI;

/// <summary>Shared colors and small style helpers for the notes UI.</summary>
internal static class UiStyle
{
    public static readonly Color WindowBg = Color.FromHtml("171a20");
    public static readonly Color PanelBg = Color.FromHtml("1e222b");
    public static readonly Color NodeBg = Color.FromHtml("232833");
    public static readonly Color NodeBgText = Color.FromHtml("262b36");
    public static readonly Color CanvasBg = Color.FromHtml("12141a");
    public static readonly Color GridDot = Color.FromHtml("262b34");
    public static readonly Color PanelBorder = Color.FromHtml("363c49");
    public static readonly Color TextMain = Color.FromHtml("e8eaf0");
    public static readonly Color TextDim = Color.FromHtml("9aa1ad");
    public static readonly Color Accent = Color.FromHtml("f0c674");
    public static readonly Color EdgeDefault = Color.FromHtml("707a8a");
    public static readonly Color BadgeBg = Color.FromHtml("0f1116");
    public static readonly Color ButtonBg = Color.FromHtml("262b35");
    public static readonly Color ButtonHover = Color.FromHtml("303743");
    public static readonly Color ButtonPressed = Color.FromHtml("1c2029");
    public static readonly Color AccentButtonBg = Color.FromHtml("3d3524");

    public static Color TypeColor(int cardType) => cardType switch
    {
        1 => Color.FromHtml("e06c5f"),
        2 => Color.FromHtml("5cb87f"),
        3 => Color.FromHtml("6a94e8"),
        4 => Color.FromHtml("8a8f98"),
        5 => Color.FromHtml("a678d6"),
        6 => Color.FromHtml("d2ab55"),
        _ => Color.FromHtml("8a8f98"),
    };

    public static Color StateColor(NodeState state) => state switch
    {
        NodeState.Tried => Color.FromHtml("8894a2"),
        NodeState.Speculated => Color.FromHtml("e8b04b"),
        NodeState.Confirmed => Color.FromHtml("5fc08a"),
        _ => PanelBorder,
    };

    public static Color RarityColor(int rarity) => rarity switch
    {
        3 => Color.FromHtml("6a94e8"),
        4 => Color.FromHtml("f0c674"),
        5 => Color.FromHtml("e08a4a"),
        6 => Color.FromHtml("5cb87f"),
        7 => Color.FromHtml("8a8f98"),
        8 => Color.FromHtml("a678d6"),
        _ => Color.FromHtml("6f7784"),
    };

    public static Color KindColor(NodeKind kind, int cardType) => kind switch
    {
        NodeKind.Card => TypeColor(cardType),
        NodeKind.Potion => Color.FromHtml("b06ad6"),
        NodeKind.Relic => Color.FromHtml("d9a13b"),
        NodeKind.Draw => Color.FromHtml("4fb3c9"),
        NodeKind.Discard => Color.FromHtml("8a8f98"),
        NodeKind.EndTurn => Color.FromHtml("5b6069"),
        NodeKind.Exhaust => Color.FromHtml("c07a4a"),
        _ => Accent,
    };

    public static string KindGlyph(NodeKind kind) => ModLocalization.IsChinese
        ? kind switch
        {
            NodeKind.Draw => "抽",
            NodeKind.Discard => "弃",
            NodeKind.Potion => "药",
            NodeKind.Relic => "遗",
            NodeKind.EndTurn => "终",
            NodeKind.Exhaust => "耗",
            _ => "",
        }
        : kind switch
        {
            NodeKind.Draw => "D",
            NodeKind.Discard => "X",
            NodeKind.Potion => "P",
            NodeKind.Relic => "R",
            NodeKind.EndTurn => "E",
            NodeKind.Exhaust => "EX",
            _ => "",
        };

    public static StyleBoxFlat Box(Color background, Color border, int radius = 6, int borderWidth = 1, bool shadow = false)
    {
        var style = new StyleBoxFlat { BgColor = background, BorderColor = border };
        style.SetBorderWidthAll(borderWidth);
        style.SetCornerRadiusAll(radius);
        style.ContentMarginLeft = 6;
        style.ContentMarginRight = 6;
        style.ContentMarginTop = 4;
        style.ContentMarginBottom = 4;
        if (shadow)
        {
            style.ShadowColor = new Color(0f, 0f, 0f, 0.45f);
            style.ShadowSize = 10;
            style.ShadowOffset = new Vector2(0, 4);
        }
        return style;
    }

    public static StyleBoxFlat ButtonStyle(Color background, Color border, int radius = 7, int padX = 12, int padY = 5)
    {
        var style = new StyleBoxFlat { BgColor = background, BorderColor = border };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(radius);
        style.ContentMarginLeft = padX;
        style.ContentMarginRight = padX;
        style.ContentMarginTop = padY;
        style.ContentMarginBottom = padY;
        return style;
    }

    public static void StyleButton(Button button, bool accent = false, int fontSize = 12)
    {
        var normal = accent ? AccentButtonBg : ButtonBg;
        var hover = accent ? Color.FromHtml("4a4130") : ButtonHover;
        var pressed = accent ? Color.FromHtml("56482f") : ButtonPressed;
        var border = accent ? Accent : PanelBorder;

        button.AddThemeStyleboxOverride("normal", ButtonStyle(normal, border));
        button.AddThemeStyleboxOverride("hover", ButtonStyle(hover, border));
        button.AddThemeStyleboxOverride("pressed", ButtonStyle(pressed, border));
        button.AddThemeStyleboxOverride("disabled", ButtonStyle(Color.FromHtml("1a1d24"), Color.FromHtml("2b303a")));
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        button.AddThemeColorOverride("font_color", TextMain);
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.AddThemeColorOverride("font_pressed_color", Accent);
        button.AddThemeColorOverride("font_disabled_color", Color.FromHtml("5b6069"));
        button.AddThemeFontSizeOverride("font_size", fontSize);
        button.FocusMode = Control.FocusModeEnum.None;
    }

    public static void StyleHandle(Panel panel, bool open)
    {
        var border = open ? Accent : PanelBorder;
        var background = open ? Color.FromHtml("2e2a1e") : Color.FromHtml("1c2029");
        panel.AddThemeStyleboxOverride("panel", ButtonStyle(background, border, 10, 10, 8));
    }

    public static void StyleRow(Button row)
    {
        row.AddThemeStyleboxOverride("normal", ButtonStyle(Color.FromHtml("20242d"), Color.FromHtml("2c313c"), 6, 8, 3));
        row.AddThemeStyleboxOverride("hover", ButtonStyle(Color.FromHtml("2a303b"), Color.FromHtml("414958"), 6, 8, 3));
        row.AddThemeStyleboxOverride("pressed", ButtonStyle(Color.FromHtml("1a1e26"), Accent, 6, 8, 3));
        row.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        row.AddThemeColorOverride("font_color", TextMain);
        row.AddThemeColorOverride("font_hover_color", Colors.White);
        row.FocusMode = Control.FocusModeEnum.None;
    }

    /// <summary>Dark theme for right-click menus.</summary>
    public static void StylePopup(PopupMenu menu)
    {
        menu.AddThemeStyleboxOverride("panel", Box(PanelBg, PanelBorder, 8, 1));
        menu.AddThemeStyleboxOverride("hover", ButtonStyle(ButtonHover, Accent, 6, 8, 4));
        menu.AddThemeColorOverride("font_color", TextMain);
        menu.AddThemeColorOverride("font_hover_color", Colors.White);
        menu.AddThemeColorOverride("font_disabled_color", Color.FromHtml("5b6069"));
        menu.AddThemeColorOverride("font_separator_color", TextDim);
        menu.AddThemeFontSizeOverride("font_size", 12);
    }

    /// <summary>Dark theme for modal dialogs (title bar + panel + buttons).</summary>
    public static void StyleDialog(Window dialog)
    {
        dialog.AddThemeStyleboxOverride("embedded_border", Box(WindowBg, PanelBorder, 10, 2, shadow: true));
        dialog.AddThemeColorOverride("title_color", Accent);
        dialog.AddThemeFontSizeOverride("title_font_size", 13);
        if (dialog is AcceptDialog accept)
        {
            accept.GetLabel().AddThemeColorOverride("font_color", TextMain);
            accept.GetLabel().AddThemeFontSizeOverride("font_size", 12);
            StyleButton(accept.GetOkButton(), accent: true);
            if (dialog is ConfirmationDialog confirm)
            {
                StyleButton(confirm.GetCancelButton());
            }
        }
    }

    public static void StyleInput(LineEdit edit)
    {
        edit.AddThemeStyleboxOverride("normal", Box(BadgeBg, PanelBorder, 6, 1));
        edit.AddThemeStyleboxOverride("focus", Box(BadgeBg, Accent, 6, 1));
        edit.AddThemeColorOverride("font_color", TextMain);
        edit.AddThemeColorOverride("font_placeholder_color", Color.FromHtml("6f7784"));
        edit.AddThemeColorOverride("caret_color", Accent);
        edit.AddThemeColorOverride("selection_color", new Color(Accent.R, Accent.G, Accent.B, 0.35f));
        edit.AddThemeFontSizeOverride("font_size", 12);
    }

    public static void StyleInput(TextEdit edit)
    {
        edit.AddThemeStyleboxOverride("normal", Box(BadgeBg, PanelBorder, 6, 1));
        edit.AddThemeStyleboxOverride("focus", Box(BadgeBg, Accent, 6, 1));
        edit.AddThemeColorOverride("font_color", TextMain);
        edit.AddThemeColorOverride("font_placeholder_color", Color.FromHtml("6f7784"));
        edit.AddThemeColorOverride("caret_color", Accent);
        edit.AddThemeColorOverride("selection_color", new Color(Accent.R, Accent.G, Accent.B, 0.35f));
        edit.AddThemeFontSizeOverride("font_size", 12);
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

    /// <summary>Human-readable annotation text. Auto-captured annotations store
    /// structured parts (raw names, enemy / pile, damage source) and are
    /// rendered per language here.</summary>
    public static string AnnotationText(NotesAnnotation annotation)
    {
        var refId = annotation.RefId;
        if (refId.StartsWith("exhaust:", StringComparison.Ordinal))
        {
            var names = annotation.Text.Split('、', StringSplitOptions.RemoveEmptyEntries).ToList();
            // Put the played card (the op that caused the exhausts) first.
            if (annotation.Meta.Length > 0)
            {
                var index = names.IndexOf(annotation.Meta);
                if (index > 0)
                {
                    names.RemoveAt(index);
                    names.Insert(0, annotation.Meta);
                }
            }
            var joined = ModLocalization.IsChinese
                ? string.Join("」、「", names)
                : string.Join(", ", names);
            return ModLocalization.T("annot_exhaust_fmt", "「{0}」被消耗").Replace("{0}", joined);
        }
        if (refId.StartsWith("discard:", StringComparison.Ordinal))
        {
            var names = annotation.Text.Split('、', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (annotation.Meta.Length > 0)
            {
                var index = names.IndexOf(annotation.Meta);
                if (index > 0)
                {
                    names.RemoveAt(index);
                    names.Insert(0, annotation.Meta);
                }
            }
            var joined = ModLocalization.IsChinese
                ? string.Join("」、「", names)
                : string.Join(", ", names);
            return ModLocalization.T("annot_discard_fmt", "「{0}」被弃置").Replace("{0}", joined);
        }
        if (refId.StartsWith("damage:", StringComparison.Ordinal))
        {
            var parts = annotation.Meta.Split('\u001f');
            var source = parts.Length > 0 ? parts[0] : "";
            var killed = parts.Length > 1 && parts[1] == "1";
            return killed
                ? Format("annot_damage_kill", "「{0}」对 {1} 造成 {2} 点伤害并击杀", source, annotation.Text, annotation.Count)
                : Format("annot_damage", "「{0}」对 {1} 造成 {2} 点伤害", source, annotation.Text, annotation.Count);
        }
        if (refId.StartsWith("insert:", StringComparison.Ordinal))
        {
            var parts = annotation.Meta.Split('\u001f');
            var enemy = parts.Length > 0 ? parts[0] : "";
            var pileKey = parts.Length > 1 ? parts[1] : "";
            var card = annotation.Count > 1
                ? Format("annot_copies", "{0}*{1}", annotation.Text, annotation.Count)
                : annotation.Text;
            return Format("annot_insert", "【{0}】将【{1}】加入到【{2}】", enemy, card, PileLabel(pileKey));
        }
        if (refId.StartsWith("loss:", StringComparison.Ordinal))
        {
            return Format("annot_loss", "战损 {0}", annotation.Count);
        }
        return annotation.Text;
    }

    /// <summary>Annotations that belong on the boundary strip between two turn
    /// regions (enemy insertions, unattributed exhausts / discards, loss).</summary>
    public static bool IsBoundaryAnnotation(string refId) => NotesAnnotation.IsBoundaryRef(refId);

    /// <summary>Accent color for an annotation chip / badge.</summary>
    public static Color AnnotationColor(string refId)
    {
        if (refId.StartsWith("relic:", StringComparison.Ordinal))
        {
            return KindColor(NodeKind.Relic, -1);
        }
        if (refId.StartsWith("exhaust:", StringComparison.Ordinal))
        {
            return KindColor(NodeKind.Exhaust, -1);
        }
        if (refId.StartsWith("discard:", StringComparison.Ordinal))
        {
            return KindColor(NodeKind.Discard, -1);
        }
        if (refId.StartsWith("insert:", StringComparison.Ordinal))
        {
            return Color.FromHtml("cf6f9a");
        }
        if (refId.StartsWith("loss:", StringComparison.Ordinal))
        {
            return Color.FromHtml("e06c5f");
        }
        if (refId.StartsWith("damage:", StringComparison.Ordinal))
        {
            return Color.FromHtml("e06c5f");
        }
        if (refId.StartsWith("card:", StringComparison.Ordinal))
        {
            return KindColor(NodeKind.Draw, -1);
        }
        return PanelBorder;
    }

    private static string PileLabel(string key) => key switch
    {
        "pile_draw" => ModLocalization.T("pile_draw", "抽牌堆"),
        "pile_hand" => ModLocalization.T("pile_hand", "手牌"),
        "pile_exhaust" => ModLocalization.T("pile_exhaust", "消耗堆"),
        _ => ModLocalization.T("pile_discard", "弃牌堆"),
    };

    private static string Format(string key, string fallback, params object[] args)
    {
        var text = ModLocalization.T(key, fallback);
        for (var i = 0; i < args.Length; i++)
        {
            text = text.Replace("{" + i + "}", args[i]?.ToString() ?? "");
        }
        return text;
    }
}
