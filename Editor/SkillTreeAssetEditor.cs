using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace AtilganSak.Skills.Editing
{
    /// The tree asset's own inspector: a button into the Skill Tree Editor, then its regular fields.
    [CustomEditor(typeof(SkillTree), true)]
    internal class SkillTreeAssetEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            var tree = (SkillTree)target;
            var open = new Button(() => SkillTreeWindow.Open(tree)) { text = "Open in Skill Tree Editor" };
            open.style.height = 28;
            open.style.marginBottom = 6;
            root.Add(open);
            int nodes = tree.Nodes.Count;
            root.Add(new HelpBox($"{nodes} skills, {tree.Groups.Count} groups. Skills, links and groups are edited in the Skill Tree Editor.",
                HelpBoxMessageType.None));
            InspectorElement.FillDefaultInspector(root, serializedObject, this);
            return root;
        }
    }
}
