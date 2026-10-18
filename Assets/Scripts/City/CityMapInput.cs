using UnityEngine;
using UnityEngine.EventSystems;

namespace Casiwar
{
    /// <summary>
    /// Мышь и касания в окне карты мира: перетаскивание двигает карту (средняя кнопка мыши,
    /// а также левая/правая и палец), колесо — масштаб, левый клик выбирает клетку или отряд, правый — приказ выбранному отряду.
    /// Добавляется окну карты само (CityMapView).
    /// </summary>
    public class CityMapInput : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
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

        public void OnScroll(PointerEventData eventData)
        {
            if (owner != null) owner.OnMapScroll(eventData);
        }
    }
}
