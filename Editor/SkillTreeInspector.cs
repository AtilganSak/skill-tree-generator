using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace HeatInteractive.SkillTreeGenerator.Editing
{
    /// The right panel. Nothing picked: the tree (issues, groups, settings, help). One skill: its fields, connections and
    /// issues. Several: the shared fields. A connection: what it is and how to change it.
    internal class SkillTreeInspector : VisualElement
    {
        private readonly SkillTreeWindow _window;
        private readonly ScrollView _scroll;
        private VisualElement _issueBox;
        private Label _headerTitle;
        private Action _refreshHeader;

        private SkillTree Tree => _window.Tree;
        private SkillTreeCanvas Canvas => _window.Canvas;

        public SkillTreeInspector(SkillTreeWindow window)
        {
            _window = window;
            AddToClassList("inspector");
            _scroll = new ScrollView(ScrollViewMode.Vertical);
            _scroll.AddToClassList("inspector__scroll");
            Add(_scroll);
        }

        public void Rebuild()
        {
            _scroll.Clear();
            _issueBox = null;
            _headerTitle = null;
            _refreshHeader = null;
            if (Tree == null) return;

            var selection = Canvas.Selection.Where(n => n != null).ToList();
            if (selection.Count == 1) BuildNode(selection[0]);
            else if (selection.Count > 1) BuildMany(selection);
            else if (Canvas.SelectedEdge is { } edge && edge.a != null && edge.b != null) BuildEdge(edge.a, edge.b);
            else BuildTree();
        }

        /// The node's name or rank changed elsewhere (the inspector itself, a simulation click).
        public void RefreshHeader() => _refreshHeader?.Invoke();

        public void RefreshIssues()
        {
            if (_issueBox == null) return;
            _issueBox.Clear();
            var selection = Canvas.Selection;
            var issues = selection.Count == 1
                ? _window.Issues.Where(i => i.Node == selection[0]).ToList()
                : _window.Issues.ToList();
            if (selection.Count == 1)
            {
                foreach (var issue in issues) _issueBox.Add(IssueRow(issue, false));
                _issueBox.style.display = issues.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                return;
            }
            if (issues.Count == 0)
            {
                var ok = new Label("Everything checks out.");
                ok.AddToClassList("issues__ok");
                _issueBox.Add(ok);
                return;
            }
            foreach (var issue in issues.OrderByDescending(i => i.IsError)) _issueBox.Add(IssueRow(issue, true));
        }

        // ---------------------------------------------------------------- building blocks

        private VisualElement Section(string title, string help = null)
        {
            var section = new VisualElement();
            section.AddToClassList("section");
            var header = new Label(title);
            header.AddToClassList("section__title");
            section.Add(header);
            if (help != null)
            {
                var h = new Label(help);
                h.AddToClassList("section__help");
                section.Add(h);
            }
            _scroll.Add(section);
            return section;
        }

        private VisualElement Header(string title, string subtitle, Color? color)
        {
            var header = new VisualElement();
            header.AddToClassList("header");
            var strip = new VisualElement();
            strip.AddToClassList("header__strip");
            strip.style.backgroundColor = color ?? new Color(0.5f, 0.5f, 0.5f, 0.4f);
            header.Add(strip);
            var texts = new VisualElement();
            texts.AddToClassList("header__texts");
            _headerTitle = new Label(title);
            _headerTitle.AddToClassList("header__title");
            texts.Add(_headerTitle);
            var sub = new Label(subtitle);
            sub.AddToClassList("header__subtitle");
            texts.Add(sub);
            header.Add(texts);
            _scroll.Add(header);
            return header;
        }

        private static Label Badge(VisualElement parent, string text, string cls)
        {
            var badge = new Label(text);
            badge.AddToClassList("badge");
            badge.AddToClassList(cls);
            parent.Add(badge);
            return badge;
        }

        private static Button ActionButton(VisualElement parent, string text, Action onClick, string cls = null)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("action");
            if (cls != null) button.AddToClassList(cls);
            parent.Add(button);
            return button;
        }

        private static VisualElement Row(VisualElement parent)
        {
            var row = new VisualElement();
            row.AddToClassList("row");
            parent.Add(row);
            return row;
        }

        private VisualElement IssueRow(SkillIssue issue, bool showNode)
        {
            var row = new VisualElement();
            row.AddToClassList("issue");
            row.AddToClassList(issue.IsError ? "issue--error" : "issue--warning");
            var icon = new Label(issue.IsError ? "✖" : "▲");
            icon.AddToClassList("issue__icon");
            row.Add(icon);
            var text = new Label(showNode && issue.Node != null ? $"<b>{issue.Node.DisplayName}</b>  {issue.Message}" : issue.Message);
            text.AddToClassList("issue__text");
            row.Add(text);
            if (showNode && issue.Node != null)
            {
                var node = issue.Node;
                row.AddToClassList("issue--clickable");
                row.tooltip = "Click to pick and frame this skill.";
                row.RegisterCallback<ClickEvent>(_ =>
                {
                    Canvas.SelectOnly(node);
                    Canvas.Frame(new[] { node });
                });
            }
            return row;
        }

        /// A skill as a chip. Click picks it, or runs click instead (with its own tooltip).
        private VisualElement NodeChip(VisualElement parent, SkillNode node, Action remove, string removeTip,
            Action click = null, string clickTip = null)
        {
            var chip = new VisualElement();
            chip.AddToClassList("chip");
            var dot = new VisualElement();
            dot.AddToClassList("chip__dot");
            dot.style.backgroundColor = node.Group != null ? node.Group.Color : new Color(0.55f, 0.58f, 0.64f);
            chip.Add(dot);
            var name = new Label(node.DisplayName);
            name.AddToClassList("chip__name");
            chip.Add(name);
            chip.tooltip = click != null ? clickTip : "Click to pick it.";
            chip.RegisterCallback<ClickEvent>(_ =>
            {
                if (click != null)
                {
                    click();
                    return;
                }
                Canvas.SelectOnly(node);
                Canvas.Frame(new[] { node });
            });
            if (remove != null)
            {
                var x = new Button(remove) { text = "×", tooltip = removeTip };
                x.AddToClassList("chip__remove");
                chip.Add(x);
            }
            parent.Add(chip);
            return chip;
        }

        /// Every visible field as a UI Toolkit PropertyField, so the package's drawers (stats, groups, effects) are used
        /// even when an inspector framework (Odin...) replaces the default editors.
        private static VisualElement Fields(SerializedObject so)
        {
            var box = new VisualElement();
            box.AddToClassList("properties");
            for (var it = so.GetIterator(); it.NextVisible(it.propertyPath == "");)
            {
                if (it.propertyPath == "m_Script") continue;
                if (it.isArray && it.propertyType == SerializedPropertyType.Generic) it.isExpanded = true; // lists open: effects, cost
                box.Add(new PropertyField(it.Copy()));
            }
            box.Bind(so);
            return box;
        }

        private static Label Hint(VisualElement parent, string text)
        {
            var hint = new Label(text);
            hint.AddToClassList("hint");
            parent.Add(hint);
            return hint;
        }

        // ---------------------------------------------------------------- one node

        private void BuildNode(SkillNode node)
        {
            var header = Header(node.DisplayName, $"{SkillTreeEditing.TypeLabel(node.GetType())}  ·  Id {node.Id}",
                node.Group != null ? node.Group.Color : null);
            header.tooltip = SkillTreeEditing.TypeTooltip(node.GetType());
            var badges = new VisualElement();
            badges.AddToClassList("header__badges");
            header.Add(badges);
            if (node.IsRoot) Badge(badges, "ROOT", "badge--root");
            _refreshHeader = () =>
            {
                if (_headerTitle != null) _headerTitle.text = string.IsNullOrWhiteSpace(node.DisplayName) ? "(unnamed)" : node.DisplayName;
            };

            _issueBox = new VisualElement();
            _issueBox.AddToClassList("issues");
            _scroll.Add(_issueBox);
            RefreshIssues();

            if (Canvas.Simulation != null) BuildSimulation(node);

            var props = Section("Properties");
            var so = new SerializedObject(node);
            props.Add(Fields(so));
            props.TrackSerializedObjectValue(so, _ => _window.OnNodeEdited(node));

            BuildConnections(node);

            var place = Section("Placement");
            var position = new PropertyField(so.FindProperty(nameof(SkillNode.Position)), "Position");
            position.Bind(so);
            place.Add(position);
            if (Canvas.Simulation == null) BuildSwap(place, node);
            var actions = Row(place);
            if (!node.IsRoot) ActionButton(actions, "Set as Root", () =>
            {
                SkillTreeEditing.SetRoot(Tree, node);
                Canvas.RefreshAll();
                _window.OnTreeEdited();
            });
            ActionButton(actions, "Duplicate", Canvas.DuplicateSelection);
            if (!node.IsRoot) ActionButton(actions, "Delete", () => Canvas.Delete(new List<SkillNode> { node }), "action--danger");
        }

        private const int SwapResults = 8;

        /// Search a skill by name, click it: the two swap places in the tree (position, links and requirements go with
        /// the place, so the drawn tree doesn't change; each skill keeps its own Id and data).
        private void BuildSwap(VisualElement section, SkillNode node)
        {
            var title = new Label("Swap places with");
            title.AddToClassList("sub-title");
            section.Add(title);
            var search = new ToolbarSearchField { tooltip = "Type part of a skill's name, then click it. The two take each other's place in the tree (links and requirements too)." };
            search.AddToClassList("stat-filter");
            section.Add(search);
            var results = new VisualElement();
            results.AddToClassList("chips");
            section.Add(results);

            search.RegisterValueChangedCallback(e =>
            {
                results.Clear();
                string text = e.newValue;
                if (string.IsNullOrWhiteSpace(text)) return;
                var found = Tree.Nodes
                    .Where(n => n != null && n != node && n.DisplayName != null && n.DisplayName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(n => n.DisplayName).ToList();
                if (found.Count == 0) Hint(results, "No such skill.");
                foreach (var other in found.Take(SwapResults))
                    NodeChip(results, other, null, null, () =>
                    {
                        SkillTreeEditing.SwapPlaces(Tree, node, other);
                        _window.OnTreeEdited();
                    }, $"Swap places with \"{other.DisplayName}\".");
                if (found.Count > SwapResults) Hint(results, $"{found.Count - SwapResults} more: type more of the name.");
            });
        }

        /// Double-click on a node: straight to its name.
        public void FocusName()
        {
            schedule.Execute(() =>
            {
                var field = _scroll.Query<PropertyField>().Where(f => f.bindingPath == nameof(SkillNode.DisplayName)).First();
                var text = field?.Q<TextField>();
                if (text == null) return;
                text.Focus();
                text.SelectAll();
            }).StartingIn(50);
        }

        private void BuildSimulation(SkillNode node)
        {
            var section = Section("Simulation", "Clicks on the canvas buy and refund ranks. Nothing here is saved.");
            section.AddToClassList("section--simulation");
            var status = new Label();
            status.AddToClassList("sim-status");
            section.Add(status);
            var row = Row(section);
            ActionButton(row, "− Refund", () => Canvas.SimulateRank(node, -1));
            ActionButton(row, "+ Buy", () => Canvas.SimulateRank(node, +1), "action--primary");

            _refreshHeader += Paint;
            Paint();

            void Paint()
            {
                var ranks = Canvas.Simulation;
                if (ranks == null) return;
                int rank = ranks.GetRank(node);
                string state = Canvas.StateOf(node) switch
                {
                    SimState.Maxed => "Maxed out.",
                    SimState.Owned => "Owned, can rank up.",
                    SimState.Available => "Can be bought now.",
                    _ => rank > 0 ? "Owned. " + (Tree.WhyLocked(node, ranks) ?? "") : "Locked: " + (Tree.WhyLocked(node, ranks) ?? "?")
                };
                status.text = $"<b>Rank {rank} / {node.MaxRank}</b>   {state}";
            }
        }

        private void BuildConnections(SkillNode node)
        {
            var section = Section("Connections");

            var links = Tree.LinksOf(node).Where(n => n != null).ToList();
            var linkTitle = new Label($"Opens from  ({links.Count})");
            linkTitle.AddToClassList("sub-title");
            section.Add(linkTitle);
            var linkBox = new VisualElement();
            linkBox.AddToClassList("chips");
            section.Add(linkBox);
            foreach (var other in links)
                NodeChip(linkBox, other, () =>
                {
                    SkillTreeEditing.Disconnect(Tree, node, other);
                    _window.OnTreeEdited();
                }, "Remove this connection");
            if (links.Count == 0 && !node.IsRoot) Hint(linkBox, "Not connected: it can never be reached. Drag from a skill's + handle onto it.");
            if (node.IsRoot) Hint(section, "The root is always open: the tree starts here.");

            var needs = node.Requires.Where(n => n != null).ToList();
            var needTitle = new Label($"Requires first  ({needs.Count})");
            needTitle.AddToClassList("sub-title");
            section.Add(needTitle);
            var needBox = new VisualElement();
            needBox.AddToClassList("chips");
            section.Add(needBox);
            foreach (var need in needs)
                NodeChip(needBox, need, () =>
                {
                    Undo.RecordObject(node, "Remove Requirement");
                    node.Requires.Remove(need);
                    EditorUtility.SetDirty(node);
                    _window.OnTreeEdited();
                }, "Remove this requirement (the link stays)");
            if (needs.Count == 0) Hint(needBox, "None. Ctrl-drag from another skill's + handle onto this one to add one.");

            var dependents = Tree.Nodes.Where(n => n != null && n.Requires.Contains(node)).ToList();
            if (dependents.Count > 0)
            {
                var depTitle = new Label($"Required by  ({dependents.Count})");
                depTitle.AddToClassList("sub-title");
                section.Add(depTitle);
                var depBox = new VisualElement();
                depBox.AddToClassList("chips");
                section.Add(depBox);
                foreach (var dependent in dependents) NodeChip(depBox, dependent, null, null);
            }
        }

        // ---------------------------------------------------------------- several nodes

        private void BuildMany(List<SkillNode> nodes)
        {
            Header($"{nodes.Count} skills selected", "Edits apply to all of them.", null);
            var group = Section("Group");
            var groups = new List<SkillGroup> { null };
            groups.AddRange(Tree.Groups.Where(g => g != null));
            var shared = nodes.Select(n => n.Group).Distinct().ToList();
            int index = shared.Count == 1 ? Mathf.Max(0, groups.IndexOf(shared[0])) : -1;
            var dropdown = new DropdownField("Group", groups.Select(g => g == null ? "None" : g.DisplayName).ToList(), index);
            if (index < 0) dropdown.SetValueWithoutNotify("— mixed —");
            dropdown.RegisterValueChangedCallback(_ =>
            {
                if (dropdown.index < 0) return;
                SkillTreeEditing.SetGroup(Tree, nodes, groups[dropdown.index]);
                Canvas.RefreshAll();
                _window.OnTreeEdited();
            });
            group.Add(dropdown);

            var type = nodes[0].GetType();
            if (nodes.All(n => n.GetType() == type))
            {
                var props = Section("Shared Properties");
                var so = new SerializedObject(nodes.Cast<UnityEngine.Object>().ToArray());
                props.Add(Fields(so));
                props.TrackSerializedObjectValue(so, _ =>
                {
                    foreach (var n in nodes) SkillTreeEditing.SyncName(n);
                    Canvas.RefreshAll();
                    _window.OnNodeEdited(null);
                });
            }
            else Hint(Section("Properties"), "These are different skill types: pick skills of one type to edit their fields together.");

            var actions = Row(Section("Actions"));
            ActionButton(actions, "Frame", () => Canvas.Frame(nodes));
            ActionButton(actions, "Duplicate", Canvas.DuplicateSelection);
            ActionButton(actions, $"Delete {nodes.Count}", () => Canvas.Delete(nodes), "action--danger");
        }

        // ---------------------------------------------------------------- a connection

        private void BuildEdge(SkillNode a, SkillNode b)
        {
            bool linked = SkillTreeEditing.AreLinked(a, b);
            bool aFirst = SkillTreeEditing.Requires(b, a), bFirst = SkillTreeEditing.Requires(a, b);
            string kind = aFirst || bFirst ? linked ? "Requirement (and link)" : "Requirement only" : "Link";
            Header($"{a.DisplayName}  —  {b.DisplayName}", kind, null);

            var section = Section("What it does");
            Hint(section, linked
                ? "Linked: once either one is owned, the other can be bought."
                : "Not linked: this connection doesn't open anything by itself.");
            if (aFirst) Hint(section, $"\"{b.DisplayName}\" can only be bought after \"{a.DisplayName}\".");
            if (bFirst) Hint(section, $"\"{a.DisplayName}\" can only be bought after \"{b.DisplayName}\".");

            var ends = Section("Skills");
            var chips = new VisualElement();
            chips.AddToClassList("chips");
            ends.Add(chips);
            NodeChip(chips, a, null, null);
            NodeChip(chips, b, null, null);

            if (linked) BuildEdgeLook(a, b);

            var change = Section("Change");
            var row = Row(change);
            ActionButton(row, $"\"{a.DisplayName}\" first", () => { SkillTreeEditing.SetRequirement(Tree, b, a); _window.OnTreeEdited(); });
            ActionButton(row, $"\"{b.DisplayName}\" first", () => { SkillTreeEditing.SetRequirement(Tree, a, b); _window.OnTreeEdited(); });
            var row2 = Row(change);
            if (aFirst || bFirst)
            {
                ActionButton(row2, "Plain link", () => { SkillTreeEditing.MakePlainLink(Tree, a, b); _window.OnTreeEdited(); });
                if (linked) ActionButton(row2, "Requirement only", () => { SkillTreeEditing.RemoveLinkOnly(Tree, a, b); _window.OnTreeEdited(); });
            }
            ActionButton(row2, "Remove", () =>
            {
                SkillTreeEditing.Disconnect(Tree, a, b);
                Canvas.ClearSelection();
                _window.OnTreeEdited();
            }, "action--danger");
        }

        private const string DefaultLook = "Default (the panel's link prefab)";
        private const string NewLook = "+ New Style";

        /// The link's line on the panel: pick a style (a preset shared by links) and edit it here.
        private void BuildEdgeLook(SkillNode a, SkillNode b)
        {
            var section = Section("Look", "The line's width and colors on the panel. A style is shared: editing it changes every link that uses it.");
            var styles = Tree.LinkStyles.Where(s => s != null).OrderBy(s => s.DisplayName).ToList();
            var current = Tree.LinkStyleOf(a, b);

            var choices = new List<string> { DefaultLook };
            choices.AddRange(styles.Select(s => s.DisplayName));
            choices.Add(NewLook);
            var dropdown = new DropdownField("Style", choices, current == null ? 0 : styles.IndexOf(current) + 1);
            dropdown.AddToClassList(BaseField<string>.alignedFieldUssClassName);
            dropdown.RegisterValueChangedCallback(_ =>
            {
                int i = dropdown.index;
                var pick = i == choices.Count - 1 ? SkillTreeEditing.CreateLinkStyle(Tree) : i <= 0 ? null : styles[i - 1];
                SkillTreeEditing.SetLinkStyle(Tree, a, b, pick);
                _window.OnTreeEdited();
            });
            section.Add(dropdown);
            if (current == null)
            {
                Hint(section, "No style: drawn as the panel's link prefab is. Pick \"+ New Style\" to change this link's look.");
                return;
            }

            var so = new SerializedObject(current);
            for (var it = so.GetIterator(); it.NextVisible(it.propertyPath == ""); )
            {
                if (it.propertyPath == "m_Script") continue;
                var field = new PropertyField(it.Copy());
                field.Bind(so);
                section.Add(field);
            }
            section.TrackSerializedObjectValue(so, _ =>
            {
                SkillTreeEditing.SyncName(current);
                _window.OnStatsEdited(false);
            });

            int uses = SkillTreeEditing.UsesOf(Tree, current);
            Hint(section, $"Used by {uses} link{(uses == 1 ? "" : "s")}.");
            var actions = Row(section);
            ActionButton(actions, "Apply to all links", () =>
            {
                if (Tree.Edges.Any(e => e != null && e.Style != current) &&
                    !EditorUtility.DisplayDialog("Apply to all links", $"Every link of the tree will use \"{current.DisplayName}\".", "Apply", "Cancel")) return;
                SkillTreeEditing.SetAllLinkStyles(Tree, current);
                _window.OnTreeEdited();
            });
            ActionButton(actions, "Delete style", () =>
            {
                if (uses > 1 && !EditorUtility.DisplayDialog("Delete style",
                        $"\"{current.DisplayName}\" is used by {uses} links. They go back to the panel's own look.", "Delete", "Cancel")) return;
                SkillTreeEditing.DeleteLinkStyle(Tree, current);
                _window.OnTreeEdited();
            }, "action--danger");
        }

        // ---------------------------------------------------------------- the tree

        private void BuildTree()
        {
            int nodes = Tree.Nodes.Count(n => n != null);
            Header(Tree.name, $"{SkillTreeEditing.Pretty(Tree.GetType())}  ·  {nodes} skills  ·  {Tree.Groups.Count(g => g != null)} groups", null);

            var issues = Section("Validation");
            _issueBox = new VisualElement();
            _issueBox.AddToClassList("issues");
            issues.Add(_issueBox);
            RefreshIssues();

            BuildStats();
            BuildCurrencies();
            BuildGroups();

            var settings = Section("Settings");
            var so = new SerializedObject(Tree);
            // Layout is hidden from regular inspectors (a PropertyField won't draw it), so its fields go one by one.
            var layout = so.FindProperty(nameof(SkillTree.Layout));
            var end = layout.GetEndProperty();
            for (var child = layout.Copy(); child.Next(child.propertyPath == layout.propertyPath) && !SerializedProperty.EqualContents(child, end);)
            {
                var field = new PropertyField(child.Copy());
                field.Bind(so);
                settings.Add(field);
            }
            // The tree's own fields (a subclass's currencies...), drawn as usual.
            var it = so.GetIterator();
            for (bool enter = true; it.NextVisible(enter); enter = false)
            {
                if (it.propertyPath == "m_Script") continue;
                var field = new PropertyField(it.Copy());
                field.Bind(so);
                settings.Add(field);
            }
            settings.TrackSerializedObjectValue(so, _ => _window.OnSettingsEdited());

            BuildHelp(nodes == 0);
        }

        private static string _statFilter = "";
        private static SkillStat _expandStat;

        private void BuildStats()
        {
            var section = Section("Stats",
                "What skills can raise. Code reads one by its Key: tree.Stat(\"Damage\"), then tree.GetStat(stat, baseValue, ranks).");
            var stats = Tree.Stats.Where(s => s != null).OrderBy(s => s.DisplayName).ToList();

            if (stats.Count > 6)
            {
                var filter = new ToolbarSearchField { value = _statFilter };
                filter.AddToClassList("stat-filter");
                section.Add(filter);
                filter.RegisterValueChangedCallback(e =>
                {
                    _statFilter = e.newValue ?? "";
                    foreach (var row in section.Query(className: "stat").ToList())
                        row.style.display = Show(row.userData as SkillStat) ? DisplayStyle.Flex : DisplayStyle.None;
                });
            }

            foreach (var stat in stats)
            {
                var s = stat;
                var users = SkillTreeEditing.UsersOf(Tree, s);
                var foldout = new Foldout { value = s == _expandStat, userData = s };
                foldout.AddToClassList("stat");
                foldout.style.display = Show(s) ? DisplayStyle.Flex : DisplayStyle.None;
                void Title() => foldout.text = $"{s.DisplayName}   <color=#888>{(s.Kind == SkillStatKind.Percent ? "%" : s.Kind == SkillStatKind.Flag ? "flag" : "flat")} · {s.Key} · {users.Count} use{(users.Count == 1 ? "" : "s")}</color>";
                Title();

                var so = new SerializedObject(s);
                VisualElement unitField = null;
                for (var it = so.GetIterator(); it.NextVisible(it.propertyPath == ""); )
                {
                    if (it.propertyPath == "m_Script") continue;
                    var field = new PropertyField(it.Copy());
                    field.Bind(so);
                    foldout.Add(field);
                    if (it.propertyPath == nameof(SkillStat.Unit)) unitField = field;
                }
                void ShowUnit() { if (unitField != null) unitField.style.display = s.Kind == SkillStatKind.Flag ? DisplayStyle.None : DisplayStyle.Flex; }
                ShowUnit();
                foldout.TrackSerializedObjectValue(so, _ =>
                {
                    SkillTreeEditing.SyncName(s);
                    Title();
                    ShowUnit();
                    _window.OnStatsEdited(false);
                });

                var actions = Row(foldout);
                if (users.Count > 0)
                    ActionButton(actions, $"Pick its {users.Count} skill{(users.Count == 1 ? "" : "s")}", () =>
                    {
                        Canvas.Select(users);
                        Canvas.Frame(users);
                    });
                ActionButton(actions, "Delete", () =>
                {
                    if (users.Count > 0 && !EditorUtility.DisplayDialog("Delete stat",
                            $"\"{s.DisplayName}\" is used by {users.Count} skill(s). Their effects will have no stat until you pick another.",
                            "Delete", "Cancel")) return;
                    SkillTreeEditing.DeleteStat(Tree, s);
                    _window.OnStatsEdited(true);
                }, "action--danger");
                section.Add(foldout);
            }

            if (stats.Count == 0) Hint(section, "No stats yet. Add one, then pick it in a skill's Effects.");
            ActionButton(Row(section), "+ Add Stat", () =>
            {
                _expandStat = SkillTreeEditing.CreateStat(Tree);
                _statFilter = "";
                _window.OnStatsEdited(true);
            });

            bool Show(SkillStat s) => s != null && (_statFilter.Length == 0
                || s.DisplayName.IndexOf(_statFilter, StringComparison.OrdinalIgnoreCase) >= 0
                || s.Key.IndexOf(_statFilter, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static SkillCurrency _expandCurrency;

        private void BuildCurrencies()
        {
            var section = Section("Currencies",
                "What skills cost. Your ISkillWallet maps each one to the game's own money by its Key, e.g. case \"gold\".");
            foreach (var currency in Tree.Currencies.Where(c => c != null).OrderBy(c => c.DisplayName))
            {
                var c = currency;
                var users = SkillTreeEditing.UsersOf(Tree, c);
                var foldout = new Foldout { value = c == _expandCurrency };
                foldout.AddToClassList("stat");
                void Title() => foldout.text = $"{c.DisplayName}   <color=#888>{c.Key} · {users.Count} use{(users.Count == 1 ? "" : "s")}</color>";
                Title();

                var so = new SerializedObject(c);
                for (var it = so.GetIterator(); it.NextVisible(it.propertyPath == ""); )
                {
                    if (it.propertyPath == "m_Script") continue;
                    var field = new PropertyField(it.Copy());
                    field.Bind(so);
                    foldout.Add(field);
                }
                foldout.TrackSerializedObjectValue(so, _ =>
                {
                    SkillTreeEditing.SyncName(c);
                    Title();
                    _window.OnStatsEdited(false);
                });

                var actions = Row(foldout);
                if (users.Count > 0)
                    ActionButton(actions, $"Pick its {users.Count} skill{(users.Count == 1 ? "" : "s")}", () =>
                    {
                        Canvas.Select(users);
                        Canvas.Frame(users);
                    });
                ActionButton(actions, "Delete", () =>
                {
                    if (users.Count > 0 && !EditorUtility.DisplayDialog("Delete currency",
                            $"\"{c.DisplayName}\" is used by {users.Count} skill(s). Those costs will have no currency until you pick another.",
                            "Delete", "Cancel")) return;
                    SkillTreeEditing.DeleteCurrency(Tree, c);
                    _window.OnStatsEdited(true);
                }, "action--danger");
                section.Add(foldout);
            }

            if (Tree.Currencies.All(c => c == null)) Hint(section, "No currencies yet. Add one, then pick it in a skill's Cost.");
            ActionButton(Row(section), "+ Add Currency", () =>
            {
                _expandCurrency = SkillTreeEditing.CreateCurrency(Tree);
                _window.OnStatsEdited(true);
            });
        }

        private void BuildGroups()
        {
            var section = Section("Groups", "Color sets for branches. A skill picks its group in its properties (or right-click > Group).");
            foreach (var group in Tree.Groups.Where(g => g != null))
            {
                var g = group;
                var row = new VisualElement();
                row.AddToClassList("group-row");
                var color = new ColorField { value = g.Color, showAlpha = false };
                color.AddToClassList("group-row__color");
                color.RegisterValueChangedCallback(e =>
                {
                    Undo.RecordObject(g, "Group Color");
                    g.Color = e.newValue;
                    EditorUtility.SetDirty(g);
                    _window.OnGroupsEdited(false);
                });
                row.Add(color);
                var name = new TextField { value = g.DisplayName, isDelayed = true };
                name.AddToClassList("group-row__name");
                name.RegisterValueChangedCallback(e =>
                {
                    Undo.RecordObject(g, "Rename Group");
                    g.DisplayName = e.newValue;
                    SkillTreeEditing.SyncName(g);
                    EditorUtility.SetDirty(g);
                    _window.OnGroupsEdited(false);
                });
                row.Add(name);
                int count = Tree.Nodes.Count(n => n != null && n.Group == g);
                var countLabel = new Label(count.ToString()) { tooltip = "Skills in this group. Click to pick them." };
                countLabel.AddToClassList("group-row__count");
                countLabel.RegisterCallback<ClickEvent>(_ =>
                {
                    var members = Tree.Nodes.Where(n => n != null && n.Group == g).ToList();
                    Canvas.Select(members);
                    Canvas.Frame(members);
                });
                row.Add(countLabel);
                var delete = new Button(() =>
                {
                    SkillTreeEditing.DeleteGroup(Tree, g);
                    _window.OnGroupsEdited(true);
                }) { text = "×", tooltip = "Delete the group (its skills keep existing, with no group)." };
                delete.AddToClassList("group-row__delete");
                row.Add(delete);
                section.Add(row);
            }
            ActionButton(Row(section), "+ Add Group", () =>
            {
                SkillTreeEditing.CreateGroup(Tree);
                _window.OnGroupsEdited(true);
            });
        }

        private void BuildHelp(bool open)
        {
            var section = Section("How to");
            var foldout = new Foldout { text = "Controls", value = open };
            section.Add(foldout);
            (string key, string what)[] lines =
            {
                ("Double-click empty space", "Create a skill there"),
                ("Drag the + handle", "Connect two skills (release on empty space: new connected skill)"),
                ("Ctrl + drag the + handle", "Requirement: the target needs this one first"),
                ("Click the copy handle (bottom right)", "Duplicate the skill on a free spot next to it"),
                ("Click / Shift-click", "Pick / add to the picked skills"),
                ("Drag on empty space", "Box select"),
                ("Middle drag, Alt + drag", "Pan"),
                ("Mouse wheel", "Zoom"),
                ("Right-click", "Menu (skill, connection or empty space)"),
                ("Del · Ctrl+D · Ctrl+A · F", "Delete · Duplicate · Select all · Frame"),
                ("Simulate (toolbar)", "Click to buy, right-click to refund: test the unlock order")
            };
            foreach (var (key, what) in lines)
            {
                var row = new VisualElement();
                row.AddToClassList("help-row");
                var k = new Label(key);
                k.AddToClassList("help-row__key");
                row.Add(k);
                var w = new Label(what);
                w.AddToClassList("help-row__what");
                row.Add(w);
                foldout.Add(row);
            }
        }
    }
}
