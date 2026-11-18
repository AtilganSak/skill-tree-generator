using System.Collections.Generic;
using System.Linq;

namespace HeatInteractive.SkillTreeGenerator
{
    /// The built-in checks plus every node's own Validate. Pure: nothing is changed, so it runs anywhere (editor, tests).
    public static class SkillTreeValidation
    {
        private const float OverlapDistance = 30f;

        public static List<SkillIssue> Run(SkillTree tree)
        {
            var issues = new List<SkillIssue>();
            if (tree == null) return issues;
            tree.Invalidate();

            if (tree.Nodes.Any(n => n == null)) issues.Add(SkillIssue.Error(null, "The Nodes list has an empty slot."));
            var nodes = tree.Nodes.Where(n => n != null).ToList();
            if (nodes.Count == 0) return issues;
            var inTree = new HashSet<SkillNode>(nodes);

            var roots = nodes.Where(n => n.IsRoot).ToList();
            if (roots.Count == 0) issues.Add(SkillIssue.Error(null, "No root node. Right-click a node > Set as Root."));
            foreach (var extra in roots.Skip(1)) issues.Add(SkillIssue.Error(extra, "A second root. Only one node can be the root."));

            foreach (var group in nodes.GroupBy(n => n.Id).Where(g => g.Count() > 1))
                foreach (var node in group)
                    issues.Add(SkillIssue.Error(node, $"Shares Id {node.Id} (the save key) with another node."));

            foreach (var node in nodes)
            {
                if (string.IsNullOrWhiteSpace(node.DisplayName)) issues.Add(SkillIssue.Warning(node, "Has no name."));
                if (node.Neighbors != null && node.Neighbors.Any(o => o == null || !inTree.Contains(o)))
                    issues.Add(SkillIssue.Error(node, "A link points to a node that isn't in this tree."));
                if (node.Requires != null && node.Requires.Any(o => o == null || !inTree.Contains(o)))
                    issues.Add(SkillIssue.Error(node, "A requirement points to a node that isn't in this tree."));
            }

            if (roots.Count > 0)
            {
                var seen = new HashSet<SkillNode> { roots[0] };
                var queue = new Queue<SkillNode>(seen);
                while (queue.Count > 0)
                    foreach (var other in tree.LinksOf(queue.Dequeue()))
                        if (inTree.Contains(other) && seen.Add(other)) queue.Enqueue(other);
                foreach (var node in nodes.Where(n => !seen.Contains(n)))
                    issues.Add(SkillIssue.Error(node, "Not connected to the root: no path of links leads here."));
            }

            foreach (var node in CycleNodes(nodes))
                issues.Add(SkillIssue.Error(node, "Its requirements form a loop, so none of them can be bought."));

            // With everything else owned at max rank nothing should hold a node back; if something does, it never opens.
            var everything = new SkillRanks();
            foreach (var node in nodes) everything.SetRank(node, node.MaxRank);
            foreach (var node in nodes)
            {
                everything.SetRank(node, 0);
                string reason = tree.LockReason(node, everything);
                if (reason != null) issues.Add(SkillIssue.Error(node, $"Can never be bought, even with everything else owned: {reason}"));
                everything.SetRank(node, node.MaxRank);
            }

            for (int i = 0; i < nodes.Count; i++)
            for (int j = i + 1; j < nodes.Count; j++)
                if ((nodes[i].Position - nodes[j].Position).sqrMagnitude < OverlapDistance * OverlapDistance)
                    issues.Add(SkillIssue.Warning(nodes[j], $"Sits on top of \"{nodes[i].DisplayName}\"."));

            CheckStats(tree, issues);
            foreach (var node in nodes) CheckEffects(tree, node, issues);
            foreach (var node in nodes) issues.AddRange(node.Validate(tree));
            issues.AddRange(tree.ValidateTree());
            return issues;
        }

