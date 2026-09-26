using Godot;
using Notes.Core.Documents;
using Notes.Core.Services;
using Notes.Game;

namespace Notes.UI;

/// <summary>
/// The mind-map canvas: pan/zoom, drag &amp; drop from the card palette, node moves,
/// branch creation and the node / edge / canvas context menus.
/// </summary>
public partial class BoardCanvas : Control
{
    private const int MenuEdit = 1;
    private const int MenuTried = 2;
    private const int MenuSpeculated = 3;
    private const int MenuConfirmed = 4;
    private const int MenuClearState = 5;
    private const int MenuDuplicate = 6;
    private const int MenuDelete = 7;
    private const int MenuLink = 8;

    private const int EdgeEdit = 11;
    private const int EdgeDelete = 12;

    private const int CanvasAddText = 21;
    private const int CanvasResetView = 22;

    private CanvasSurface _surface = null!;
    private PopupMenu _nodeMenu = null!;
    private PopupMenu _edgeMenu = null!;
    private PopupMenu _canvasMenu = null!;
    private NoteEditDialog _editor = null!;
    private NotesBoard? _board;
    private string? _linkFrom;
    private string _menuNodeId = "";
    private string _menuEdgeId = "";
    private bool _panning;

    public bool IsLinking => _linkFrom != null;

    public string? LinkFromId => _linkFrom;

    public override void _Ready()
    {
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.None;

        _surface = new CanvasSurface { Name = "Surface" };
        _surface.Setup(this);
        AddChild(_surface);

        _nodeMenu = new PopupMenu { Name = "NodeMenu" };
        _nodeMenu.AddItem(ModLocalization.T("node_edit", "Edit…"), MenuEdit);
        _nodeMenu.AddItem(ModLocalization.T("node_state_tried", "Tried"), MenuTried);
        _nodeMenu.AddItem(ModLocalization.T("node_state_speculated", "Speculated"), MenuSpeculated);
        _nodeMenu.AddItem(ModLocalization.T("node_state_confirmed", "Confirmed"), MenuConfirmed);
        _nodeMenu.AddItem(ModLocalization.T("node_state_clear", "Clear mark"), MenuClearState);
        _nodeMenu.AddItem(ModLocalization.T("node_duplicate", "Duplicate"), MenuDuplicate);
        _nodeMenu.AddItem(ModLocalization.T("node_link", "Link from here"), MenuLink);
        _nodeMenu.AddItem(ModLocalization.T("node_delete", "Delete node"), MenuDelete);
        _nodeMenu.IdPressed += OnNodeMenuId;
        AddChild(_nodeMenu);

        _edgeMenu = new PopupMenu { Name = "EdgeMenu" };
        _edgeMenu.AddItem(ModLocalization.T("edge_edit", "Edit condition…"), EdgeEdit);
        _edgeMenu.AddItem(ModLocalization.T("edge_delete", "Delete branch"), EdgeDelete);
        _edgeMenu.IdPressed += OnEdgeMenuId;
        AddChild(_edgeMenu);

        _canvasMenu = new PopupMenu { Name = "CanvasMenu" };
        _canvasMenu.AddItem(ModLocalization.T("canvas_add_text", "Add text here"), CanvasAddText);
        _canvasMenu.AddItem(ModLocalization.T("canvas_reset", "Reset view"), CanvasResetView);
        _canvasMenu.IdPressed += OnCanvasMenuId;
        AddChild(_canvasMenu);

        _editor = new NoteEditDialog { Name = "NoteEditor" };
        AddChild(_editor);

        NotesRuntime.Changed += OnRuntimeChanged;
        OnRuntimeChanged();
    }

    public override void _ExitTree()
    {
        NotesRuntime.Changed -= OnRuntimeChanged;
    }

    private void OnRuntimeChanged()
    {
        _board = NotesRuntime.ActiveBoard;
        _linkFrom = null;
        _surface.SetBoard(_board);
        ApplyView();
    }

    private void ApplyView()
    {
        if (_board == null)
        {
            return;
        }
        _surface.Scale = new Vector2(_board.Zoom, _board.Zoom);
        _surface.Position = new Vector2(_board.PanX, _board.PanY);
        _surface.QueueRedraw();
    }

    public void ResetView()
    {
        if (_board == null)
        {
            return;
        }
        _board.Zoom = 1f;
        _board.PanX = 0;
        _board.PanY = 0;
        ApplyView();
        NotesRuntime.ScheduleSave();
    }

