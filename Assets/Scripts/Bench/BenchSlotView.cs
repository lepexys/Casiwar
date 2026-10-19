using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Casiwar
{
    /// <summary>
    /// Визуал одного слота армии (UI): место бойца или вожака в верхней или нижней линейке панели.
    /// Клик и перетаскивание бойца передаются в BenchManager.
    /// Префаб: Image (фон слота) + BenchSlotView, дочерние Unit (Image), Label и Level (TextMeshPro),
    /// значок звёзд StarsBadge (Image + текст «★★») и значки снаряжения ArmorBadge и WeaponBadge
    /// (Image; цвет = уровень предмета).
    /// Если подписи уровня в префабе нет (сцена собрана старой версией), она создаётся сама.
    /// </summary>
    public class BenchSlotView : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [Tooltip("Фон слота. Если пусто — берётся Image на этом объекте.")]
        public Image background;
        [Tooltip("Изображение юнита (фигурка из UnitArt / UnitData или цветной круг-заглушка)")]
        public Image unitImage;
        [Tooltip("Буква класса поверх заглушки")]
        public TMP_Text label;
        [Tooltip("Подпись уровня: «ур.3»")]
        public TMP_Text levelText;
        [Tooltip("Звёзды «★★» (если пусто — пишутся рядом с уровнем)")]
        public TMP_Text starsText;
        [Tooltip("Подложка звёзд")]
        public Image starsBackground;
        [Tooltip("Значок надетой брони")]
        public Image armorBadge;
        [Tooltip("Значок надетого оружия")]
        public Image weaponBadge;

        public Color emptyColor = new Color(0.19f, 0.21f, 0.27f);
        public Color filledColor = new Color(0.27f, 0.30f, 0.38f);
        public Color selectedColor = new Color(0.96f, 0.78f, 0.30f);
        public Color levelColor = new Color(1f, 0.82f, 0.30f);
        [Tooltip("Цвет уровня на выбранном (жёлтом) слоте")]
        public Color selectedLevelColor = new Color(0.16f, 0.12f, 0.06f);
        [Tooltip("Подпись пустого слота (у слота вожака — «вожак»)")]
        public string emptyCaption = string.Empty;

        private BenchManager owner;
        private PanelRow row;
        private int index;

        public BenchManager Owner => owner;
        public PanelRow Row => row;
        /// <summary>Место в линейке: ArmyRow.LeaderIndex — вожак, 0… — бойцы.</summary>
        public int Index => index;

        public void Init(BenchManager bench, PanelRow panelRow, int slotIndex)
        {
            owner = bench;
            row = panelRow;
            index = slotIndex;
            name = slotIndex == ArmyRow.LeaderIndex ? $"BenchSlot_{panelRow}_Leader" : $"BenchSlot_{panelRow}_{slotIndex + 1}";
            if (background == null) background = GetComponent<Image>();
            if (levelText == null) levelText = FindOrCreateLevelText();
        }

        public void Bind(BenchUnit unit, bool selected)
        {
            bool hasUnit = unit != null && unit.data != null;
            Sprite figure = hasUnit ? UnitArt.Figure(unit.data, unit.stars, owner != null && owner.HasCrossbows, hero: unit.hero) : null;
            bool hasSprite = figure != null;

            if (background != null) background.color = selected ? selectedColor : (hasUnit ? filledColor : emptyColor);
            if (unitImage != null)
            {
                unitImage.enabled = hasUnit;
                if (hasUnit)
                {
                    unitImage.sprite = hasSprite ? figure : GameVisuals.Circle;
                    unitImage.color = hasSprite ? Color.white : GameVisuals.ClassColor(unit.data.unitClass);
                    unitImage.preserveAspect = true;
                }
            }
            if (label != null)
            {
                label.text = hasUnit && !hasSprite ? GameVisuals.ClassLetter(unit.data.unitClass) : string.Empty;
            }

            string stars = hasUnit ? GameVisuals.Stars(unit.stars) : string.Empty;
            if (levelText != null)
            {
                string prefix = hasUnit && starsText == null ? stars + " " : string.Empty;
                levelText.text = hasUnit ? $"{prefix}ур.{unit.level}" : emptyCaption;
                levelText.color = selected ? selectedLevelColor : hasUnit ? levelColor : new Color(levelColor.r, levelColor.g, levelColor.b, 0.55f);
            }
            if (starsText != null) starsText.text = stars;
            if (starsBackground != null) starsBackground.enabled = hasUnit;

            BindBadge(armorBadge, hasUnit ? unit.armor : null, GameVisuals.Shield);
            BindBadge(weaponBadge, hasUnit ? unit.weapon : null, GameVisuals.Sword);
        }

        /// <summary>Значок надетого: иконка предмета нужного уровня (UnitArt.Gear), а без неё — щит/меч цвета уровня.</summary>
        private static void BindBadge(Image badge, EquippedItem item, Sprite sprite)
        {
            if (badge == null) return;
            bool show = EquippedItem.IsValid(item);
            badge.enabled = show;
            if (!show)
            {
                // Предмета нет (боец ушёл, погиб, пришёл другой) — прячем и кружок, оставшийся от прошлого бойца
                Image stale = Backdrop(badge, create: false);
                if (stale != null) stale.enabled = false;
                return;
            }
            Sprite gear = UnitArt.Gear(item.data.unitClass, item.data.slot, item.tier);
            badge.sprite = gear != null ? gear : sprite;
            badge.color = gear != null ? Color.white : GameVisuals.TierColor(item.tier);
            badge.preserveAspect = true;
            if (gear != null) badge.rectTransform.sizeDelta = new Vector2(42f, 42f); // у рисунка мелкие детали — чуть крупнее
            Image backdrop = Backdrop(badge, create: gear != null);
            if (backdrop != null) backdrop.enabled = gear != null;
        }

        /// <summary>Тёмный кружок под значком-рисунком: тонкий лук или посох не теряется на фигурке юнита. create = false — только найти.</summary>
        private static Image Backdrop(Image badge, bool create)
        {
            Transform parent = badge.transform.parent;
            string backdropName = badge.name + "Backdrop";
            Transform existing = parent.Find(backdropName);
            if (existing != null) return existing.GetComponent<Image>();
            if (!create) return null;

            var go = new GameObject(backdropName, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            RectTransform source = badge.rectTransform;
            rect.SetParent(parent, false);
            rect.anchorMin = source.anchorMin;
            rect.anchorMax = source.anchorMax;
            rect.pivot = source.pivot;
            rect.anchoredPosition = source.anchoredPosition;
            rect.sizeDelta = new Vector2(42f, 42f);
            rect.SetSiblingIndex(source.GetSiblingIndex()); // под значком
            var image = go.GetComponent<Image>();
            image.sprite = GameVisuals.Circle;
            image.color = new Color(0.05f, 0.06f, 0.09f, 0.8f);
            image.raycastTarget = false;
            return image;
        }

        /// <summary>Подпись уровня из префаба («Level», в старых сценах — «Stars»), а если её нет — новая внизу слота.</summary>
        private TMP_Text FindOrCreateLevelText()
        {
            foreach (string childName in new[] { "Level", "Stars" })
            {
                Transform child = transform.Find(childName);
                TMP_Text existing = child != null ? child.GetComponent<TMP_Text>() : null;
                if (existing != null) return existing;
            }

            var go = new GameObject("Level", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(transform, false);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 4f);
            rect.sizeDelta = new Vector2(-8f, 36f);
            var text = go.AddComponent<TextMeshProUGUI>();
            if (label != null) text.font = label.font;
            text.enableAutoSizing = true;
            text.fontSizeMin = 14f;
            text.fontSizeMax = 26f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            return text;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (owner != null) owner.OnSlotClicked(row, index);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (owner != null) owner.BeginUnitDrag(row, index, eventData);
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
