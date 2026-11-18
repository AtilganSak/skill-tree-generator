using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace HeatInteractive.SkillTreeGenerator
{
    /// One node of a SkillTree. EffectSkill is the ready-made one (stat effects); derive from SkillNode or
    /// EffectSkill for your own node types: every serialized field you add shows up in the Skill Tree Editor's
    /// inspector, and the editor's "Add Skill" menus list every non-abstract subclass.
    /// Nodes are stored inside their tree asset (as hidden sub-assets), so one tree = one file.
    public abstract class SkillNode : ScriptableObject
    {
        [Tooltip("The save key. Given by the tree, never reused, never changed.")]
        [HideInInspector] public int Id;

        public string DisplayName = "New Skill";
        [TextArea(2, 6)] public string Description;
        public Sprite Icon;
        public SkillGroup Group;
        [Min(1)] public int MaxRank = 1;
        [Tooltip("Rank 1's price. Empty = free. The currency is any asset the game's ISkillWallet knows (a coin, a loot type...).")]
        public List<SkillCost> Cost = new();
        [Min(1f), Tooltip("Each rank after the first costs this many times the one before.")]
        public float CostGrowth = 1.35f;

        // Edited on the canvas, not in the inspector.
        [HideInInspector] public bool IsRoot;
        [HideInInspector] public Vector2 Position;
        [Tooltip("Two-way: once any neighbour is owned, this node can be bought.")]
        [HideInInspector] public List<SkillNode> Neighbors = new();
        [Tooltip("All of these must be owned before this node can be bought, linked or not.")]
        [HideInInspector] public List<SkillNode> Requires = new();

        /// What the node adds per rank. The tree sums these for tree.Get.
        public virtual IEnumerable<SkillEffect> GetEffects()
        {
            yield break;
        }

        /// Why this node can't be bought, on top of Requires and reachability. Null = nothing.
        public virtual string LockReason(SkillTree tree, ISkillRanks ranks) => null;

        /// One short line of what the node does, drawn under its name in the editor.
        public virtual string Summary
        {
            get
            {
                var sb = new StringBuilder();
                foreach (var effect in GetEffects())
                {
                    if (effect == null) continue;
                    if (sb.Length > 0) sb.Append(" · ");
                    if (effect.Stat == null)
                    {
                        sb.Append("(no stat)");
                        continue;
                    }
                    sb.Append(effect.Stat.DisplayName).Append(' ').Append(effect.Stat.Format(effect.PerRank));
                }
                return sb.Length > 0 ? sb.ToString() : null;
            }
        }

        /// An extra ring color in the editor. Default: none.
        public virtual Color? Accent => null;

        /// Your own checks, run by the editor's validation next to the built-in ones (Ids, links, effects, cycles...).
        public virtual IEnumerable<SkillIssue> Validate(SkillTree tree)
        {
            yield break;
        }
    }
}