    /// <summary>View center in board coordinates (for keyboard/button placement).</summary>
    public Vector2 ViewCenterInBoardCoords()
    {
        var center = Size / 2f;
        var zoom = _board?.Zoom ?? 1f;
        return (center - _surface.Position) / zoom;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (_board == null)
        {
            return;
        }

        if (@event is InputEventMouseButton button)
        {
            if (button.Pressed && button.ButtonIndex == MouseButton.WheelUp)
            {
                ZoomAt(button.Position, 1.1f);
                AcceptEvent();
            }
            else if (button.Pressed && button.ButtonIndex == MouseButton.WheelDown)
            {
                ZoomAt(button.Position, 1f / 1.1f);
                AcceptEvent();
            }
            else if (button.ButtonIndex == MouseButton.Middle)
            {
                _panning = button.Pressed;
                AcceptEvent();
            }
            else if (button.Pressed && button.ButtonIndex == MouseButton.Right)
            {
                _canvasMenu.Popup(new Rect2I((Vector2I)GetGlobalMousePosition(), Vector2I.Zero));
                AcceptEvent();
            }
            else if (button.Pressed && button.ButtonIndex == MouseButton.Left)
            {
                if (IsLinking)
                {
                    CancelLink();
                    AcceptEvent();
                    return;
                }
                var local = _surface.GetLocalMousePosition();
                if (_surface.TryGetEdgeNear(local, 8f, out var edgeId))
                {
                    _menuEdgeId = edgeId;
                    _edgeMenu.Popup(new Rect2I((Vector2I)GetGlobalMousePosition(), Vector2I.Zero));
                }
                AcceptEvent();
            }
        }
        else if (@event is InputEventMouseMotion motion && _panning && _board != null)
        {
            _surface.Position += motion.Relative;
            _board.PanX = _surface.Position.X;
            _board.PanY = _surface.Position.Y;
            NotesRuntime.ScheduleSave();
            AcceptEvent();
        }
    }

    private void ZoomAt(Vector2 mouse, float factor)
    {
        if (_board == null)
        {
            return;
        }
        var zoom = Math.Clamp(_board.Zoom * factor, 0.5f, 2f);
        if (Math.Abs(zoom - _board.Zoom) < 0.001f)
        {
            return;
        }
        var ratio = zoom / _board.Zoom;
        _surface.Position = mouse - (mouse - _surface.Position) * ratio;
        _board.Zoom = zoom;
        _board.PanX = _surface.Position.X;
        _board.PanY = _surface.Position.Y;
        ApplyView();
        NotesRuntime.ScheduleSave();
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        if (data.VariantType != Variant.Type.Dictionary)
        {
            return false;
        }
        var dict = data.AsGodotDictionary();
        return dict.ContainsKey("kind") && dict["kind"].AsString() == "card";
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (_board == null)
        {
            return;
        }
        var dict = data.AsGodotDictionary();
        var snapshot = new CardSnapshot(
            dict["refId"].AsString(),
            dict["title"].AsString(),
            dict["cost"].AsInt32(),
            dict["type"].AsInt32(),
            dict["rarity"].AsInt32(),
            dict["upgraded"].AsBool());
        var surfaceLocal = (atPosition - _surface.Position) / _surface.Scale;
        AddCardNode(snapshot, surfaceLocal - new Vector2(NodeControl.NodeWidth / 2f, NodeControl.NodeHeight / 2f));
    }

    public void AddCardNode(CardSnapshot snapshot, Vector2 position)
    {
        if (_board == null)
        {
            return;
        }
        var node = NotesRuntime.CreateCardNode(snapshot, position.X, position.Y);
        NotesRuntime.Commands.Execute(new AddNodeCommand(NotesRuntime.ActiveDocument, _board.Id, node));
        NotesRuntime.Raise();
    }

    public void AddTextNode(Vector2 position)
    {
        if (_board == null)
        {
            return;
        }
        var node = NotesRuntime.CreateTextNode(ModLocalization.T("text_node_default", "Idea"), position.X, position.Y);
        NotesRuntime.Commands.Execute(new AddNodeCommand(NotesRuntime.ActiveDocument, _board.Id, node));
        NotesRuntime.Raise();
        OpenEditor(node.Id);
    }

    public void OnNodeMoved() => _surface.QueueRedraw();

    public void CommitNodeMove(string nodeId, Vector2 from, Vector2 to)
    {
        if (_board == null)
        {
            return;
        }
        NotesRuntime.Commands.PushApplied(new MoveNodeCommand(
            NotesRuntime.ActiveDocument, _board.Id, nodeId, from.X, from.Y, to.X, to.Y));
        NotesRuntime.Raise();
    }

    public void BeginLink(string nodeId)
    {
        _linkFrom = nodeId;
        _surface.QueueRedraw();
    }

    public void CancelLink()
    {
        _linkFrom = null;
        _surface.QueueRedraw();
    }

