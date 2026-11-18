using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HeatInteractive.SkillTreeGenerator
{
    /// One thing a skill adds per rank: PerRank of a stat, for some targets (or everywhere).
    [Serializable]
    public class SkillEffect
    {
        public SkillStat Stat;
        [Tooltip("Per rank. Percent stats: 10 = +10%. Flat stats: the amount itself. Negative lowers it.")]
        public float PerRank = 10f;
        [Tooltip("Empty = everywhere. Otherwise only these assets (a unit, an ability...), and the skill waits until one of them is unlocked.")]
        public List<Object> Targets = new();
        [Min(1), Tooltip("The effect starts at this rank (e.g. 2 = rank 1 does nothing for this effect).")]
        public int StartRank = 1;
        [Tooltip("An asset the effect carries, read by code with tree.PayloadOf (e.g. which turret a 'base turret' skill builds). Usually empty.")]
        public Object Payload;

        public bool HasTargets
        {
            get
            {
                if (Targets == null) return false;
                foreach (var t in Targets) if (t != null) return true;
                return false;
            }
        }

        /// No targets: applies everywhere. Targets: only to those.
        public bool AppliesTo(Object target) => !HasTargets || (target != null && Targets.Contains(target));
    }

    /// Lets your assets tell the skill tree their name and whether they start unlocked (both optional).
    public interface ISkillTarget
    {
        string SkillName { get; }
        /// True: usable without any skill, unless some skill in the tree unlocks it (then that skill must be owned).
        bool SkillUnlockedByDefault { get; }
    }

    public static class SkillTargets
    {
        public static string NameOf(Object target) =>
            target == null ? "?" : target is ISkillTarget t && !string.IsNullOrEmpty(t.SkillName) ? t.SkillName : target.name;

        public static bool UnlockedByDefault(Object target) => target is not ISkillTarget t || t.SkillUnlockedByDefault;
    }
}
