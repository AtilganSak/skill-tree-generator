using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HeatInteractive.SkillTreeGenerator.UI
{
    /// One currency line: an icon, the currency's name and an amount. Used for the wallet and for the price in the
    /// detail view; duplicate the prefab and assign your copy on the panel / detail view to restyle either.
    /// Every part is optional. A color passed in tints the texts; none leaves the prefab's own colors.
    public class SkillCostRow : MonoBehaviour
    {
        public Image Icon;
        public TMP_Text NameText;
        public TMP_Text AmountText;

        public void Bind(SkillCurrency currency, string amount, Color? tint = null)
        {
            var sprite = currency != null ? currency.Icon : null;
            if (Icon != null)
            {
                Icon.sprite = sprite;
                Icon.gameObject.SetActive(sprite != null); // off = no empty gap in the row's layout
            }
            if (NameText != null)
            {
                NameText.text = currency != null ? currency.DisplayName : "?";
                if (tint.HasValue) NameText.color = tint.Value;
            }
            if (AmountText == null) return;
            AmountText.text = amount;
            if (tint.HasValue) AmountText.color = tint.Value;
        }
    }
}
