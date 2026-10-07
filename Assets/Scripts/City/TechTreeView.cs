using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Casiwar
{
    /// <summary>
    /// Вкладка «Наука»: единое дерево технологий в четырёх колонках (Хозяйство, Знания, Война, Вера).
    /// Технологии изучаются за очки знаний 📖 и открывают здания (строятся за ⚒) и улучшения (за 🪙)
    /// на карте города. Если технологии нужна карточка прямо над ней, их соединяет стрелка — цепочка;
    /// требования из других колонок написаны на кнопке («Нужно: Мистицизм»).
    /// </summary>
    public class TechTreeView : MonoBehaviour
    {
        [Header("Ссылки")]
        public TechTree techTree;
        public GameManager game;
        public CityManager city;
        [Tooltip("Колонки по TechBranch: 0 — Хозяйство, 1 — Знания, 2 — Война, 3 — Вера (VerticalLayoutGroup без отступов)")]
        public RectTransform[] columns = new RectTransform[4];
        public TechCardView cardPrefab;
        public TMP_Text summaryText;

        [Header("Связи")]
        [Min(4f)] public float linkHeight = 22f;
        [Min(1f)] public float linkWidth = 6f;
        public Color linkLockedColor = new Color(0.30f, 0.32f, 0.38f);
        public Color linkOpenColor = new Color(0.75f, 0.78f, 0.86f);
        public Color linkDoneColor = new Color(0.45f, 0.80f, 0.50f);

        private readonly List<TechCardView> cards = new List<TechCardView>();
        private readonly List<(string from, string to, Image bar)> links = new List<(string from, string to, Image bar)>();

        private void Awake()
        {
            if (techTree == null) techTree = FindFirstObjectByType<TechTree>();
            if (game == null) game = FindFirstObjectByType<GameManager>();
            if (city == null) city = FindFirstObjectByType<CityManager>();
            BuildCards();
            if (techTree != null) techTree.Changed += Refresh;
            if (city != null) city.Changed += Refresh;
        }

        private void OnDestroy()
        {
            if (techTree != null) techTree.Changed -= Refresh;
            if (city != null) city.Changed -= Refresh;
        }

        private void OnEnable() => Refresh();

        public void Research(string techId)
        {
            if (game != null) game.TryResearch(techId);
        }

        private void BuildCards()
        {
            if (techTree == null || cardPrefab == null) return;
            techTree.EnsureDefaults(); // новые технологии — и в сценах старой версии
            for (int column = 0; column < columns.Length; column++)
            {
                if (columns[column] == null) continue;
                TechConfig previous = null;
                foreach (TechConfig tech in techTree.techs.Where(t => t != null && (int)t.branch == column))
                {
                    // Стрелка — если технологии нужна карточка прямо над ней; иначе просто отступ
                    if (previous != null) AddGap(columns[column], tech.requires != null && tech.requires.Contains(previous.id) ? previous.id : null, tech.id);
                    TechCardView card = Instantiate(cardPrefab, columns[column]);
                    card.Init(this, tech.id);
                    cards.Add(card);
                    previous = tech;
                }
            }
        }

        private void AddGap(RectTransform column, string fromId, string toId)
        {
            var gap = new GameObject(fromId != null ? $"Link_{fromId}_{toId}" : "Gap", typeof(RectTransform), typeof(LayoutElement));
            gap.layer = column.gameObject.layer;
            gap.transform.SetParent(column, false);
            ((RectTransform)gap.transform).sizeDelta = new Vector2(0f, linkHeight); // колонка не управляет высотой детей
            gap.GetComponent<LayoutElement>().preferredHeight = linkHeight;
            if (fromId == null) return;

            var barGo = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            barGo.layer = gap.layer;
            var bar = (RectTransform)barGo.transform;
            bar.SetParent(gap.transform, false);
            bar.anchorMin = new Vector2(0.5f, 0f);
            bar.anchorMax = new Vector2(0.5f, 1f);
            bar.sizeDelta = new Vector2(linkWidth, 0f);
            var image = barGo.GetComponent<Image>();
            image.raycastTarget = false;
            links.Add((fromId, toId, image));
        }

        public void Refresh()
        {
            if (!isActiveAndEnabled || techTree == null) return;
            int knowledge = city != null ? city.Knowledge : 0;
            if (summaryText != null && city != null)
            {
                int bonus = Mathf.RoundToInt((city.KnowledgeMultiplier - 1f) * 100f);
                summaryText.text =
                    $"Очки знаний: {knowledge} {GameVisuals.IconKnowledge} · каждое утро +{city.KnowledgePerDay} " +
                    $"(жители и ратуша {city.KnowledgeBase}{(bonus > 0 ? $", школа +{bonus}%" : string.Empty)})\n" +
                    $"Изученное открывает здания ({GameVisuals.IconProduction}) и улучшения ({GameVisuals.IconGold}) на карте города";
            }

            foreach (TechCardView card in cards)
            {
                TechConfig tech = techTree.Find(card.TechId);
                if (tech == null) continue;
                TechState state = techTree.IsResearched(tech.id) ? TechState.Researched
                    : techTree.PrerequisitesMet(tech) ? TechState.Available : TechState.Locked;
                card.Bind(tech, state, knowledge >= tech.knowledgeCost, techTree.MissingPrerequisites(tech));
            }

            foreach ((string from, string to, Image bar) in links)
            {
                bar.color = techTree.IsResearched(to) ? linkDoneColor : techTree.IsResearched(from) ? linkOpenColor : linkLockedColor;
            }
        }
    }
}
