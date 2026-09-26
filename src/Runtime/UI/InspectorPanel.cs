using Godot;
using Notes.Game;

namespace Notes.UI;

/// <summary>Bottom-right panel: details of the selected step / turn / world line.</summary>
public partial class InspectorPanel : PanelContainer
{
    private Label _title = null!;
    private Label _body = null!;

    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.PanelBg, UiStyle.PanelBorder, shadow: true));
        CustomMinimumSize = new Vector2(292, 150);

        var root = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        root.AddThemeConstantOverride("separation", 4);
        AddChild(root);

        _title = new Label { Text = ModLocalization.T("inspector_title", "Detail") };
        _title.AddThemeFontSizeOverride("font_size", 13);
        _title.AddThemeColorOverride("font_color", UiStyle.Accent);
        root.AddChild(_title);

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        root.AddChild(scroll);

        _body = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _body.AddThemeFontSizeOverride("font_size", 11);
        _body.AddThemeColorOverride("font_color", UiStyle.TextMain);
        scroll.AddChild(_body);
    }

    public void Show(string title, string body)
    {
        _title.Text = title;
        _body.Text = body;
    }
}
