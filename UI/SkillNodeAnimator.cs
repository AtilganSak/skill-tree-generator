using UnityEngine;

namespace AtilganSak.Skills.UI
{
    /// Drives an Animator from the node transitions, so animation is clips and a controller, no code. Parameters (a
    /// missing one is skipped, so a controller only needs the ones it uses):
    ///   int State (SkillNodeState value), bool Selected, bool Owned,
    ///   triggers Revealed, Purchased, Refunded, BuyFailed.
    /// Instant paints (first show) set the values and skip the triggers. Rename any of them below.
    [RequireComponent(typeof(Animator))]
    public class SkillNodeAnimator : SkillNodeFeedback
    {
        public string StateParam = "State";
        public string SelectedParam = "Selected";
        public string OwnedParam = "Owned";
        public string RevealedTrigger = "Revealed";
        public string PurchasedTrigger = "Purchased";
        public string RefundedTrigger = "Refunded";
        public string BuyFailedTrigger = "BuyFailed";

        private Animator _animator;
        private Animator Anim => _animator != null ? _animator : _animator = GetComponent<Animator>();

        public override void OnState(SkillNodeView view, SkillNodeState from, SkillNodeState to, bool instant)
        {
            Set(StateParam, (int)to);
            Set(OwnedParam, to >= SkillNodeState.Owned);
            if (!instant && from == SkillNodeState.Hidden) Fire(RevealedTrigger);
            if (instant) Anim.Update(0f); // jump to the state's pose, don't blend from the previous one
        }

        public override void OnSelected(SkillNodeView view, bool selected, bool instant) => Set(SelectedParam, selected);
        public override void OnPurchased(SkillNodeView view) => Fire(PurchasedTrigger);
        public override void OnRefunded(SkillNodeView view) => Fire(RefundedTrigger);
        public override void OnBuyFailed(SkillNodeView view, string reason) => Fire(BuyFailedTrigger);

        // ponytail: parameters cached on first use (Animator.parameters allocates an array per call); a controller swapped at runtime isn't re-read.
        private AnimatorControllerParameter[] _params;

        private bool Has(string name, AnimatorControllerParameterType type)
        {
            if (string.IsNullOrEmpty(name) || Anim.runtimeAnimatorController == null) return false;
            _params ??= Anim.parameters;
            foreach (var p in _params)
                if (p.type == type && p.name == name) return true;
            return false;
        }

        private void Set(string name, int value) { if (Has(name, AnimatorControllerParameterType.Int)) Anim.SetInteger(name, value); }
        private void Set(string name, bool value) { if (Has(name, AnimatorControllerParameterType.Bool)) Anim.SetBool(name, value); }
        private void Fire(string name) { if (Has(name, AnimatorControllerParameterType.Trigger)) Anim.SetTrigger(name); }
    }
}
