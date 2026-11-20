using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace HeatInteractive.SkillTreeGenerator.Editing
{
    /// The pan/zoom canvas. Left drag: move skills (on a skill), box select (on empty space), connect (from the "+"
    /// handle). Middle drag or Alt + left drag: pan. Wheel: zoom. Right click: context menu. In simulation mode left
    /// click buys a rank and right click refunds one.
    internal class SkillTreeCanvas : VisualElement
    {
        private enum Drag { None, Pending, Move, Pan, Band, Connect }

        public const float MinZoom = 0.1f, MaxZoom = 2.5f;
        private const float FarZoom = 0.35f, DragThreshold = 4f, EdgePickDistance = 6f;

        private static readonly Color MinorGrid = new(1f, 1f, 1f, 0.035f), MajorGrid = new(1f, 1f, 1f, 0.07f);
        private static readonly Color RingColor = new(1f, 1f, 1f, 0.07f);
        private static readonly Color LinkColor = new(0.85f, 0.88f, 0.95f);
        private static readonly Color RequireColor = new(1f, 0.62f, 0.2f);
        private static readonly Color PickColor = new(0.3f, 0.85f, 1f);
        private static readonly Color BandFill = new(0.3f, 0.65f, 1f, 0.1f), BandLine = new(0.3f, 0.65f, 1f, 0.8f);

        public SkillTree Tree { get; private set; }
        public float Zoom => _zoom;
        public Vector2 Pan => _pan;
        public IReadOnlyList<SkillNode> Selection => _selection;
        public (SkillNode a, SkillNode b)? SelectedEdge { get; private set; }
        public SkillRanks Simulation { get; private set; }

        /// Selection (nodes or the edge) changed.
        public event Action SelectionChanged;
        /// The tree's structure or positions changed (an undoable edit is done).
        public event Action Edited;
        public event Action ViewChanged;
        public event Action SimulationChanged;
        public event Action<SkillNode> NodeDoubleClicked;
        public event Action<string> Notify;
        public event Action<string> HintChanged;

        private readonly VisualElement _background, _content, _overlay;
        private readonly Dictionary<SkillNode, SkillNodeElement> _elements = new();
        private readonly List<SkillNode> _selection = new();
        private readonly HashSet<SkillNode> _picked = new(), _dimmed = new(); // _selection / filter misses, rebuilt by ApplyStates
        private Object[] _moveObjects = Array.Empty<Object>();
        private float _zoom = 1f;
        private Vector2 _pan;

        private Drag _drag;
        private int _pointerId = -1;
        private Vector2 _downScreen, _lastScreen;
        private SkillNode _dragNode;
        private bool _pendingSelectOnly;
        private readonly Dictionary<SkillNode, Vector2> _moveStart = new();
        private Vector2 _moveMouseStart;
        private int _undoGroup;
        private readonly List<SkillNode> _bandStart = new();
        private Rect _band;
        private SkillNodeElement _connectTarget;
        private bool _connectRequire;

        private string _search = "";
        private SkillGroup _groupFilter;
        private readonly Dictionary<SkillNode, (string errors, string warnings)> _issues = new();

        public SkillTreeCanvas()
        {
            AddToClassList("canvas");
            focusable = true;
            tabIndex = 0;

            _background = new VisualElement { pickingMode = PickingMode.Ignore };
            _background.AddToClassList("canvas__background");
            _background.generateVisualContent += DrawBackground;
            Add(_background);

            _content = new VisualElement { pickingMode = PickingMode.Ignore };
            _content.AddToClassList("canvas__content");
            _content.style.transformOrigin = new TransformOrigin(0, 0, 0);
            Add(_content);

            _overlay = new VisualElement { pickingMode = PickingMode.Ignore };
            _overlay.AddToClassList("canvas__overlay");
            _overlay.generateVisualContent += DrawOverlay;
            Add(_overlay);

            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerCaptureOutEvent>(_ => CancelDrag());
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            RegisterCallback<GeometryChangedEvent>(_ => _background.MarkDirtyRepaint());
        }

        // ---------------------------------------------------------------- tree and view

        public void SetTree(SkillTree tree)
        {
            CancelDrag();
            Tree = tree;
            Simulation = null;
            _selection.Clear();
            SelectedEdge = null;
            _issues.Clear();
            Rebuild();
            SelectionChanged?.Invoke();
        }

        /// Makes the node elements again from the tree (after an undo, a tree switch...).
        public void Rebuild()
        {
            _content.Clear();
            _elements.Clear();
            if (Tree != null)
                foreach (var node in Tree.Nodes)
                    if (node != null && !_elements.ContainsKey(node)) AddElement(node);
            int before = _selection.Count;
            _selection.RemoveAll(n => n == null || !_elements.ContainsKey(n));
            bool edgeGone = SelectedEdge is { } edge && (edge.a == null || edge.b == null || !Connected(edge.a, edge.b));
            if (edgeGone) SelectedEdge = null;
            ApplyStates();
            if (before != _selection.Count || edgeGone) SelectionChanged?.Invoke();
        }

        private SkillNodeElement AddElement(SkillNode node)
        {
            var element = new SkillNodeElement(node);
            _elements[node] = element;
            _content.Add(element);
            return element;
        }

        public void RefreshNode(SkillNode node)
        {
            if (node != null && _elements.TryGetValue(node, out var element)) element.Refresh();
            ApplyStates();
        }

        public void RefreshAll()
        {
            foreach (var element in _elements.Values) element.Refresh();
            ApplyStates();
        }

        public void SetView(Vector2 pan, float zoom)
        {
            _pan = pan;
            _zoom = Mathf.Clamp(zoom, MinZoom, MaxZoom);
            _content.style.translate = new Translate(_pan.x, _pan.y);
            _content.style.scale = new Scale(new Vector3(_zoom, _zoom, 1f));
            EnableInClassList("canvas--far", _zoom < FarZoom);
            _background.MarkDirtyRepaint();
            _overlay.MarkDirtyRepaint();
            ViewChanged?.Invoke();
        }

        public Vector2 WorldToScreen(Vector2 world) => new Vector2(world.x, -world.y) * _zoom + _pan;

        public Vector2 ScreenToWorld(Vector2 screen)
        {
            var local = (screen - _pan) / _zoom;
            return new Vector2(local.x, -local.y);
        }

        public Vector2 ViewCenter => ScreenToWorld(layout.size * 0.5f);

        public Vector2 Snap(Vector2 world)
        {
            if (Tree == null || !Tree.Layout.Snap) return world;
            float g = Mathf.Max(1f, Tree.Layout.GridSize);
            return new Vector2(Mathf.Round(world.x / g) * g, Mathf.Round(world.y / g) * g);
        }

        public void FrameAll() => Frame(_elements.Keys);

        public void FrameSelection()
        {
            if (_selection.Count > 0) Frame(_selection);
            else if (SelectedEdge is { } edge) Frame(new[] { edge.a, edge.b });
            else FrameAll();
        }

        public void Frame(IEnumerable<SkillNode> nodes)
        {
            var list = nodes.Where(n => n != null).ToList();
            var view = layout.size;
            if (list.Count == 0 || view.x < 10f || view.y < 10f) return;
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (var node in list)
            {
                var p = new Vector2(node.Position.x, -node.Position.y);
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            var size = max - min + new Vector2(260f, 220f);
            float zoom = Mathf.Clamp(Mathf.Min(view.x / size.x, view.y / size.y), MinZoom, 1.2f);
            SetView(view * 0.5f - (min + max) * 0.5f * zoom, zoom);
        }

        // ---------------------------------------------------------------- selection

        public void SelectOnly(SkillNode node)
        {
            _selection.Clear();
            if (node != null) _selection.Add(node);
            SelectedEdge = null;
            SelectionDone();
        }

        public void Select(IEnumerable<SkillNode> nodes)
        {
            _selection.Clear();
            _selection.AddRange(nodes.Where(n => n != null && _elements.ContainsKey(n)).Distinct());
            SelectedEdge = null;
            SelectionDone();
        }

        public void ClearSelection() => Select(Array.Empty<SkillNode>());

        public void SelectAll() => Select(_elements.Keys);

        private void Toggle(SkillNode node)
        {
            if (!_selection.Remove(node)) _selection.Add(node);
            SelectedEdge = null;
            SelectionDone();
        }

        public void SelectEdge(SkillNode a, SkillNode b)
        {
            _selection.Clear();
            SelectedEdge = (a, b);
            SelectionDone();
        }

        private void SelectionDone()
        {
            ApplyStates();
            SelectionChanged?.Invoke();
        }

        // ---------------------------------------------------------------- filter, issues, simulation

        public void SetFilter(string search, SkillGroup group)
        {
            _search = search?.Trim() ?? "";
            _groupFilter = group;
            ApplyStates();
        }

        private bool FilterActive => _search.Length > 0 || _groupFilter != null;

        public bool Matches(SkillNode node)
        {
            if (_groupFilter != null && node.Group != _groupFilter) return false;
            if (_search.Length == 0) return true;
            return Contains(node.DisplayName) || Contains(node.GetType().Name) || Contains(node.Description) || Contains(node.Summary); // Summary builds a string: last
            bool Contains(string s) => s != null && s.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public void FrameMatches()
        {
            var matches = _elements.Keys.Where(Matches).ToList();
            if (matches.Count == 0) return;
            Select(matches);
            Frame(matches);
        }

        public void SetIssues(IEnumerable<SkillIssue> issues)
        {
            _issues.Clear();
            foreach (var issue in issues)
            {
                if (issue.Node == null) continue;
                _issues.TryGetValue(issue.Node, out var entry);
                if (issue.IsError) entry.errors = entry.errors == null ? issue.Message : entry.errors + "\n" + issue.Message;
                else entry.warnings = entry.warnings == null ? issue.Message : entry.warnings + "\n" + issue.Message;
                _issues[issue.Node] = entry;
            }
            ApplyStates();
        }

        public void SetSimulation(bool on)
        {
            CancelDrag();
            Simulation = on ? new SkillRanks() : null;
            EnableInClassList("canvas--simulating", on);
            ApplyStates();
            SimulationChanged?.Invoke();
        }

        public void ResetSimulation()
        {
            Simulation?.Clear();
            ApplyStates();
            SimulationChanged?.Invoke();
        }

        public SimState StateOf(SkillNode node)
        {
            if (Simulation == null || Tree == null || node == null) return SimState.Off;
            int rank = Simulation.GetRank(node);
            if (rank >= node.MaxRank) return SimState.Maxed;
            if (Tree.CanRankUp(node, Simulation)) return rank > 0 ? SimState.Owned : SimState.Available;
            return rank > 0 ? SimState.Owned : SimState.Locked;
        }

        public void SimulateRank(SkillNode node, int delta)
        {
            if (Simulation == null || node == null) return;
            int rank = Simulation.GetRank(node);
            if (delta > 0)
            {
                if (rank >= node.MaxRank) { Notify?.Invoke("Already at max rank."); return; }
                if (!Tree.CanRankUp(node, Simulation)) { Notify?.Invoke(Tree.WhyLocked(node, Simulation) ?? "Locked."); return; }
            }
            Simulation.SetRank(node, Mathf.Clamp(rank + delta, 0, node.MaxRank));
            ApplyStates();
            SimulationChanged?.Invoke();
        }

        private void ApplyStates()
        {
            // Sets, so a repaint's per-edge checks are O(1): Matches builds strings, Selection.Contains is a scan.
            _picked.Clear();
            _picked.UnionWith(_selection);
            _dimmed.Clear();
            bool filter = FilterActive;
            foreach (var (node, element) in _elements)
            {
                bool dim = filter && !Matches(node);
                if (dim) _dimmed.Add(node);
                element.SetSelected(_picked.Contains(node));
                element.SetDimmed(dim);
                _issues.TryGetValue(node, out var issue);
                element.SetIssues(issue.errors, issue.warnings);
                element.SetSimulation(StateOf(node), Simulation?.GetRank(node) ?? 0);
            }
            _background.MarkDirtyRepaint();
        }

        // ---------------------------------------------------------------- pointer

        private static SkillNodeElement ElementOf(VisualElement target)
        {
            for (var e = target; e != null; e = e.parent)
                if (e is SkillNodeElement node) return node;
            return null;
        }

        private static bool IsPart(VisualElement target, SkillNodeElement element, VisualElement part)
        {
            for (var e = target; e != null && e != element; e = e.parent)
                if (e == part) return true;
            return false;
        }

        private SkillNodeElement Pick(Vector2 panelPosition) => panel != null ? ElementOf(panel.Pick(panelPosition)) : null;

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (Tree == null || _drag != Drag.None) return;
            Focus();
            var p = this.WorldToLocal(evt.position);
            _downScreen = _lastScreen = p;
            _pointerId = evt.pointerId;
            var element = ElementOf(evt.target as VisualElement);

            if (evt.button == 2 || (evt.button == 0 && evt.altKey))
            {
                Begin(Drag.Pan, evt);
                return;
            }
            if (evt.button == 1)
            {
                evt.StopPropagation(); // the menu opens on release
                return;
            }
            if (evt.button != 0) return;

            if (Simulation != null)
            {
                if (element != null)
                {
                    SelectOnly(element.Node);
                    if (!evt.shiftKey) SimulateRank(element.Node, +1);
                }
                else Begin(Drag.Pan, evt);
                evt.StopPropagation();
                return;
            }

            if (element != null && IsPart(evt.target as VisualElement, element, element.DuplicatePort))
            {
                DuplicateNear(element.Node);
                evt.StopPropagation();
                return;
            }

            if (evt.clickCount == 2)
            {
                if (element != null) NodeDoubleClicked?.Invoke(element.Node);
                else ShowCreateMenu(Snap(ScreenToWorld(p)), null, false);
                evt.StopPropagation();
                return;
            }

            if (element != null && IsPart(evt.target as VisualElement, element, element.Port))
            {
                _dragNode = element.Node;
                _connectRequire = evt.actionKey;
                Begin(Drag.Connect, evt);
                ConnectHint();
                return;
            }

            if (element != null)
            {
                var node = element.Node;
                _pendingSelectOnly = false;
                if (evt.shiftKey || evt.actionKey) Toggle(node);
                else if (!_selection.Contains(node)) SelectOnly(node);
                else _pendingSelectOnly = _selection.Count > 1; // a click (no drag) on a picked one narrows to it
                _dragNode = _selection.Contains(node) ? node : null;
                if (_dragNode != null) Begin(Drag.Pending, evt);
                else evt.StopPropagation();
                return;
            }

            var edge = EdgeAt(p);
            if (edge != null)
            {
                SelectEdge(edge.Value.a, edge.Value.b);
                evt.StopPropagation();
                return;
            }

            _bandStart.Clear();
            if (evt.shiftKey || evt.actionKey) _bandStart.AddRange(_selection);
            else ClearSelection();
            _band = new Rect(p, Vector2.zero);
            Begin(Drag.Band, evt);
            HintChanged?.Invoke("Box select: release to pick every skill inside. Hold Shift to add to the selection.");
        }

        private void Begin(Drag drag, EventBase evt)
        {
            _drag = drag;
            this.CapturePointer(_pointerId);
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (_drag == Drag.None || evt.pointerId != _pointerId) return;
            var p = this.WorldToLocal(evt.position);
            var delta = p - _lastScreen;
            _lastScreen = p;

            switch (_drag)
            {
                case Drag.Pan:
                    SetView(_pan + delta, _zoom);
                    break;
                case Drag.Pending:
                    if ((p - _downScreen).sqrMagnitude < DragThreshold * DragThreshold) break;
                    StartMove();
                    UpdateMove(p);
                    break;
                case Drag.Move:
                    UpdateMove(p);
                    break;
                case Drag.Band:
                    _band = Rect.MinMaxRect(Mathf.Min(_downScreen.x, p.x), Mathf.Min(_downScreen.y, p.y),
                        Mathf.Max(_downScreen.x, p.x), Mathf.Max(_downScreen.y, p.y));
                    var inside = _elements.Keys.Where(n => _band.Contains(WorldToScreen(n.Position)));
                    _selection.Clear();
                    _selection.AddRange(_bandStart.Concat(inside).Distinct());
                    ApplyStates();
                    _overlay.MarkDirtyRepaint();
                    break;
                case Drag.Connect:
                    _connectRequire = evt.actionKey;
                    var target = Pick(evt.position);
                    if (target != null && target.Node == _dragNode) target = null;
                    if (target != _connectTarget)
                    {
                        _connectTarget?.SetTarget(false);
                        _connectTarget = target;
                        _connectTarget?.SetTarget(true);
                    }
                    ConnectHint();
                    _overlay.MarkDirtyRepaint();
                    break;
            }
            evt.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            var p = this.WorldToLocal(evt.position);
            if (evt.button == 1 && _drag == Drag.None)
            {
                if (Simulation != null)
                {
                    var simElement = ElementOf(evt.target as VisualElement);
                    if (simElement != null) SimulateRank(simElement.Node, -1);
                }
                else ShowContextMenu(p, ElementOf(evt.target as VisualElement));
                evt.StopPropagation();
                return;
            }
            if (_drag == Drag.None || evt.pointerId != _pointerId) return;

            var drag = _drag;
            var node = _dragNode;
            var target = _connectTarget?.Node;
            bool require = _connectRequire;
            EndDrag();

            switch (drag)
            {
                case Drag.Pending:
                    if (_pendingSelectOnly && node != null) SelectOnly(node);
                    break;
                case Drag.Move:
                    Undo.CollapseUndoOperations(_undoGroup);
                    Edited?.Invoke();
                    break;
                case Drag.Band:
                    SelectionChanged?.Invoke();
                    break;
                case Drag.Connect:
                    if (target != null) Connect(node, target, require);
                    else if ((p - _downScreen).sqrMagnitude > 400f) ShowCreateMenu(Snap(ScreenToWorld(p)), node, require);
                    break;
            }
            evt.StopPropagation();
        }

        private void StartMove()
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(_selection.Count > 1 ? "Move Skills" : "Move Skill");
            _undoGroup = Undo.GetCurrentGroup();
            _moveStart.Clear();
            foreach (var node in _selection) _moveStart[node] = node.Position;
            _moveObjects = _moveStart.Keys.Cast<Object>().ToArray(); // once per drag, not per pointer move
            _moveMouseStart = ScreenToWorld(_downScreen);
            _drag = Drag.Move;
            HintChanged?.Invoke(Tree.Layout.Snap ? "Moving: snapped to the grid (Snap in the toolbar turns it off)." : "Moving.");
        }

        private void UpdateMove(Vector2 p)
        {
            var delta = ScreenToWorld(p) - _moveMouseStart;
            if (Tree.Layout.Snap && _moveStart.TryGetValue(_dragNode, out var anchor)) delta = Snap(anchor + delta) - anchor;
            // Recorded before every change: Unity folds them into the one undo step opened in StartMove.
            Undo.RecordObjects(_moveObjects, "Move Skills");
            foreach (var (node, start) in _moveStart)
            {
                node.Position = start + delta;
                _elements[node].UpdatePosition();
            }
            _background.MarkDirtyRepaint();
        }

        private void EndDrag()
        {
            if (_pointerId >= 0 && this.HasPointerCapture(_pointerId)) this.ReleasePointer(_pointerId);
            _drag = Drag.None;
            _dragNode = null;
            _connectTarget?.SetTarget(false);
            _connectTarget = null;
            _overlay.MarkDirtyRepaint();
            HintChanged?.Invoke(null);
        }

        private void CancelDrag()
        {
            if (_drag == Drag.Move)
            {
                Undo.CollapseUndoOperations(_undoGroup);
                Edited?.Invoke();
            }
            if (_drag != Drag.None) EndDrag();
        }

        private void OnWheel(WheelEvent evt)
        {
            if (Tree == null) return;
            var p = this.WorldToLocal(evt.mousePosition);
            var before = ScreenToWorld(p);
            float zoom = Mathf.Clamp(_zoom * Mathf.Pow(1.1f, -evt.delta.y / 3f), MinZoom, MaxZoom);
            _zoom = zoom;
            var after = WorldToScreen(before);
            SetView(_pan + (p - after), zoom);
            evt.StopPropagation();
        }

        private void ConnectHint() => HintChanged?.Invoke(_connectRequire
            ? "Requirement: release on a skill, it can only be bought after this one. Release on empty space for a new skill."
            : "Link: release on a skill to connect them. Hold Ctrl (Cmd) for a requirement. Release on empty space for a new skill.");

        // ---------------------------------------------------------------- keyboard

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (Tree == null) return;
            bool handled = true;
            switch (evt.keyCode)
            {
                case KeyCode.Delete:
                case KeyCode.Backspace:
                    DeleteSelection();
                    break;
                case KeyCode.D when evt.actionKey:
                    DuplicateSelection();
                    break;
                case KeyCode.A when evt.actionKey:
                    SelectAll();
                    break;
                case KeyCode.F:
                    FrameSelection();
                    break;
                case KeyCode.Escape:
                    if (_drag != Drag.None) CancelDrag();
                    else ClearSelection();
                    break;
                default:
                    handled = false;
                    break;
            }
            if (handled) evt.StopPropagation();
        }

        // ---------------------------------------------------------------- commands

        public void DeleteSelection()
        {
            if (Simulation != null) return;
            if (_selection.Count > 0) Delete(_selection.ToList());
            else if (SelectedEdge is { } edge)
            {
                SkillTreeEditing.Disconnect(Tree, edge.a, edge.b);
                SelectedEdge = null;
                Changed();
                SelectionChanged?.Invoke();
            }
        }

        public void Delete(List<SkillNode> nodes)
        {
            if (nodes.Any(n => n.IsRoot))
            {
                Notify?.Invoke("The root can't be deleted. Right-click another skill > Set as Root first.");
                nodes = nodes.Where(n => !n.IsRoot).ToList();
            }
            if (nodes.Count == 0) return;
            SkillTreeEditing.DeleteNodes(Tree, nodes);
            Rebuild();
            Changed();
            SelectionChanged?.Invoke();
        }

        public void DuplicateSelection()
        {
            if (Simulation != null || _selection.Count == 0) return;
            var copies = SkillTreeEditing.Duplicate(Tree, _selection, new Vector2(60f, -60f));
            foreach (var copy in copies) AddElement(copy);
            Select(copies);
            Changed();
        }

        /// The duplicate handle: a copy of the node (same links, like Ctrl+D) on a free spot next to it.
        private void DuplicateNear(SkillNode node)
        {
            var spot = Snap(SkillTreeEditing.PlaceNear(Tree, node, Mathf.Max(100f, Tree.Layout.RingSpacing * 0.6f)));
            var copies = SkillTreeEditing.Duplicate(Tree, new[] { node }, spot - node.Position);
            foreach (var copy in copies) AddElement(copy);
            Select(copies);
            Changed();
        }

        /// A new node of the type at the position, connected to from (a link, or a requirement on from).
        public SkillNode CreateNode(Type type, Vector2 world, SkillNode from = null, bool require = false)
        {
            var node = SkillTreeEditing.CreateNode(Tree, type, world);
            if (from != null)
            {
                if (require) SkillTreeEditing.SetRequirement(Tree, node, from);
                else SkillTreeEditing.Link(Tree, from, node);
            }
            AddElement(node);
            SelectOnly(node);
            Changed();
            return node;
        }

        private void Connect(SkillNode from, SkillNode to, bool require)
        {
            if (require)
            {
                if (SkillTreeEditing.SetRequirement(Tree, to, from)) Notify?.Invoke($"\"{to.DisplayName}\" now requires \"{from.DisplayName}\".");
            }
            else if (!SkillTreeEditing.Link(Tree, from, to))
            {
                Notify?.Invoke("These two are already connected.");
                return;
            }
            SelectEdge(from, to);
            Changed();
        }

        private void Changed()
        {
            ApplyStates();
            Edited?.Invoke();
        }

        private void ShowCreateMenu(Vector2 world, SkillNode from, bool require)
        {
            var types = SkillTreeEditing.NodeTypes;
            if (types.Count == 0)
            {
                Notify?.Invoke("No skill types yet: create a class that derives from SkillNode.");
                return;
            }
            if (types.Count == 1)
            {
                CreateNode(types[0], world, from, require);
                return;
            }
            var menu = new GenericMenu();
            foreach (var type in types)
            {
                var t = type;
                menu.AddItem(new GUIContent(SkillTreeEditing.MenuPath(t)), false, () => CreateNode(t, world, from, require));
            }
            menu.ShowAsContext();
        }

        private void ShowContextMenu(Vector2 p, SkillNodeElement element)
        {
            if (Tree == null) return;
            var menu = new GenericMenu();
            if (element != null) NodeMenu(menu, element.Node);
            else if (EdgeAt(p) is { } edge)
            {
                SelectEdge(edge.a, edge.b);
                EdgeMenu(menu, edge.a, edge.b);
            }
            else EmptyMenu(menu, Snap(ScreenToWorld(p)));
            menu.ShowAsContext();
        }

        private void NodeMenu(GenericMenu menu, SkillNode node)
        {
            if (!_selection.Contains(node)) SelectOnly(node);
            var targets = _selection.ToList();
            bool many = targets.Count > 1;

            menu.AddItem(new GUIContent(many ? $"Duplicate {targets.Count} Skills  (Ctrl+D)" : "Duplicate  (Ctrl+D)"), false, DuplicateSelection);
            menu.AddItem(new GUIContent(many ? $"Delete {targets.Count} Skills  (Del)" : "Delete  (Del)"), false, () => Delete(targets));
            menu.AddSeparator("");
            if (!many) menu.AddItem(new GUIContent("Set as Root"), node.IsRoot, () =>
            {
                SkillTreeEditing.SetRoot(Tree, node);
                RefreshAll();
                Edited?.Invoke();
            });
            menu.AddItem(new GUIContent("Group/None"), targets.All(n => n.Group == null), () => SetGroup(targets, null));
            foreach (var group in Tree.Groups.Where(g => g != null))
            {
                var g = group;
                menu.AddItem(new GUIContent("Group/" + g.DisplayName), targets.All(n => n.Group == g), () => SetGroup(targets, g));
            }
            menu.AddSeparator("Group/");
            menu.AddItem(new GUIContent("Group/New Group"), false, () => SetGroup(targets, SkillTreeEditing.CreateGroup(Tree)));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Disconnect"), false, () =>
            {
                SkillTreeEditing.DisconnectAll(Tree, targets);
                Changed();
            });
            menu.AddItem(new GUIContent("Frame  (F)"), false, () => Frame(targets));
        }

        private void SetGroup(List<SkillNode> nodes, SkillGroup group)
        {
            SkillTreeEditing.SetGroup(Tree, nodes, group);
            RefreshAll();
            Edited?.Invoke();
        }

        private void EdgeMenu(GenericMenu menu, SkillNode a, SkillNode b)
        {
            bool linked = SkillTreeEditing.AreLinked(a, b);
            bool aNeedsB = SkillTreeEditing.Requires(a, b), bNeedsA = SkillTreeEditing.Requires(b, a);
            menu.AddItem(new GUIContent("Remove Connection  (Del)"), false, () =>
            {
                SkillTreeEditing.Disconnect(Tree, a, b);
                ClearSelection();
                Changed();
            });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent($"Requirement: \"{a.DisplayName}\" first"), bNeedsA, () => Require(b, a));
            menu.AddItem(new GUIContent($"Requirement: \"{b.DisplayName}\" first"), aNeedsB, () => Require(a, b));
            if (aNeedsB || bNeedsA)
            {
                menu.AddItem(new GUIContent("Plain Link (no requirement)"), false, () =>
                {
                    SkillTreeEditing.MakePlainLink(Tree, a, b);
                    Changed();
                });
                if (linked) menu.AddItem(new GUIContent("Requirement Only (remove the link)"), false, () =>
                {
                    SkillTreeEditing.RemoveLinkOnly(Tree, a, b);
                    Changed();
                });
            }
        }

        private void Require(SkillNode node, SkillNode need)
        {
            SkillTreeEditing.SetRequirement(Tree, node, need);
            Changed();
        }

        private void EmptyMenu(GenericMenu menu, Vector2 world)
        {
            var types = SkillTreeEditing.NodeTypes;
            if (types.Count == 0) menu.AddDisabledItem(new GUIContent("No skill types (derive a class from SkillNode)"));
            else if (types.Count == 1) menu.AddItem(new GUIContent("Create Skill Here"), false, () => CreateNode(types[0], world));
            else
                foreach (var type in types)
                {
                    var t = type;
                    menu.AddItem(new GUIContent("Create Skill Here/" + SkillTreeEditing.MenuPath(t)), false, () => CreateNode(t, world));
                }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Select All  (Ctrl+A)"), false, SelectAll);
            menu.AddItem(new GUIContent("Frame All  (F)"), false, FrameAll);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Snap to Grid"), Tree.Layout.Snap, () => SetLayout(l => l.Snap = !l.Snap));
            menu.AddItem(new GUIContent("Show Rings"), Tree.Layout.ShowRings, () => SetLayout(l => l.ShowRings = !l.ShowRings));
        }

        public void SetLayout(Action<SkillTreeLayout> change)
        {
            Undo.RecordObject(Tree, "Change Layout");
            change(Tree.Layout);
            EditorUtility.SetDirty(Tree);
            _background.MarkDirtyRepaint();
            Edited?.Invoke();
        }

        // ---------------------------------------------------------------- edges

        private bool Connected(SkillNode a, SkillNode b) =>
            SkillTreeEditing.AreLinked(a, b) || SkillTreeEditing.Requires(a, b) || SkillTreeEditing.Requires(b, a);

        private static (SkillNode, SkillNode) Key(SkillNode a, SkillNode b) =>
