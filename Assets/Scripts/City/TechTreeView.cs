using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Casiwar
{
    /// <summary>
    /// Вкладка «Наука» — дерево технологий (SkillTreeGraph), как дерево прокачки в tower defense: пять веток-столбцов
    /// (Хозяйство, Знания, Пехота, Стрелки, Вера), строка — глубина технологии (сколько всего нужно изучить до неё).
    /// Связи — кривые от нужных технологий цветом своей ветки; наведи на узел — его связи подсветятся: видно, как одна
    /// ветка входит в другую (Философия ← Мистицизм, Порох ← Военное дело, Былины ← все гибриды).
    /// Технологии изучаются за очки знаний 📖 и открывают здания (строятся за ⚒) и улучшения (за 🪙).
    /// Клик по узлу — подробности снизу, ещё клик (или «Изучить») — изучить. Всё строится кодом (старые колонки
    /// карточек прячутся) — сцену пересобирать не надо.
    /// </summary>
    public class TechTreeView : MonoBehaviour
    {
        private static readonly string[] BranchTitles = { "Хозяйство", "Знания", "Пехота", "Стрелки", "Вера" };
        private const float DetailsHeight = 150f;
        private const float SummaryHeight = 76f;
        private const float ColumnStep = 2f;

        [Header("Ссылки")]
        public TechTree techTree;
        public GameManager game;
        public CityManager city;
        public TMP_Text summaryText;
        [Tooltip("Где рисуется дерево (пусто — вся вкладка между сводкой и подробностями)")]
        public RectTransform treeArea;

        [Header("Устарело: колонки карточек из старых сцен (прячутся)")]
        public RectTransform[] columns = new RectTransform[0];
        public TechCardView cardPrefab;

        private SkillTreeGraph graph;
        private readonly List<SkillNode> nodes = new List<SkillNode>();
        private string selected;
        private RectTransform details;
        private Image detailsDisc;
        private Image detailsIcon;
        private TMP_Text detailsIconText;
        private TMP_Text detailsTitle;
        private TMP_Text detailsText;
        private Button actionButton;
        private TMP_Text actionLabel;

        private void Awake()
        {
            if (techTree == null) techTree = FindFirstObjectByType<TechTree>();
            if (game == null) game = FindFirstObjectByType<GameManager>();
            if (city == null) city = FindFirstObjectByType<CityManager>();
            if (techTree != null) techTree.Changed += Refresh;
            if (city != null) city.Changed += Refresh;
        }

        private void Start()
        {
            BuildTree();
            Refresh();
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

        // =====================================================================
        //  Построение
        // =====================================================================

        private void BuildTree()
        {
            if (techTree == null || graph != null) return;
            techTree.EnsureDefaults();
            foreach (RectTransform column in columns ?? new RectTransform[0])
            {
                if (column != null) column.gameObject.SetActive(false);
            }
            foreach (Transform child in transform)
            {
                if (child.name.StartsWith("Header_")) child.gameObject.SetActive(false); // заголовки старых колонок
            }

            var tab = (RectTransform)transform;
            if (treeArea == null)
            {
                treeArea = NewRect("TreeArea", tab);
                treeArea.anchorMin = Vector2.zero;
                treeArea.anchorMax = Vector2.one;
                treeArea.offsetMin = new Vector2(10f, DetailsHeight + 10f);
                treeArea.offsetMax = new Vector2(-10f, -SummaryHeight);
            }
            BuildDetails(tab);

            graph = treeArea.gameObject.AddComponent<SkillTreeGraph>();
            graph.rowHeight = 200f;
            graph.topPadding = 120f;
            if (summaryText != null) graph.font = summaryText.font;
            graph.Clicked += OnNodeClicked;

            // Строка — глубина (самая длинная цепочка нужных технологий), в столбце на одной строке — рядом
            List<TechConfig> techs = techTree.techs.Where(t => t != null).ToList();
            var depth = new Dictionary<string, int>();
            int Depth(TechConfig tech, int guard)
            {
                if (depth.TryGetValue(tech.id, out int known)) return known;
                int value = 0;
                if (guard < 30)
                {
                    foreach (string id in tech.requires ?? new string[0])
                    {
                        TechConfig parent = techs.FirstOrDefault(t => t.id == id);
                        if (parent != null) value = Mathf.Max(value, Depth(parent, guard + 1) + 1);
                    }
                }
                depth[tech.id] = value;
                return value;
            }
            nodes.Clear();
            // Ряды — сверху вниз; соседи в ряду встают в порядке своих «родителей» (чей родитель левее — тот левее),
            // чтобы связи меньше перекрещивались
            var placedX = new Dictionary<string, float>();
            float ParentX(TechConfig tech)
            {
                List<float> xs = (tech.requires ?? new string[0]).Where(placedX.ContainsKey).Select(id => placedX[id]).ToList();
                return xs.Count > 0 ? xs.Average() : ((int)tech.branch - 2) * ColumnStep;
            }
            foreach (IGrouping<(TechBranch branch, int row), TechConfig> group in techs.GroupBy(t => (t.branch, Depth(t, 0))).OrderBy(g => g.Key.Item2))
            {
                List<TechConfig> list = group.OrderBy(ParentX).ThenBy(t => techs.IndexOf(t)).ToList();
                for (int i = 0; i < list.Count; i++)
                {
                    TechConfig tech = list[i];
                    float x = ((int)tech.branch - 2) * ColumnStep + (i - (list.Count - 1) * 0.5f) * 0.84f;
                    placedX[tech.id] = x;
                    var node = new SkillNode
                    {
                        Id = tech.id,
                        Pos = new Vector2(x, group.Key.row),
                        Title = tech.title,
                        Accent = AccentOf(tech),
                        Big = tech.id == TechIds.Epics,
                        Parents = (tech.requires ?? new string[0]).Where(id => techs.Any(t => t.id == id)).ToArray(),
                    };
                    SetIcon(node, tech);
                    nodes.Add(node);
                }
            }

            var labels = new List<SkillLabel>();
            for (int i = 0; i < BranchTitles.Length; i++)
            {
                Color color = AccentOf(new TechConfig { branch = (TechBranch)i });
                labels.Add(new SkillLabel { Text = BranchTitles[i], Pos = new Vector2((i - 2) * ColumnStep, -0.5f), Color = Color.Lerp(color, Color.white, 0.45f) });
            }
            graph.Build(treeArea, nodes, labels);
        }

        /// <summary>Подробности снизу: кружок, название, что открывает, что нужно, кнопка «Изучить».</summary>
        private void BuildDetails(RectTransform tab)
        {
            details = NewRect("Details", tab);
            details.anchorMin = new Vector2(0f, 0f);
            details.anchorMax = new Vector2(1f, 0f);
            details.pivot = new Vector2(0.5f, 0f);
            details.offsetMin = new Vector2(16f, 8f);
            details.offsetMax = new Vector2(-16f, 8f + DetailsHeight - 10f);
            Image plate = details.gameObject.AddComponent<Image>();
            plate.sprite = GameVisuals.RoundedRect;
            plate.type = Image.Type.Sliced;
            plate.color = new Color(0.08f, 0.09f, 0.12f, 0.95f);

            RectTransform disc = NewRect("Disc", details);
            disc.anchorMin = disc.anchorMax = new Vector2(0f, 0.5f);
            disc.anchoredPosition = new Vector2(78f, 0f);
            disc.sizeDelta = new Vector2(104f, 104f);
            detailsDisc = disc.gameObject.AddComponent<Image>();
            detailsDisc.sprite = GameVisuals.Circle;
            RectTransform icon = NewRect("Icon", disc);
            Stretch(icon, 14f);
            detailsIcon = icon.gameObject.AddComponent<Image>();
            detailsIcon.preserveAspect = true;
            detailsIconText = NewText("IconText", disc, 42f, Color.white);
            Stretch(detailsIconText.rectTransform, 0f);

            detailsTitle = NewText("Title", details, 30f, new Color(0.6f, 0.82f, 1f));
            detailsTitle.alignment = TextAlignmentOptions.TopLeft;
            RectTransform title = detailsTitle.rectTransform;
            title.anchorMin = new Vector2(0f, 1f);
            title.anchorMax = new Vector2(1f, 1f);
            title.offsetMin = new Vector2(150f, -50f);
            title.offsetMax = new Vector2(-330f, -10f);
            detailsText = NewText("Text", details, 22f, new Color(0.88f, 0.9f, 0.95f));
            detailsText.alignment = TextAlignmentOptions.TopLeft;
            detailsText.enableWordWrapping = true;
            detailsText.enableAutoSizing = true;
            detailsText.fontSizeMin = 13f;
            detailsText.fontSizeMax = 22f;
            RectTransform text = detailsText.rectTransform;
            text.anchorMin = Vector2.zero;
            text.anchorMax = Vector2.one;
            text.offsetMin = new Vector2(150f, 10f);
            text.offsetMax = new Vector2(-330f, -50f);

            RectTransform button = NewRect("Btn_Research", details);
            button.anchorMin = button.anchorMax = new Vector2(1f, 0.5f);
            button.anchoredPosition = new Vector2(-160f, 0f);
            button.sizeDelta = new Vector2(290f, 96f);
            Image buttonImage = button.gameObject.AddComponent<Image>();
            buttonImage.sprite = GameVisuals.RoundedRect;
            buttonImage.type = Image.Type.Sliced;
            buttonImage.color = new Color(0.24f, 0.45f, 0.72f);
            actionButton = button.gameObject.AddComponent<Button>();
            actionButton.targetGraphic = buttonImage;
            actionButton.onClick.AddListener(() =>
            {
                if (selected != null) Research(selected);
            });
            actionLabel = NewText("Label", button, 28f, Color.white);
            Stretch(actionLabel.rectTransform, 6f);
            actionLabel.fontStyle = FontStyles.Bold;
            actionLabel.enableAutoSizing = true;
            actionLabel.fontSizeMin = 14f;
            actionLabel.fontSizeMax = 28f;
        }

        // =====================================================================
        //  Состояние
        // =====================================================================

        public void Refresh()
        {
            if (!isActiveAndEnabled || techTree == null || graph == null) return;
            int knowledge = city != null ? city.Knowledge : 0;
            if (summaryText != null && city != null)
            {
                int bonus = Mathf.RoundToInt((city.KnowledgeMultiplier - 1f) * 100f);
                summaryText.text =
                    $"Очки знаний: {knowledge} {GameVisuals.IconKnowledge} · каждый ход +{city.KnowledgePerDay} " +
                    $"(жители и ратуша {city.KnowledgeBase}{(bonus > 0 ? $", школа +{bonus}%" : string.Empty)})\n" +
                    "Клик по узлу — что он открывает, ещё клик — изучить. Линии между ветками — что из чего растёт";
            }
            foreach (SkillNode node in nodes)
            {
                TechConfig tech = techTree.Find(node.Id);
                if (tech == null) continue;
                node.Price = $"{tech.knowledgeCost}{GameVisuals.IconKnowledge}";
                node.State = techTree.IsResearched(tech.id) ? SkillState.Done
                    : !techTree.PrerequisitesMet(tech) ? SkillState.Locked
                    : knowledge >= tech.knowledgeCost ? SkillState.Ready : SkillState.Open;
            }
            graph.Refresh(nodes);
            ShowDetails();
        }

        private void OnNodeClicked(string id)
        {
            SkillNode node = nodes.FirstOrDefault(n => n.Id == id);
            if (node == null) return;
            if (selected == id && node.State == SkillState.Ready)
            {
                Research(id);
                return;
            }
            selected = id;
            graph.Select(id);
            ShowDetails();
        }

        private void ShowDetails()
        {
            if (details == null) return;
            SkillNode node = nodes.FirstOrDefault(n => n.Id == selected);
            TechConfig tech = node != null ? techTree.Find(node.Id) : null;
            if (tech == null)
            {
                detailsDisc.color = new Color(0.2f, 0.22f, 0.28f);
                detailsIcon.enabled = false;
                detailsIconText.text = GameVisuals.IconKnowledge;
                detailsTitle.text = "Дерево науки";
                detailsText.text = "Технологии открывают здания (строятся за ⚒ на карте) и улучшения (за 🪙 в зданиях и в «Войсках»). " +
                                   "Строка — глубина: чем ниже, тем больше нужно изучить до неё.";
                actionButton.gameObject.SetActive(false);
                return;
            }
            actionButton.gameObject.SetActive(true);
            detailsDisc.color = Color.Lerp(node.Accent, Color.black, 0.3f);
            detailsIcon.sprite = node.Icon;
            detailsIcon.enabled = node.Icon != null;
            detailsIconText.text = node.Icon == null ? node.IconText ?? string.Empty : string.Empty;
            detailsTitle.text = tech.title;
            string missing = techTree.MissingPrerequisites(tech);
            string needs = tech.requires != null && tech.requires.Length > 0 ? $"Нужно: {string.Join(", ", tech.requires.Select(techTree.Title))}" : "Можно изучать сразу";
            string state = node.State == SkillState.Done ? "<color=#8CE99A>Изучено</color>"
                : missing != null ? $"<color=#FFB86C>Сначала: {missing}</color>"
                : node.State == SkillState.Ready ? "<color=#8CE99A>Можно изучить</color>"
                : $"<color=#FFB86C>Нужно {tech.knowledgeCost} {GameVisuals.IconKnowledge}</color>";
            detailsText.text = $"{tech.description}\n<size=85%>{state} · {needs}</size>";
            actionLabel.text = node.State == SkillState.Done ? "Изучено" : $"Изучить · {node.Price}";
            actionButton.interactable = node.State == SkillState.Ready;
        }

        // =====================================================================
        //  Вид узлов
        // =====================================================================

        private static Color AccentOf(TechConfig tech)
        {
            switch (tech.id)
            {
                case TechIds.Gunpowder: return GameVisuals.ClassColor(UnitClass.Bomber);
                case TechIds.MartialArts: return GameVisuals.ClassColor(UnitClass.Monk);
                case TechIds.Sorcery: return GameVisuals.ClassColor(UnitClass.Mage);
                case TechIds.Epics: return new Color(0.85f, 0.65f, 0.25f);
            }
            switch (tech.branch)
            {
                case TechBranch.Economy: return new Color(0.78f, 0.62f, 0.28f);
                case TechBranch.Knowledge: return new Color(0.32f, 0.52f, 0.86f);
                case TechBranch.Infantry: return GameVisuals.ClassColor(UnitClass.Warrior);
                case TechBranch.Ranged: return GameVisuals.ClassColor(UnitClass.Archer);
                default: return GameVisuals.ClassColor(UnitClass.Priest);
            }
        }

        /// <summary>Рисунок узла: что технология открывает — ресурс, здание, снаряжение или класс.</summary>
        private static void SetIcon(SkillNode node, TechConfig tech)
        {
            switch (tech.id)
            {
                case TechIds.Agriculture:
                case TechIds.Pottery: node.IconText = GameVisuals.IconFood; return;
                case TechIds.Trade: node.IconText = GameVisuals.IconGold; return;
                case TechIds.Crafts: node.IconText = GameVisuals.IconProduction; return;
                case TechIds.Masonry: node.IconText = GameVisuals.IconWalls; return;
                case TechIds.Writing:
                case TechIds.Mathematics:
                case TechIds.Philosophy: node.IconText = GameVisuals.IconKnowledge; return;
                case TechIds.Shields:
                case TechIds.TowerShields: node.Icon = UnitArt.Gear(UnitClass.Warrior, ItemSlot.Armor, tech.id == TechIds.Shields ? 1 : 3); break;
                case TechIds.Bronze: node.Icon = UnitArt.Gear(UnitClass.Warrior, ItemSlot.Weapon, 2); break;
                case TechIds.CompositeBow: node.Icon = UnitArt.Gear(UnitClass.Archer, ItemSlot.Weapon, 2); break;
                case TechIds.Crossbows: node.Icon = UnitArt.Gear(UnitClass.Archer, ItemSlot.Weapon, 3); break;
                case TechIds.Staffs: node.Icon = UnitArt.Gear(UnitClass.Priest, ItemSlot.Weapon, 2); break;
                case TechIds.Healing: node.IconText = GameVisuals.IconHp; return;
                case TechIds.Monasticism: node.Icon = UnitArt.Gear(UnitClass.Priest, ItemSlot.Armor, 3); break;
                case TechIds.Gunpowder: node.Icon = UnitArt.Icon(UnitClass.Bomber); break;
                case TechIds.MartialArts: node.Icon = UnitArt.Icon(UnitClass.Monk); break;
                case TechIds.Sorcery: node.Icon = UnitArt.Icon(UnitClass.Mage); break;
                case TechIds.Epics: node.Icon = UnitArt.Icon(UnitClass.Hero); break;
            }
            if (node.Icon != null) return;
            switch (tech.branch)
            {
                case TechBranch.Infantry: node.Icon = UnitArt.Icon(UnitClass.Warrior); break;
                case TechBranch.Ranged: node.Icon = UnitArt.Icon(UnitClass.Archer); break;
                case TechBranch.Faith: node.Icon = UnitArt.Icon(UnitClass.Priest); break;
            }
            if (node.Icon == null) node.IconText = GameVisuals.IconKnowledge;
        }

        // =====================================================================
        //  UI-помощники
        // =====================================================================

        private TMP_Text NewText(string name, RectTransform parent, float size, Color color)
        {
            RectTransform rect = NewRect(name, parent);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (summaryText != null) text.font = summaryText.font;
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.Center;
            text.color = color;
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            return text;
        }

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }
    }
}
