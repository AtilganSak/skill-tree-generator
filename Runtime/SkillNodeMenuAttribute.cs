using System;

namespace AtilganSak.Skills
{
    /// Where a SkillNode type sits in the editor's "Add Skill" menus, e.g. "Units/Unlock". Without it the class name is
    /// used. Tooltip is shown in the inspector header.
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class SkillNodeMenuAttribute : Attribute
    {
        public readonly string Path;
        public readonly string Tooltip;

        public SkillNodeMenuAttribute(string path, string tooltip = null)
        {
            Path = path;
            Tooltip = tooltip;
        }
    }
}
