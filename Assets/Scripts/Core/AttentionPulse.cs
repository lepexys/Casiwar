using UnityEngine;
using UnityEngine.UI;

namespace Casiwar
{
    /// <summary>
    /// Пульсирующая золотая обводка «сюда стоит нажать»: кнопка «В БОЙ!», когда на поле больше нечего делать,
    /// «Дальше» после трофеев, вкладки и здания, где можно что-то купить. Вешается кодом на объект с Image.
    /// </summary>
    [DisallowMultipleComponent]
    public class AttentionPulse : MonoBehaviour
    {
        public Color color = new Color(1f, 0.84f, 0.30f);
        [Tooltip("Толщина обводки, px: от min до max и обратно")]
        public float minDistance = 2f;
        public float maxDistance = 6f;
        [Tooltip("Пульсаций в секунду × 2π")]
        public float speed = 5f;

        private Outline outline;
        private bool on;

        public bool IsOn => on;

        /// <summary>Подсветка для этого элемента (создаётся при первом обращении).</summary>
        public static AttentionPulse For(Graphic graphic)
        {
            if (graphic == null) return null;
            AttentionPulse pulse = graphic.GetComponent<AttentionPulse>();
            return pulse != null ? pulse : graphic.gameObject.AddComponent<AttentionPulse>();
        }

        /// <summary>Включить или выключить подсветку элемента (null — ничего не делает).</summary>
        public static void Set(Graphic graphic, bool value)
        {
            if (graphic == null || (!value && graphic.GetComponent<AttentionPulse>() == null)) return;
            For(graphic).Set(value);
        }

        public void Set(bool value)
        {
            EnsureOutline();
            if (on == value) return;
            on = value;
            outline.enabled = value;
            if (value) Update();
        }

        private void EnsureOutline()
        {
            if (outline != null) return;
            outline = gameObject.AddComponent<Outline>();
            outline.useGraphicAlpha = false;
            outline.enabled = false;
        }

        private void Update()
        {
            if (!on || outline == null) return;
            float t = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * speed);
            outline.effectColor = new Color(color.r, color.g, color.b, Mathf.Lerp(0.45f, 1f, t));
            float distance = Mathf.Lerp(minDistance, maxDistance, t);
            outline.effectDistance = new Vector2(distance, -distance);
        }
    }
}
