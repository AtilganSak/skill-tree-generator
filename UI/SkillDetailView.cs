using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace HeatInteractive.SkillTreeGenerator.UI
{
    /// The info card of the picked node: name, group, icon, description, summary, rank, price, why it can't be bought,
    /// and the Buy button (the panel wires it). Every part is optional: leave a slot empty to drop that line.
    public class SkillDetailView : MonoBehaviour
    {
        [Header("Parts")]
        public Image Icon;
        public TMP_Text Title;
        public TMP_Text GroupText;
        public TMP_Text Description;
        [Tooltip("The node's one-line effect summary (SkillNode.Summary).")]
        public TMP_Text Summary;
        public TMP_Text RankText;
        [Tooltip("Why it can't be bought right now. Hidden while it can.")]
        public TMP_Text ReasonText;
        public Button BuyButton;
        public TMP_Text BuyLabel;
        [Tooltip("Price rows go here.")]
        public RectTransform CostRoot;
        public SkillCostRow CostRowPrefab;
        [Tooltip("Off: the button stays clickable, so a refused buy plays the node's BuyFailed hooks (shake, buzz...).")]
        public bool DisableBuyWhenBlocked = true;

        [Header("Texts")]
        public string BuyText = "Buy";
        public string MaxedText = "Max";
        [Tooltip("{0} = rank, {1} = max rank.")]
        public string RankFormat = "Rank {0} / {1}";
        [Tooltip("The amount text of a price row. {0} = price, {1} = what the player has. The currency's name has its own text in the row.")]
        public string CostFormat = "{0} ({1})";
        public Color AffordableColor = new(0.53f, 0.93f, 0.53f);
        public Color MissingColor = new(1f, 0.4f, 0.4f);

        [Header("Hooks")]
        [Tooltip("A node was shown (also when the same node refreshes). Play a fade, a slide...")]
        public UnityEvent Shown;
        [Tooltip("A different node than before was shown.")]
        public UnityEvent NodeChanged;
        public UnityEvent Cleared;

        public SkillNode Node { get; private set; }

        public void Show(SkillNode node, SkillBook book)
        {
            bool changed = node != Node;
            Node = node;
            int rank = book.GetRank(node);
            bool maxed = rank >= node.MaxRank;
            string why = book.WhyNot(node);

            if (Icon != null)
            {
                Icon.sprite = node.Icon;
                Icon.gameObject.SetActive(node.Icon != null); // off = no empty gap in the header's layout
            }
            if (Title != null) Title.text = node.DisplayName;
            if (GroupText != null)
            {
                GroupText.text = node.Group != null ? node.Group.DisplayName : "";
                if (node.Group != null) GroupText.color = node.Group.Color;
            }
            if (Description != null) Description.text = node.Description;
            if (Summary != null) Summary.text = node.Summary ?? "";
            if (RankText != null) RankText.text = string.Format(RankFormat, rank, node.MaxRank);
            if (ReasonText != null)
            {
                ReasonText.gameObject.SetActive(why != null);
                ReasonText.text = why ?? "";
            }
            if (BuyButton != null) BuyButton.interactable = why == null || !DisableBuyWhenBlocked;
            if (BuyLabel != null) BuyLabel.text = maxed ? MaxedText : BuyText;
            RebuildCost(node, book, maxed);

            if (changed) NodeChanged?.Invoke();
            Shown?.Invoke();
        }

        public void Clear()
        {
            Node = null;
            if (CostRoot != null) ClearCost();
            Cleared?.Invoke();
        }

        private void RebuildCost(SkillNode node, SkillBook book, bool maxed)
        {
            if (CostRoot == null || CostRowPrefab == null) return;
            ClearCost();
            if (maxed || book.Wallet == null) return;
            foreach (var cost in book.Price(node))
            {
                int have = book.Wallet.Balance(cost.Currency);
                var row = Instantiate(CostRowPrefab, CostRoot);
                row.Bind(cost.Currency, string.Format(CostFormat, cost.Amount, have),
                    have >= cost.Amount ? AffordableColor : MissingColor);
            }
        }

        private void ClearCost()
        {
            for (int i = CostRoot.childCount - 1; i >= 0; i--)
            {
                var child = CostRoot.GetChild(i).gameObject;
                child.SetActive(false); // Destroy is deferred in play, immediate in edit mode: either way it is gone from layouts
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
        }
    }
}
