using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AtilganSak.Skills.Editing
{
    /// A SkillGroup field picks from its own tree's groups (a dropdown with the group colors), not from the whole project.
    [CustomPropertyDrawer(typeof(SkillGroup))]
    internal class SkillGroupDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var path = AssetDatabase.GetAssetPath(property.serializedObject.targetObject);
            var tree = AssetDatabase.LoadMainAssetAtPath(path) as SkillTree;
            if (tree == null)
            {
                var field = new ObjectField(property.displayName) { objectType = typeof(SkillGroup), allowSceneObjects = false };
                field.BindProperty(property);
                return field;
            }

            var row = new VisualElement();
            row.AddToClassList("group-field");
            var swatch = new VisualElement();
            swatch.AddToClassList("group-field__swatch");

            var groups = new List<SkillGroup> { null };
            groups.AddRange(tree.Groups.Where(g => g != null));
            var names = groups.Select(g => g == null ? "None" : g.DisplayName).ToList();
            var current = property.objectReferenceValue as SkillGroup;
            int index = Mathf.Max(0, groups.IndexOf(current));
            if (property.hasMultipleDifferentValues) index = -1;

            var dropdown = new DropdownField(property.displayName, names, index);
            dropdown.AddToClassList("group-field__dropdown");
            dropdown.AddToClassList(BaseField<string>.alignedFieldUssClassName);
            if (index < 0) dropdown.SetValueWithoutNotify("—");
            dropdown.RegisterValueChangedCallback(_ =>
            {
                int i = dropdown.index;
                if (i < 0 || i >= groups.Count) return;
                property.objectReferenceValue = groups[i];
                property.serializedObject.ApplyModifiedProperties();
                Paint();
            });

            void Paint()
            {
                var g = dropdown.index > 0 && dropdown.index < groups.Count ? groups[dropdown.index] : null;
                swatch.style.backgroundColor = g != null ? g.Color : new Color(0.5f, 0.5f, 0.5f, 0.3f);
            }

            row.Add(dropdown);
            row.Add(swatch);
            Paint();
            return row;
        }
    }
}
