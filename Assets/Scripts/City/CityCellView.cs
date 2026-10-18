using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Casiwar
{
    /// <summary>
    /// Клетка карты города (UI): здание или пустое место «+». Клик выбирает клетку в CityMapView.
    /// Префаб: Image (фон) + CityCellView, дочерние Title, Detail (TextMeshPro) и HpBar (Image Filled).
    /// Есть рисунок здания (UnitArt.Building) — он стоит прямо на местности: фон прозрачный (выбранная клетка — светлая
    /// плашка), названия нет (оно в панели справа), звёзды/жители/прочность — белым с обводкой поверх рисунка,
    /// разрушенное — серое, «можно улучшить» — золотая пульсация по силуэту. Чтобы здание не терялось на местности —
    /// мягкая тень под основанием и падающая тень от силуэта (свет сверху слева). Эти слои создаются сами.
    /// </summary>
    public class CityCellView : MonoBehaviour, IPointerClickHandler
    {
        public Image background;
        public TMP_Text title;
        public TMP_Text detail;
        [Tooltip("Полоска прочности (Image типа Filled, Horizontal)")]
        public Image hpBar;

        public Color emptyColor = new Color(0.15f, 0.17f, 0.22f);
        public Color selectedTint = new Color(1f, 0.92f, 0.55f);

        private CityMapView owner;
        private Vector2Int cell;
        private Image art;
        private Image groundShadow;

        public void Init(CityMapView map, Vector2Int mapCell)
        {
            owner = map;
            cell = mapCell;
            name = $"CityCell_{mapCell.x}_{mapCell.y}";
            if (background == null) background = GetComponent<Image>();
        }

        /// <param name="note">Подпись вместо звёзд улучшений (например, жители дома «👤3»).</param>
        /// <param name="upgradable">В здании можно что-то купить прямо сейчас — клетка пульсирует золотой обводкой.</param>
        public void Bind(Building building, bool selected, string note = null, bool upgradable = false)
        {
            Sprite sprite = building != null ? UnitArt.Building(building.Type) : null;
            bool hasArt = sprite != null;
            if (hasArt || art != null) EnsureArt();
            if (art != null)
            {
                art.enabled = hasArt;
                groundShadow.enabled = hasArt;
                art.sprite = sprite;
                art.color = building != null && building.IsRuined ? new Color(0.38f, 0.36f, 0.36f) : Color.white;
            }
            AttentionPulse.Set(background, !hasArt && building != null && upgradable);
            AttentionPulse.Set(art, hasArt && upgradable);
            detail.outlineWidth = hasArt ? 0.25f : 0f;
            detail.outlineColor = Color.black;

            if (building == null)
            {
                background.color = selected ? Color.Lerp(emptyColor, selectedTint, 0.35f) : emptyColor;
                title.text = "+";
                title.color = new Color(1f, 1f, 1f, 0.35f);
                detail.text = string.Empty;
                hpBar.enabled = false;
                return;
            }

            Color color = building.Config.color;
            if (building.IsRuined) color = Color.Lerp(color, Color.black, 0.65f);
            if (hasArt) background.color = selected ? new Color(selectedTint.r, selectedTint.g, selectedTint.b, 0.35f) : Color.clear;
            else background.color = selected ? Color.Lerp(color, selectedTint, 0.45f) : color;
            title.text = hasArt ? string.Empty : building.Config.shortLabel;
            title.color = GameVisuals.ContrastText(color);

            int upgrades = building.Upgrades.Count;
            string stars = note ?? (upgrades > 0 ? GameVisuals.Stars(upgrades) : string.Empty);
            string hp = building.IsDamaged ? $"{building.Hp}/{building.MaxHp}" : string.Empty;
            detail.text = building.IsRuined ? "РАЗРУШЕНО" : $"{stars} {hp}".Trim();
            detail.color = building.IsRuined ? new Color(1f, 0.45f, 0.4f) : hasArt ? Color.white : GameVisuals.ContrastText(color);

            hpBar.enabled = building.IsDamaged;
            float share = building.MaxHp > 0 ? building.Hp / (float)building.MaxHp : 0f;
            hpBar.fillAmount = share;
            hpBar.color = Color.Lerp(new Color(0.95f, 0.3f, 0.25f), new Color(0.35f, 0.9f, 0.4f), share);
        }

        /// <summary>Слой рисунка здания с тенями — под подписями и полоской прочности (в старых префабах его нет).</summary>
        private void EnsureArt()
        {
            if (art != null) return;

            // Пятно тени под основанием: здание стоит в нижней половине клетки, свет — сверху слева
            var shadowGo = new GameObject("GroundShadow", typeof(RectTransform));
            shadowGo.layer = gameObject.layer;
            var shadowRect = (RectTransform)shadowGo.transform;
            shadowRect.SetParent(transform, false);
            shadowRect.SetAsFirstSibling();
            shadowRect.anchorMin = new Vector2(0.06f, -0.04f);
            shadowRect.anchorMax = new Vector2(0.98f, 0.42f);
            shadowRect.offsetMin = shadowRect.offsetMax = Vector2.zero;
            shadowRect.anchoredPosition = new Vector2(4f, -2f);
            groundShadow = shadowGo.AddComponent<Image>();
            groundShadow.sprite = GameVisuals.SoftCircle;
            groundShadow.color = new Color(0f, 0f, 0f, 0.5f);
            groundShadow.raycastTarget = false;

            var go = new GameObject("Art", typeof(RectTransform));
            go.layer = gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(transform, false);
            rect.SetSiblingIndex(1); // над тенью, под подписями
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(-5f, -5f); // рисунок на всю клетку карты, включая зазор между плашками
            rect.offsetMax = new Vector2(5f, 5f);
            art = go.AddComponent<Image>();
            art.preserveAspect = true;
            art.raycastTarget = false;
            // Падающая тень от силуэта: тёмная копия рисунка со сдвигом вправо-вниз
            var drop = go.AddComponent<Shadow>();
            drop.effectColor = new Color(0f, 0f, 0f, 0.55f);
            drop.effectDistance = new Vector2(4f, -3f);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            // Отпустили после перетаскивания карты — не клик; выбирает (или ведёт армию) только левая кнопка
            if (owner == null || eventData.dragging || eventData.button != PointerEventData.InputButton.Left) return;
            owner.ClickCell(cell);
        }
    }
}
