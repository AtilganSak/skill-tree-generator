using UnityEngine;

namespace HeatInteractive.SkillTreeGenerator
{
    public enum SkillStatKind
    {
        [Tooltip("10 per rank = +10%. tree.Get gives a multiplier (1.1), or multiplies the base value you pass.")]
        Percent,
        [Tooltip("The number itself is added: +1 pierce, +0.5 s. tree.Get gives the amount, or adds it to the base value you pass.")]
        Flat
    }

    /// Something skills can raise: damage, fire rate, max health... Defined per tree in the Skill Tree Editor (Stats), no
    /// code needed. Game code reads it by Key: tree.Stat("Damage"), then tree.Get(stat, baseValue, ranks).
    public class SkillStat : ScriptableObject
    {
        [Tooltip("What code asks for: tree.Stat(\"Key\"). Keep it stable once code uses it.")]
        public string Key = "";
        public string DisplayName = "New Stat";
        [Tooltip("Percent: 10 per rank = +10%. Flat: the number is added as it is.")]
        public SkillStatKind Kind = SkillStatKind.Percent;
        [Tooltip("Shown after flat amounts, e.g. \"s\" or \"pcs\".")]
        public string Unit = "";
        [TextArea(2, 4)] public string Description = "";

        /// "+10%" or "+1 s".
        public string Format(float perRank) => Kind == SkillStatKind.Percent
            ? perRank.ToString("+0.#;-0.#") + "%"
            : perRank.ToString("+0.##;-0.##") + (string.IsNullOrEmpty(Unit) ? "" : " " + Unit);
    }
}
