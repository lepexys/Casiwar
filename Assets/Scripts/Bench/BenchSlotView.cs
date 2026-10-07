using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Casiwar
{
    /// <summary>
    /// Визуал одного слота скамейки (UI). Клик передаётся в BenchManager.
    /// Префаб: Image (фон слота) + BenchSlotView, дочерние Unit (Image), Label и Level (TextMeshPro),
    /// значок звёзд StarsBadge (Image + текст «★★») и значки снаряжения ArmorBadge и WeaponBadge
    /// (Image; цвет = уровень предмета).
    /// Если подписи уровня в префабе нет (сцена собрана старой версией), она создаётся сама.
    /// </summary>
    public class BenchSlotView : MonoBehaviour, IPointerClickHandler
    {
        [Tooltip("Фон слота. Если пусто — берётся Image на этом объекте.")]
        public Image background;
        [Tooltip("Изображение юнита (спрайт из UnitData или цветной круг-заглушка)")]
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

        private BenchManager owner;
        private int index;

        public void Init(BenchManager bench, int slotIndex)
        {
            owner = bench;
            index = slotIndex;
            name = $"BenchSlot_{slotIndex + 1}";
            if (background == null) background = GetComponent<Image>();
            if (levelText == null) levelText = FindOrCreateLevelText();
        }

        public void Bind(BenchUnit unit, bool selected)
        {
            bool hasUnit = unit != null && unit.data != null;
            bool hasSprite = hasUnit && unit.data.sprite != null;

            if (background != null) background.color = selected ? selectedColor : (hasUnit ? filledColor : emptyColor);
            if (unitImage != null)
            {
                unitImage.enabled = hasUnit;
                if (hasUnit)
                {
                    unitImage.sprite = hasSprite ? unit.data.sprite : GameVisuals.Circle;
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
                levelText.text = hasUnit ? $"{prefix}ур.{unit.level}" : string.Empty;
                levelText.color = selected ? selectedLevelColor : levelColor;
            }
            if (starsText != null) starsText.text = stars;
            if (starsBackground != null) starsBackground.enabled = hasUnit;

            BindBadge(armorBadge, hasUnit ? unit.armor : null, GameVisuals.Shield);
            BindBadge(weaponBadge, hasUnit ? unit.weapon : null, GameVisuals.Sword);
        }

        private static void BindBadge(Image badge, EquippedItem item, Sprite sprite)
        {
            if (badge == null) return;
            bool show = EquippedItem.IsValid(item);
            badge.enabled = show;
            if (!show) return;
            badge.sprite = sprite;
            badge.color = GameVisuals.TierColor(item.tier);
            badge.preserveAspect = true;
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
            if (owner != null) owner.OnSlotClicked(index);
        }
    }
}
