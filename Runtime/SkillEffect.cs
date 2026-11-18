using System;
using UnityEngine;

namespace HeatInteractive.SkillTreeGenerator
{
    /// One thing a skill adds per rank: PerRank of a stat.
    [Serializable]
    public class SkillEffect
    {
        public SkillStat Stat;
        [Tooltip("Per rank. Percent stats: 10 = +10%. Flat stats: the amount itself. Negative lowers it.")]
        public float PerRank = 10f;
        [Min(1), Tooltip("The effect starts at this rank (e.g. 2 = rank 1 does nothing for this effect).")]
        public int StartRank = 1;
    }
}
