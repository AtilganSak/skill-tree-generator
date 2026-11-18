using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AtilganSak.Skills.Editing
{
    /// Tools > Skill Tree Editor (or double-click a SkillTree asset). Canvas on the left, inspector on the right,
    /// everything undoable, saved on its own a moment after each change.
    public class SkillTreeWindow : EditorWindow
    {
        // SkillTreeWindow.uss by its GUID, so the folder can live anywhere (Assets or Packages).
        private const string StyleGuid = "466c61d1311c8ec488e2c0b6d5f21d62";
        private const string LastTreeKey = "AtilganSak.Skills.LastTree";
        private const string DefaultHint = "Double-click empty space to add a skill  ·  drag a skill's + handle to connect  ·  right-click for more";

        [SerializeField] private SkillTree _tree;

        public SkillTree Tree => _tree;
        internal SkillTreeCanvas Canvas { get; private set; }
        internal IReadOnlyList<SkillIssue> Issues => _issues;

        private List<SkillIssue> _issues = new();
        private SkillTreeInspector _inspector;
        private ObjectField _treeField;
        private ToolbarSearchField _search;
        private ToolbarMenu _groupMenu, _addMenu;
        private ToolbarToggle _snapToggle, _simToggle;
        private ToolbarButton _resetSim, _issuesButton;
        private Label _hint, _zoomLabel, _simBanner, _noTypes;
        private VisualElement _main, _empty;
        private SkillGroup _groupFilter;
        private IVisualElementScheduledItem _validateLater, _saveLater;

        [MenuItem("Tools/Skill Tree Editor", priority = 0)]
        public static void ShowWindow() => Open(null);

        public static SkillTreeWindow Open(SkillTree tree)
        {
            var window = GetWindow<SkillTreeWindow>();
            window.titleContent = new GUIContent("Skill Tree", EditorGUIUtility.IconContent("d_BlendTree Icon").image);
            window.minSize = new Vector2(720f, 420f);
            if (tree != null) window.SetTree(tree);
            window.Show();
            return window;
        }

        [OnOpenAsset]
        private static bool OnOpenAsset(EntityId entityId, int line)
        {
            if (EditorUtility.EntityIdToObject(entityId) is not SkillTree tree) return false;
            Open(tree);
            return true;
        }

        private void OnEnable() => Undo.undoRedoPerformed += OnUndoRedo;

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            SaveView();
            SaveNow();
        }

        private void OnLostFocus() => SaveNow();

        private void OnProjectChange()
        {
            // The open tree's asset was deleted.
            if (Canvas != null && Canvas.Tree != null && _tree == null) SetTree(null);
        }

        // ---------------------------------------------------------------- layout

        private void CreateGUI()
        {
            var root = rootVisualElement;
            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(AssetDatabase.GUIDToAssetPath(StyleGuid));
            if (style != null) root.styleSheets.Add(style);
            root.AddToClassList("skill-window");
            root.AddToClassList(EditorGUIUtility.isProSkin ? "skill-window--dark" : "skill-window--light");

            root.Add(BuildToolbar());

            _main = new TwoPaneSplitView(1, 380f, TwoPaneSplitViewOrientation.Horizontal);
            _main.AddToClassList("main");
            var canvasHost = new VisualElement();
            canvasHost.AddToClassList("canvas-host");
            Canvas = new SkillTreeCanvas();
            canvasHost.Add(Canvas);
            _simBanner = new Label("SIMULATION  ·  click a skill to buy a rank, right-click to refund  ·  nothing is saved");
            _simBanner.AddToClassList("sim-banner");
            canvasHost.Add(_simBanner);
            _simBanner.pickingMode = PickingMode.Ignore;
            _noTypes = new Label { pickingMode = PickingMode.Ignore };
            _noTypes.AddToClassList("no-types");
            canvasHost.Add(_noTypes);
            _main.Add(canvasHost);
            _inspector = new SkillTreeInspector(this);
            _main.Add(_inspector);
            root.Add(_main);

            _empty = BuildEmptyState();
            root.Add(_empty);
            root.Add(BuildStatusBar());

            Canvas.SelectionChanged += () => _inspector.Rebuild();
            Canvas.Edited += OnTreeEdited;
            Canvas.ViewChanged += () => _zoomLabel.text = $"{Mathf.RoundToInt(Canvas.Zoom * 100f)}%";
            Canvas.Notify += text => ShowNotification(new GUIContent(text), 2.5);
            Canvas.HintChanged += text => _hint.text = text ?? DefaultHint;
            Canvas.NodeDoubleClicked += node =>
            {
                Canvas.SelectOnly(node);
                _inspector.FocusName();
            };
            Canvas.SimulationChanged += () => _inspector.RefreshHeader();

            root.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.F || !evt.actionKey) return;
                _search.Q<TextField>()?.Focus();
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);

            _validateLater = root.schedule.Execute(Validate);
            _validateLater.Pause();
            _saveLater = root.schedule.Execute(SaveNow);
            _saveLater.Pause();

            if (_tree == null)
            {
                string guid = EditorPrefs.GetString(LastTreeKey, "");
                if (guid.Length > 0) _tree = AssetDatabase.LoadAssetAtPath<SkillTree>(AssetDatabase.GUIDToAssetPath(guid));
            }
            ShowTree();
        }

        private Toolbar BuildToolbar()
        {
            var toolbar = new Toolbar();
            toolbar.AddToClassList("toolbar");

            _treeField = new ObjectField { objectType = typeof(SkillTree), allowSceneObjects = false, tooltip = "The tree being edited" };
            _treeField.AddToClassList("toolbar__tree");
            _treeField.RegisterValueChangedCallback(e => SetTree(e.newValue as SkillTree));
            toolbar.Add(_treeField);

            var newTree = new ToolbarMenu { text = "New Tree", tooltip = "Create a new skill tree asset" };
            foreach (var type in SkillTreeEditing.TreeTypes)
            {
                var t = type;
                newTree.menu.AppendAction(SkillTreeEditing.Pretty(t), _ => CreateTree(t));
            }
            toolbar.Add(newTree);
            toolbar.Add(new ToolbarSpacer());

            _addMenu = new ToolbarMenu { text = "Add Skill", tooltip = "Add a skill (next to the picked one, linked to it, or in the middle of the view)" };
            _addMenu.AddToClassList("toolbar__add");
            if (SkillTreeEditing.NodeTypes.Count == 0) _addMenu.menu.AppendAction("No SkillNode types in the project", null, DropdownMenuAction.Status.Disabled);
            foreach (var type in SkillTreeEditing.NodeTypes)
            {
                var t = type;
                _addMenu.menu.AppendAction(SkillTreeEditing.MenuPath(t), _ => AddSkill(t));
            }
            toolbar.Add(_addMenu);

            _search = new ToolbarSearchField { tooltip = "Find skills by name, summary or type (Ctrl+F). Enter picks and frames the matches." };
            _search.AddToClassList("toolbar__search");
            _search.RegisterValueChangedCallback(e => Canvas.SetFilter(e.newValue, _groupFilter));
            _search.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode is not (KeyCode.Return or KeyCode.KeypadEnter)) return;
                Canvas.FrameMatches();
                e.StopPropagation();
            }, TrickleDown.TrickleDown);
            toolbar.Add(_search);

            _groupMenu = new ToolbarMenu { text = "All groups", tooltip = "Show one group, dim the rest" };
            toolbar.Add(_groupMenu);

            var flex = new VisualElement();
            flex.AddToClassList("toolbar__flex");
            toolbar.Add(flex);

            _snapToggle = new ToolbarToggle { text = "Snap", tooltip = "Snap moved skills to the grid" };
            _snapToggle.RegisterValueChangedCallback(e =>
            {
                if (_tree != null && _tree.Layout.Snap != e.newValue) Canvas.SetLayout(l => l.Snap = e.newValue);
            });
            toolbar.Add(_snapToggle);
            toolbar.Add(new ToolbarButton(() => Canvas.FrameSelection()) { text = "Frame", tooltip = "Frame the picked skills, or all of them (F)" });

            _simToggle = new ToolbarToggle { text = "Simulate", tooltip = "Test the tree: click skills to buy ranks, right-click to refund. Nothing is saved." };
            _simToggle.AddToClassList("toolbar__simulate");
            _simToggle.RegisterValueChangedCallback(e => SetSimulation(e.newValue));
            toolbar.Add(_simToggle);
            _resetSim = new ToolbarButton(() => Canvas.ResetSimulation()) { text = "Reset", tooltip = "Refund every simulated rank" };
            toolbar.Add(_resetSim);

            _issuesButton = new ToolbarButton(() => Canvas.ClearSelection()) { tooltip = "Validation: click to see the list" };
            _issuesButton.AddToClassList("toolbar__issues");
            toolbar.Add(_issuesButton);
            return toolbar;
        }

        private VisualElement BuildStatusBar()
        {
            var bar = new VisualElement();
            bar.AddToClassList("status-bar");
            _hint = new Label(DefaultHint);
            _hint.AddToClassList("status-bar__hint");
            bar.Add(_hint);
            _zoomLabel = new Label("100%");
            _zoomLabel.AddToClassList("status-bar__zoom");
            bar.Add(_zoomLabel);
            return bar;
        }

        private VisualElement BuildEmptyState()
        {
            var empty = new VisualElement();
            empty.AddToClassList("empty");
            var card = new VisualElement();
            card.AddToClassList("empty__card");
            empty.Add(card);

            var title = new Label("No skill tree open");
            title.AddToClassList("empty__title");
            card.Add(title);
            var text = new Label("Create a new tree, or open one of the trees in this project.");
            text.AddToClassList("empty__text");
            card.Add(text);

            foreach (var type in SkillTreeEditing.TreeTypes)
            {
                var t = type;
                var create = new Button(() => CreateTree(t)) { text = $"Create {SkillTreeEditing.Pretty(t)}…" };
                create.AddToClassList("action");
                create.AddToClassList("action--primary");
                card.Add(create);
            }

            var list = new VisualElement();
            list.AddToClassList("empty__list");
            card.Add(list);
            empty.RegisterCallback<AttachToPanelEvent>(_ => FillTreeList(list));
            return empty;
        }

        private void FillTreeList(VisualElement list)
        {
            list.Clear();
            var paths = AssetDatabase.FindAssets("t:" + nameof(SkillTree)).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p).ToList();
            if (paths.Count == 0) return;
            var header = new Label("Trees in this project");
            header.AddToClassList("sub-title");
            list.Add(header);
            foreach (var path in paths)
            {
                var p = path;
                var open = new Button(() => SetTree(AssetDatabase.LoadAssetAtPath<SkillTree>(p))) { text = p, tooltip = "Open" };
                open.AddToClassList("empty__tree");
                list.Add(open);
            }
        }

        // ---------------------------------------------------------------- tree

        public void SetTree(SkillTree tree)
        {
            if (Canvas == null)
            {
                _tree = tree; // CreateGUI shows it
                return;
            }
            if (tree == _tree && Canvas.Tree == tree) return;
            SaveView();
            SaveNow();
            _tree = tree;
            ShowTree();
        }

        private void ShowTree()
        {
            if (_tree != null) EditorPrefs.SetString(LastTreeKey, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(_tree)));
            _treeField.SetValueWithoutNotify(_tree);
            _main.style.display = _tree != null ? DisplayStyle.Flex : DisplayStyle.None;
            _empty.style.display = _tree != null ? DisplayStyle.None : DisplayStyle.Flex;
            if (_tree == null) FillTreeList(_empty.Q(className: "empty__list"));

            _simToggle.SetValueWithoutNotify(false);
            Canvas.SetTree(_tree);
            SetSimulation(false);
            _groupFilter = null;
            _search.SetValueWithoutNotify("");
            Canvas.SetFilter("", null);
            RefreshToolbar();
            Validate();
            _inspector.Rebuild();
            if (_tree != null) RestoreView();
        }

        private void CreateTree(Type type)
        {
            string path = EditorUtility.SaveFilePanelInProject("New Skill Tree", "SkillTree", "asset", "Where should the new skill tree go?");
            if (string.IsNullOrEmpty(path)) return;
            var tree = SkillTreeEditing.CreateTree(type, path);
            SetTree(tree);
            Canvas.schedule.Execute(() => Canvas.FrameAll()).StartingIn(50);
        }

        private void AddSkill(Type type)
        {
            if (_tree == null) return;
            var from = Canvas.Selection.Count == 1 ? Canvas.Selection[0] : null;
            var position = from != null ? SkillTreeEditing.PlaceNear(_tree, from, Mathf.Max(100f, _tree.Layout.RingSpacing * 0.6f)) : Canvas.ViewCenter;
            Canvas.CreateNode(type, Canvas.Snap(position), from);
            _inspector.FocusName();
        }

        private void SetSimulation(bool on)
        {
            if (_simToggle.value != on) _simToggle.SetValueWithoutNotify(on);
            Canvas.SetSimulation(on && _tree != null);
            _resetSim.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            _simBanner.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            rootVisualElement.EnableInClassList("skill-window--simulating", on);
            _inspector.Rebuild();
        }

        private void RefreshToolbar()
        {
            bool hasTree = _tree != null;
            _addMenu.SetEnabled(hasTree && SkillTreeEditing.NodeTypes.Count > 0);
            _snapToggle.SetEnabled(hasTree);
            _simToggle.SetEnabled(hasTree);
            bool noTypes = SkillTreeEditing.NodeTypes.Count == 0;
            bool emptyTree = hasTree && !_tree.Nodes.Any(n => n != null);
            _noTypes.text = noTypes
                ? "No skill types in this project yet.\nCreate a class that derives from AtilganSak.Skills.SkillNode,\nthen its skills can be added here."
                : "This tree is empty.\nDouble-click anywhere to add the first skill: it becomes the root,\nthe skill the whole tree opens from.";
            _noTypes.style.display = hasTree && (noTypes || emptyTree) ? DisplayStyle.Flex : DisplayStyle.None;
            if (hasTree) _snapToggle.SetValueWithoutNotify(_tree.Layout.Snap);

            _groupMenu.menu.MenuItems().Clear();
            _groupMenu.menu.AppendAction("All groups", _ => SetGroupFilter(null),
                _ => _groupFilter == null ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            if (hasTree)
                foreach (var group in _tree.Groups.Where(g => g != null))
                {
                    var g = group;
                    _groupMenu.menu.AppendAction(g.DisplayName, _ => SetGroupFilter(g),
                        _ => _groupFilter == g ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
                }
            if (_groupFilter != null && (!hasTree || !_tree.Groups.Contains(_groupFilter))) SetGroupFilter(null);
            _groupMenu.text = _groupFilter != null ? _groupFilter.DisplayName : "All groups";
            _groupMenu.SetEnabled(hasTree && _tree.Groups.Count > 0);
        }

        private void SetGroupFilter(SkillGroup group)
        {
            _groupFilter = group;
            _groupMenu.text = group != null ? group.DisplayName : "All groups";
            Canvas.SetFilter(_search.value, group);
        }

        // ---------------------------------------------------------------- changes

        /// Structure, links or positions changed (the canvas or the inspector's buttons).
        internal void OnTreeEdited()
        {
            if (_tree == null) return;
            _tree.Invalidate();
            Canvas.Rebuild();
            _inspector.Rebuild();
            RefreshToolbar();
            MarkChanged();
        }

        /// A field of a node changed in the inspector: the inspector itself stays as it is (focus, typing).
        internal void OnNodeEdited(SkillNode node)
        {
            if (_tree == null) return;
            if (node != null)
            {
                SkillTreeEditing.SyncName(node);
                Canvas.RefreshNode(node);
            }
            _inspector.RefreshHeader();
            MarkChanged();
        }

        internal void OnGroupsEdited(bool structural)
        {
            Canvas.RefreshAll();
            RefreshToolbar();
            if (structural) _inspector.Rebuild();
            MarkChanged();
        }

        internal void OnStatsEdited(bool structural)
        {
            if (_tree == null) return;
            _tree.Invalidate();
            Canvas.RefreshAll();
            if (structural) _inspector.Rebuild();
            MarkChanged();
        }

        /// Stats changed from outside the window (a stat field's "+ New Stat"): every open editor redraws.
        public static void RefreshOpen()
        {
            EditorApplication.delayCall += () =>
            {
                if (!HasOpenInstances<SkillTreeWindow>()) return;
                GetWindow<SkillTreeWindow>(false, null, false).OnStatsEdited(true);
            };
        }

        internal void OnSettingsEdited()
        {
            _snapToggle.SetValueWithoutNotify(_tree.Layout.Snap);
            Canvas.RefreshAll();
            MarkChanged();
        }

        private void MarkChanged()
        {
            EditorUtility.SetDirty(_tree);
            _tree.Invalidate();
            _validateLater?.ExecuteLater(250);
            _saveLater?.ExecuteLater(1500);
        }

        private void OnUndoRedo()
        {
            if (Canvas == null || _tree == null) return;
            _tree.Invalidate();
            Canvas.Rebuild();
            Canvas.RefreshAll();
            _inspector.Rebuild();
            RefreshToolbar();
            MarkChanged();
        }

        private void Validate()
        {
            _issues = _tree != null ? SkillTreeValidation.Run(_tree) : new List<SkillIssue>();
            Canvas.SetIssues(_issues);
            _inspector.RefreshIssues();

            int errors = _issues.Count(i => i.IsError), warnings = _issues.Count - errors;
            _issuesButton.text = errors > 0 ? $"✖ {errors} error{(errors == 1 ? "" : "s")}" + (warnings > 0 ? $"  ▲ {warnings}" : "")
                : warnings > 0 ? $"▲ {warnings} warning{(warnings == 1 ? "" : "s")}" : "✔ Valid";
            _issuesButton.EnableInClassList("toolbar__issues--error", errors > 0);
            _issuesButton.EnableInClassList("toolbar__issues--warning", errors == 0 && warnings > 0);
            _issuesButton.style.display = _tree != null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void SaveNow()
        {
            if (_tree != null && EditorUtility.IsDirty(_tree)) AssetDatabase.SaveAssetIfDirty(_tree);
        }

        // ---------------------------------------------------------------- view per tree (this session)

        private string ViewKey => _tree != null ? "AtilganSak.Skills.View." + AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(_tree)) : null;

        private void SaveView()
        {
            if (Canvas == null || Canvas.Tree == null || ViewKey == null) return;
            SessionState.SetVector3(ViewKey, new Vector3(Canvas.Pan.x, Canvas.Pan.y, Canvas.Zoom));
        }

        private void RestoreView()
        {
            var saved = SessionState.GetVector3(ViewKey, Vector3.zero);
            if (saved.z > 0f)
            {
                Canvas.SetView(new Vector2(saved.x, saved.y), saved.z);
                return;
            }
            // First time: frame everything once the canvas has a size.
            void Frame(GeometryChangedEvent _)
            {
                if (Canvas.layout.width < 10f) return;
                Canvas.UnregisterCallback<GeometryChangedEvent>(Frame);
                Canvas.FrameAll();
                if (_tree.Nodes.Count(n => n != null) <= 1) Canvas.SetView(Canvas.layout.size * 0.5f, 1f);
            }
            if (Canvas.layout.width >= 10f) Frame(null);
            else Canvas.RegisterCallback<GeometryChangedEvent>(Frame);
        }
    }
}
