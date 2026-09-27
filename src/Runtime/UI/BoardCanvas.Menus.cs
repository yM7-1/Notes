using Godot;
using Notes.Core.Documents;
using Notes.Core.Services;
using Notes.Game;

namespace Notes.UI;

/// <summary>BoardCanvas partial: the four context menus and the node operations
/// they invoke (state marks, duplicate, slots, detach, edit). Split out of
/// BoardCanvas.cs so the canvas core stays about pan / zoom / drag &amp; drop.</summary>
public partial class BoardCanvas
{
    private const int MenuEdit = 1;
    private const int MenuTried = 2;
    private const int MenuSpeculated = 3;
    private const int MenuConfirmed = 4;
    private const int MenuClearState = 5;
    private const int MenuDuplicate = 6;
    private const int MenuDelete = 7;
    private const int MenuLink = 8;
    private const int MenuToggleUpgrade = 9;
    private const int MenuAddSlot = 10;
    private const int MenuDetachRegion = 11;

    private const int EdgeEdit = 11;
    private const int EdgeDelete = 12;

    private const int CanvasAddText = 21;
    private const int CanvasResetView = 22;
    private const int CanvasNewWorldLine = 23;

    private const int RegionClear = 41;
    private const int RegionDelete = 42;

    /// <summary>Builds the node / edge / canvas / region menus and the shared
    /// edit dialog; called once from <c>_Ready</c>.</summary>
    private void BuildMenus()
    {
        _nodeMenu = new PopupMenu { Name = "NodeMenu" };
        _nodeMenu.AddItem(ModLocalization.T("node_edit", "Edit…"), MenuEdit);
        _nodeMenu.AddItem(ModLocalization.T("node_state_tried", "Tried"), MenuTried);
        _nodeMenu.AddItem(ModLocalization.T("node_state_speculated", "Speculated"), MenuSpeculated);
        _nodeMenu.AddItem(ModLocalization.T("node_state_confirmed", "Confirmed"), MenuConfirmed);
        _nodeMenu.AddItem(ModLocalization.T("node_state_clear", "Clear mark"), MenuClearState);
        _nodeMenu.AddItem(ModLocalization.T("node_duplicate", "Duplicate"), MenuDuplicate);
        _nodeMenu.AddItem(ModLocalization.T("node_link", "Link from here"), MenuLink);
        _nodeMenu.AddItem(ModLocalization.T("node_toggle_upgrade", "Toggle upgraded +"), MenuToggleUpgrade);
        _nodeMenu.AddItem(ModLocalization.T("node_add_slot", "Add parallel slot"), MenuAddSlot);
        _nodeMenu.AddItem(ModLocalization.T("node_detach_region", "Detach from turn region"), MenuDetachRegion);
        _nodeMenu.AddItem(ModLocalization.T("node_delete", "Delete node"), MenuDelete);
        _nodeMenu.IdPressed += OnNodeMenuId;
        UiStyle.StylePopup(_nodeMenu);
        AddChild(_nodeMenu);

        _edgeMenu = new PopupMenu { Name = "EdgeMenu" };
        _edgeMenu.AddItem(ModLocalization.T("edge_edit", "Edit condition…"), EdgeEdit);
        _edgeMenu.AddItem(ModLocalization.T("edge_delete", "Delete branch"), EdgeDelete);
        _edgeMenu.IdPressed += OnEdgeMenuId;
        UiStyle.StylePopup(_edgeMenu);
        AddChild(_edgeMenu);

        _canvasMenu = new PopupMenu { Name = "CanvasMenu" };
        _canvasMenu.AddItem(ModLocalization.T("canvas_add_text", "Add text here"), CanvasAddText);
        _canvasMenu.AddItem(ModLocalization.T("world_line_new", "+ World line"), CanvasNewWorldLine);
        _canvasMenu.AddItem(ModLocalization.T("canvas_reset", "Reset view"), CanvasResetView);
        _canvasMenu.IdPressed += OnCanvasMenuId;
        UiStyle.StylePopup(_canvasMenu);
        AddChild(_canvasMenu);

        _regionMenu = new PopupMenu { Name = "RegionMenu" };
        _regionMenu.AddItem(ModLocalization.T("region_clear", "Clear this turn"), RegionClear);
        _regionMenu.AddItem(ModLocalization.T("region_delete", "Delete this turn region"), RegionDelete);
        _regionMenu.IdPressed += OnRegionMenuId;
        UiStyle.StylePopup(_regionMenu);
        AddChild(_regionMenu);

        _editor = new NoteEditDialog { Name = "NoteEditor" };
        AddChild(_editor);
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
            case MenuToggleUpgrade:
                ToggleUpgrade(_menuNodeId);
                break;
            case MenuAddSlot:
                AddParallelSlot(_menuNodeId);
                break;
            case MenuDetachRegion:
                DetachFromRegion(_menuNodeId);
                break;
            case MenuDelete:
                NotesRuntime.Commands.Execute(new RemoveNodeCommand(NotesRuntime.ActiveDocument, _board.Id, _menuNodeId));
                NotesRuntime.Raise();
                break;
        }
    }

    private void SetNodeState(string nodeId, NodeState state)
    {
        NotesRuntime.SetNodeState(nodeId, state);
    }

    private void ToggleUpgrade(string nodeId)
    {
        if (_board == null)
        {
            return;
        }
        var before = _board.FindNode(nodeId)?.Clone();
        if (before == null || before.Kind != NodeKind.Card)
        {
            return;
        }
        var after = before.Clone();
        after.Upgraded = !after.Upgraded;
        NotesRuntime.Commands.Execute(new UpdateNodeCommand(
            NotesRuntime.ActiveDocument, _board.Id, nodeId, before, after));
        NotesRuntime.Raise();
    }

    private void AddParallelSlot(string nodeId)
    {
        if (_board == null)
        {
            return;
        }
        var before = _board.FindNode(nodeId)?.Clone();
        if (before == null)
        {
            return;
        }
        var effective = NotesLayout.EffectiveSlotCount(_board, before);
        var after = before.Clone();
        after.NextSlotCount = effective + 1;
        NotesRuntime.Commands.Execute(new UpdateNodeCommand(
            NotesRuntime.ActiveDocument, _board.Id, nodeId, before, after));
        NotesRuntime.Raise();
    }

    private void DetachFromRegion(string nodeId)
    {
        if (_board == null)
        {
            return;
        }
        var before = _board.FindNode(nodeId)?.Clone();
        if (before == null)
        {
            return;
        }
        var after = before.Clone();
        after.RegionId = "";
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
        copy.RegionId = "";
        copy.SourceOpId = "";
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
                AddTextNode(SuggestFreePosition(ViewCenterInBoardCoords()
                    - new Vector2(NodeControl.NodeWidth / 2f, NodeControl.NodeHeight / 2f)));
                break;
            case CanvasNewWorldLine:
                NotesRuntime.NewWorldLine();
                break;
            case CanvasResetView:
                ResetView();
                break;
        }
    }

    private void OnRegionMenuId(long id)
    {
        if (_board == null)
        {
            return;
        }
        var region = _board.FindRegion(_menuRegionId);
        if (region == null)
        {
            return;
        }
        var document = NotesRuntime.ActiveDocument;
        switch ((int)id)
        {
            case RegionClear:
            {
                var commands = _board.NodesOfRegion(region.Id)
                    .Select(node => (INotesCommand)new RemoveNodeCommand(document, _board.Id, node.Id))
                    .ToList();
                if (region.TurnEvents.Count > 0)
                {
                    commands.Add(new ClearRegionEventsCommand(document, _board.Id, region.Id));
                }
                if (commands.Count > 0)
                {
                    NotesRuntime.Commands.Execute(new CompositeCommand(commands, "ClearRegion"));
                }
                NotesRuntime.Raise();
                break;
            }
            case RegionDelete:
                NotesRuntime.Commands.Execute(new RemoveTurnRegionCommand(document, _board.Id, region.Id));
                NotesRuntime.Raise();
                break;
        }
    }
}
