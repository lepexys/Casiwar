using UnityEngine;
using UnityEngine.EventSystems;

namespace Casiwar
{
    /// <summary>
    /// Мышь и касания в окне карты мира: перетаскивание двигает карту (средняя кнопка мыши,
    /// а также левая/правая и палец), клик выбирает клетку. Добавляется окну карты само (CityMapView).
    /// </summary>
    public class CityMapInput : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public CityMapView owner;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (owner != null) owner.OnMapClick(eventData);
        }

        public void OnBeginDrag(PointerEventData eventData) { }

        public void OnDrag(PointerEventData eventData)
        {
            if (owner != null) owner.OnMapDrag(eventData);
        }

        public void OnEndDrag(PointerEventData eventData) { }
    }
}
