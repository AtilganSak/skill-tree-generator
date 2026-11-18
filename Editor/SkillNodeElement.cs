using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace AtilganSak.Skills.Editing
{
    internal enum SimState { Off, Locked, Available, Owned, Maxed }

    /// One node on the canvas: a colored circle (group color, icon or initials), its name and summary under it, a rank
    /// pill, an issue badge and the "+" connect handle (shown on hover).
    internal class SkillNodeElement : VisualElement
    {
        public const float Radius = 32f, RootRadius = 42f;
        private static readonly Color NoGroup = new(0.55f, 0.58f, 0.64f), Base = new(0.13f, 0.14f, 0.16f);

        public readonly SkillNode Node;
        public readonly VisualElement Port;
        private readonly VisualElement _circle, _accent, _badge;
        private readonly Image _icon;
        private readonly Label _initials, _title, _summary, _rank;
        private SimState _sim;
        private int _simRank;

        public static float RadiusOf(SkillNode node) => node.IsRoot ? RootRadius : Radius;

        public SkillNodeElement(SkillNode node)
        {
            Node = node;
            AddToClassList("skill-node");

            _accent = Child(this, "skill-node__accent");
            Child(this, "skill-node__selection");
            _circle = Child(this, "skill-node__circle");
            _icon = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            _icon.AddToClassList("skill-node__icon");
            _circle.Add(_icon);
            _initials = NewLabel(_circle, "skill-node__initials");

            _rank = NewLabel(this, "skill-node__rank");
            _badge = Child(this, "skill-node__badge");
            NewLabel(_badge, "skill-node__badge-text").text = "!";

            Port = Child(this, "skill-node__port");
            Port.pickingMode = PickingMode.Position;
            Port.tooltip = "Drag to another skill to connect them.\nHold Ctrl (Cmd) while releasing to make it a requirement.\nRelease on empty space to create a new connected skill.";
            NewLabel(Port, "skill-node__port-text").text = "+";

            var label = Child(this, "skill-node__label");
            label.pickingMode = PickingMode.Position; // clicking the name picks the node too
            _title = NewLabel(label, "skill-node__title");
            _summary = NewLabel(label, "skill-node__summary");

            Refresh();
        }

        private static VisualElement Child(VisualElement parent, string cls)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList(cls);
            parent.Add(e);
            return e;
        }

        private static Label NewLabel(VisualElement parent, string cls)
        {
            var l = new Label { pickingMode = PickingMode.Ignore };
            l.AddToClassList(cls);
            parent.Add(l);
            return l;
        }

        public void Refresh()
        {
            if (Node == null) return;
            EnableInClassList("skill-node--root", Node.IsRoot);
            _circle.pickingMode = PickingMode.Position;

            var color = Node.Group != null ? Node.Group.Color : NoGroup;
            SetBorderColor(_circle, color);
            _circle.style.backgroundColor = Color.Lerp(Base, color, 0.3f);

            var accent = Node.Accent;
            _accent.style.display = accent.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
            if (accent.HasValue) SetBorderColor(_accent, accent.Value);

            bool hasIcon = Node.Icon != null;
            _icon.sprite = Node.Icon;
            _icon.style.display = hasIcon ? DisplayStyle.Flex : DisplayStyle.None;
            _initials.style.display = hasIcon ? DisplayStyle.None : DisplayStyle.Flex;
            _initials.text = Initials(Node.DisplayName);

            _title.text = string.IsNullOrWhiteSpace(Node.DisplayName) ? "(unnamed)" : Node.DisplayName;
            string summary = Node.Summary;
            _summary.text = summary ?? "";
            _summary.style.display = string.IsNullOrEmpty(summary) ? DisplayStyle.None : DisplayStyle.Flex;

            RefreshRank();
            UpdatePosition();
        }

        public void UpdatePosition()
        {
            style.left = Node.Position.x;
            style.top = -Node.Position.y;
        }

        public void SetSelected(bool on) => EnableInClassList("skill-node--selected", on);
        public void SetDimmed(bool on) => EnableInClassList("skill-node--dimmed", on);
        public void SetTarget(bool on) => EnableInClassList("skill-node--target", on);

        public void SetIssues(string errors, string warnings)
        {
            bool any = errors != null || warnings != null;
            _badge.style.display = any ? DisplayStyle.Flex : DisplayStyle.None;
            _badge.EnableInClassList("skill-node__badge--error", errors != null);
            _badge.tooltip = (errors + (errors != null && warnings != null ? "\n" : "") + warnings) ?? "";
        }

        public void SetSimulation(SimState state, int rank)
        {
            _sim = state;
            _simRank = rank;
            EnableInClassList("sim-locked", state == SimState.Locked);
            EnableInClassList("sim-available", state == SimState.Available);
            EnableInClassList("sim-owned", state is SimState.Owned or SimState.Maxed);
            EnableInClassList("sim-maxed", state == SimState.Maxed);
            RefreshRank();
        }

        private void RefreshRank()
        {
            string text = _sim != SimState.Off ? $"{_simRank}/{Node.MaxRank}" : Node.MaxRank > 1 ? $"×{Node.MaxRank}" : "";
            _rank.text = text;
            _rank.style.display = text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static void SetBorderColor(VisualElement e, Color c)
        {
            e.style.borderTopColor = c;
            e.style.borderBottomColor = c;
            e.style.borderLeftColor = c;
            e.style.borderRightColor = c;
        }

        private static string Initials(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "?";
            var sb = new StringBuilder();
            foreach (var word in name.Split(' ', System.StringSplitOptions.RemoveEmptyEntries))
            {
                sb.Append(char.ToUpperInvariant(word[0]));
                if (sb.Length == 2) break;
            }
            return sb.ToString();
        }
    }
}
