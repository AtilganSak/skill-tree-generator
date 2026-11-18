using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace HeatInteractive.SkillTreeGenerator.UI
{
    /// The line between two linked nodes. The panel stretches it from one node to the other (width = Image's height,
    /// length = its width, rotation around the middle), then calls SetOn: On = both ends are owned.
    /// Hooks: the UnityEvents, or the Animator bool "On" (skipped when the controller has none).
    public class SkillLinkView : MonoBehaviour
    {
        public Image Line;
        public Color OffColor = new(1f, 1f, 1f, 0.18f);
        public Color OnColor = new(1f, 1f, 1f, 0.85f);
        public string OnParam = "On";
        public UnityEvent Activated;
        public UnityEvent Deactivated;

        public SkillNode A { get; private set; }
        public SkillNode B { get; private set; }
        public bool IsOn { get; private set; }

        private Animator _animator;
        private AnimatorControllerParameter[] _params;

        public void Place(SkillNode a, SkillNode b)
        {
            A = a;
            B = b;
            var rect = (RectTransform)transform;
            Vector2 delta = b.Position - a.Position;
            rect.sizeDelta = new Vector2(delta.magnitude, rect.sizeDelta.y);
            rect.anchoredPosition = (a.Position + b.Position) * 0.5f;
            rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }

        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
        }

        public void SetOn(bool on, bool instant)
        {
            bool changed = on != IsOn;
            IsOn = on;
            if (Line != null) Line.color = on ? OnColor : OffColor;
            if (!gameObject.activeInHierarchy) return;
            // ponytail: animator and its parameters read once (Animator.parameters allocates per call, and Refresh hits every link).
            _animator ??= GetComponent<Animator>();
            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                _params ??= _animator.parameters;
                foreach (var p in _params)
                    if (p.type == AnimatorControllerParameterType.Bool && p.name == OnParam) _animator.SetBool(OnParam, on);
            }
            if (!changed || instant) return;
            (on ? Activated : Deactivated)?.Invoke();
        }
    }
}
