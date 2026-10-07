using TMPro;
using UnityEngine;

namespace Casiwar
{
    /// <summary>
    /// Мини-эффекты без ассетов: расширяющийся круг, полоса (молния, выстрел) и всплывающий текст
    /// («В СПИНУ ×2», «БЛОК», «ЗАЛП!»). Каждый эффект сам гаснет и уничтожается.
    /// Позже легко заменить на ParticleSystem.
    /// </summary>
    public class BattleVfx : MonoBehaviour
    {
        private const int SortingOrder = 20000; // поверх юнитов

        private SpriteRenderer spriteRenderer;
        private TextMeshPro text;
        private Color color;
        private Vector3 scaleFrom;
        private Vector3 scaleTo;
        private Vector3 rise;
        private Vector3 startPosition;
        private float duration;
        private float time;

        /// <summary>Круг: вырастает до радиуса radius и тает.</summary>
        public static void Ring(Vector2 position, float radius, Color color, float duration = 0.4f)
        {
            float diameter = radius * 2f;
            Spawn(GameVisuals.Circle, position, Quaternion.identity,
                new Vector3(diameter * 0.3f, diameter * 0.3f, 1f), new Vector3(diameter, diameter, 1f), color, duration);
        }

        /// <summary>Полоса от from до to (молния, выстрел).</summary>
        public static void Bolt(Vector2 from, Vector2 to, Color color, float width = 0.12f, float duration = 0.2f)
        {
            Vector2 delta = to - from;
            float length = delta.magnitude;
            if (length < 0.001f) return;
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            Spawn(GameVisuals.Square, (from + to) * 0.5f, Quaternion.Euler(0f, 0f, angle),
                new Vector3(length, width, 1f), new Vector3(length, width * 0.3f, 1f), color, duration);
        }

        /// <summary>Всплывающий текст над ареной (TextMeshPro в мире).</summary>
        public static void FloatingText(Vector2 position, string message, Color color, float fontSize = 3f, float duration = 0.9f)
        {
            var go = new GameObject("FloatingText");
            go.transform.position = position;
            var label = go.AddComponent<TextMeshPro>();
            label.text = message;
            label.fontSize = fontSize;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false;
            label.color = color;
            label.sortingOrder = SortingOrder + 1;
            label.rectTransform.sizeDelta = new Vector2(4f, 1f);

            var vfx = go.AddComponent<BattleVfx>();
            vfx.text = label;
            vfx.color = color;
            vfx.startPosition = go.transform.position;
            vfx.rise = new Vector3(0f, 0.45f, 0f);
            vfx.scaleFrom = vfx.scaleTo = Vector3.one;
            vfx.duration = Mathf.Max(0.01f, duration);
        }

        private static void Spawn(Sprite sprite, Vector2 position, Quaternion rotation, Vector3 fromScale, Vector3 toScale, Color color, float duration)
        {
            var go = new GameObject("Vfx");
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = fromScale;

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = SortingOrder;

            var vfx = go.AddComponent<BattleVfx>();
            vfx.spriteRenderer = renderer;
            vfx.color = color;
            vfx.startPosition = go.transform.position;
            vfx.scaleFrom = fromScale;
            vfx.scaleTo = toScale;
            vfx.duration = Mathf.Max(0.01f, duration);
        }

        private void Update()
        {
            time += Time.deltaTime;
            float t = Mathf.Clamp01(time / duration);
            transform.localScale = Vector3.Lerp(scaleFrom, scaleTo, t);
            transform.position = startPosition + rise * t;

            Color current = color;
            current.a *= text != null ? 1f - t * t : 1f - t; // текст держится дольше и гаснет в конце
            if (spriteRenderer != null) spriteRenderer.color = current;
            if (text != null) text.color = current;

            if (t >= 1f) Destroy(gameObject);
        }
    }
}
