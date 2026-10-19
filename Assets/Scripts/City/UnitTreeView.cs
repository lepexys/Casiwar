using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Casiwar
{
    /// <summary>
    /// Вкладка «Войска» — общее дерево прокачки, как в казуальных tower defense (SkillTreeGraph):
    /// • сверху три гильдии: казармы (воины, слева), стрельбище (лучники, посередине), святилище (жрецы, справа);
    ///   от каждой вниз идёт своя ветка: два пути сходятся в «ключ» (щиты, арбалеты, посохи), дальше снова два пути
    ///   и вершина ветки (суперудар класса уже с 4 в ряд);
    /// • гибриды стоят между ветками своих «родителей» и растут из ключей обеих: подрывники — между воинами и
    ///   лучниками, маги — между лучниками и жрецами; монахи — внизу, из вершины воинов или жрецов; богатырь — в самом
    ///   низу, из вершины любого гибрида;
    /// • улучшения — это улучшения зданий-гильдий (гибрид — у одной из родительских), работают, пока гильдия цела.
    /// Клик по узлу — подробности снизу, ещё клик (или «Купить») — купить за золото. Всё строится кодом —
    /// сцену пересобирать не надо (старые колонки карточек прячутся).
    /// </summary>
    public class UnitTreeView : MonoBehaviour
    {
        private static readonly UnitClass[] Branches = { UnitClass.Warrior, UnitClass.Archer, UnitClass.Priest };
        private const float DetailsHeight = 150f;
        private const float SummaryHeight = 70f;

        [Header("Ссылки")]
        public CityManager city;
        public TechTree techTree;
        public GameManager game;
        public TMP_Text summaryText;
        [Tooltip("Где рисуется дерево (пусто — вся вкладка между сводкой и подробностями)")]
        public RectTransform treeArea;

        [Header("Устарело: колонки карточек из старых сцен (прячутся)")]
        public RectTransform[] columns = new RectTransform[3];
        public TMP_Text[] columnStatus = new TMP_Text[3];
        public UpgradeCardView cardPrefab;

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
            if (city == null) city = FindFirstObjectByType<CityManager>();
            if (techTree == null) techTree = FindFirstObjectByType<TechTree>();
            if (game == null) game = FindFirstObjectByType<GameManager>();
            if (city != null) city.Changed += Refresh;
            if (techTree != null) techTree.Changed += Refresh;
        }

        private void Start()
        {
            BuildTree();
            Refresh();
        }

        private void OnDestroy()
        {
            if (city != null) city.Changed -= Refresh;
            if (techTree != null) techTree.Changed -= Refresh;
        }

        private void OnEnable() => Refresh();

        // =====================================================================
        //  Построение
        // =====================================================================

        private void BuildTree()
        {
            if (city == null || graph != null) return;
            HideLegacyColumns();
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
            graph.rowHeight = 170f;
            if (summaryText != null) graph.font = summaryText.font;
            graph.Clicked += OnNodeClicked;

            nodes.Clear();
            foreach (UnitClass branch in Branches)
            {
                BuildingConfig guild = city.GuildConfig(branch);
                if (guild == null) continue;
                nodes.Add(new SkillNode
                {
                    Id = GuildId(branch),
                    Pos = new Vector2(BranchX(branch), 0f),
                    Title = guild.title,
                    Icon = UnitArt.Building(guild.type),
                    IconText = GameVisuals.ClassIcon(branch),
                    Accent = GameVisuals.ClassColor(branch),
                    Big = true,
                });
                foreach (UpgradeConfig upgrade in guild.upgrades)
                {
                    UnitClass nodeClass = NodeClass(upgrade, branch);
                    var node = new SkillNode
                    {
                        Id = upgrade.id,
                        Pos = Spaced(upgrade.treePos == Vector2.zero ? new Vector2(BranchX(branch), upgrade.tier * 2f) : upgrade.treePos),
                        Title = upgrade.title,
                        Accent = GameVisuals.ClassColor(nodeClass),
                        Big = upgrade.gate,
                        Parents = upgrade.requires != null && upgrade.requires.Length > 0 ? upgrade.requires : new[] { GuildId(branch) },
                        EdgeTint = UnitClasses.IsHybrid(nodeClass) || nodeClass == UnitClass.Hero ? GameVisuals.ClassColor(nodeClass) : (Color?)null,
                    };
                    SetIcon(node, upgrade, nodeClass);
                    if (node.Icon == null) node.Icon = GameVisuals.GlyphSprite(node.IconText); // картинка крупнее значка-текста
                    nodes.Add(node);
                }
            }

            var labels = new List<SkillLabel>
            {
                new SkillLabel { Text = "Подрывники", Pos = Spaced(new Vector2(-1.5f, 6.8f)), Color = Color.Lerp(GameVisuals.ClassColor(UnitClass.Bomber), Color.white, 0.3f) },
                new SkillLabel { Text = "Маги", Pos = Spaced(new Vector2(1.5f, 6.8f)), Color = Color.Lerp(GameVisuals.ClassColor(UnitClass.Mage), Color.white, 0.3f) },
                new SkillLabel { Text = "Монахи", Pos = Spaced(new Vector2(1.1f, 7.6f)), Color = Color.Lerp(GameVisuals.ClassColor(UnitClass.Monk), Color.white, 0.3f) },
                new SkillLabel { Text = "Богатырь", Pos = Spaced(new Vector2(1.1f, 10.9f)), Color = new Color(1f, 0.85f, 0.4f) },
            };
            graph.Build(treeArea, nodes, labels);
        }

        /// <summary>Старые колонки карточек (сцены прошлых версий) — прочь: дерево рисуется само.</summary>
        private void HideLegacyColumns()
        {
            foreach (RectTransform column in columns ?? new RectTransform[0])
            {
                if (column == null) continue;
                ScrollRect scroll = column.GetComponentInParent<ScrollRect>(true);
                (scroll != null ? scroll.gameObject : column.gameObject).SetActive(false);
            }
            foreach (TMP_Text status in columnStatus ?? new TMP_Text[0])
            {
                if (status != null) status.gameObject.SetActive(false);
            }
            foreach (Transform child in transform)
            {
                if (child.name.StartsWith("Branch_")) child.gameObject.SetActive(false); // заголовки веток
            }
        }

        /// <summary>Подробности снизу: кружок узла, название, что даёт и что нужно, кнопка «Купить».</summary>
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

            detailsTitle = NewText("Title", details, 30f, new Color(1f, 0.85f, 0.4f));
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

            RectTransform button = NewRect("Btn_Action", details);
            button.anchorMin = button.anchorMax = new Vector2(1f, 0.5f);
            button.anchoredPosition = new Vector2(-160f, 0f);
            button.sizeDelta = new Vector2(290f, 96f);
            Image buttonImage = button.gameObject.AddComponent<Image>();
            buttonImage.sprite = GameVisuals.RoundedRect;
            buttonImage.type = Image.Type.Sliced;
            buttonImage.color = new Color(0.25f, 0.55f, 0.32f);
            actionButton = button.gameObject.AddComponent<Button>();
            actionButton.targetGraphic = buttonImage;
            actionButton.onClick.AddListener(OnAction);
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
            if (!isActiveAndEnabled || city == null || graph == null) return;
            if (summaryText != null)
            {
                summaryText.text = $"Золото: {city.Gold} {GameVisuals.IconGold} · клик по узлу — что он даёт, ещё клик — купить. " +
                                   "Гибриды растут между ветками своих гильдий, богатырь — в самом низу";
            }
            foreach (SkillNode node in nodes)
            {
                if (node.Id.StartsWith("guild:"))
                {
                    UnitClass branch = BranchOf(node.Id);
                    BuildingConfig config = city.GuildConfig(branch);
                    Building guild = city.Guild(branch);
                    node.State = guild != null && !guild.IsRuined ? SkillState.Done
                        : config != null && city.IsUnlocked(config) ? SkillState.Open : SkillState.Locked;
                    node.Price = guild == null && config != null ? $"{config.productionCost}{GameVisuals.IconProduction}" : string.Empty;
                    continue;
                }
                UpgradeConfig upgrade = city.FindUnitUpgrade(node.Id, out BuildingConfig owner);
                if (upgrade == null) continue;
                Building building = city.Guild(owner.unitBranch);
                int cost = building != null ? city.UpgradeCost(building, upgrade) : upgrade.goldCost;
                node.Price = $"{cost}{GameVisuals.IconGold}";
                if (city.HasUnitUpgrade(upgrade.id))
                {
                    node.State = SkillState.Done;
                    continue;
                }
                string reason = Reason(upgrade, owner, building);
                node.State = reason == null ? SkillState.Ready
                    : reason.StartsWith("Сначала") || reason.StartsWith("Нужна наука") ? SkillState.Locked : SkillState.Open;
            }
            graph.Refresh(nodes);
            ShowDetails();
        }

        /// <summary>Почему узел не купить (null — можно): нет гильдии-хозяйки или то, что скажет город.</summary>
        private string Reason(UpgradeConfig upgrade, BuildingConfig owner, Building building)
        {
            if (building == null)
            {
                // Сначала связи и наука — так понятнее, чем «нет здания» у дальнего узла
                string chain = upgrade.requires != null && upgrade.requires.Length > 0 &&
                               (upgrade.requiresAny ? !upgrade.requires.Any(city.HasUnitUpgrade) : !upgrade.requires.All(city.HasUnitUpgrade))
                    ? $"Сначала: {string.Join(upgrade.requiresAny ? " или " : ", ", upgrade.requires.Where(r => !city.HasUnitUpgrade(r)).Select(Title))}"
                    : null;
                return chain ?? $"Нет здания: {owner.title} (стройте на карте)";
            }
            return city.WhyCannotUpgrade(building, upgrade);
        }

        private string Title(string id) => city.FindUnitUpgrade(id, out _)?.title ?? id;

        private void OnNodeClicked(string id)
        {
            SkillNode node = nodes.FirstOrDefault(n => n.Id == id);
            if (node == null) return;
            if (selected == id && node.State == SkillState.Ready)
            {
                Buy(id);
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
            if (node == null)
            {
                detailsDisc.color = new Color(0.2f, 0.22f, 0.28f);
                detailsIcon.sprite = GameVisuals.GlyphSprite(GameVisuals.IconStar);
                detailsIcon.enabled = detailsIcon.sprite != null;
                detailsIconText.text = detailsIcon.enabled ? string.Empty : GameVisuals.IconStar;
                detailsTitle.text = "Дерево войск";
                detailsText.text = "Каждая гильдия — своя ветка: два пути сходятся в ключ (щиты, арбалеты, посохи), вершина ветки даёт суперудар уже с 4 в ряд. " +
                                   "Гибриды растут из двух веток сразу. Улучшения работают, пока цела гильдия.";
                actionButton.gameObject.SetActive(false);
                return;
            }
            detailsDisc.color = Color.Lerp(node.Accent, Color.black, 0.3f);
            detailsIcon.sprite = node.Icon;
            detailsIcon.enabled = node.Icon != null;
            detailsIconText.text = node.Icon == null ? node.IconText ?? string.Empty : string.Empty;
            actionButton.gameObject.SetActive(true);

            if (node.Id.StartsWith("guild:"))
            {
                UnitClass branch = BranchOf(node.Id);
                BuildingConfig config = city.GuildConfig(branch);
                Building guild = city.Guild(branch);
                detailsTitle.text = config.title;
                detailsText.text = config.description + "\n" + (guild == null
                    ? city.IsUnlocked(config) ? $"Строится на карте города за {config.productionCost} {GameVisuals.IconProduction}" : $"Нужна наука: {city.RequiredTechTitles(config)}"
                    : guild.IsRuined ? "<color=#FF7A6E>Разрушено — почините на карте, иначе улучшения не работают</color>" : "Построено");
                actionLabel.text = "На карту";
                actionButton.interactable = true;
                return;
            }

            UpgradeConfig upgrade = city.FindUnitUpgrade(node.Id, out BuildingConfig owner);
            Building building = city.Guild(owner.unitBranch);
            string reason = node.State == SkillState.Done ? null : Reason(upgrade, owner, building);
            string tech = string.IsNullOrEmpty(upgrade.requiredTech) ? string.Empty
                : $" · наука «{(techTree != null ? techTree.Title(upgrade.requiredTech) : upgrade.requiredTech)}»";
            string needs = upgrade.requires != null && upgrade.requires.Length > 0
                ? $"Нужно{(upgrade.requiresAny ? " (любое)" : string.Empty)}: {string.Join(", ", upgrade.requires.Select(Title))}{tech}"
                : tech.Length > 0 ? $"Нужно:{tech.Substring(2)}" : string.Empty;
            detailsTitle.text = upgrade.gate ? $"{upgrade.title} · ключ" : upgrade.title;
            string state = node.State == SkillState.Done ? "<color=#8CE99A>Куплено</color>"
                : reason == null ? "<color=#8CE99A>Можно купить</color>" : $"<color=#FFB86C>{reason}</color>";
            detailsText.text = $"{upgrade.description}\n<size=85%>{owner.title} · {state}{(needs.Length > 0 ? $" · {needs}" : string.Empty)}</size>";
            actionLabel.text = node.State == SkillState.Done ? "Куплено" : $"Купить · {node.Price}";
            actionButton.interactable = node.State == SkillState.Ready;
        }

        private void OnAction()
        {
            if (selected == null) return;
            if (selected.StartsWith("guild:"))
            {
                if (game != null) game.ShowCityTab();
                return;
            }
            Buy(selected);
        }

        /// <summary>Купить узел в его гильдии-хозяйке.</summary>
        public void Buy(string upgradeId)
        {
            if (city == null) return;
            UpgradeConfig upgrade = city.FindUnitUpgrade(upgradeId, out BuildingConfig owner);
            if (upgrade == null) return;
            Building guild = city.Guild(owner.unitBranch);
            string message;
            if (guild == null) message = $"Сначала постройте: {owner.title}";
            else city.TryUpgrade(guild, upgradeId, out message);
            if (game != null && !string.IsNullOrEmpty(message)) game.ShowMessage(message);
        }

        /// <summary>Старое имя: купить улучшение в гильдии ветки (карточки старых сцен).</summary>
        public void Buy(UnitClass branch, string upgradeId) => Buy(upgradeId);

        // =====================================================================
        //  Узлы: класс, место, рисунок
        // =====================================================================

        private static string GuildId(UnitClass branch) => $"guild:{branch}";

        /// <summary>Под ключами (щиты, арбалеты, посохи) — лишние полряда: оттуда расходится много связей.</summary>
        private static Vector2 Spaced(Vector2 pos) => pos.y >= 3.9f ? new Vector2(pos.x, pos.y + 0.5f) : pos;

        private static UnitClass BranchOf(string guildId) =>
            System.Enum.TryParse(guildId.Substring("guild:".Length), out UnitClass branch) ? branch : UnitClass.None;

        /// <summary>Столбец ветки: воины слева, лучники посередине, жрецы справа.</summary>
        private static float BranchX(UnitClass branch) => branch == UnitClass.Warrior ? -3f : branch == UnitClass.Priest ? 3f : 0f;

        /// <summary>Чей узел: класс из эффектов (гибриды, богатырь, эффекты «для класса»), иначе — гильдии-хозяйки.</summary>
        private static UnitClass NodeClass(UpgradeConfig upgrade, UnitClass owner)
        {
            foreach (EffectValue value in upgrade.effects)
            {
                switch (value.effect)
                {
                    case CityEffect.BomberStats:
                    case CityEffect.BomberSkill: return UnitClass.Bomber;
                    case CityEffect.MonkStats:
                    case CityEffect.MonkSkill: return UnitClass.Monk;
                    case CityEffect.MageStats:
                    case CityEffect.MageSkill: return UnitClass.Mage;
                    case CityEffect.HeroStats:
                    case CityEffect.HeroBanner: return UnitClass.Hero;
                }
                if (value.target != UnitClass.None) return value.target;
            }
            return owner;
        }

        /// <summary>Рисунок узла — по главному эффекту: снаряжение, значок класса, спин, знамя, свеча…</summary>
        private static void SetIcon(SkillNode node, UpgradeConfig upgrade, UnitClass nodeClass)
        {
            EffectValue first = upgrade.effects.FirstOrDefault();
            CityEffect effect = first != null ? first.effect : CityEffect.ArmyStats;
            int tier = upgrade.goldCost >= 60 ? 3 : upgrade.goldCost >= 40 ? 2 : 1;
            Sprite classIcon = UnitArt.Icon(nodeClass);
            switch (effect)
            {
                case CityEffect.WarriorStats:
                case CityEffect.ArcherStats:
                case CityEffect.PriestStats:
                    node.Icon = UnitArt.Gear(nodeClass, ItemSlot.Weapon, tier) ?? classIcon;
                    break;
                case CityEffect.ShieldLevel:
                case CityEffect.ShieldBlockBonus:
                    node.Icon = UnitArt.Gear(UnitClass.Warrior, ItemSlot.Armor, tier);
                    break;
                case CityEffect.Crossbows:
                    node.Icon = UnitArt.Gear(UnitClass.Archer, ItemSlot.Weapon, 3);
                    break;
                case CityEffect.PriestHeal:
                    node.IconText = GameVisuals.IconHp;
                    break;
                case CityEffect.RecruitShare:
                case CityEffect.ExtraGuildSpin:
                case CityEffect.RecruitLevel:
                    node.IconText = GameVisuals.IconSpin;
                    break;
                case CityEffect.OrderWarriors:
                case CityEffect.OrderArchers:
                case CityEffect.OrderPriests:
                case CityEffect.FireArrows:
                    node.IconText = GameVisuals.IconMorale;
                    break;
                case CityEffect.BannerSymbols:
                case CityEffect.HeroBanner:
                    node.IconText = GameVisuals.IconBanner;
                    break;
                case CityEffect.MultiplierSymbols:
                    node.IconText = "×2";
                    break;
                case CityEffect.MultiplierValue:
                    node.IconText = "×3";
                    break;
                case CityEffect.RelicSymbols:
                case CityEffect.MiraclePower:
                case CityEffect.Martyrs:
                    node.IconText = GameVisuals.IconCandle;
                    break;
                case CityEffect.SuperAt4:
                    node.IconText = $"4{GameVisuals.IconStar}";
                    break;
                case CityEffect.Haste:
                    node.IconText = GameVisuals.IconSpeed;
                    break;
                case CityEffect.Bloodlust:
                    node.IconText = GameVisuals.IconHp;
                    break;
                case CityEffect.DoubleStrike:
                case CityEffect.Shockwave:
                case CityEffect.HeroCleave:
                    node.IconText = GameVisuals.IconAttack;
                    break;
                case CityEffect.ChargeRange:
                case CityEffect.Dodge:
                    node.IconText = GameVisuals.IconMove;
                    break;
                case CityEffect.Bleed:
                    node.Icon = UnitArt.Gear(UnitClass.Warrior, ItemSlot.Weapon, 2);
                    break;
                case CityEffect.ShieldWall:
                case CityEffect.Toughness:
                    node.Icon = UnitArt.Gear(nodeClass == UnitClass.Hero ? UnitClass.Warrior : nodeClass, ItemSlot.Armor, 3);
                    break;
                case CityEffect.RangeBonus:
                    node.Icon = UnitArt.Gear(UnitClass.Archer, ItemSlot.Weapon, 1);
                    break;
                case CityEffect.Pierce:
                    node.Icon = UnitArt.Gear(UnitClass.Archer, ItemSlot.Weapon, 2);
                    break;
                case CityEffect.PriestChain:
                    node.Icon = UnitArt.Gear(UnitClass.Priest, ItemSlot.Weapon, 2);
                    break;
                case CityEffect.SecondWind:
                    node.Icon = UnitArt.Gear(UnitClass.Priest, ItemSlot.Armor, 3);
                    break;
                case CityEffect.HealSplash:
                case CityEffect.Cleanse:
                case CityEffect.Lifesteal:
                    node.IconText = GameVisuals.IconHp;
                    break;
                case CityEffect.LeaderBoost:
                    node.IconText = GameVisuals.IconBanner;
                    break;
                default:
                    node.Icon = classIcon; // навыки, статы гибридов и богатыря, руны
                    break;
            }
            if (node.Icon == null && string.IsNullOrEmpty(node.IconText)) node.IconText = GameVisuals.ClassIcon(nodeClass);
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
