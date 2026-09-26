using Godot;
using Notes.Core.Documents;
using Notes.Core.Services;
using Notes.Game;

namespace Notes.UI;

/// <summary>
/// The mind-map canvas: pan/zoom, drag &amp; drop (free or into turn-region
/// slots), structured branch moves, link mode and the context menus.
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

    private CanvasSurface _surface = null!;
    private PopupMenu _nodeMenu = null!;
    private PopupMenu _edgeMenu = null!;
    private PopupMenu _canvasMenu = null!;
    private PopupMenu _regionMenu = null!;
    private NoteEditDialog _editor = null!;
    private NotesBoard? _board;
    private string? _linkFrom;
    private string? _dragNode;
    private string _menuNodeId = "";
    private string _menuEdgeId = "";
    private string _menuRegionId = "";
    private bool _panning;
    private bool _linkMode;
    private bool _dragActive;
    private Color _backdrop = UiStyle.CanvasBg;
    private StyleBoxFlat _trashStyle = new();

    private Rect2 TrashRect => new(16, MathF.Max(16, Size.Y - 80), 156, 64);

    public bool IsLinking => _linkFrom != null;

    public bool LinkMode => _linkMode;

    public string? LinkFromId => _linkFrom;

    public string? ActiveLinkSource => _linkFrom;

    public bool SlotHint => _dragActive || _dragNode != null;

    public void SetBackdrop(Color color)
    {
        _backdrop = color;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), _backdrop, true);
        DrawTrash();
    }

    private void DrawTrash()
    {
        var rect = TrashRect;
        var hovering = SlotHint && rect.HasPoint(GetLocalMousePosition());
        _trashStyle.BgColor = hovering ? Color.FromHtml("5a2626") : Color.FromHtml("241d1f");
        _trashStyle.BorderColor = hovering ? Color.FromHtml("e06c5f") : Color.FromHtml("4a3a3d");
        _trashStyle.SetBorderWidthAll(hovering ? 2 : 1);
        _trashStyle.SetCornerRadiusAll(10);
        DrawStyleBox(_trashStyle, rect);

        var font = ThemeDB.FallbackFont;
        var label = ModLocalization.T("trash_label", "删除");
        var size = font.GetStringSize(label, HorizontalAlignment.Left, -1, 15);
        DrawString(font,
            rect.Position + new Vector2((rect.Size.X - size.X) / 2f, 27),
            label, HorizontalAlignment.Left, -1, 15,
            hovering ? Color.FromHtml("e06c5f") : UiStyle.TextDim);

        var hint = ModLocalization.T("trash_hint", "拖到此处删除");
        var hintSize = font.GetStringSize(hint, HorizontalAlignment.Left, -1, 10);
        DrawString(font,
            rect.Position + new Vector2((rect.Size.X - hintSize.X) / 2f, 47),
            hint, HorizontalAlignment.Left, -1, 10, UiStyle.TextDim);
    }

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
        _nodeMenu.AddItem(ModLocalization.T("node_toggle_upgrade", "Toggle upgraded +"), MenuToggleUpgrade);
        _nodeMenu.AddItem(ModLocalization.T("node_add_slot", "Add parallel slot"), MenuAddSlot);
        _nodeMenu.AddItem(ModLocalization.T("node_detach_region", "Detach from turn region"), MenuDetachRegion);
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
        _canvasMenu.AddItem(ModLocalization.T("world_line_new", "+ World line"), CanvasNewWorldLine);
        _canvasMenu.AddItem(ModLocalization.T("canvas_reset", "Reset view"), CanvasResetView);
        _canvasMenu.IdPressed += OnCanvasMenuId;
        AddChild(_canvasMenu);

        _regionMenu = new PopupMenu { Name = "RegionMenu" };
        _regionMenu.AddItem(ModLocalization.T("region_clear", "Clear this turn"), RegionClear);
        _regionMenu.AddItem(ModLocalization.T("region_delete", "Delete this turn region"), RegionDelete);
        _regionMenu.IdPressed += OnRegionMenuId;
        AddChild(_regionMenu);

        _editor = new NoteEditDialog { Name = "NoteEditor" };
        AddChild(_editor);

        NotesRuntime.Changed += OnRuntimeChanged;
        NotesRuntime.SelectionChanged += OnSelectionChanged;
        OnRuntimeChanged();
    }

    public override void _ExitTree()
    {
        NotesRuntime.Changed -= OnRuntimeChanged;
        NotesRuntime.SelectionChanged -= OnSelectionChanged;
    }

    private void OnSelectionChanged()
    {
        _surface.QueueRedraw();
        _surface.RefreshNodeDraw();
    }

    public override void _Notification(int what)
    {
        if (what == (int)NotificationDragEnd && _dragActive)
        {
            _dragActive = false;
            _surface.QueueRedraw();
            QueueRedraw();
        }
    }

    public override void _Process(double delta)
    {
        if (SlotHint)
        {
            QueueRedraw(); // keep the trash highlight in sync while dragging
        }
    }

    private void OnRuntimeChanged()
    {
        _board = NotesRuntime.ActiveBoard;
        if (_board != null)
        {
            NotesLayout.Apply(_board);
        }
        if (_linkFrom != null && _board?.FindNode(_linkFrom) == null)
        {
            _linkFrom = null;
        }
        if (_dragNode != null && _board?.FindNode(_dragNode) == null)
        {
            _dragNode = null;
        }
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
                var surfacePosition = _surface.GetLocalMousePosition();
                var region = _board.TurnRegions.FirstOrDefault(r =>
                    new Rect2(r.X, r.Y, r.Width, r.Height).HasPoint(surfacePosition));
                if (region != null)
                {
                    _menuRegionId = region.Id;
                    _regionMenu.Popup(new Rect2I((Vector2I)GetGlobalMousePosition(), Vector2I.Zero));
                }
                else
                {
                    _canvasMenu.Popup(new Rect2I((Vector2I)GetGlobalMousePosition(), Vector2I.Zero));
                }
                AcceptEvent();
            }
            else if (button.Pressed && button.ButtonIndex == MouseButton.Left)
            {
                if (IsLinking)
                {
                    CancelLink();
                    NotesRuntime.Raise();
                    AcceptEvent();
                    return;
                }
                var local = _surface.GetLocalMousePosition();
                if (_surface.TryGetEdgeNear(local, 10f, out var edgeId))
                {
                    _menuEdgeId = edgeId;
                    _edgeMenu.Popup(new Rect2I((Vector2I)GetGlobalMousePosition(), Vector2I.Zero));
                }
                else if (TryGetRegionAt(local, out var regionId))
                {
                    NotesRuntime.SelectRegion(regionId);
                }
                else if (TryGetWorldLineAt(local, out var worldLineId))
                {
                    NotesRuntime.SelectWorldLine(worldLineId);
                }
                else
                {
                    NotesRuntime.ClearSelection();
                }
                AcceptEvent();
            }
        }
        else if (@event is InputEventMouseMotion motion && _panning)
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

    // ---- drag & drop ----------------------------------------------------------

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        if (data.VariantType != Variant.Type.Dictionary)
        {
            return false;
        }
        var dict = data.AsGodotDictionary();
        if (!(dict.ContainsKey("kind") && dict["kind"].AsString() == "card"))
        {
            return false;
        }
        _dragActive = true;
        _surface.QueueRedraw();
        return true;
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        _dragActive = false;
        QueueRedraw();
        if (_board == null)
        {
            return;
        }
        if (TrashRect.HasPoint(atPosition))
        {
            return; // dropping a palette legend on the trash just cancels
        }
        var dict = data.AsGodotDictionary();
        var snapshot = new CardSnapshot(
            dict["refId"].AsString(),
            dict["title"].AsString(),
            dict["cost"].AsInt32(),
            dict["type"].AsInt32(),
            dict["rarity"].AsInt32(),
            dict["upgraded"].AsBool());
        var speculated = dict.ContainsKey("speculated") && dict["speculated"].AsBool();

        var hit = FindSlot(_surface.GetLocalMousePosition(), null);
        if (hit is { } target)
        {
            var (region, slot) = target;
            var node = NotesRuntime.CreateCardNode(snapshot, slot.X, slot.Y);
            node.RegionId = region.Id;
            node.State = speculated ? NodeState.Speculated : NodeState.None;
            var document = NotesRuntime.ActiveDocument;
            var commands = new List<INotesCommand>
            {
                new AddNodeCommand(document, _board.Id, node),
            };
            if (slot.ParentId.Length > 0)
            {
                commands.Add(new AddEdgeCommand(document, _board.Id, new NotesEdge
                {
                    Id = IdFactory.NewEdgeId(),
                    From = slot.ParentId,
                    To = node.Id,
                }));
            }
            NotesRuntime.Commands.Execute(new CompositeCommand(commands, "PlaceInSlot"));
            NotesRuntime.Raise();
            return;
        }

        var surfaceLocal = (atPosition - _surface.Position) / _surface.Scale;

        // Dropped inside a turn region but not on a slot: still belongs to that turn.
        if (TryGetRegionAt(surfaceLocal, out var regionId))
        {
            var node = NotesRuntime.CreateCardNode(snapshot, surfaceLocal.X, surfaceLocal.Y);
            node.RegionId = regionId;
            node.State = speculated ? NodeState.Speculated : NodeState.None;
            NotesRuntime.Commands.Execute(new AddNodeCommand(NotesRuntime.ActiveDocument, _board.Id, node));
            NotesRuntime.Raise();
            return;
        }

        AddCardNode(snapshot,
            surfaceLocal - new Vector2(NodeControl.NodeWidth / 2f, NodeControl.NodeHeight / 2f),
            speculated);
    }

    private bool TryGetRegionAt(Vector2 surfacePosition, out string regionId)
    {
        regionId = "";
        if (_board == null)
        {
            return false;
        }
        var region = _board.TurnRegions.FirstOrDefault(r =>
            new Rect2(r.X, r.Y, r.Width, r.Height).HasPoint(surfacePosition));
        if (region == null)
        {
            return false;
        }
        regionId = region.Id;
        return true;
    }

    private bool TryGetWorldLineAt(Vector2 surfacePosition, out string worldLineId)
    {
        worldLineId = "";
        if (_board == null)
        {
            return false;
        }
        foreach (var line in _board.WorldLines)
        {
            var regions = _board.RegionsOf(line.Id).ToList();
            if (regions.Count == 0)
            {
                continue;
            }
            var first = regions[0];
            var header = new Rect2(first.X - 8, first.Y - 48, 300, 40);
            if (header.HasPoint(surfacePosition))
            {
                worldLineId = line.Id;
                return true;
            }
        }
        return false;
    }

    public void AddCardNode(CardSnapshot snapshot, Vector2 position, bool speculated = false)
    {
        if (_board == null)
        {
            return;
        }
        var node = NotesRuntime.CreateCardNode(snapshot, position.X, position.Y);
        node.State = speculated ? NodeState.Speculated : NodeState.None;
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

    private (NotesTurnRegion Region, NotesSlot Slot)? FindSlot(Vector2 surfacePosition, string? excludeNodeId)
    {
        if (_board == null)
        {
            return null;
        }
        (NotesTurnRegion, NotesSlot)? best = null;
        var bestDistance = float.MaxValue;
        foreach (var region in _board.TurnRegions)
        {
            foreach (var slot in NotesLayout.FreeSlots(_board, region))
            {
                if (excludeNodeId != null && IsInSubtree(excludeNodeId, slot.ParentId))
                {
                    continue;
                }
                var rect = new Rect2(
                    slot.X - 16,
                    slot.Y - 16,
                    NodeControl.NodeWidth + 32,
                    NodeControl.NodeHeight + 32);
                if (!rect.HasPoint(surfacePosition))
                {
                    continue;
                }
                var center = new Vector2(
                    slot.X + NodeControl.NodeWidth / 2f,
                    slot.Y + NodeControl.NodeHeight / 2f);
                var distance = center.DistanceTo(surfacePosition);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = (region, slot);
                }
            }
        }
        return best;
    }

    /// <summary>True when <paramref name="candidateId"/> is the anchor or inside
    /// its subtree (prevents slot drops that would create a cycle).</summary>
    private bool IsInSubtree(string anchorId, string candidateId)
    {
        if (_board == null)
        {
            return false;
        }
        if (candidateId.Length == 0 || candidateId == anchorId)
        {
            return true;
        }
        var queue = new Queue<string>();
        queue.Enqueue(anchorId);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!seen.Add(current))
            {
                continue;
            }
            foreach (var edge in _board.Edges.Where(e => e.From == current))
            {
                if (edge.To == candidateId)
                {
                    return true;
                }
                queue.Enqueue(edge.To);
            }
        }
        return false;
    }

    // ---- node drag (free move / slot placement / re-slot / detach) ------------

    public void OnNodeMoved() => _surface.QueueRedraw();

    /// <summary>Any node drag shows the free slot markers.</summary>
    public void BeginNodeDrag(string nodeId)
    {
        _dragNode = nodeId;
        _surface.QueueRedraw();
    }

    public void EndNodeDrag(string nodeId, Vector2 startPosition, Vector2 currentPosition, bool moved)
    {
        _dragNode = null;
        _surface.QueueRedraw();
        QueueRedraw();
        if (_board == null)
        {
            return;
        }
        var node = _board.FindNode(nodeId);
        if (node == null)
        {
            return;
        }
        if (!moved)
        {
            NotesRuntime.SelectNode(nodeId);
            return;
        }
        if (TrashRect.HasPoint(GetLocalMousePosition()))
        {
            NotesRuntime.Commands.Execute(new RemoveNodeCommand(NotesRuntime.ActiveDocument, _board.Id, nodeId));
            NotesRuntime.Raise();
            return;
        }
        var structured = node.RegionId.Length > 0;

        var document = NotesRuntime.ActiveDocument;
        var commands = new List<INotesCommand>();
        var incoming = _board.Edges.FirstOrDefault(e => e.To == nodeId);
        var hit = FindSlot(_surface.GetLocalMousePosition(), nodeId);

        if (hit is { } target)
        {
            var (region, slot) = target;
            if (incoming != null)
            {
                commands.Add(new RemoveEdgeCommand(document, _board.Id, incoming.Id));
            }
            if (node.RegionId != region.Id)
            {
                var before = node.Clone();
                var after = node.Clone();
                after.RegionId = region.Id;
                commands.Add(new UpdateNodeCommand(document, _board.Id, nodeId, before, after));
            }
            if (slot.ParentId.Length > 0)
            {
                commands.Add(new AddEdgeCommand(document, _board.Id, new NotesEdge
                {
                    Id = IdFactory.NewEdgeId(),
                    From = slot.ParentId,
                    To = nodeId,
                }));
            }
        }
        else if (structured)
        {
            // Dropped on empty canvas: detach from the chain, keep as free node
            // at the position where it was dropped.
            if (incoming != null)
            {
                commands.Add(new RemoveEdgeCommand(document, _board.Id, incoming.Id));
            }
            var before = node.Clone();
            var after = node.Clone();
            after.RegionId = "";
            after.X = currentPosition.X;
            after.Y = currentPosition.Y;
            commands.Add(new UpdateNodeCommand(document, _board.Id, nodeId, before, after));
        }
        else
        {
            NotesRuntime.Commands.PushApplied(new MoveNodeCommand(
                document, _board.Id, nodeId, startPosition.X, startPosition.Y, currentPosition.X, currentPosition.Y));
        }

        if (commands.Count > 0)
        {
            NotesRuntime.Commands.Execute(new CompositeCommand(commands, "MoveNode"));
        }
        NotesRuntime.Raise();
    }

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

    // ---- linking --------------------------------------------------------------

    public void BeginLink(string nodeId)
    {
        _linkFrom = nodeId;
        _surface.QueueRedraw();
    }

    /// <summary>Enables click-to-link mode (click source, then click target).</summary>
    public void SetLinkMode(bool enabled)
    {
        _linkMode = enabled;
        if (!enabled)
        {
            _linkFrom = null;
        }
        _surface.QueueRedraw();
    }

    public void LinkClick(string nodeId)
    {
        if (_board == null)
        {
            return;
        }
        if (_linkFrom == null)
        {
            BeginLink(nodeId);
            NotesRuntime.Raise();
            return;
        }
        if (_linkFrom == nodeId)
        {
            CancelLink();
            NotesRuntime.Raise();
            return;
        }
        var edge = new NotesEdge
        {
            Id = IdFactory.NewEdgeId(),
            From = _linkFrom,
            To = nodeId,
        };
        NotesRuntime.Commands.Execute(new AddEdgeCommand(NotesRuntime.ActiveDocument, _board.Id, edge));
        NotesRuntime.Raise();
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

    // ---- context menus --------------------------------------------------------

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
        after.State = state;
        NotesRuntime.Commands.Execute(new UpdateNodeCommand(
            NotesRuntime.ActiveDocument, _board.Id, nodeId, before, after));
        NotesRuntime.Raise();
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
        var effective = before.NextSlotCount > 0
            ? before.NextSlotCount
            : (NotesLayout.IsRoot(_board, before) ? NotesLayout.RootParallelSlots : 1);
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
                AddTextNode(ViewCenterInBoardCoords() - new Vector2(NodeControl.NodeWidth / 2f, NodeControl.NodeHeight / 2f));
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
