using System.Collections.Generic;
using UnityEngine;

namespace HeatInteractive.SkillTreeGenerator
{
    /// The ready-made skill: stat effects per rank and the assets it unlocks. Enough for a whole tree without code;
    /// derive from it to add your own fields (a cost...).
    [SkillNodeMenu("Skill", "Stat effects per rank, and the assets it unlocks.")]
    public class EffectSkill : SkillNode
    {
        [Tooltip("Assets this skill opens (a unit, an ability...). Until it is owned they stay locked, and skills whose effects target them wait.")]
        public List<Object> Unlocks = new();
        public List<SkillEffect> Effects = new();

        public override IEnumerable<SkillEffect> GetEffects() => Effects;
        public override IEnumerable<Object> GetUnlocks() => Unlocks;
    }
}
