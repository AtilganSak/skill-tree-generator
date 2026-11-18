using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AtilganSak.Skills.Editing
{
    /// A SkillStat field picks from its own tree's stats (or makes a new one), not from the whole project.
    [CustomPropertyDrawer(typeof(SkillStat))]
    internal class SkillStatDrawer : PropertyDrawer
    {
        private const string NewStat = "+ New Stat";

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var tree = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GetAssetPath(property.serializedObject.targetObject)) as SkillTree;
            if (tree == null)
            {
                var field = new ObjectField(property.displayName) { objectType = typeof(SkillStat), allowSceneObjects = false };
                field.BindProperty(property);
                return field;
            }

            var stats = tree.Stats.Where(s => s != null).OrderBy(s => s.DisplayName).ToList();
            var choices = new List<string> { "None" };
            choices.AddRange(stats.Select(Label));
            choices.Add(NewStat);
            var current = property.objectReferenceValue as SkillStat;
            int index = current == null ? 0 : stats.IndexOf(current) + 1;

            var dropdown = new DropdownField(property.displayName, choices, Mathf.Max(0, index));
            dropdown.AddToClassList(BaseField<string>.alignedFieldUssClassName);
            dropdown.tooltip = "The stat this effect raises. Stats are defined in the tree panel (pick nothing on the canvas) > Stats.";
            if (property.hasMultipleDifferentValues) dropdown.SetValueWithoutNotify("—");
            dropdown.RegisterValueChangedCallback(_ =>
            {
                int i = dropdown.index;
                SkillStat pick;
                if (i == choices.Count - 1)
                {
                    pick = SkillTreeEditing.CreateStat(tree);
                    SkillTreeWindow.RefreshOpen();
                }
                else pick = i <= 0 ? null : stats[i - 1];
                property.objectReferenceValue = pick;
                property.serializedObject.ApplyModifiedProperties();
                if (pick != null && i == choices.Count - 1) dropdown.SetValueWithoutNotify(Label(pick));
            });
            return dropdown;
        }

        private static string Label(SkillStat s) => $"{s.DisplayName}  ({(s.Kind == SkillStatKind.Percent ? "%" : string.IsNullOrEmpty(s.Unit) ? "flat" : s.Unit)})";
    }

    /// One effect: the stat, how much per rank (with its unit), which targets, and the rarely needed extras folded away.
    [CustomPropertyDrawer(typeof(SkillEffect))]
    internal class SkillEffectDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();
            root.AddToClassList("effect");

            var statProp = property.FindPropertyRelative(nameof(SkillEffect.Stat));
            var stat = new PropertyField(statProp, "Stat");
            root.Add(stat);

            var amountRow = new VisualElement();
            amountRow.AddToClassList("effect__amount");
            var perRank = new PropertyField(property.FindPropertyRelative(nameof(SkillEffect.PerRank)), "Per Rank");
            perRank.AddToClassList("effect__per-rank");
            amountRow.Add(perRank);
            var unit = new Label();
            unit.AddToClassList("effect__unit");
            amountRow.Add(unit);
            root.Add(amountRow);

            var targetsProp = property.FindPropertyRelative(nameof(SkillEffect.Targets));
            targetsProp.isExpanded = true;
            var targets = new PropertyField(targetsProp, "Targets  (empty = everywhere)");
            root.Add(targets);

            var more = new Foldout { text = "More", value = false };
            more.AddToClassList("effect__more");
            more.Add(new PropertyField(property.FindPropertyRelative(nameof(SkillEffect.StartRank))));
            more.Add(new PropertyField(property.FindPropertyRelative(nameof(SkillEffect.Payload))));
            root.Add(more);

            void Paint()
            {
                var s = statProp.objectReferenceValue as SkillStat;
                unit.text = s == null ? "" : s.Kind == SkillStatKind.Percent ? "% per rank" : (string.IsNullOrEmpty(s.Unit) ? "" : s.Unit + " ") + "per rank";
                var type = s != null ? s.ResolvedTargetType : null;
                targets.tooltip = type != null ? $"Only {type.Name} assets." : "Any asset. Empty = applies everywhere.";
                // The pickers only offer what the stat accepts.
                targets.Query<ObjectField>().ForEach(f => f.objectType = type ?? typeof(Object));
            }

            root.TrackPropertyValue(statProp, _ => Paint());
            targets.RegisterCallback<GeometryChangedEvent>(_ => Paint());
            root.RegisterCallback<AttachToPanelEvent>(_ => Paint());
            return root;
        }
    }

    /// A string marked [SkillTargetType] picks one of the project's ScriptableObject types (or Any).
    [CustomPropertyDrawer(typeof(SkillTargetTypeAttribute))]
    internal class SkillTargetTypeDrawer : PropertyDrawer
    {
        private static List<Type> _types;

        private static IReadOnlyList<Type> Types => _types ??= TypeCache.GetTypesDerivedFrom<ScriptableObject>()
            .Where(t => !t.IsGenericType && t.IsPublic && IsProjectAssembly(t.Assembly.GetName().Name) && !typeof(SkillNode).IsAssignableFrom(t)
                        && t != typeof(SkillStat) && t != typeof(SkillGroup) && !typeof(SkillTree).IsAssignableFrom(t))
            .OrderBy(t => t.Name).ToList();

        private static bool IsProjectAssembly(string name) =>
            !(name.StartsWith("Unity") || name.StartsWith("System") || name.StartsWith("Mono") || name.StartsWith("Microsoft")
              || name.StartsWith("nunit") || name.Contains("Editor") || name.StartsWith("AtilganSak.Skills"));

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var types = Types;
            var choices = new List<string> { "Any" };
            choices.AddRange(types.Select(t => types.Count(o => o.Name == t.Name) > 1 ? t.FullName : t.Name));
            int index = 0;
            for (int i = 0; i < types.Count; i++)
                if (SkillTreeEditing.TypeKey(types[i]) == property.stringValue) index = i + 1;

            var dropdown = new DropdownField(property.displayName, choices, index) { tooltip = "Only assets of this type can be targets of the stat." };
            dropdown.AddToClassList(BaseField<string>.alignedFieldUssClassName);
            if (index == 0 && !string.IsNullOrEmpty(property.stringValue)) dropdown.SetValueWithoutNotify(property.stringValue + " (missing)");
            dropdown.RegisterValueChangedCallback(_ =>
            {
                int i = dropdown.index;
                property.stringValue = i <= 0 ? "" : SkillTreeEditing.TypeKey(types[i - 1]);
                property.serializedObject.ApplyModifiedProperties();
            });
            return dropdown;
        }
    }
}
