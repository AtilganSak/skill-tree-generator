using UnityEngine;

namespace AtilganSak.Skills.UI
{
    /// The default node motion on the view's Visual: a scale pop when a rank is bought, a bigger resting scale while
    /// selected, a sideways shake when buying is refused. Curves and amounts are yours to edit. Replace freely.
    public class SkillNodePunch : SkillNodeFeedback
    {
        [Min(1f)] public float SelectedScale = 1.12f;
        [Min(0.01f)] public float PunchTime = 0.3f;
        public float PunchScale = 0.25f;
        [Tooltip("0..1 over the punch time, multiplied by Punch Scale and added to the scale.")]
        public AnimationCurve PunchCurve = new(new Keyframe(0f, 0f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0f));
        [Min(0.01f)] public float ShakeTime = 0.25f;
        public float ShakeDistance = 8f;
        [Tooltip("How fast the selected scale is reached.")]
        public float Follow = 18f;

        private RectTransform _target;
        private float _scale = 1f, _goal = 1f, _punch = 1f, _shake = 1f;
        private Vector2 _home;

        public override void OnBound(SkillNodeView view)
        {
            _target = view.Visual != null ? view.Visual : (RectTransform)view.transform;
            _home = _target.anchoredPosition;
        }

        public override void OnSelected(SkillNodeView view, bool selected, bool instant)
        {
            _goal = selected ? SelectedScale : 1f;
            if (instant) _scale = _goal;
        }

        public override void OnPurchased(SkillNodeView view) => _punch = 0f;

        public override void OnBuyFailed(SkillNodeView view, string reason) => _shake = 0f;

        private void Update()
        {
            if (_target == null) return;
            float dt = Time.unscaledDeltaTime;
            _scale = Mathf.Lerp(_scale, _goal, 1f - Mathf.Exp(-Follow * dt));
            float scale = _scale;
            if (_punch < 1f)
            {
                _punch = Mathf.Min(1f, _punch + dt / PunchTime);
                scale += PunchCurve.Evaluate(_punch) * PunchScale;
            }
            var v = Vector3.one * scale;
            if (_target.localScale != v) _target.localScale = v; // at rest: no write, no per-frame canvas re-batch for every node

            if (_shake < 1f)
            {
                _shake = Mathf.Min(1f, _shake + dt / ShakeTime);
                _target.anchoredPosition = _home + Vector2.right * (Mathf.Sin(_shake * Mathf.PI * 6f) * (1f - _shake) * ShakeDistance);
            }
            else if (_target.anchoredPosition != _home) _target.anchoredPosition = _home;
        }
    }
}
