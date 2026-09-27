using Godot;
using Notes.Game;

namespace Notes.UI;

/// <summary>NotesWindow partial: the settings popup (persisted in the global
/// profile data, applied immediately).</summary>
public partial class NotesWindow
{
    private PopupPanel _settingsPopup = null!;
    private CheckBox _settingsAutoRecord = null!;
    private SpinBox _settingsMessageLifetime = null!;
    private SpinBox _settingsUndoLimit = null!;
    private OptionButton _settingsDefaultLibrary = null!;
    private SpinBox _settingsCodexLimit = null!;
    private CheckBox _settingsQuickRecord = null!;
    private CheckBox _settingsQuickCopy = null!;
    private CheckBox _settingsQuickSettings = null!;
    private bool _settingsUpdating;

    private void BuildSettings()
    {
        _settingsPopup = new PopupPanel { Name = "SettingsPopup" };
        _settingsPopup.AddThemeStyleboxOverride("panel",
            UiStyle.Box(UiStyle.PanelBg, UiStyle.PanelBorder, 8, 1, shadow: true));

        var panel = new VBoxContainer { CustomMinimumSize = new Vector2(360, 240) };
        panel.AddThemeConstantOverride("separation", 8);
        _settingsPopup.AddChild(panel);

        var title = new Label { Text = ModLocalization.T("settings_title", "设置") };
        title.AddThemeFontSizeOverride("font_size", 15);
        title.AddThemeColorOverride("font_color", UiStyle.Accent);
        panel.AddChild(title);

        _settingsAutoRecord = new CheckBox
        {
            Text = ModLocalization.T("settings_auto_record", "自动记录战斗操作（当前世界线）"),
        };
        _settingsAutoRecord.Toggled += on =>
        {
            if (!_settingsUpdating)
            {
                NotesRuntime.UpdateSettings(data => data.AutoRecordOps = on);
            }
        };
        panel.AddChild(_settingsAutoRecord);

        _settingsMessageLifetime = AddSpinRow(panel,
            ModLocalization.T("settings_message_lifetime", "状态栏提示时长（秒，0=常驻）"), 0, 60, 1);
        _settingsMessageLifetime.ValueChanged += value =>
        {
            if (!_settingsUpdating)
            {
                NotesRuntime.UpdateSettings(data => data.MessageLifetimeSeconds = value);
            }
        };

        _settingsUndoLimit = AddSpinRow(panel,
            ModLocalization.T("settings_undo_limit", "撤销步数上限"), 10, 1000, 10);
        _settingsUndoLimit.ValueChanged += value =>
        {
            if (!_settingsUpdating)
            {
                NotesRuntime.UpdateSettings(data => data.UndoLimit = (int)value);
            }
        };

        var libraryRow = new HBoxContainer();
        libraryRow.AddThemeConstantOverride("separation", 8);
        var libraryLabel = new Label
        {
            Text = ModLocalization.T("settings_default_library", "开局的默认库"),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        libraryLabel.AddThemeFontSizeOverride("font_size", 11);
        libraryRow.AddChild(libraryLabel);
        _settingsDefaultLibrary = new OptionButton { FocusMode = FocusModeEnum.None };
        UiStyle.StyleOptionButton(_settingsDefaultLibrary, fontSize: 11);
        _settingsDefaultLibrary.AddItem(ModLocalization.T("library_run", "库：本局"));
        _settingsDefaultLibrary.AddItem(ModLocalization.T("library_global", "库：全局"));
        _settingsDefaultLibrary.ItemSelected += index =>
        {
            if (!_settingsUpdating)
            {
                NotesRuntime.UpdateSettings(data => data.DefaultLibrary = (int)index);
            }
        };
        libraryRow.AddChild(_settingsDefaultLibrary);
        panel.AddChild(libraryRow);

        _settingsCodexLimit = AddSpinRow(panel,
            ModLocalization.T("settings_codex_limit", "图鉴搜索结果上限"), 10, 500, 10);
        _settingsCodexLimit.ValueChanged += value =>
        {
            if (!_settingsUpdating)
            {
                NotesRuntime.UpdateSettings(data => data.CodexLimit = (int)value);
            }
        };

        var quickTitle = new Label { Text = ModLocalization.T("quick_actions_title", "悬浮球快捷动作") };
        quickTitle.AddThemeFontSizeOverride("font_size", 11);
        quickTitle.AddThemeColorOverride("font_color", UiStyle.TextDim);
        panel.AddChild(quickTitle);
        _settingsQuickRecord = MakeQuickCheck(panel, "quick_act_record", "记本回合", 1);
        _settingsQuickCopy = MakeQuickCheck(panel, "quick_act_copy", "复制→新线", 2);
        _settingsQuickSettings = MakeQuickCheck(panel, "quick_act_settings", "打开设置", 4);

        var exportRow = new HBoxContainer();
        exportRow.AddThemeConstantOverride("separation", 8);
        var exportLabel = new Label
        {
            Text = ModLocalization.T("settings_export_dir", "导出目录（Markdown + PNG）"),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        exportLabel.AddThemeFontSizeOverride("font_size", 11);
        exportRow.AddChild(exportLabel);
        var exportButton = new Button { Text = ModLocalization.T("settings_open_export", "打开目录") };
        UiStyle.StyleButton(exportButton, fontSize: 11);
        exportButton.Pressed += OpenExportDirectory;
        exportRow.AddChild(exportButton);
        panel.AddChild(exportRow);

        var close = new Button { Text = ModLocalization.T("close", "Close") };
        UiStyle.StyleButton(close);
        close.Pressed += () => _settingsPopup.Hide();
        panel.AddChild(close);

        AddChild(_settingsPopup);
    }

    private static SpinBox AddSpinRow(VBoxContainer parent, string label, int min, int max, int step)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var text = new Label { Text = label, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        text.AddThemeFontSizeOverride("font_size", 11);
        row.AddChild(text);
        var spin = new SpinBox { MinValue = min, MaxValue = max, Step = step, FocusMode = FocusModeEnum.None };
        spin.GetLineEdit().SelectAllOnFocus = true;
        row.AddChild(spin);
        parent.AddChild(row);
        return spin;
    }

    /// <summary>One quick-action toggle (bit in <c>QuickActionsMask</c>).</summary>
    private CheckBox MakeQuickCheck(VBoxContainer parent, string key, string fallback, int bit)
    {
        var check = new CheckBox { Text = ModLocalization.T(key, fallback) };
        check.Toggled += on =>
        {
            if (_settingsUpdating)
            {
                return;
            }
            NotesRuntime.UpdateSettings(data =>
                data.QuickActionsMask = on
                    ? data.QuickActionsMask | bit
                    : data.QuickActionsMask & ~bit);
        };
        parent.AddChild(check);
        return check;
    }

    public void OpenSettings()
    {
        _settingsUpdating = true;
        try
        {
            NotesPersistence.TryGetGlobalData(out var data);
            _settingsAutoRecord.ButtonPressed = data.AutoRecordOps;
            _settingsMessageLifetime.Value = data.MessageLifetimeSeconds;
            _settingsUndoLimit.Value = data.UndoLimit;
            _settingsDefaultLibrary.Selected = data.DefaultLibrary == 1 ? 1 : 0;
            _settingsCodexLimit.Value = data.CodexLimit;
            _settingsQuickRecord.ButtonPressed = (data.QuickActionsMask & 1) != 0;
            _settingsQuickCopy.ButtonPressed = (data.QuickActionsMask & 2) != 0;
            _settingsQuickSettings.ButtonPressed = (data.QuickActionsMask & 4) != 0;
        }
        finally
        {
            _settingsUpdating = false;
        }
        _settingsPopup.PopupCentered(new Vector2I(400, 420));
    }
}