    public void CompleteLink()
    {
        if (_board == null || _linkFrom == null)
        {
            CancelLink();
            return;
        }
        var from = _linkFrom;
        _linkFrom = null;
        if (_surface.TryGetNodeAt(_surface.GetLocalMousePosition(), out var target) && target != from)
        {
            var edge = new NotesEdge
            {
                Id = IdFactory.NewEdgeId(),
                From = from,
                To = target,
            };
            NotesRuntime.Commands.Execute(new AddEdgeCommand(NotesRuntime.ActiveDocument, _board.Id, edge));
            NotesRuntime.Raise();
        }
        else
        {
            _surface.QueueRedraw();
        }
    }

    public void OpenNodeMenu(string nodeId, Vector2 globalPosition)
    {
        _menuNodeId = nodeId;
        _nodeMenu.Popup(new Rect2I((Vector2I)globalPosition, Vector2I.Zero));
    }

    private void OnNodeMenuId(long id)
    {
        if (_board == null || _board.FindNode(_menuNodeId) == null)
        {
            return;
        }
        switch ((int)id)
        {
            case MenuEdit:
                OpenEditor(_menuNodeId);
                break;
            case MenuTried:
                SetNodeState(_menuNodeId, NodeState.Tried);
                break;
            case MenuSpeculated:
                SetNodeState(_menuNodeId, NodeState.Speculated);
                break;
            case MenuConfirmed:
                SetNodeState(_menuNodeId, NodeState.Confirmed);
                break;
            case MenuClearState:
                SetNodeState(_menuNodeId, NodeState.None);
                break;
            case MenuDuplicate:
                DuplicateNode(_menuNodeId);
                break;
            case MenuLink:
                BeginLink(_menuNodeId);
                break;
            case MenuDelete:
                NotesRuntime.Commands.Execute(new RemoveNodeCommand(NotesRuntime.ActiveDocument, _board.Id, _menuNodeId));
                NotesRuntime.Raise();
                break;
        }
    }

    private void SetNodeState(string nodeId, NodeState state)
    {
        if (_board == null)
        {
            return;
        }
        var node = _board.FindNode(nodeId);
        if (node == null)
        {
            return;
        }
        var before = node.Clone();
        var after = node.Clone();
        after.State = state;
        NotesRuntime.Commands.Execute(new UpdateNodeCommand(
            NotesRuntime.ActiveDocument, _board.Id, nodeId, before, after));
        NotesRuntime.Raise();
    }

    private void DuplicateNode(string nodeId)
    {
        if (_board == null)
        {
            return;
        }
        var node = _board.FindNode(nodeId);
        if (node == null)
        {
            return;
        }
        var copy = node.Clone();
        copy.Id = IdFactory.NewNodeId();
        copy.X += 24;
        copy.Y += 24;
        NotesRuntime.Commands.Execute(new AddNodeCommand(NotesRuntime.ActiveDocument, _board.Id, copy));
        NotesRuntime.Raise();
    }

    public void OpenEditor(string nodeId)
    {
        if (_board == null)
        {
            return;
        }
        var node = _board.FindNode(nodeId);
        if (node == null)
        {
            return;
        }
        var before = node.Clone();
        _editor.OpenFor(node.Title, node.Note, allowNote: true, (title, note) =>
        {
            var after = before.Clone();
            after.Title = string.IsNullOrWhiteSpace(title) ? before.Title : title;
            after.Note = note;
            NotesRuntime.Commands.Execute(new UpdateNodeCommand(
                NotesRuntime.ActiveDocument, _board.Id, nodeId, before, after));
            NotesRuntime.Raise();
        });
    }

    private void OnEdgeMenuId(long id)
    {
        if (_board == null)
        {
            return;
        }
        var edge = _board.FindEdge(_menuEdgeId);
        if (edge == null)
        {
            return;
        }
        switch ((int)id)
        {
            case EdgeEdit:
            {
                var before = edge.Label;
                _editor.OpenFor(
                    edge.Label,
                    "",
                    allowNote: false,
                    (label, _) =>
                    {
                        NotesRuntime.Commands.Execute(new UpdateEdgeCommand(
                            NotesRuntime.ActiveDocument, _board.Id, edge.Id, before, label));
                        NotesRuntime.Raise();
                    },
                    dialogTitle: ModLocalization.T("dialog_edge_title", "Edit branch condition"));
                break;
            }
            case EdgeDelete:
                NotesRuntime.Commands.Execute(new RemoveEdgeCommand(
                    NotesRuntime.ActiveDocument, _board.Id, edge.Id));
                NotesRuntime.Raise();
                break;
        }
    }

    private void OnCanvasMenuId(long id)
    {
        switch ((int)id)
        {
            case CanvasAddText:
                AddTextNode(ViewCenterInBoardCoords() - new Vector2(NodeControl.NodeWidth / 2f, NodeControl.NodeHeight / 2f));
                break;
            case CanvasResetView:
                ResetView();
                break;
        }
    }
}
