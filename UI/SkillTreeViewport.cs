using UnityEngine;
using UnityEngine.EventSystems;

namespace AtilganSak.Skills.UI
{
    /// Pan (drag) and zoom (wheel) for the tree: moves and scales Content inside this rect. Put it on the viewport,
    /// an Image with Raycast Target on (even fully transparent) so it gets the drags that start on empty space or on a node.
    public class SkillTreeViewport : MonoBehaviour, IDragHandler, IScrollHandler
    {
        [Tooltip("The rect the nodes and links live in, centered on the viewport.")]
        public RectTransform Content;
        [SerializeField, Min(0.05f)] private float _minZoom = 0.25f;
        [SerializeField, Min(0.1f)] private float _maxZoom = 1.6f;
        [SerializeField, Min(1.01f)] private float _zoomStep = 1.12f;

        private RectTransform _rect;
        private Canvas _canvas;

        private RectTransform Rect => _rect != null ? _rect : _rect = (RectTransform)transform;

        /// Back to the middle at this zoom (clamped).
        public void ResetView(float zoom)
        {
            Content.localScale = Vector3.one * Mathf.Clamp(zoom, _minZoom, _maxZoom);
            Content.anchoredPosition = Vector2.zero;
        }

        /// Puts a point of the content (a node's Position) in the middle of the viewport.
        public void Focus(Vector2 contentPoint) => Content.anchoredPosition = -contentPoint * Content.localScale.x;

        public void OnDrag(PointerEventData eventData)
        {
            if (_canvas == null) _canvas = GetComponentInParent<Canvas>();
            float scale = _canvas != null ? _canvas.scaleFactor : 1f;
            Content.anchoredPosition += eventData.delta / scale;
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (Mathf.Approximately(eventData.scrollDelta.y, 0f)) return;
            float old = Content.localScale.x;
            float next = Mathf.Clamp(old * (eventData.scrollDelta.y > 0f ? _zoomStep : 1f / _zoomStep), _minZoom, _maxZoom);
            if (Mathf.Approximately(old, next)) return;

            // Keep the point under the cursor where it is.
            RectTransformUtility.ScreenPointToLocalPointInRectangle(Rect, eventData.position, eventData.pressEventCamera, out var local);
            Vector2 under = (local - Content.anchoredPosition) / old;
            Content.localScale = Vector3.one * next;
            Content.anchoredPosition = local - under * next;
        }
    }
}
