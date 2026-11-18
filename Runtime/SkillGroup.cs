using UnityEngine;

namespace AtilganSak.Skills
{
    /// A named color for a set of nodes (a branch, a sector, a theme). Stored inside its tree asset.
    public class SkillGroup : ScriptableObject
    {
        public string DisplayName = "Group";
        public Color Color = new(0.4f, 0.7f, 1f);
    }
}
