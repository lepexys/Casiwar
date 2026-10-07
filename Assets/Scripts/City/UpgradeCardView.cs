using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Casiwar
{
    /// <summary>
    /// Карточка улучшения отряда на вкладке «Войска». Префаб: Image (фон) + UpgradeCardView,
    /// дочерние Title, Description (TextMeshPro) и Button с TextMeshPro внутри.
    /// Ключ яруса (щиты, арбалеты, посохи) выделен цветом и подписью «открывает ярус».
    /// </summary>
    public class UpgradeCardView : MonoBehaviour
    {
        public Image background;
        public TMP_Text title;
        public TMP_Text description;
        public Button button;
        public TMP_Text buttonLabel;

        public Color boughtColor = new Color(0.20f, 0.36f, 0.24f);
        public Color availableColor = new Color(0.21f, 0.24f, 0.32f);
        public Color lockedColor = new Color(0.13f, 0.14f, 0.18f);
        public Color gateColor = new Color(0.46f, 0.36f, 0.14f);
        public Color gateLockedColor = new Color(0.26f, 0.21f, 0.11f);

        public UnitClass Branch { get; private set; }
        public string UpgradeId { get; private set; }

        public void Init(UnitTreeView tree, UnitClass branch, string upgradeId)
        {
            Branch = branch;
            UpgradeId = upgradeId;
            name = $"Upgrade_{upgradeId}";
            if (background == null) background = GetComponent<Image>();
            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => tree.Buy(branch, upgradeId));
            }
        }

        /// <param name="reason">Почему нельзя купить (null — можно).</param>
        /// <param name="onlyGold">Не хватает только золота — на кнопке остаётся цена.</param>
        public void Bind(UpgradeConfig upgrade, bool bought, string reason, bool onlyGold)
        {
            bool open = bought || reason == null || onlyGold;
            if (background != null)
            {
                background.color = bought ? boughtColor
                    : upgrade.gate ? (open ? gateColor : gateLockedColor)
                    : open ? availableColor : lockedColor;
            }
            if (title != null)
            {
                title.text = upgrade.gate ? $"{upgrade.title} · ключ" : upgrade.title;
                title.color = open ? Color.white : new Color(1f, 1f, 1f, 0.55f);
            }
            if (description != null)
                description.text = upgrade.gate ? $"{upgrade.description}. Открывает ярус {upgrade.tier + 1}" : upgrade.description;

            if (button != null) button.interactable = !bought && reason == null;
            if (buttonLabel != null)
            {
                buttonLabel.text = bought ? "Куплено"
                    : reason == null || onlyGold ? $"Купить · {upgrade.goldCost} {GameVisuals.IconGold}"
                    : reason;
            }
        }
    }
}
