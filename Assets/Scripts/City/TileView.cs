using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Casiwar
{
    /// <summary>Кто принимает клики/свайпы по клеткам (поле города, поле трофеев).</summary>
    public interface ITileInputHandler
    {
        void OnTileClicked(Vector2Int cell);
        void OnTileSwiped(Vector2Int cell, Vector2Int direction);
    }

    /// <summary>
    /// Визуал одной клетки (UI). Общий для поля города, поля трофеев и боевого слота.
    /// Клетка никуда не переезжает: при обмене/падении она показывает новый символ и «доезжает»
    /// до своей позиции (Home) из стартовой точки.
    /// Префаб: Image (фон) + TileView, дочерние Icon (Image) и Label (TextMeshPro).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class TileView : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [Tooltip("Фон-форма символа. Если пусто — берётся Image на этом объекте.")]
        public Image background;
        [Tooltip("Иконка (спрайт юнита/предмета). Скрывается, если спрайта нет.")]
        public Image icon;
        [Tooltip("Подпись-заглушка: буква класса, символ и т. п.")]
        public TMP_Text label;

        private ITileInputHandler owner;
        private RectTransform rect;
        private CanvasGroup group;
        private Vector2 home;
        private float moveSpeed = 1000f; // px/сек
        private bool selected;
        private bool hinted;
        private bool popping;
        private float popTime;
        private float popDuration = 0.2f;
        private bool swipeUsed;

        public Vector2Int Coords { get; private set; }
        public Vector2 Home => home;
        public bool IsSettled => rect == null || (rect.anchoredPosition - home).sqrMagnitude < 0.01f;

        private void Awake()
        {
            rect = (RectTransform)transform;
            if (background == null) background = GetComponent<Image>();
            group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
        }

        /// <summary>handler может быть null — тогда клетка не реагирует на ввод (боевой слот).</summary>
        public void Init(ITileInputHandler handler, Vector2Int coords)
        {
            if (rect == null) Awake();
            owner = handler;
            Coords = coords;
            name = $"Tile_{coords.x}_{coords.y}";
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
        }

        /// <summary>Задать «домашнюю» позицию и размер (вызывается при раскладке поля).</summary>
        public void Place(Vector2 homePosition, float tileSize, float speed)
        {
            home = homePosition;
            moveSpeed = Mathf.Max(1f, speed);
            rect.sizeDelta = new Vector2(tileSize, tileSize);
            rect.anchoredPosition = homePosition;
        }

        public void SetMoveSpeed(float speed) => moveSpeed = Mathf.Max(1f, speed);

        /// <summary>Мгновенно сдвинуть визуал в точку start — дальше он сам доедет до Home.</summary>
        public void AnimateFrom(Vector2 start) => rect.anchoredPosition = start;

        public void SetSelected(bool value) => selected = value;
        public void SetHint(bool value) => hinted = value;
        public void SetDimmed(bool value) => group.alpha = value ? 0.3f : 1f;

        public void PlayPop(float duration)
        {
            popping = true;
            popTime = 0f;
            popDuration = Mathf.Max(0.01f, duration);
        }

        /// <summary>Показать фишку поля (null — пустая клетка).</summary>
        public void Bind(TileData tile)
        {
            if (tile == null) Hide();
            else Show(GameVisuals.LookFor(tile));
        }

        public void Show(SymbolLook look)
        {
            ResetState();
            if (background != null)
            {
                background.enabled = true;
                background.sprite = look.shape;
                background.type = look.sliced ? Image.Type.Sliced : Image.Type.Simple;
                background.color = look.color;
            }
            if (icon != null)
            {
                icon.enabled = look.icon != null;
                icon.sprite = look.icon;
                icon.preserveAspect = true;
            }
            if (label != null)
            {
                label.text = look.label ?? string.Empty;
                label.color = GameVisuals.ContrastText(look.color);
                float inset = rect.rect.width * look.labelInset;
                label.margin = new Vector4(inset, inset * 0.5f, inset, inset * 0.5f);
            }
        }

        /// <summary>Пустая клетка: ничего не видно и по ней нельзя кликнуть.</summary>
        public void Hide()
        {
            ResetState();
            if (background != null) background.enabled = false;
            if (icon != null) icon.enabled = false;
            if (label != null) label.text = string.Empty;
        }

        private void ResetState()
        {
            popping = false;
            selected = false;
            hinted = false;
            group.alpha = 1f;
            transform.localScale = Vector3.one;
        }

        private void Update()
        {
            // Доезжаем до своей клетки (обмен, падение, досыпание)
            if (rect.anchoredPosition != home)
                rect.anchoredPosition = Vector2.MoveTowards(rect.anchoredPosition, home, moveSpeed * Time.deltaTime);

            // Масштаб: лопание, выбор, пульсация подсказки
            float scale = 1f;
            if (popping)
            {
                popTime += Time.deltaTime;
                scale = 1f - Mathf.Clamp01(popTime / popDuration);
            }
            else if (selected)
            {
                scale = 1.12f;
            }
            else if (hinted)
            {
                scale = 1f + 0.08f * (0.5f + 0.5f * Mathf.Sin(Time.time * 9f));
            }
            transform.localScale = new Vector3(scale, scale, 1f);
        }

        // ---------- Ввод ----------

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.dragging || owner == null) return; // клик после свайпа не считаем
            owner.OnTileClicked(Coords);
        }

        public void OnBeginDrag(PointerEventData eventData) => swipeUsed = false;

        public void OnDrag(PointerEventData eventData)
        {
            if (swipeUsed || owner == null) return;

            Vector2 delta = eventData.position - eventData.pressPosition;
            float tileOnScreen = rect.rect.width * rect.lossyScale.x;
            if (delta.magnitude < Mathf.Max(20f, tileOnScreen * 0.3f)) return;

            swipeUsed = true;
            // Экранная ось Y смотрит вверх, а строки поля растут вниз
            Vector2Int direction = Mathf.Abs(delta.x) > Mathf.Abs(delta.y)
                ? new Vector2Int(delta.x > 0f ? 1 : -1, 0)
                : new Vector2Int(0, delta.y > 0f ? -1 : 1);
            owner.OnTileSwiped(Coords, direction);
        }

        public void OnEndDrag(PointerEventData eventData) { }
    }
}
