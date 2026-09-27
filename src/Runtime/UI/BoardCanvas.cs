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
    private StyleBoxFlat _zoomStyle = new();

    private Rect2 TrashRect => new(16, MathF.Max(16, Size.Y - 80), 156, 64);

    public bool IsLinking => _linkFrom != null;

    public bool LinkMode => _linkMode;

    public string? ActiveLinkSource => _linkFrom;

    public bool SlotHint => _dragActive || _dragNode != null;

    /// <summary>Branch under the mouse (hover highlight); empty when none.</summary>
    public string HoverEdgeId { get; private set; } = "";

    public void SetBackdrop(Color color)
    {
        _backdrop = color;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), _backdrop, true);
        // The trash only appears while something is being dragged, so the
        // canvas stays clean the rest of the time.
        if (_board?.IsReadOnly != true && SlotHint)
        {
            DrawTrash();
        }
        DrawZoomBadge();
    }

    /// <summary>Bottom-right zoom percentage so the current scale is never a mystery.</summary>
    private void DrawZoomBadge()
    {
        var font = ThemeDB.FallbackFont;
        var text = Mathf.RoundToInt((_board?.Zoom ?? 1f) * 100f) + "%";
        var size = font.GetStringSize(text, HorizontalAlignment.Left, -1, 11);
        var rect = new Rect2(Size.X - size.X - 26f, Size.Y - 30f, size.X + 14f, 20f);
        _zoomStyle.BgColor = Color.FromHtml("171a20cc");
        _zoomStyle.BorderColor = UiStyle.PanelBorder;
        _zoomStyle.SetBorderWidthAll(1);
        _zoomStyle.SetCornerRadiusAll(6);
        DrawStyleBox(_zoomStyle, rect);
        DrawString(font, rect.Position + new Vector2(7, 14), text,
            HorizontalAlignment.Left, -1, 11, UiStyle.TextDim);
    }

    private void DrawTrash()
    {
        var rect = TrashRect;
        var hovering = rect.HasPoint(GetLocalMousePosition());
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

        BuildMenus();

        NotesRuntime.Changed += () => OnRuntimeChanged(applyLayout: false);
        NotesRuntime.SelectionChanged += OnSelectionChanged;
        MouseExited += () =>
        {
            if (HoverEdgeId.Length > 0)
            {
                HoverEdgeId = "";
                _surface.QueueRedraw();
            }
        };
        OnRuntimeChanged(applyLayout: true);
    }

    public override void _ExitTree()
    {
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

    private void OnRuntimeChanged(bool applyLayout)
    {
        var board = NotesRuntime.ActiveBoard;
        var sameBoard = ReferenceEquals(_board, board);
        _board = board;
        if (_board != null && applyLayout)
        {
            // NotesRuntime.Raise already applied the layout for model changes;
            // only the initial attach needs it here.
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
        if (HoverEdgeId.Length > 0 && _board?.FindEdge(HoverEdgeId) == null)
        {
            HoverEdgeId = "";
        }
        if (sameBoard)
        {
            _surface.Refresh(); // reuse node controls (stable hover / selection)
        }
        else
        {
            _surface.SetBoard(_board);
        }
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
            else if (!button.Pressed && button.ButtonIndex == MouseButton.Left && IsLinking)
            {
                // A link drag released over empty canvas must not leave the
                // dashed preview hanging: complete (= cancel if no target).
                CompleteLink();
                AcceptEvent();
            }
            else if (button.Pressed && button.ButtonIndex == MouseButton.Right)
            {
                if (_board.IsReadOnly)
                {
                    AcceptEvent();
                    return;
                }
                var surfacePosition = _surface.GetLocalMousePosition();
                if (_surface.TryGetEdgeNear(surfacePosition, 10f, out var edgeId))
                {
                    _menuEdgeId = edgeId;
                    _edgeMenu.Popup(new Rect2I((Vector2I)GetGlobalMousePosition(), Vector2I.Zero));
                }
                else
                {
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
                if (TryGetOverviewCardAt(local, out var overviewBoardId))
                {
                    NotesRuntime.SetActiveBoard(overviewBoardId);
                    AcceptEvent();
                    return;
                }
                if (TryGetRegionAt(local, out var regionId))
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
        else if (@event is InputEventMouseMotion)
        {
            var hovered = _surface.TryGetEdgeNear(_surface.GetLocalMousePosition(), 8f, out var edgeId)
                ? edgeId
                : "";
            if (hovered != HoverEdgeId)
            {
                HoverEdgeId = hovered;
                _surface.QueueRedraw();
            }
        }
    }

    private void ZoomAt(Vector2 mouse, float factor)
    {
        if (_board == null)
        {
            return;
        }
        var zoom = Math.Clamp(_board.Zoom * factor, 0.5f, 3f);
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

    public override bool _CanDropData(Vector2 atPosition, Variant data) => CanAcceptDrop(data);

    public override void _DropData(Vector2 atPosition, Variant data) => HandleDrop(data);

    /// <summary>Shared drop acceptance (also forwarded by child node controls so
    /// the slot hints stay visible while hovering a legend).</summary>
    public bool CanAcceptDrop(Variant data)
    {
        if (_board?.IsReadOnly == true)
        {
            return false;
        }
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

    /// <summary>Shared drop handling; uses the current mouse position so drops
    /// over child node controls land exactly like drops on empty canvas.</summary>
    public void HandleDrop(Variant data)
    {
        _dragActive = false;
        QueueRedraw();
        if (_board == null || _board.IsReadOnly)
        {
            return;
        }
        var boardLocal = GetLocalMousePosition();
        if (TrashRect.HasPoint(boardLocal))
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

        var surfaceLocal = (boardLocal - _surface.Position) / _surface.Scale;

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

    /// <summary>Overview board: hit-test the world-line summary cards.</summary>
    private bool TryGetOverviewCardAt(Vector2 surfacePosition, out string boardId)
    {
        boardId = "";
        if (_board == null || _board.Kind != BoardKind.Overview)
        {
            return false;
        }
        foreach (var card in NotesLayout.OverviewCards(NotesRuntime.ActiveDocument))
        {
            if (new Rect2(card.X, card.Y, card.Width, card.Height).HasPoint(surfacePosition))
            {
                boardId = card.BoardId;
                return true;
            }
        }
        return false;
    }

    public void AddCardNode(CardSnapshot snapshot, Vector2 position, bool speculated = false)
    {
        if (_board == null || _board.IsReadOnly)
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
        if (_board == null || _board.IsReadOnly)
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
    private bool IsInSubtree(string anchorId, string candidateId) =>
        _board != null && NotesGraph.IsInSubtree(_board, anchorId, candidateId);

    // ---- node drag (free move / slot placement / re-slot / detach) ------------

    public void OnNodeMoved() => _surface.QueueRedraw();

    /// <summary>Any node drag shows the free slot markers.</summary>
    public void BeginNodeDrag(string nodeId)
    {
        _dragNode = nodeId;
        _surface.QueueRedraw();
    }

    /// <summary>Drops the drag hint without moving / re-slotting the node
    /// (used when a release was consumed by the link flow).</summary>
    public void CancelNodeDrag()
    {
        _dragNode = null;
        _surface.QueueRedraw();
        QueueRedraw();
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
            // Dropped on empty canvas. If the drop is still inside a turn
            // region, the node stays part of the structured board (otherwise
            // it would silently become a free node and lose its next-step
            // slots); only drops outside every region detach it.
            if (TryGetRegionAt(_surface.GetLocalMousePosition(), out var stayRegionId))
            {
                if (node.RegionId != stayRegionId)
                {
                    if (incoming != null)
                    {
                        commands.Add(new RemoveEdgeCommand(document, _board.Id, incoming.Id));
                    }
                    var before = node.Clone();
                    var after = node.Clone();
                    after.RegionId = stayRegionId;
                    commands.Add(new UpdateNodeCommand(document, _board.Id, nodeId, before, after));
                }
            }
            else
            {
                // Detach from the chain, keep as free node at the drop position.
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

    // ---- linking --------------------------------------------------------------

    public void BeginLink(string nodeId)
    {
        _linkFrom = nodeId;
        _surface.QueueRedraw();
    }

    /// <summary>Enables click-to-link mode (click source, then click target).</summary>
    public void SetLinkMode(bool enabled)
    {
        _linkMode = enabled && _board?.IsReadOnly != true;
        if (!_linkMode)
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
        var from = _linkFrom;
        _linkFrom = null;
        var edge = new NotesEdge
        {
            Id = IdFactory.NewEdgeId(),
            From = from,
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
}
