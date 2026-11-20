using UnityEngine;

namespace HeatInteractive.SkillTreeGenerator
{
    /// How a link line looks: a preset shared by any number of links (change it once, every link using it follows).
    /// Defined per tree in the Skill Tree Editor: pick a link, then its Look section. Stored inside its tree asset.
    public class SkillLinkStyle : ScriptableObject
    {
        public string DisplayName = "Link Style";
        [Min(0.5f), Tooltip("The line's thickness on the panel (the link prefab's height).")]
        public float Width = 5f;
        [Tooltip("While the link isn't active (not both ends owned).")]
        public Color OffColor = new(1f, 1f, 1f, 0.18f);
        [Tooltip("While both ends are owned.")]
        public Color OnColor = new(1f, 1f, 1f, 0.85f);
    }
}
