using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Casiwar
{
    /// <summary>Мышь над узлом дерева прокачки (SkillTreeGraph подсвечивает его связи). Добавляется кодом.</summary>
    public class SkillNodeHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Action Entered;
        public Action Exited;

        public void OnPointerEnter(PointerEventData eventData) => Entered?.Invoke();

        public void OnPointerExit(PointerEventData eventData) => Exited?.Invoke();
    }
}
