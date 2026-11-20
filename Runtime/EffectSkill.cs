using System.Collections.Generic;

namespace HeatInteractive.SkillTreeGenerator
{
    /// The ready-made skill: stat effects per rank. Enough for a whole tree without code;
    /// derive from it to add your own fields (a cost...).
    [SkillNodeMenu("Skill", "Stat effects per rank.")]
    public class EffectSkill : SkillNode
    {
        public List<SkillEffect> Effects = new();

        public override IEnumerable<SkillEffect> GetEffects() => Effects;
    }
}
