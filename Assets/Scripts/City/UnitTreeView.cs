using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Casiwar
{
    /// <summary>
    /// Вкладка «Войска»: прокачка отрядов за золото, как в tower defense. Три ветки — по гильдиям:
    ///   Ближний бой (казармы, воины), Дальний бой (стрельбище, лучники), Магия (святилище, жрецы).
    /// Улучшения — это улучшения здания-гильдии: работают, пока гильдия цела. Ветка идёт ярусами:
    /// следующий ярус открывает «ключ» (щиты, арбалеты, посохи) — ему нужны своя наука и 2 улучшения яруса.
    /// Здесь же открываются особые символы боевого слота: Знамя (джокер), ×2 и Свечи («Чудо»).
    /// </summary>
    public class UnitTreeView : MonoBehaviour
    {
        private static readonly UnitClass[] Branches = { UnitClass.Warrior, UnitClass.Archer, UnitClass.Priest };

        [Header("Ссылки")]
        public CityManager city;
        public TechTree techTree;
        public GameManager game;
        [Tooltip("Колонки веток: 0 — ближний бой, 1 — дальний бой, 2 — магия (VerticalLayoutGroup)")]
        public RectTransform[] columns = new RectTransform[3];
        [Tooltip("Строка под заголовком ветки: есть ли гильдия, сколько куплено")]
        public TMP_Text[] columnStatus = new TMP_Text[3];
        public UpgradeCardView cardPrefab;
        public TMP_Text summaryText;
        [Min(4f)] public float tierLabelHeight = 26f;

        private readonly List<UpgradeCardView> cards = new List<UpgradeCardView>();

        private void Awake()
        {
            if (city == null) city = FindFirstObjectByType<CityManager>();
            if (techTree == null) techTree = FindFirstObjectByType<TechTree>();
            if (game == null) game = FindFirstObjectByType<GameManager>();
            BuildCards();
            if (city != null) city.Changed += Refresh;
            if (techTree != null) techTree.Changed += Refresh;
        }

        private void OnDestroy()
        {
            if (city != null) city.Changed -= Refresh;
            if (techTree != null) techTree.Changed -= Refresh;
        }

        private void OnEnable() => Refresh();

        /// <summary>Кнопка карточки: купить улучшение в гильдии ветки.</summary>
        public void Buy(UnitClass branch, string upgradeId)
        {
            if (city == null) return;
            Building guild = city.Guild(branch);
            string message;
            if (guild == null) message = $"Сначала постройте: {city.GuildConfig(branch)?.title}";
            else city.TryUpgrade(guild, upgradeId, out message);
            if (game != null && !string.IsNullOrEmpty(message)) game.ShowMessage(message);
        }

        private void BuildCards()
        {
            if (city == null || cardPrefab == null) return;
            for (int i = 0; i < Branches.Length && i < columns.Length; i++)
            {
                BuildingConfig guild = city.GuildConfig(Branches[i]);
                if (guild == null || columns[i] == null) continue;
                int tier = 0;
                foreach (UpgradeConfig upgrade in guild.upgrades.OrderBy(u => u.tier))
                {
                    if (upgrade.tier != tier)
                    {
                        tier = upgrade.tier;
                        AddTierLabel(columns[i], tier);
                    }
                    UpgradeCardView card = Instantiate(cardPrefab, columns[i]);
                    card.Init(this, Branches[i], upgrade.id);
                    cards.Add(card);
                }
            }
        }

        private void AddTierLabel(RectTransform column, int tier)
        {
            var go = new GameObject($"Tier_{tier}", typeof(RectTransform), typeof(LayoutElement));
            go.layer = column.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(column, false);
            rect.sizeDelta = new Vector2(0f, tierLabelHeight);
            go.GetComponent<LayoutElement>().preferredHeight = tierLabelHeight;
            var label = go.AddComponent<TextMeshProUGUI>();
            if (summaryText != null) label.font = summaryText.font;
            label.text = $"— ярус {Roman(tier)} —";
            label.fontSize = 20f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(0.75f, 0.78f, 0.85f, 0.8f);
            label.raycastTarget = false;
        }

        private static string Roman(int value) => value == 1 ? "I" : value == 2 ? "II" : value == 3 ? "III" : value.ToString();

        public void Refresh()
        {
            if (!isActiveAndEnabled || city == null) return;
            if (summaryText != null)
            {
                summaryText.text = $"Золото: {city.Gold} {GameVisuals.IconGold} · улучшения отрядов работают, пока цела их гильдия.\n" +
                                   "Ключ яруса (щиты, арбалеты, посохи) открывает следующий ярус: нужна его наука и 2 улучшения яруса";
            }

            for (int i = 0; i < Branches.Length && i < columnStatus.Length; i++)
            {
                if (columnStatus[i] == null) continue;
                BuildingConfig config = city.GuildConfig(Branches[i]);
                Building guild = city.Guild(Branches[i]);
                if (config == null) columnStatus[i].text = string.Empty;
                else if (guild == null)
                    columnStatus[i].text = city.IsUnlocked(config) ? $"Постройте: {config.title}" : $"Нужна наука: {city.RequiredTechTitles(config)}";
                else if (guild.IsRuined) columnStatus[i].text = $"{config.title} разрушено — почините";
                else columnStatus[i].text = $"{config.title}: куплено {guild.Upgrades.Count}/{config.upgrades.Count}";
            }

            foreach (UpgradeCardView card in cards)
            {
                BuildingConfig config = city.GuildConfig(card.Branch);
                UpgradeConfig upgrade = config?.upgrades.FirstOrDefault(u => u.id == card.UpgradeId);
                if (upgrade == null) continue;
                Building guild = city.Guild(card.Branch);
                if (guild == null)
                {
                    card.Bind(upgrade, false, $"Нет здания: {config.title}", false);
                    continue;
                }
                bool bought = guild.Upgrades.Contains(upgrade.id);
                string reason = bought ? null : city.WhyCannotUpgrade(guild, upgrade);
                bool onlyGold = reason != null && city.Gold < upgrade.goldCost && reason.StartsWith("Нужно ") && reason.Contains(GameVisuals.IconGold);
                card.Bind(upgrade, bought, reason, onlyGold);
            }
        }
    }
}
