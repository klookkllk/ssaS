using UnityEngine;
using UnityEngine.EventSystems;

namespace WeaponCustomization
{
    /// <summary>Вращение превью перетаскиванием мыши.</summary>
    public class PreviewDragHandler : MonoBehaviour, IDragHandler
    {
        public WeaponPreviewRenderer preview;

        public void OnDrag(PointerEventData eventData)
        {
            if (preview != null) preview.Drag(eventData.delta);
        }
    }
}
