using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace HeatInteractive.SkillTreeGenerator.UI
{
    /// Where a node is for the player. Drives the look (SkillNodeTint), the Animator ("State" int = this enum's value)
    /// and every transition hook.
    public enum SkillNodeState
    {
        /// Not revealed yet (see SkillTreePanel.Reveal): the node's GameObject is off.
        Hidden = 0,
        /// Can't be bought now: not reachable, a requirement missing, or the game vetoes it.
        Locked = 1,
        /// Can be bought, but not paid for yet.
        Available = 2,
        /// Can be bought right now.
        Affordable = 3,
        /// Rank 1 or more, below max rank.
        Owned = 4,
        /// At max rank.
        Maxed = 5
    }

    /// One node on the panel. Built by SkillTreePanel from the node prefab; it only shows data and raises hooks, the look
    /// lives in the feedback components next to it (SkillNodeTint, SkillNodePunch, SkillNodeAnimator, or your own
    /// SkillNodeFeedback). Hook into transitions three ways: the UnityEvents below (wire them in the prefab: play a
    /// particle, a sound...), a SkillNodeFeedback component on the prefab (code), or SkillTreePanel's C# events.
    public class SkillNodeView : MonoBehaviour, IPointerClickHandler
    {
        [Header("Parts (all optional)")]
        [Tooltip("What feedback components scale and tween. Keep the click area (the Outline image) inside it.")]
        public RectTransform Visual;
        public Image Icon;
        public TMP_Text RankText;
        public TMP_Text NameText;
        [Tooltip("Shown as rank / max rank. {0} = rank, {1} = max rank.")]
        public string RankFormat = "{0}/{1}";

        [Header("Transition hooks")]
        public UnityEvent<SkillNodeState> StateChanged;
        public UnityEvent Revealed;
        public UnityEvent Purchased;
        public UnityEvent Refunded;
        public UnityEvent Selected;
        public UnityEvent Deselected;
        [Tooltip("Buy was asked for and refused (can't afford, locked...).")]
        public UnityEvent BuyFailed;

        public SkillNode Node { get; private set; }
        public SkillNodeState State { get; private set; } = SkillNodeState.Hidden;
        public int Rank { get; private set; }
        public bool IsSelected { get; private set; }

        /// Set by the panel: the node was clicked.
        public Action<SkillNodeView> Clicked;

        private SkillNodeFeedback[] _feedback = Array.Empty<SkillNodeFeedback>();

        public void OnPointerClick(PointerEventData eventData) => Clicked?.Invoke(this);

        /// Gives the view its node. Call once, right after instantiating.
        public void Bind(SkillNode node)
        {
            Node = node;
            _feedback = GetComponentsInChildren<SkillNodeFeedback>(true);
            if (Icon != null)
            {
                Icon.sprite = node.Icon;
                Icon.enabled = node.Icon != null;
            }
            if (NameText != null) NameText.text = node.DisplayName;
            foreach (var f in _feedback) f.OnBound(this);
        }

        /// Called by the panel after any change. instant = the first paint (or a rebuild): snap, no transition hooks.
        public void Apply(SkillNodeState state, int rank, bool instant)
        {
            var from = State;
            int fromRank = Rank;
            State = state;
            Rank = rank;

            bool visible = state != SkillNodeState.Hidden;
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
            if (RankText != null && (instant || fromRank != rank)) RankText.text = string.Format(RankFormat, rank, Node.MaxRank); // no string + TMP rebuild per node per refresh

            if (!visible) return;
            if (from != state || instant)
            {
                foreach (var f in _feedback) f.OnState(this, from, state, instant);
                if (!instant)
                {
                    StateChanged?.Invoke(state);
                    if (from == SkillNodeState.Hidden) Revealed?.Invoke();
                }
            }
            if (fromRank == rank) return;
            foreach (var f in _feedback) f.OnRank(this, fromRank, rank, instant);
            if (instant) return;
            if (rank > fromRank)
            {
                foreach (var f in _feedback) f.OnPurchased(this);
                Purchased?.Invoke();
            }
            else
            {
                foreach (var f in _feedback) f.OnRefunded(this);
                Refunded?.Invoke();
            }
        }

        public void SetSelected(bool selected, bool instant = false)
        {
            if (IsSelected == selected && !instant) return;
            IsSelected = selected;
            foreach (var f in _feedback) f.OnSelected(this, selected, instant);
            if (instant) return;
            (selected ? Selected : Deselected)?.Invoke();
        }

        public void NotifyBuyFailed(string reason)
        {
            foreach (var f in _feedback) f.OnBuyFailed(this, reason);
            BuyFailed?.Invoke();
        }
    }
}
