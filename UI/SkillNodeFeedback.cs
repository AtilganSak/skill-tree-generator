using UnityEngine;

namespace HeatInteractive.SkillTreeGenerator.UI
{
    /// The code hook for node transitions: derive, override what you care about, put the component anywhere inside the
    /// node prefab (the SkillNodeView finds it). Every ready-made look (SkillNodeTint, SkillNodePunch, SkillNodeAnimator)
    /// is one of these, so replace or remove them freely.
    /// instant = the first paint or a rebuild: snap to the end state, don't play a transition.
    public abstract class SkillNodeFeedback : MonoBehaviour
    {
        /// Once, after the view got its node.
        public virtual void OnBound(SkillNodeView view) { }

        /// The state changed (Hidden -> something is a reveal).
        public virtual void OnState(SkillNodeView view, SkillNodeState from, SkillNodeState to, bool instant) { }

        /// The rank changed, for any reason (buy, refund, respec, a cheat).
        public virtual void OnRank(SkillNodeView view, int from, int to, bool instant) { }

        /// A rank was bought (rank went up, not a first paint).
        public virtual void OnPurchased(SkillNodeView view) { }

        /// A rank went away (a respec).
        public virtual void OnRefunded(SkillNodeView view) { }

        public virtual void OnSelected(SkillNodeView view, bool selected, bool instant) { }

        /// Buy was asked for and refused: reason is the player-facing text (shake, red flash, a buzz...).
        public virtual void OnBuyFailed(SkillNodeView view, string reason) { }
    }
}
