using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace HeatInteractive.SkillTreeGenerator.UI
{
    /// A small yes / cancel question over the panel. Keep the object inactive in the prefab: Open turns it on, Yes
    /// or Cancel turns it off. Its background should be a full-size Image with Raycast Target on, so nothing behind
    /// it can be clicked meanwhile. Its texts are just texts in the prefab: change them there. Fade or slide it in
    /// with the UnityEvents (Opened, Closed...).
    public class SkillConfirmPopup : MonoBehaviour
    {
        public Button ConfirmButton;
        public Button CancelButton;

        [Header("Hooks")]
        public UnityEvent Opened;
        public UnityEvent Confirmed;
        public UnityEvent Cancelled;
        [Tooltip("After Yes or Cancel.")]
        public UnityEvent Closed;

        private Action _onConfirm;

        private void OnEnable()
        {
            if (ConfirmButton != null) ConfirmButton.onClick.AddListener(Confirm);
            if (CancelButton != null) CancelButton.onClick.AddListener(Cancel);
        }

        private void OnDisable()
        {
            if (ConfirmButton != null) ConfirmButton.onClick.RemoveListener(Confirm);
            if (CancelButton != null) CancelButton.onClick.RemoveListener(Cancel);
        }

        /// Asks the question; onConfirm runs if the player says yes.
        public void Open(Action onConfirm)
        {
            _onConfirm = onConfirm;
            gameObject.SetActive(true);
            transform.SetAsLastSibling(); // above everything else in the panel
            Opened?.Invoke();
        }

        public void Confirm()
        {
            var action = _onConfirm;
            Hide();
            Confirmed?.Invoke();
            action?.Invoke();
        }

        public void Cancel()
        {
            Hide();
            Cancelled?.Invoke();
        }

        private void Hide()
        {
            _onConfirm = null;
            gameObject.SetActive(false);
            Closed?.Invoke();
        }
    }
}
