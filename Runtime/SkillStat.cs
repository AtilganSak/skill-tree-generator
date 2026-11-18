using UnityEngine;

namespace HeatInteractive.SkillTreeGenerator
{
    public enum SkillStatKind
    {
        [Tooltip("10 per rank = +10%. GetStat gives a multiplier (1.1) when the base is 1, or multiplies the base value you pass.")]
        Percent,
        [Tooltip("The number itself is added: +1 pierce, +0.5 s. GetStat gives the amount, or adds it to the base value you pass.")]
        Flat,
        [Tooltip("On or off, e.g. \"can build turret 2\". It is on once an effect of it is active (the skill reached its Start Rank). Read it with GetStat<bool>.")]
        Flag
    }

    /// Something skills can raise: damage, fire rate, max health... Defined per tree in the Skill Tree Editor (Stats), no
    /// code needed. Game code reads it by Key: tree.Stat("Damage"), then tree.GetStat(stat, baseValue, ranks).
    public class SkillStat : ScriptableObject
    {
        [Tooltip("What code asks for: tree.Stat(\"Key\"). Keep it stable once code uses it.")]
        public string Key = "";
        public string DisplayName = "New Stat";
        [Tooltip("Percent: 10 per rank = +10%. Flat: the number is added as it is. Flag: on or off (bool).")]
        public SkillStatKind Kind = SkillStatKind.Percent;
        [Tooltip("Shown after flat amounts, e.g. \"s\" or \"pcs\".")]
        public string Unit = "";
        [TextArea(2, 4)] public string Description = "";

        /// "+10%" or "+1 s"; empty for a flag.
        public string Format(float perRank) => Kind == SkillStatKind.Flag ? ""
            : Kind == SkillStatKind.Percent
            ? perRank.ToString("+0.#;-0.#") + "%"
            : perRank.ToString("+0.##;-0.##") + (string.IsNullOrEmpty(Unit) ? "" : " " + Unit);
    }
}
