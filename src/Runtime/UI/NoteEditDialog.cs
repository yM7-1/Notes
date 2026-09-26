using Godot;
using Notes.Game;

namespace Notes.UI;

/// <summary>Small modal for editing a node's title/note or a branch condition.</summary>
public partial class NoteEditDialog : AcceptDialog
{
    private LineEdit _title = null!;
    private Label _titleLabel = null!;
    private VBoxContainer _noteBox = null!;
    private Label _noteLabel = null!;
    private TextEdit _note = null!;
    private Action<string, string>? _onSaved;
    private bool _allowNote = true;

    public override void _Ready()
    {
        OkButtonText = ModLocalization.T("save", "Save");
        UiStyle.StyleDialog(this);

        var root = new VBoxContainer { CustomMinimumSize = new Vector2(360, 0) };
        root.AddThemeConstantOverride("separation", 4);
        AddChild(root);

        _titleLabel = new Label { Text = ModLocalization.T("dialog_title_label", "Title") };
        _titleLabel.AddThemeColorOverride("font_color", UiStyle.TextDim);
        root.AddChild(_titleLabel);
        _title = new LineEdit();
        UiStyle.StyleInput(_title);
        _title.TextSubmitted += _ =>
        {
            Hide();
            OnConfirmed();
        };
        root.AddChild(_title);

        _noteBox = new VBoxContainer();
        root.AddChild(_noteBox);
        _noteLabel = new Label { Text = ModLocalization.T("dialog_note_label", "Note") };
        _noteLabel.AddThemeColorOverride("font_color", UiStyle.TextDim);
        _noteBox.AddChild(_noteLabel);
        _note = new TextEdit
        {
            CustomMinimumSize = new Vector2(360, 120),
            WrapMode = TextEdit.LineWrappingMode.Boundary,
        };
        UiStyle.StyleInput(_note);
        _noteBox.AddChild(_note);

        Confirmed += OnConfirmed;
    }

    public void OpenFor(string title, string note, bool allowNote, Action<string, string> onSaved, string? dialogTitle = null)
    {
        _onSaved = onSaved;
        _allowNote = allowNote;
        Title = dialogTitle ?? ModLocalization.T("dialog_node_title", "Edit node");
        _title.Text = title;
        _note.Text = note;
        _noteBox.Visible = allowNote;
        _titleLabel.Text = allowNote
            ? ModLocalization.T("dialog_title_label", "Title")
            : ModLocalization.T("dialog_label_label", "Condition (optional)");
        PopupCentered(new Vector2I(420, allowNote ? 320 : 180));
    }

    private void OnConfirmed()
    {
        var callback = _onSaved;
        _onSaved = null;
        callback?.Invoke(_title.Text.Trim(), _allowNote ? _note.Text : "");
    }
}