        private static void CheckStats(SkillTree tree, List<SkillIssue> issues)
        {
            var stats = tree.Stats.Where(s => s != null).ToList();
            foreach (var stat in stats.Where(s => string.IsNullOrWhiteSpace(s.Key)))
                issues.Add(SkillIssue.Error(null, $"Stat \"{stat.DisplayName}\" has no Key, so code can't ask for it."));
            foreach (var group in stats.Where(s => !string.IsNullOrWhiteSpace(s.Key)).GroupBy(s => s.Key.ToLowerInvariant()).Where(g => g.Count() > 1))
                issues.Add(SkillIssue.Error(null, $"{group.Count()} stats share the Key \"{group.First().Key}\": code would only ever find the first."));
            foreach (var stat in stats.Where(s => !string.IsNullOrEmpty(s.TargetType) && s.ResolvedTargetType == null))
                issues.Add(SkillIssue.Warning(null, $"Stat \"{stat.DisplayName}\": target type \"{stat.TargetType}\" no longer exists."));
            if (!string.IsNullOrEmpty(tree.PriceStat) && tree.Stat(tree.PriceStat) == null)
                issues.Add(SkillIssue.Warning(null, $"Price Stat \"{tree.PriceStat}\" isn't a stat of this tree, so prices aren't scaled."));
        }

        private static void CheckEffects(SkillTree tree, SkillNode node, List<SkillIssue> issues)
        {
            foreach (var unlock in node.GetUnlocks())
                if (unlock == null) issues.Add(SkillIssue.Warning(node, "An empty slot in Unlocks."));
            if (node.Cost != null && node.Cost.Any(c => c.Currency == null))
                issues.Add(SkillIssue.Warning(node, "A cost has no currency, so it is skipped."));

            foreach (var effect in node.GetEffects())
            {
                if (effect == null) continue;
                if (effect.Stat == null)
                {
                    issues.Add(SkillIssue.Error(node, "An effect has no stat."));
                    continue;
                }
                string stat = effect.Stat.DisplayName;
                if (!tree.Stats.Contains(effect.Stat)) issues.Add(SkillIssue.Error(node, $"Uses stat \"{stat}\" from another tree."));
                if (effect.PerRank == 0f) issues.Add(SkillIssue.Warning(node, $"{stat} adds 0 per rank."));
                if (effect.StartRank > node.MaxRank) issues.Add(SkillIssue.Warning(node, $"{stat} starts at rank {effect.StartRank}, past the max rank."));
                if (!effect.HasTargets) continue;

                foreach (var target in effect.Targets.Where(t => t != null && !effect.Stat.Accepts(t)))
                    issues.Add(SkillIssue.Error(node, $"{stat} only applies to {effect.Stat.ResolvedTargetType?.Name}, and {SkillTargets.NameOf(target)} isn't one."));
                bool anyCanOpen = effect.Targets.Any(t => t != null && (tree.IsGated(t) || SkillTargets.UnlockedByDefault(t)));
                if (!anyCanOpen)
                    issues.Add(SkillIssue.Error(node, $"{stat} targets only things that start locked and no skill unlocks, so it never applies."));
            }
        }

        private static IEnumerable<SkillNode> CycleNodes(List<SkillNode> nodes)
        {
            var state = new Dictionary<SkillNode, int>(); // 1 = on the path, 2 = done
            var inCycle = new HashSet<SkillNode>();
            var path = new List<SkillNode>();

            void Visit(SkillNode node)
            {
                state[node] = 1;
                path.Add(node);
                if (node.Requires != null)
                    foreach (var need in node.Requires)
                    {
                        if (need == null || need == node) continue;
                        if (!state.TryGetValue(need, out int s)) Visit(need);
                        else if (s == 1)
                            for (int i = path.IndexOf(need); i < path.Count; i++) inCycle.Add(path[i]);
                    }
                path.RemoveAt(path.Count - 1);
                state[node] = 2;
            }

            foreach (var node in nodes)
                if (!state.ContainsKey(node)) Visit(node);
            return inCycle;
        }
    }
}
