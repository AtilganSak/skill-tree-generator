using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HeatInteractive.SkillTreeGenerator.UI
{
    /// The default node colors: the Fill and Outline images (and the rank text) take one Look per state, fading over
    /// FadeTime. UseGroupColor multiplies the fill with the node's group color. Replace freely: it is one feedback.
    public class SkillNodeTint : SkillNodeFeedback
    {
        [Serializable]
        public struct Look
        {
            public Color Fill;
            public Color Outline;
            public Color Text;
            public bool UseGroupColor;
        }

        public Image Fill;
        public Image Outline;
        public TMP_Text Text;
        [Min(0f)] public float FadeTime = 0.15f;

        public Look Locked = new() { Fill = new Color(0.16f, 0.16f, 0.18f), Outline = new Color(1f, 1f, 1f, 0.1f), Text = new Color(1f, 1f, 1f, 0.4f) };
        public Look Available = new() { Fill = new Color(0.45f, 0.45f, 0.45f), Outline = new Color(1f, 0.55f, 0.1f, 0.55f), Text = Color.white, UseGroupColor = true };
        public Look Affordable = new() { Fill = new Color(0.45f, 0.45f, 0.45f), Outline = new Color(1f, 0.9f, 0.2f), Text = Color.white, UseGroupColor = true };
        public Look Owned = new() { Fill = Color.white, Outline = new Color(1f, 1f, 1f, 0.55f), Text = Color.white, UseGroupColor = true };
        public Look Maxed = new() { Fill = Color.white, Outline = Color.white, Text = Color.white, UseGroupColor = true };

        private Color _fill, _outline, _text, _fromFill, _fromOutline, _fromText;
        private float _t = 1f;
        private Color _group = Color.white;

        public override void OnBound(SkillNodeView view)
        {
            _group = view.Node.Group != null ? view.Node.Group.Color : Color.white;
        }

        public override void OnState(SkillNodeView view, SkillNodeState from, SkillNodeState to, bool instant)
        {
            if (to == SkillNodeState.Hidden) return;
            var look = LookOf(to);
            _fromFill = Fill != null ? Fill.color : look.Fill;
            _fromOutline = Outline != null ? Outline.color : look.Outline;
            _fromText = Text != null ? Text.color : look.Text;
            _fill = look.UseGroupColor ? look.Fill * _group : look.Fill;
            _outline = look.Outline;
            _text = look.Text;
            _t = instant || FadeTime <= 0f ? 1f : 0f;
            Apply();
        }

        private void Update()
        {
            if (_t >= 1f) return;
            _t = Mathf.Min(1f, _t + Time.unscaledDeltaTime / FadeTime);
            Apply();
        }

        private void Apply()
        {
            if (Fill != null) Fill.color = Color.Lerp(_fromFill, _fill, _t);
            if (Outline != null) Outline.color = Color.Lerp(_fromOutline, _outline, _t);
            if (Text != null) Text.color = Color.Lerp(_fromText, _text, _t);
        }

        private Look LookOf(SkillNodeState state) => state switch
        {
            SkillNodeState.Available => Available,
            SkillNodeState.Affordable => Affordable,
            SkillNodeState.Owned => Owned,
            SkillNodeState.Maxed => Maxed,
            _ => Locked
        };
    }
}
