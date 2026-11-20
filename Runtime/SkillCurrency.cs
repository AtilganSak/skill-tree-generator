using UnityEngine;

namespace HeatInteractive.SkillTreeGenerator
{
    /// Something skills cost: gold, gems, a crafting material... Defined per tree in the Skill Tree Editor (Currencies),
    /// no code needed. The game's ISkillWallet maps it to its own money by Key.
    public class SkillCurrency : ScriptableObject
    {
        [Tooltip("What your wallet switches on: case \"gold\". Keep it stable once code uses it.")]
        public string Key = "";
        public string DisplayName = "New Currency";
        [Tooltip("Shown next to the amount. Empty = no icon.")]
        public Sprite Icon;
    }
}
