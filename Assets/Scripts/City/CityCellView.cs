using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Casiwar
{
    /// <summary>
    /// Клетка карты города (UI): здание или пустое место «+». Клик выбирает клетку в CityMapView.
    /// Префаб: Image (фон) + CityCellView, дочерние Title, Detail (TextMeshPro) и HpBar (Image Filled).
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
            AttentionPulse.Set(background, building != null && upgradable);
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
            background.color = selected ? Color.Lerp(color, selectedTint, 0.45f) : color;
            title.text = building.Config.shortLabel;
            title.color = GameVisuals.ContrastText(color);

            int upgrades = building.Upgrades.Count;
            string stars = note ?? (upgrades > 0 ? GameVisuals.Stars(upgrades) : string.Empty);
            string hp = building.IsDamaged ? $"{building.Hp}/{building.MaxHp}" : string.Empty;
            detail.text = building.IsRuined ? "РАЗРУШЕНО" : $"{stars} {hp}".Trim();
            detail.color = building.IsRuined ? new Color(1f, 0.45f, 0.4f) : GameVisuals.ContrastText(color);

            hpBar.enabled = building.IsDamaged;
            float share = building.MaxHp > 0 ? building.Hp / (float)building.MaxHp : 0f;
            hpBar.fillAmount = share;
            hpBar.color = Color.Lerp(new Color(0.95f, 0.3f, 0.25f), new Color(0.35f, 0.9f, 0.4f), share);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (owner != null) owner.Select(cell);
        }
    }
}