#if UNITY_6000_3_OR_NEWER
            a.GetEntityId().CompareTo(b.GetEntityId()) < 0 ? (a, b) : (b, a); // unique, unlike an object hash code that can collide
#else
            a.GetInstanceID() < b.GetInstanceID() ? (a, b) : (b, a);
#endif

        /// Every connected pair once. For a requirement, a is the one needed first.
        private IEnumerable<(SkillNode a, SkillNode b, bool linked, bool required)> Edges()
        {
            var seen = new HashSet<(SkillNode, SkillNode)>();
            foreach (var node in _elements.Keys)
            {
                foreach (var need in node.Requires)
                    if (need != null && need != node && _elements.ContainsKey(need) && seen.Add(Key(need, node)))
                        yield return (need, node, SkillTreeEditing.AreLinked(need, node), true);
            }
            foreach (var node in _elements.Keys)
            {
                foreach (var other in node.Neighbors)
                    if (other != null && other != node && _elements.ContainsKey(other) && seen.Add(Key(node, other)))
                        yield return (node, other, true, false);
            }
        }

        private (SkillNode a, SkillNode b)? EdgeAt(Vector2 p)
        {
            (SkillNode, SkillNode)? best = null;
            float bestDistance = EdgePickDistance;
            foreach (var (a, b, _, _) in Edges())
            {
                float d = DistanceToSegment(p, WorldToScreen(a.Position), WorldToScreen(b.Position));
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = (a, b);
                }
            }
            return best;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = ab.sqrMagnitude < 0.001f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        // ---------------------------------------------------------------- drawing

        private void DrawBackground(MeshGenerationContext ctx)
        {
            var size = _background.contentRect.size;
            if (size.x < 1f || size.y < 1f || Tree == null) return;
            var p = ctx.painter2D;
            p.lineCap = LineCap.Butt;

            float step = Mathf.Max(1f, Tree.Layout.GridSize) * _zoom;
            Grid(p, size, step, MinorGrid, _pan);
            Grid(p, size, step * 5f, MajorGrid, _pan);

            var root = Tree.Root;
            if (Tree.Layout.ShowRings && root != null && _elements.ContainsKey(root))
            {
                float spacing = Mathf.Max(10f, Tree.Layout.RingSpacing);
                float far = _elements.Keys.Max(n => (n.Position - root.Position).magnitude);
                int rings = Mathf.Min(40, Mathf.CeilToInt(far / spacing) + 1);
                var center = WorldToScreen(root.Position);
                p.strokeColor = RingColor;
                p.lineWidth = 1.5f;
                for (int i = 1; i <= rings; i++)
                {
                    p.BeginPath();
                    p.Arc(center, i * spacing * _zoom, Angle.Degrees(0f), Angle.Degrees(360f));
                    p.Stroke();
                }
            }

            p.lineCap = LineCap.Round;
            foreach (var (a, b, linked, required) in Edges())
            {
                var from = WorldToScreen(a.Position);
                var to = WorldToScreen(b.Position);
                var dir = to - from;
                float length = dir.magnitude;
                float ra = SkillNodeElement.RadiusOf(a) * _zoom + 2f, rb = SkillNodeElement.RadiusOf(b) * _zoom + 2f;
                if (length <= ra + rb) continue;
                dir /= length;
                from += dir * ra;
                to -= dir * rb;

                bool picked = SelectedEdge is { } e && ((e.a == a && e.b == b) || (e.a == b && e.b == a));
                bool touches = _picked.Contains(a) || _picked.Contains(b);
                float alpha = EdgeAlpha(a, b, touches);
                var style = required ? null : Tree.LinkStyleOf(a, b); // a style shows its color and width, so the canvas matches the panel
                var color = picked ? PickColor : required ? RequireColor : style != null ? style.OnColor : LinkColor;
                color.a = picked ? 1f : required ? Mathf.Min(1f, alpha + 0.25f) : alpha;
                float width = (picked ? 3.5f : touches ? 3f : 2f) * Mathf.Clamp(_zoom * 1.4f, 0.6f, 1.4f)
                    * (style != null ? Mathf.Clamp(style.Width / 5f, 0.4f, 3f) : 1f); // 5 = the link prefab's default width

                if (required && !linked) Dashed(p, from, to, color, width);
                else Line(p, from, to, color, width);
                if (required) Arrow(p, to, dir, color, Mathf.Max(7f, 11f * Mathf.Clamp(_zoom * 1.4f, 0.6f, 1.4f)));
            }
        }

        private float EdgeAlpha(SkillNode a, SkillNode b, bool touches)
        {
            if (Simulation != null)
            {
                bool ao = Simulation.IsOwned(a), bo = Simulation.IsOwned(b);
                return ao && bo ? 0.9f : ao || bo ? 0.45f : 0.12f;
            }
            if (_dimmed.Contains(a) || _dimmed.Contains(b)) return 0.07f;
            return touches ? 0.8f : 0.3f;
        }

        private static void Grid(Painter2D p, Vector2 size, float step, Color color, Vector2 origin)
        {
            if (step < 7f) return;
            p.strokeColor = color;
            p.lineWidth = 1f;
            p.BeginPath();
            for (float x = Mathf.Repeat(origin.x, step); x < size.x; x += step)
            {
                p.MoveTo(new Vector2(x, 0f));
                p.LineTo(new Vector2(x, size.y));
            }
            for (float y = Mathf.Repeat(origin.y, step); y < size.y; y += step)
            {
                p.MoveTo(new Vector2(0f, y));
                p.LineTo(new Vector2(size.x, y));
            }
            p.Stroke();
        }

        private static void Line(Painter2D p, Vector2 a, Vector2 b, Color color, float width)
        {
            p.strokeColor = color;
            p.lineWidth = width;
            p.BeginPath();
            p.MoveTo(a);
            p.LineTo(b);
            p.Stroke();
        }

        private static void Dashed(Painter2D p, Vector2 a, Vector2 b, Color color, float width)
        {
            p.strokeColor = color;
            p.lineWidth = width;
            var dir = b - a;
            float length = dir.magnitude;
            dir /= length;
            p.BeginPath();
            for (float t = 0f; t < length; t += 14f)
            {
                p.MoveTo(a + dir * t);
                p.LineTo(a + dir * Mathf.Min(length, t + 8f));
            }
            p.Stroke();
        }

        private static void Arrow(Painter2D p, Vector2 tip, Vector2 dir, Color color, float size)
        {
            var side = new Vector2(-dir.y, dir.x) * size * 0.55f;
            p.fillColor = color;
            p.BeginPath();
            p.MoveTo(tip);
            p.LineTo(tip - dir * size + side);
            p.LineTo(tip - dir * size - side);
            p.ClosePath();
            p.Fill();
        }

        private void DrawOverlay(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            if (_drag == Drag.Band && _band.width > 1f && _band.height > 1f)
            {
                p.fillColor = BandFill;
                p.strokeColor = BandLine;
                p.lineWidth = 1f;
                p.BeginPath();
                p.MoveTo(_band.min);
                p.LineTo(new Vector2(_band.xMax, _band.yMin));
                p.LineTo(_band.max);
                p.LineTo(new Vector2(_band.xMin, _band.yMax));
                p.ClosePath();
                p.Fill();
                p.Stroke();
            }
            if (_drag == Drag.Connect && _dragNode != null)
            {
                var from = WorldToScreen(_dragNode.Position);
                var to = _connectTarget != null ? WorldToScreen(_connectTarget.Node.Position) : _lastScreen;
                var color = _connectRequire ? RequireColor : PickColor;
                p.lineCap = LineCap.Round;
                Line(p, from, to, color, 2.5f);
                var dir = to - from;
                if (_connectRequire && dir.sqrMagnitude > 1f)
                {
                    dir.Normalize();
                    float r = _connectTarget != null ? SkillNodeElement.RadiusOf(_connectTarget.Node) * _zoom + 2f : 0f;
                    Arrow(p, to - dir * r, dir, color, 12f);
                }
                p.fillColor = color;
                p.BeginPath();
                p.Arc(from, 4f, Angle.Degrees(0f), Angle.Degrees(360f));
                p.Fill();
            }
        }
    }
}
