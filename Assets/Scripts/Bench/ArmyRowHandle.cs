using UnityEngine;
using UnityEngine.EventSystems;

namespace Casiwar
{
    /// <summary>
    /// Подпись линейки в панели армии («Гарнизон», «Отряд 2»): её тянут на другую линейку — туда переходят все бойцы
    /// (как перетаскивание героя в «Героях»). Клик — выбранный боец из другой линейки встаёт сюда на свободное место.
    /// Добавляется кодом (BenchManager).
    /// </summary>
    public class ArmyRowHandle : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public BenchManager owner;
        public PanelRow row;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (owner != null) owner.OnRowLabelClicked(row);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (owner != null) owner.BeginRowDrag(row, eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (owner != null) owner.OnDragMove(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (owner != null) owner.EndDrag(eventData);
        }
    }
}
