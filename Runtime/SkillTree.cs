using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HeatInteractive.SkillTreeGenerator
{
    /// A skill tree: its nodes, groups and stats (all stored inside this asset), the buy rules, and the stat sums.
    /// Buy rules: the root is always open; any other node opens once a neighbour is owned; then every Requires must be
    /// owned, and the node's own LockReason must be null.
    /// Reading stats from game code: var dmg = tree.Stat("Damage"); damage = tree.Get(dmg, baseDamage, ranks).
    /// Derive from it to add tree-wide data (currencies...) or to reword the lock messages.
    [CreateAssetMenu(fileName = "SkillTree", menuName = "Skill Tree Generator/Skill Tree", order = 0)]
    public class SkillTree : ScriptableObject
    {
        // Edited in the Skill Tree Editor, not as raw lists.
        [HideInInspector] public List<SkillNode> Nodes = new();
        [HideInInspector] public List<SkillGroup> Groups = new();
        [HideInInspector] public List<SkillStat> Stats = new();
        [Tooltip("The next new node's Id. Only grows, so a deleted node's save key is never handed out again.")]
        [HideInInspector] public int NextId = 1;
        [HideInInspector] public SkillTreeLayout Layout = new();
        [Tooltip("Key of a stat that scales every price (e.g. a percent stat at -10% = cheaper skills). Empty = none.")]
        public string PriceStat = "";

        [NonSerialized] private bool _built;
        [NonSerialized] private SkillNode _root;
        [NonSerialized] private readonly Dictionary<int, SkillNode> _byId = new();
        [NonSerialized] private readonly Dictionary<SkillNode, List<SkillNode>> _links = new();
        [NonSerialized] private readonly Dictionary<Type, object> _byType = new();
        [NonSerialized] private readonly Dictionary<SkillStat, List<(SkillNode node, SkillEffect effect)>> _byStat = new();
        [NonSerialized] private readonly Dictionary<string, SkillStat> _statByKey = new(StringComparer.OrdinalIgnoreCase);
        private static readonly List<SkillNode> NoLinks = new();

        /// Percent stats never multiply below this (a -150% cooldown would be nonsense).
        public virtual float MinMultiplier => 0.05f;

        /// The node the tree starts from (IsRoot). Null if none is marked.
        public SkillNode Root
        {
            get
            {
                Build();
                return _root;
            }
        }

        public SkillNode Find(int id)
        {
            Build();
            return _byId.TryGetValue(id, out var node) ? node : null;
        }

        /// Every node of type T (subclasses included), cached.
        public IReadOnlyList<T> NodesOf<T>() where T : SkillNode
        {
            Build();
            if (_byType.TryGetValue(typeof(T), out var cached)) return (List<T>)cached;
            var list = new List<T>();
            foreach (var node in Nodes)
                if (node is T typed) list.Add(typed);
            _byType[typeof(T)] = list;
            return list;
        }

        /// Neighbours in both directions: a link set on either side counts.
        public IReadOnlyList<SkillNode> LinksOf(SkillNode node)
        {
            Build();
            return node != null && _links.TryGetValue(node, out var list) ? list : NoLinks;
        }

        /// The stat with this Key (case doesn't matter), or null.
        public SkillStat Stat(string key)
        {
            Build();
            return key != null && _statByKey.TryGetValue(key, out var stat) ? stat : null;
        }

        /// Drops the caches. Call it after changing nodes, links, effects or stats from code (the editor does).
        public void Invalidate()
        {
            _built = false;
            _byType.Clear();
        }

        protected virtual void OnEnable() => Invalidate();
        protected virtual void OnValidate() => Invalidate();

        private void Build()
        {
            if (_built) return;
            _built = true;
            _root = null;
            _byId.Clear();
            _links.Clear();
            _byStat.Clear();
            _statByKey.Clear();

            foreach (var stat in Stats)
                if (stat != null && !string.IsNullOrEmpty(stat.Key)) _statByKey.TryAdd(stat.Key, stat);

            foreach (var node in Nodes)
            {
                if (node == null) continue;
                if (node.IsRoot && _root == null) _root = node;
                _byId.TryAdd(node.Id, node);
                Link(node, null);
                if (node.Neighbors != null)
                    foreach (var other in node.Neighbors)
                    {
                        if (other == null || other == node) continue;
                        Link(node, other);
                        Link(other, node);
                    }

                foreach (var effect in node.GetEffects())
                {
                    if (effect?.Stat == null) continue;
                    if (!_byStat.TryGetValue(effect.Stat, out var list)) _byStat[effect.Stat] = list = new();
                    list.Add((node, effect));
                }
            }
        }

        private void Link(SkillNode from, SkillNode to)
        {
            if (!_links.TryGetValue(from, out var list)) _links[from] = list = new List<SkillNode>();
            if (to != null && !list.Contains(to)) list.Add(to);
        }

        // ---------------------------------------------------------------- stats

        /// The stat's effects summed over the owned ranks.
        public float Sum(SkillStat stat, ISkillRanks ranks)
        {
            if (stat == null || ranks == null) return 0f;
            Build();
            if (!_byStat.TryGetValue(stat, out var list)) return 0f;
            float total = 0f;
            foreach (var (node, effect) in list)
            {
                int count = ranks.GetRank(node) - Mathf.Max(1, effect.StartRank) + 1;
                if (count <= 0) continue;
                total += effect.PerRank * count;
            }
            return total;
        }

        /// The stat's value, by its kind: a percent stat gives a multiplier (+30% -> 1.3, 1 without skills, never below
        /// MinMultiplier), a flat stat the amount to add (0 without skills). No stat: 0.
        public float Get(SkillStat stat, ISkillRanks ranks)
        {
            if (stat == null) return 0f;
            float sum = Sum(stat, ranks);
            return stat.Kind == SkillStatKind.Percent ? Mathf.Max(MinMultiplier, 1f + sum / 100f) : sum;
        }

        /// The base value with the stat applied, by its kind: percent multiplies, flat adds. No stat: the base value.
        public float Get(SkillStat stat, float baseValue, ISkillRanks ranks)
        {
            if (stat == null) return baseValue;
            float value = Get(stat, ranks);
            return stat.Kind == SkillStatKind.Percent ? baseValue * value : baseValue + value;
        }

        // ---------------------------------------------------------------- rules

        /// The root, or a node with an owned neighbour.
        public bool IsReachable(SkillNode node, ISkillRanks ranks)
        {
            if (node == null) return false;
            if (node.IsRoot) return true;
            foreach (var other in LinksOf(node))
                if (ranks.IsOwned(other)) return true;
            return false;
        }

        /// What holds a node back even when it is reachable: a missing Requires, then the node's own LockReason.
        public string LockReason(SkillNode node, ISkillRanks ranks)
        {
            if (node == null) return null;
            if (node.Requires != null)
                foreach (var need in node.Requires)
                    if (need != null && !ranks.IsOwned(need)) return RequirementText(need);
            return node.LockReason(this, ranks);
        }

        /// Reachable, nothing holds it back, and not at its max rank yet. Costs are the game's business.
        public bool CanRankUp(SkillNode node, ISkillRanks ranks) =>
            node != null && ranks.GetRank(node) < node.MaxRank && IsReachable(node, ranks) && LockReason(node, ranks) == null;

        /// Why a node can't be bought right now, null = it can (max rank isn't a lock).
        public string WhyLocked(SkillNode node, ISkillRanks ranks) =>
            IsReachable(node, ranks) ? LockReason(node, ranks) : UnreachableText(node);

        public virtual string RequirementText(SkillNode need) => $"Requires \"{need.DisplayName}\" first.";
        public virtual string UnreachableText(SkillNode node) => "Own a connected skill first.";
        public virtual string MaxRankText(SkillNode node) => "Already at max rank.";
        public virtual string BlockedText(SkillNode node) => "Can't be bought right now.";
        public virtual string CantAffordText(SkillNode node) => "Not enough to pay for it.";

        /// Tree-wide checks of your own, shown with the built-in validation.
        public virtual IEnumerable<SkillIssue> ValidateTree()
        {
            yield break;
        }

#if UNITY_EDITOR
        /// A save key no node has ever had. Editor only: the editor gives every new node one.
        public int TakeId()
        {
            ReserveIds();
            return NextId++;
        }

        /// Moves NextId past every Id in the tree.
        public void ReserveIds()
        {
            foreach (var node in Nodes)
                if (node != null && node.Id >= NextId) NextId = node.Id + 1;
            if (NextId < 1) NextId = 1;
        }
#endif
    }

    /// Editor canvas settings, saved with the tree so everyone sees the same grid.
    [Serializable]
    public class SkillTreeLayout
    {
        [Min(1f)] public float GridSize = 20f;
        public bool Snap = true;
        public bool ShowRings = true;
        [Min(10f)] public float RingSpacing = 240f;
    }
}
