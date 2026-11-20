using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeatInteractive.SkillTreeGenerator
{
    /// A skill tree: its nodes, groups and stats (all stored inside this asset), the buy rules, and the stat sums.
    /// Buy rules: the root is always open; any other node opens once a neighbour is owned; then every Requires must be
    /// owned, and the node's own LockReason must be null.
    /// Reading stats from game code: var dmg = tree.Stat("Damage"); damage = tree.GetStat(dmg, baseDamage, ranks).
    /// Derive from it to add tree-wide data (currencies...) or to reword the lock messages.
    [CreateAssetMenu(fileName = "SkillTree", menuName = "Skill Tree Generator/Skill Tree", order = 0)]
    public class SkillTree : ScriptableObject
    {
        // Edited in the Skill Tree Editor, not as raw lists.
        [HideInInspector] public List<SkillNode> Nodes = new();
        [HideInInspector] public List<SkillGroup> Groups = new();
        [HideInInspector] public List<SkillStat> Stats = new();
        [HideInInspector] public List<SkillCurrency> Currencies = new();
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

        /// The currency with this Key (case doesn't matter), or null.
        public SkillCurrency Currency(string key)
        {
            if (key == null) return null;
            foreach (var currency in Currencies)
                if (currency != null && string.Equals(currency.Key, key, StringComparison.OrdinalIgnoreCase)) return currency;
            return null;
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

        /// The stat's effects summed over the owned ranks. A flag is 1 when any of its effects is active, else 0.
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
                if (stat.Kind == SkillStatKind.Flag) return 1f;
                total += effect.PerRank * count;
            }
            return total;
        }

        /// The stat with the base value applied, as float, int or bool (nothing else). Percent multiplies the base
        /// (+30% -> x1.3, never below MinMultiplier), flat adds to it (int rounds); pass 1 or 0 to get the bare
        /// multiplier or amount. A flag reads as bool: on when an effect of it is active, else the base. A kind that
        /// doesn't match T warns and gives the base. No stat: the base.
        public T GetStat<T>(SkillStat stat, T baseValue, ISkillRanks ranks)
        {
            if (stat == null) return baseValue;
            bool flag = stat.Kind == SkillStatKind.Flag;
            var toFloat = StatValue<T>.ToFloat;
            if (toFloat == null || flag != StatValue<T>.IsBool) return Mismatch(stat, baseValue);

            float b = toFloat(baseValue);
            float sum = Sum(stat, ranks);
            float value = flag ? (sum > 0f || b > 0f ? 1f : 0f)
                : stat.Kind == SkillStatKind.Percent ? b * Mathf.Max(MinMultiplier, 1f + sum / 100f) : b + sum;
            return StatValue<T>.FromFloat(value);
        }

        private static T Mismatch<T>(SkillStat stat, T baseValue)
        {
            Debug.LogWarning($"Skills: stat \"{stat.Key}\" is a {stat.Kind} stat and can't be read as {typeof(T).Name} (use float or int, bool for flags).");
            return baseValue;
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

    /// float, int and bool as floats (bool = 0/1) and back, for SkillTree.GetStat<T>. Delegates cached per T: no boxing.
    internal static class StatValue<T>
    {
        public static readonly Func<T, float> ToFloat;
        public static readonly Func<float, T> FromFloat;
        public static readonly bool IsBool;

        static StatValue()
        {
            if (typeof(T) == typeof(float))
            {
                ToFloat = (Func<T, float>)(object)(Func<float, float>)(v => v);
                FromFloat = (Func<float, T>)(object)(Func<float, float>)(v => v);
            }
            else if (typeof(T) == typeof(int))
            {
                ToFloat = (Func<T, float>)(object)(Func<int, float>)(v => v);
                FromFloat = (Func<float, T>)(object)(Func<float, int>)Mathf.RoundToInt;
            }
            else if (typeof(T) == typeof(bool))
            {
                IsBool = true;
                ToFloat = (Func<T, float>)(object)(Func<bool, float>)(v => v ? 1f : 0f);
                FromFloat = (Func<float, T>)(object)(Func<float, bool>)(v => v > 0f);
            }
        }
    }
}
