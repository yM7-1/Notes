using Godot;
using Notes.Game;

namespace Notes.UI;

/// <summary>NotesWindow partial: the help popup (core loop + all shortcuts in
/// one place, so the toolbar and status bar can stay clean).</summary>
public partial class NotesWindow
{
    private PopupPanel _helpPopup = null!;

    private void BuildHelp()
    {
        _helpPopup = new PopupPanel { Name = "HelpPopup" };
        _helpPopup.AddThemeStyleboxOverride("panel",
            UiStyle.Box(UiStyle.PanelBg, UiStyle.PanelBorder, 8, 1, shadow: true));

        var panel = new VBoxContainer { CustomMinimumSize = new Vector2(460, 300) };
        panel.AddThemeConstantOverride("separation", 8);
        _helpPopup.AddChild(panel);

        var title = new Label { Text = ModLocalization.T("help_title", "帮助") };
        title.AddThemeFontSizeOverride("font_size", 15);
        title.AddThemeColorOverride("font_color", UiStyle.Accent);
        panel.AddChild(title);

        var loop = new Label
        {
            Text = ModLocalization.T("help_loop_title", "核心循环") + "\n"
                + "① " + ModLocalization.T("onboarding_step1", "") + "\n"
                + "② " + ModLocalization.T("onboarding_step2", "") + "\n"
                + "③ " + ModLocalization.T("onboarding_step3", ""),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        loop.AddThemeFontSizeOverride("font_size", 11);
        panel.AddChild(loop);

        var keys = new Label
        {
            Text = ModLocalization.T("help_keys_title", "快捷键") + "\n"
                + ModLocalization.T("help_keys", ""),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        keys.AddThemeFontSizeOverride("font_size", 11);
        keys.AddThemeColorOverride("font_color", UiStyle.TextDim);
        panel.AddChild(keys);

        var close = new Button { Text = ModLocalization.T("close", "关闭") };
        UiStyle.StyleButton(close);
        close.Pressed += () => _helpPopup.Hide();
        panel.AddChild(close);

        AddChild(_helpPopup);
    }

    private void OpenHelp() => _helpPopup.PopupCentered(new Vector2I(500, 360));
}
