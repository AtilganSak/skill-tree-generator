using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HeatInteractive.SkillTreeGenerator.Editing
{
    /// Every change the editor makes to a tree, each one undoable. Nodes, groups and stats are hidden sub-assets of the
    /// tree. Public so your own editor tools can build trees the same way.
    public static class SkillTreeEditing
    {
        private static readonly Color[] Palette =
        {
            new(0.35f, 0.65f, 1f), new(1f, 0.55f, 0.25f), new(0.45f, 0.85f, 0.45f), new(0.75f, 0.45f, 1f),
            new(1f, 0.82f, 0.3f), new(0.95f, 0.4f, 0.45f), new(0.35f, 0.85f, 0.85f), new(0.9f, 0.5f, 0.8f)
        };

        private static List<Type> _nodeTypes, _treeTypes;

        /// Every concrete SkillNode class in the project, by menu path. A type that the project extends with its own
        /// concrete subclass (EffectSkill -> your skill with a cost) is left out: the subclass replaces it.
        public static IReadOnlyList<Type> NodeTypes => _nodeTypes ??= TypeCache.GetTypesDerivedFrom<SkillNode>()
            .Where(Concrete)
            .Where(t => !TypeCache.GetTypesDerivedFrom(t).Any(Concrete))
            .OrderBy(MenuPath).ToList();

        /// SkillTree and its concrete subclasses, the project's own ones first.
        public static IReadOnlyList<Type> TreeTypes => _treeTypes ??= TypeCache.GetTypesDerivedFrom<SkillTree>()
            .Append(typeof(SkillTree)).Where(Concrete).Distinct()
            .OrderBy(t => t == typeof(SkillTree) ? 1 : 0).ThenBy(t => t.Name).ToList();

        private static bool Concrete(Type t) => !t.IsAbstract && !t.IsGenericTypeDefinition;

        public static string Pretty(Type type) => ObjectNames.NicifyVariableName(type.Name);

        private static SkillNodeMenuAttribute MenuOf(Type type) =>
            (SkillNodeMenuAttribute)Attribute.GetCustomAttribute(type, typeof(SkillNodeMenuAttribute), false);

        /// "Units/Unlock" from [SkillNodeMenu], else the nicified class name. Slashes make submenus.
        public static string MenuPath(Type type) => MenuOf(type)?.Path ?? Pretty(type);

        /// "Units › Unlock": the type as one line, for headers.
        public static string TypeLabel(Type type) => MenuPath(type).Replace("/", " › ");

        public static string TypeTooltip(Type type) => MenuOf(type)?.Tooltip;

        private static void Mark(SkillTree tree, params Object[] changed)
        {
            foreach (var o in changed) if (o != null) EditorUtility.SetDirty(o);
            EditorUtility.SetDirty(tree);
            // A style stays only on a pair that is still linked (a deleted node, a removed link: the entry goes too).
            tree.Edges.RemoveAll(e => e == null || e.A == null || e.B == null || e.Style == null || !AreLinked(e.A, e.B));
            tree.Invalidate();
        }

        /// The sub-asset's name follows its display name, so the asset file reads well.
        public static void SyncName(Object o)
        {
            string want = o switch
            {
                SkillNode node => node.DisplayName,
                SkillGroup group => group.DisplayName,
                SkillStat stat => stat.DisplayName,
                SkillCurrency currency => currency.DisplayName,
                SkillLinkStyle style => style.DisplayName,
                _ => o.name
            };
            if (string.IsNullOrWhiteSpace(want)) want = "Unnamed";
            if (o.name != want) o.name = want;
        }

        // ---------------------------------------------------------------- trees

        public static SkillTree CreateTree(Type type, string path)
        {
            var tree = (SkillTree)ScriptableObject.CreateInstance(type);
            tree.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(tree, path);
            // With one node type the root can be made right away; with several the first skill added becomes the root.
            if (NodeTypes.Count == 1)
            {
                var root = AddNode(tree, NodeTypes[0], Vector2.zero);
                root.DisplayName = "Root";
                SyncName(root);
            }
            AssetDatabase.SaveAssets();
            return tree;
        }

        // ---------------------------------------------------------------- nodes

        private static SkillNode AddNode(SkillTree tree, Type type, Vector2 position)
        {
            bool first = !tree.Nodes.Any(n => n != null && n.IsRoot);
            var node = (SkillNode)ScriptableObject.CreateInstance(type);
            node.hideFlags = HideFlags.HideInHierarchy;
            node.Id = tree.TakeId();
            node.Position = position;
            node.IsRoot = first;
            SyncName(node);
            AssetDatabase.AddObjectToAsset(node, tree);
            tree.Nodes.Add(node);
            Mark(tree, node);
            return node;
        }

        public static SkillNode CreateNode(SkillTree tree, Type type, Vector2 position)
        {
            Undo.RecordObject(tree, "Create Skill");
            var node = AddNode(tree, type, position);
            Undo.RegisterCreatedObjectUndo(node, "Create Skill");
            return node;
        }

        /// Copies keep their fields and the links among themselves. A single copy also keeps the original's outside
        /// links and requirements, so it sits in the same place of the tree.
        public static List<SkillNode> Duplicate(SkillTree tree, IEnumerable<SkillNode> nodes, Vector2 offset)
        {
            var originals = nodes.Where(n => n != null).Distinct().ToList();
            var map = new Dictionary<SkillNode, SkillNode>();
            if (originals.Count == 0) return new List<SkillNode>();

            // Links in both directions (a link is stored on one side only), taken before the copies join the tree.
            tree.Invalidate();
            var links = originals.ToDictionary(o => o, o => tree.LinksOf(o).ToList());

            int group = BeginGroup("Duplicate Skills");
            Undo.RecordObject(tree, "Duplicate Skills");
            foreach (var original in originals)
            {
                var copy = Object.Instantiate(original);
                copy.hideFlags = HideFlags.HideInHierarchy;
                copy.Id = tree.TakeId();
                copy.IsRoot = false;
                copy.Position = original.Position + offset;
                SyncName(copy);
                AssetDatabase.AddObjectToAsset(copy, tree);
                Undo.RegisterCreatedObjectUndo(copy, "Duplicate Skills");
                tree.Nodes.Add(copy);
                map[original] = copy;
            }
            bool single = originals.Count == 1;
            foreach (var (original, copy) in map)
            {
                copy.Neighbors = links[original].Where(n => n != null && (single || map.ContainsKey(n)))
                    .Select(n => map.TryGetValue(n, out var c) ? c : n).ToList();
                copy.Requires = original.Requires.Where(n => n != null && (single || map.ContainsKey(n)))
                    .Select(n => map.TryGetValue(n, out var c) ? c : n).ToList();
            }
            Undo.CollapseUndoOperations(group);
            Mark(tree, map.Values.Cast<Object>().ToArray());
            return map.Values.ToList();
        }

        public static void DeleteNodes(SkillTree tree, IEnumerable<SkillNode> nodes)
        {
            var doomed = new HashSet<SkillNode>(nodes.Where(n => n != null));
            if (doomed.Count == 0) return;

            int group = BeginGroup(doomed.Count == 1 ? "Delete Skill" : "Delete Skills");
            Undo.RecordObject(tree, "Delete Skills");
            tree.ReserveIds(); // their Ids stay taken: saves that own them must not pass to a new node
            foreach (var other in tree.Nodes)
            {
                if (other == null || doomed.Contains(other)) continue;
                if (!other.Neighbors.Any(doomed.Contains) && !other.Requires.Any(doomed.Contains)) continue;
                Undo.RecordObject(other, "Delete Skills");
                other.Neighbors.RemoveAll(doomed.Contains);
                other.Requires.RemoveAll(doomed.Contains);
                EditorUtility.SetDirty(other);
            }
            tree.Nodes.RemoveAll(n => n == null || doomed.Contains(n));
            foreach (var node in doomed) Undo.DestroyObjectImmediate(node);
            Undo.CollapseUndoOperations(group);
            Mark(tree);
        }

        /// The two nodes trade places in the tree: position, links, requirements, link styles and the root mark move
        /// with the place, so the drawn tree looks exactly the same. What stays with each node: Id (the save key),
        /// name, effects, cost and every other field.
        public static void SwapPlaces(SkillTree tree, SkillNode a, SkillNode b)
        {
            if (a == null || b == null || a == b) return;
            SkillNode Other(SkillNode n) => n == a ? b : n == b ? a : n;
            bool Touches(SkillNode n) => n == a || n == b;

            var nodes = tree.Nodes.Where(n => n != null).ToList();
            var touched = nodes.Where(n => Touches(n) || n.Neighbors.Any(Touches) || n.Requires.Any(Touches)).Cast<Object>().ToList();
            touched.Add(tree);
            Undo.RecordObjects(touched.ToArray(), "Swap Skills");

            // Every link and requirement that touches a or b, taken before they are rewritten.
            var links = new List<(SkillNode x, SkillNode y)>();
            var needs = new List<(SkillNode node, SkillNode need)>();
            foreach (var n in nodes)
            {
                links.AddRange(n.Neighbors.Where(o => o != null && (Touches(n) || Touches(o))).Select(o => (n, o)));
                needs.AddRange(n.Requires.Where(o => o != null && (Touches(n) || Touches(o))).Select(o => (n, o)));
                if (Touches(n))
                {
                    n.Neighbors.Clear();
                    n.Requires.Clear();
                }
                else
                {
                    n.Neighbors.RemoveAll(Touches);
                    n.Requires.RemoveAll(Touches);
                }
            }
            // The same pairs with a and b exchanged.
            foreach (var (x, y) in links)
                if (!AreLinked(Other(x), Other(y))) Other(x).Neighbors.Add(Other(y));
            foreach (var (node, need) in needs)
                if (!Other(node).Requires.Contains(Other(need))) Other(node).Requires.Add(Other(need));
            foreach (var edge in tree.Edges)
                if (edge != null)
                {
                    edge.A = Other(edge.A);
                    edge.B = Other(edge.B);
                }

            (a.Position, b.Position) = (b.Position, a.Position);
            (a.IsRoot, b.IsRoot) = (b.IsRoot, a.IsRoot);
            Mark(tree, touched.ToArray());
        }

        public static void SetRoot(SkillTree tree, SkillNode root)
        {
            var nodes = tree.Nodes.Where(n => n != null).ToArray();
            Undo.RecordObjects(nodes.Cast<Object>().ToArray(), "Set Root");
            foreach (var node in nodes) node.IsRoot = node == root;
            Mark(tree, nodes.Cast<Object>().ToArray());
        }

        public static void SetGroup(SkillTree tree, IReadOnlyCollection<SkillNode> nodes, SkillGroup group)
        {
            Undo.RecordObjects(nodes.Cast<Object>().ToArray(), "Set Group");
            foreach (var node in nodes) node.Group = group;
            Mark(tree, nodes.Cast<Object>().ToArray());
        }

        /// Near a node, outward from the root, on a free spot.
        public static Vector2 PlaceNear(SkillTree tree, SkillNode from, float distance)
        {
            var root = tree.Root;
            var dir = root != null && root != from ? from.Position - root.Position : Vector2.up;
            dir = dir.sqrMagnitude < 1f ? Vector2.up : dir.normalized;
            for (int i = 0; i < 14; i++)
            {
                float angle = 22f * ((i + 1) / 2) * (i % 2 == 0 ? 1f : -1f);
                var pos = from.Position + (Vector2)(Quaternion.Euler(0f, 0f, angle) * dir) * distance;
                if (!tree.Nodes.Any(n => n != null && (n.Position - pos).sqrMagnitude < 70f * 70f)) return pos;
            }
            return from.Position + dir * distance;
        }

        // ---------------------------------------------------------------- connections

        public static bool AreLinked(SkillNode a, SkillNode b) => a.Neighbors.Contains(b) || b.Neighbors.Contains(a);

        public static bool Requires(SkillNode node, SkillNode need) => node.Requires.Contains(need);

        public static bool Link(SkillTree tree, SkillNode a, SkillNode b)
        {
            if (a == null || b == null || a == b || AreLinked(a, b)) return false;
            Undo.RecordObject(a, "Connect Skills");
            a.Neighbors.Add(b);
            Mark(tree, a);
            return true;
        }

        /// need must be owned before node. Also links them, and drops the opposite requirement (it would be a loop).
        public static bool SetRequirement(SkillTree tree, SkillNode node, SkillNode need)
        {
            if (node == null || need == null || node == need || (Requires(node, need) && AreLinked(node, need))) return false;
            Undo.RecordObjects(new Object[] { node, need }, "Add Requirement");
            need.Requires.RemoveAll(x => x == node);
            if (!node.Requires.Contains(need)) node.Requires.Add(need);
            if (!AreLinked(node, need)) node.Neighbors.Add(need);
            Mark(tree, node, need);
            return true;
        }

        /// Removes everything between the two: links and requirements, both ways.
        public static void Disconnect(SkillTree tree, SkillNode a, SkillNode b)
        {
            Undo.RecordObjects(new Object[] { a, b }, "Disconnect Skills");
            a.Neighbors.RemoveAll(x => x == b);
            b.Neighbors.RemoveAll(x => x == a);
            a.Requires.RemoveAll(x => x == b);
            b.Requires.RemoveAll(x => x == a);
            Mark(tree, a, b);
        }

        /// Keeps the link, drops the requirements between the two.
        public static void MakePlainLink(SkillTree tree, SkillNode a, SkillNode b)
        {
            Undo.RecordObjects(new Object[] { a, b }, "Make Plain Link");
            a.Requires.RemoveAll(x => x == b);
            b.Requires.RemoveAll(x => x == a);
            if (!AreLinked(a, b)) a.Neighbors.Add(b);
            Mark(tree, a, b);
        }

        /// Keeps the requirements, drops the link (the node then opens from its other neighbours).
        public static void RemoveLinkOnly(SkillTree tree, SkillNode a, SkillNode b)
        {
            Undo.RecordObjects(new Object[] { a, b }, "Remove Link");
            a.Neighbors.RemoveAll(x => x == b);
            b.Neighbors.RemoveAll(x => x == a);
            Mark(tree, a, b);
        }

        public static void DisconnectAll(SkillTree tree, IReadOnlyCollection<SkillNode> nodes)
        {
            var set = new HashSet<SkillNode>(nodes);
            var touched = tree.Nodes.Where(n => n != null && (set.Contains(n) || n.Neighbors.Any(set.Contains) || n.Requires.Any(set.Contains)))
                .ToArray();
            Undo.RecordObjects(touched.Cast<Object>().ToArray(), "Disconnect Skills");
            foreach (var node in touched)
            {
                if (set.Contains(node))
                {
                    node.Neighbors.Clear();
                    node.Requires.Clear();
                }
                else
                {
                    node.Neighbors.RemoveAll(set.Contains);
                    node.Requires.RemoveAll(set.Contains);
                }
            }
            Mark(tree, touched.Cast<Object>().ToArray());
        }

        // ---------------------------------------------------------------- stats

        public static SkillStat CreateStat(SkillTree tree, string key = null, string displayName = null,
            SkillStatKind kind = SkillStatKind.Percent, string unit = "")
        {
            var stat = ScriptableObject.CreateInstance<SkillStat>();
            stat.hideFlags = HideFlags.HideInHierarchy;
            displayName ??= UniqueStatName(tree);
            stat.DisplayName = displayName;
            stat.Key = key ?? displayName.Replace(" ", "");
            stat.Kind = kind;
            stat.Unit = unit ?? "";
            SyncName(stat);
            Undo.RecordObject(tree, "Create Stat");
            AssetDatabase.AddObjectToAsset(stat, tree);
            Undo.RegisterCreatedObjectUndo(stat, "Create Stat");
            tree.Stats.Add(stat);
            Mark(tree, stat);
            return stat;
        }

        private static string UniqueStatName(SkillTree tree)
        {
            for (int i = 1; ; i++)
            {
                string name = i == 1 ? "New Stat" : $"New Stat {i}";
                if (!tree.Stats.Any(s => s != null && s.DisplayName == name)) return name;
            }
        }

        /// Removes the stat; effects that used it are left with no stat (validation points them out).
        public static void DeleteStat(SkillTree tree, SkillStat stat)
        {
            int undo = BeginGroup("Delete Stat");
            Undo.RecordObject(tree, "Delete Stat");
            foreach (var node in UsersOf(tree, stat))
            {
                Undo.RecordObject(node, "Delete Stat");
                foreach (var effect in node.GetEffects())
                    if (effect != null && effect.Stat == stat) effect.Stat = null;
                EditorUtility.SetDirty(node);
            }
            tree.Stats.Remove(stat);
            Undo.DestroyObjectImmediate(stat);
            Undo.CollapseUndoOperations(undo);
            Mark(tree);
        }

        public static List<SkillNode> UsersOf(SkillTree tree, SkillStat stat) =>
            tree.Nodes.Where(n => n != null && n.GetEffects().Any(e => e != null && e.Stat == stat)).ToList();

        // ---------------------------------------------------------------- currencies

        public static SkillCurrency CreateCurrency(SkillTree tree)
        {
            var currency = ScriptableObject.CreateInstance<SkillCurrency>();
            currency.hideFlags = HideFlags.HideInHierarchy;
            for (int i = 1; ; i++)
            {
                string name = i == 1 ? "New Currency" : $"New Currency {i}";
                if (tree.Currencies.Any(c => c != null && c.DisplayName == name)) continue;
                currency.DisplayName = name;
                break;
            }
            currency.Key = currency.DisplayName.Replace(" ", "");
            SyncName(currency);
            Undo.RecordObject(tree, "Create Currency");
            AssetDatabase.AddObjectToAsset(currency, tree);
            Undo.RegisterCreatedObjectUndo(currency, "Create Currency");
            tree.Currencies.Add(currency);
            Mark(tree, currency);
            return currency;
        }

        /// Removes the currency; costs that used it are left with no currency (validation points them out).
        public static void DeleteCurrency(SkillTree tree, SkillCurrency currency)
        {
            int undo = BeginGroup("Delete Currency");
            Undo.RecordObject(tree, "Delete Currency");
            foreach (var node in UsersOf(tree, currency))
            {
                Undo.RecordObject(node, "Delete Currency");
                for (int i = 0; i < node.Cost.Count; i++)
                    if (node.Cost[i].Currency == currency) node.Cost[i] = new SkillCost { Amount = node.Cost[i].Amount };
                EditorUtility.SetDirty(node);
            }
            tree.Currencies.Remove(currency);
            Undo.DestroyObjectImmediate(currency);
            Undo.CollapseUndoOperations(undo);
            Mark(tree);
        }

        public static List<SkillNode> UsersOf(SkillTree tree, SkillCurrency currency) =>
            tree.Nodes.Where(n => n != null && n.Cost != null && n.Cost.Any(c => c.Currency == currency)).ToList();

        // ---------------------------------------------------------------- link styles

        public static SkillLinkStyle CreateLinkStyle(SkillTree tree)
        {
            var style = ScriptableObject.CreateInstance<SkillLinkStyle>();
            style.hideFlags = HideFlags.HideInHierarchy;
            style.DisplayName = $"Link Style {tree.LinkStyles.Count + 1}";
            SyncName(style);
            Undo.RecordObject(tree, "Create Link Style");
            AssetDatabase.AddObjectToAsset(style, tree);
            Undo.RegisterCreatedObjectUndo(style, "Create Link Style");
            tree.LinkStyles.Add(style);
            Mark(tree, style);
            return style;
        }

        /// Removes the style; its links go back to the panel's link prefab as it is.
        public static void DeleteLinkStyle(SkillTree tree, SkillLinkStyle style)
        {
            int undo = BeginGroup("Delete Link Style");
            Undo.RecordObject(tree, "Delete Link Style");
            tree.Edges.RemoveAll(e => e == null || e.Style == style);
            tree.LinkStyles.Remove(style);
            Undo.DestroyObjectImmediate(style);
            Undo.CollapseUndoOperations(undo);
            Mark(tree);
        }

        /// Gives the link between a and b this style (null = back to the panel's own look).
        public static void SetLinkStyle(SkillTree tree, SkillNode a, SkillNode b, SkillLinkStyle style)
        {
            Undo.RecordObject(tree, "Set Link Style");
            tree.Edges.RemoveAll(e => e != null && ((e.A == a && e.B == b) || (e.A == b && e.B == a)));
            if (style != null) tree.Edges.Add(new SkillEdge { A = a, B = b, Style = style });
            Mark(tree);
        }

        /// Every link of the tree uses this style.
        public static void SetAllLinkStyles(SkillTree tree, SkillLinkStyle style)
        {
            Undo.RecordObject(tree, "Apply Link Style To All");
            tree.Edges.Clear();
            foreach (var node in tree.Nodes)
            {
                if (node == null) continue;
                foreach (var other in tree.LinksOf(node))
                    if (other != null && node.Id < other.Id) tree.Edges.Add(new SkillEdge { A = node, B = other, Style = style });
            }
            Mark(tree);
        }

        public static int UsesOf(SkillTree tree, SkillLinkStyle style) => tree.Edges.Count(e => e != null && e.Style == style);

        // ---------------------------------------------------------------- groups

        public static SkillGroup CreateGroup(SkillTree tree)
        {
            var group = ScriptableObject.CreateInstance<SkillGroup>();
            group.hideFlags = HideFlags.HideInHierarchy;
            group.DisplayName = $"Group {tree.Groups.Count + 1}";
            group.Color = Palette[tree.Groups.Count % Palette.Length];
            SyncName(group);
            Undo.RecordObject(tree, "Create Group");
            AssetDatabase.AddObjectToAsset(group, tree);
            Undo.RegisterCreatedObjectUndo(group, "Create Group");
            tree.Groups.Add(group);
            Mark(tree, group);
            return group;
        }

        public static void DeleteGroup(SkillTree tree, SkillGroup group)
        {
            int undo = BeginGroup("Delete Group");
            Undo.RecordObject(tree, "Delete Group");
            foreach (var node in tree.Nodes.Where(n => n != null && n.Group == group))
            {
                Undo.RecordObject(node, "Delete Group");
                node.Group = null;
                EditorUtility.SetDirty(node);
            }
            tree.Groups.Remove(group);
            Undo.DestroyObjectImmediate(group);
            Undo.CollapseUndoOperations(undo);
            Mark(tree);
        }

        private static int BeginGroup(string name)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(name);
            return Undo.GetCurrentGroup();
        }
    }
}
