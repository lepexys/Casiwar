using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Casiwar
{
    public enum BuildingType
    {
        TownHall,  // ратуша: сердце города (+знания; разрушена — забег окончен)
        House,     // дом: 3 жителя, ставится сам, когда город растёт
        Bazaar,    // базар: золото с монет
        Barracks,     // казармы: гильдия ближнего боя (воины)
        Sanctuary,    // святилище: гильдия магии (жрецы)
        Granary,      // амбар: больше еды
        Workshop,     // мастерская: больше производства
        School,       // школа: больше очков знаний
        ArcheryRange, // стрельбище: гильдия дальнего боя (лучники)
        // Чудеса света: уникальные дорогие постройки за ⚒ с сильным бонусом
        Stonehenge,
        Pyramids,
        GreatWall,
        Colossus,
        Tiltyard,
        GreatLibrary,
        TerracottaArmy,
    }

    /// <summary>Что дают здания и улучшения.</summary>
    public enum CityEffect
    {
        Housing,             // мест для жителей
        GoldPerCoin,         // +золота с каждой монеты на поле
        FoodPerSymbol,       // +еды с каждого символа еды
        Reserve,             // +символов в запасе на день
        UnlockWarriors,      // воины падают на поле
        UnlockArchers,       // лучники падают на поле
        UnlockPriests,         // жрецы падают на поле
        WarriorStats,        // +доля к ХП и атаке воинов
        ArcherStats,         // +доля к ХП и атаке лучников
        PriestHeal,            // +доля к лечению жрецов
        RaidDefense,         // −доля урона от набегов
        ProductionPerSymbol, // +⚒ с каждого символа производства
        KnowledgePercent,    // +доля к очкам знаний
        GrowthDiscount,      // −доля еды на рост города
        PriestStats,           // +доля к ХП и атаке жрецов
        ShieldLevel,         // щиты воинов: 1 — обычные, 2 — ростовые (берётся лучший, не сумма)
        Crossbows,           // луки стали арбалетами: болт пробивает щиты
        Reshuffles,          // перемешиваний поля в день (вера)
        WarriorSkill,        // +сила «Рывка» / «Тарана щитом»
        ArcherSkill,         // +сила «Залпа»
        PriestSkill,           // +сила «Молитвы»
        ShieldBlockBonus,    // +шанс поймать удар щитом целиком
        BannerSymbols,       // Знамёна (джокеры) в боевом слоте — уровень
        MultiplierSymbols,   // ×2 в боевом слоте — уровень
        MultiplierValue,     // +1 к множителю (×2 → ×3)
        RelicSymbols,        // Свечи (3+ — «Чудо») в боевом слоте — уровень
        MiraclePower,        // +сила «Чуда»
        BomberStats,         // +доля к ХП и атаке подрывников (сверху «родительских» гильдий)
        MonkStats,           // +доля к ХП и атаке монахов
        MageStats,           // +доля к ХП и атаке магов
        HeroStats,           // +доля к ХП и атаке богатыря
        BomberSkill,         // +сила «Динамита»
        MonkSkill,           // +сила «Огненных клинков»
        MageSkill,           // +сила «Цепной молнии»
        ArmyStats,           // +доля к ХП и атаке всех отрядов (и ополченцев)
        BattleXp,            // +опыта каждому выжившему за бой
    }

    [Serializable]
    public class EffectValue
    {
        public CityEffect effect;
        public float value = 1f;
        [Tooltip("Эффект работает, только если изучена эта технология (пусто — всегда)")]
        public string requiredTech = "";

        public EffectValue() { }

        public EffectValue(CityEffect effect, float value, string requiredTech = "")
        {
            this.effect = effect;
            this.value = value;
            this.requiredTech = requiredTech;
        }
    }

    /// <summary>
    /// Улучшение здания: покупается за золото, может требовать науку.
    /// У гильдий (казармы, стрельбище, святилище) улучшения — это ветка прокачки отряда по ярусам:
    /// ярус открывается «ключом» прошлого яруса (щиты, арбалеты, посохи), а ключу нужны его наука
    /// и requiredInTier купленных улучшений своего яруса.
    /// </summary>
    [Serializable]
    public class UpgradeConfig
    {
        public string id = "upgrade";
        public string title = "Улучшение";
        public string description = "";
        [Tooltip("Технология, которая открывает улучшение (пусто — доступно сразу)")]
        public string requiredTech = "";
        [Min(0)] public int goldCost = 6;
        [Tooltip("Ярус в ветке отряда (1 — с самого начала)")]
        [Min(1)] public int tier = 1;
        [Tooltip("Ключ яруса: открывает следующий ярус")]
        public bool gate;
        [Tooltip("Ключу нужно столько купленных улучшений своего яруса")]
        [Min(0)] public int requiredInTier = 2;
        public List<EffectValue> effects = new List<EffectValue>();

        public UpgradeConfig() { }

        public UpgradeConfig(string id, string title, string description, string requiredTech, int goldCost, params EffectValue[] effects)
        {
            this.id = id;
            this.title = title;
            this.description = description;
            this.requiredTech = requiredTech;
            this.goldCost = goldCost;
            this.effects = effects.ToList();
        }
    }

    [Serializable]
    public class BuildingConfig
    {
        public BuildingType type;
        public string title = "Здание";
        [Tooltip("Короткая подпись на клетке карты")]
        public string shortLabel = "ЗДАНИЕ";
        public Color color = Color.gray;
        public string description = "";
        [Tooltip("Нужна ЛЮБАЯ из этих технологий (пусто — доступно сразу)")]
        public string[] requiresAnyTech = new string[0];
        [Tooltip("Цена постройки в производстве ⚒")]
        [Min(0)] public int productionCost = 5;
        [Min(1)] public int maxHp = 50;
        [Tooltip("Можно построить только одно")]
        public bool unique = true;
        [Tooltip("Игрок строит сам (ратуша и базар стоят с начала, дома ставятся сами)")]
        public bool buildable = true;
        [Tooltip("Гильдия: чей отряд прокачивается в этом здании (вкладка «Войска»). None — обычное здание")]
        public UnitClass unitBranch = UnitClass.None;
        public List<EffectValue> effects = new List<EffectValue>();
        public List<UpgradeConfig> upgrades = new List<UpgradeConfig>();
    }

    /// <summary>Построенное здание на карте города.</summary>
    public sealed class Building
    {
        public readonly BuildingConfig Config;
        public readonly Vector2Int Cell;
        public readonly HashSet<string> Upgrades = new HashSet<string>();
        public int Hp;

        public Building(BuildingConfig config, Vector2Int cell)
        {
            Config = config;
            Cell = cell;
            Hp = config.maxHp;
        }

        public BuildingType Type => Config.type;
        public int MaxHp => Config.maxHp;
        public bool IsRuined => Hp <= 0;
        public bool IsDamaged => Hp < MaxHp;
    }

    /// <summary>
    /// Город (в духе «Цивилизации»):
    /// • город стоит на большой карте мира (WorldMap 50×50: трава, лес, камни, реки, озёра) и занимает
    ///   свои клетки: строить можно только в границах города (квадрат вокруг ратуши) и не на воде.
    ///   Когда новому дому не хватает места, границы расширяются сами (до maxCityRadius);
    /// • на старте — ратуша, дом и базар. Новые здания открывает наука
    ///   (TechTree, за очки знаний 📖), строятся они за производство ⚒, а улучшаются за золото 🪙;
    /// • золото, производство и еда падают символами на поле города. Фокус (например 2:1:1) решает,
    ///   каких ресурсов завтра упадёт больше; юниты падают как обычно;
    /// • жители каждый день едят; излишек еды растит население → поле и запас символов больше.
    ///   В доме живут 3 жителя; когда дома заполнены, новый дом ставится сам;
    /// • очки знаний приходят каждое утро — от жителей и ратуши, школа и библиотека их умножают;
    /// • лишнее производство уходит в чудеса света (дорогие уникальные постройки с сильным бонусом),
    ///   расширение границ в ратуше и караваны с базара (⚒ → 🪙);
    /// • у каждого здания есть прочность: после проигранного боя варвары бьют по зданиям.
    ///   Разрушенное здание не работает, пока его не починят. Разрушена ратуша — забег окончен.
    /// </summary>
    public class CityManager : MonoBehaviour
    {
        private const int ResourceCount = 3; // золото, производство, еда (ResourceType)

        [Header("Ссылки")]
        public TechTree techTree;

        [Header("Карта мира и границы города")]
        [Tooltip("Размер карты мира в клетках (как в «Цивилизации»)")]
        [Min(10)] public int worldWidth = 50;
        [Min(10)] public int worldHeight = 50;
        [Tooltip("0 — новая карта каждый забег, иначе всегда одна и та же")]
        public int worldSeed;
        [Tooltip("Границы города: радиус вокруг ратуши (2 → квадрат 5×5)")]
        [Min(1)] public int cityRadius = 2;
        [Tooltip("До какого радиуса границы расширяются (сами — когда новому дому не хватает места, или за ⚒ в ратуше)")]
        [Min(1)] public int maxCityRadius = 5;
        [Tooltip("Расширить границы за производство в ратуше: цена = это × новый радиус")]
        [Min(1)] public int borderCostPerRadius = 12;

        [Header("Караваны с базара: лишнее производство → золото")]
        [Min(1)] public int caravanProduction = 15;
        [Min(0)] public int caravanGold = 5;
        public List<BuildingConfig> buildings = DefaultBuildings();

        [Header("Население и еда")]
        [Min(1)] public int startPopulation = 3;
        [Tooltip("Еды на жителя в день (всего округляется вверх)")]
        [Min(0f)] public float foodPerCitizen = 1.5f;
        [Tooltip("Еды на рост = база + за каждого жителя")]
        [Min(1)] public int baseGrowthCost = 6;
        [Min(0)] public int growthCostPerCitizen = 3;

        [Header("Поле города (растёт с населением)")]
        [Min(3)] public int baseBoardSize = 4;
        [Min(3)] public int maxBoardSize = 7;
        [Tooltip("+1 к размеру поля за каждых N жителей сверх стартовых")]
        [Min(1)] public int citizensPerBoardSize = 2;
        [Tooltip("Символов в запасе на день = база + за каждого жителя (+ улучшения ратуши)")]
        [Min(0)] public int baseReserve = 6;
        [Min(0)] public int reservePerCitizen = 3;

        [Header("Ресурсы с символов (без зданий)")]
        [Min(1)] public int baseGoldPerCoin = 1;
        [Min(1)] public int baseProductionPerSymbol = 1;
        [Min(1)] public int baseFoodPerSymbol = 2;
        [Tooltip("Фокус поля: вес каждого ресурса от 1 до этого числа (1:1:1 … 3:1:1)")]
        [Min(1)] public int maxFocusWeight = 3;

        [Header("Знания")]
        [Min(0)] public int startKnowledge = 5;
        [Tooltip("Очков знаний в день от каждого жителя")]
        [Min(0)] public int knowledgePerCitizen = 1;
        [Tooltip("Очков знаний в день от ратуши")]
        [Min(0)] public int townHallKnowledge = 2;

        [Header("Действия на поле")]
        [Tooltip("Ходов на поле в день при стартовом поле и запасе (позже будет зависеть от культуры)")]
        [Min(1)] public int actionsPerDay = 10;
        [Tooltip("+1 ход за каждые N символов дня (поле + запас) сверх стартовых — чем больше город, тем больше ходов")]
        [Min(1)] public int symbolsPerExtraAction = 5;

        [Header("Старт и ремонт")]
        [Tooltip("Еды на первое утро, чтобы город не голодал из-за неудачного первого поля")]
        [Min(0)] public int startFood = 3;
        [Min(0)] public int startGold = 6;
        [Tooltip("Хватает на первые казармы или святилище в первый же день")]
        [Min(0)] public int startProduction = 6;
        [Tooltip("Сколько прочности чинит 1 ⚒")]
        [Min(1)] public int hpPerProduction = 10;

        private readonly List<Building> placed = new List<Building>();
        private readonly int[] plannedFocus = { 1, 1, 1 };
        private readonly int[] activeFocus = { 1, 1, 1 };

        public int Population { get; private set; }
        public int Food { get; private set; }
        public int Production { get; private set; }
        public int Gold { get; private set; }
        public int Knowledge { get; private set; }
        public IReadOnlyList<Building> Buildings => placed;

        /// <summary>Карта мира (местность и видимость) — новая каждый забег.</summary>
        public WorldMap World { get; private set; }
        /// <summary>Центр города — клетка ратуши.</summary>
        public Vector2Int CityCenter { get; private set; }
        /// <summary>Радиус границ города (квадрат вокруг ратуши).</summary>
        public int TerritoryRadius { get; private set; }

        private bool bordersGrew;

        /// <summary>Изменилось что-то в городе (ресурсы, здания, фокус, наука).</summary>
        public event Action Changed;

        private void Awake()
        {
            if (techTree == null) techTree = FindFirstObjectByType<TechTree>();
            if (techTree != null) techTree.Changed += NotifyChanged;
        }

        private void OnDestroy()
        {
            if (techTree != null) techTree.Changed -= NotifyChanged;
        }

        // =====================================================================
        //  Здания по умолчанию
        // =====================================================================

        public static List<BuildingConfig> DefaultBuildings()
        {
            const string gold = GameVisuals.IconGold;
            const string food = GameVisuals.IconFood;
            const string production = GameVisuals.IconProduction;
            const string knowledge = GameVisuals.IconKnowledge;
            const string people = GameVisuals.IconPopulation;
            const string banner = GameVisuals.IconBanner;
            const string candle = GameVisuals.IconCandle;
            return new List<BuildingConfig>
            {
                new BuildingConfig
                {
                    type = BuildingType.TownHall, title = "Ратуша", shortLabel = "РАТУША", color = new Color(0.78f, 0.62f, 0.32f),
                    description = $"Сердце города: +2 {knowledge} в день. Здесь же — расширение границ за {production}. Разрушена — забег окончен.",
                    maxHp = 120, buildable = false,
                    upgrades =
                    {
                        new UpgradeConfig("bookkeeping", "Учёт", "+6 символов на поле каждый день", TechIds.Mathematics, 8,
                            new EffectValue(CityEffect.Reserve, 6)),
                        new UpgradeConfig("palisade", "Частокол", "Набеги наносят на 40% меньше урона", TechIds.Masonry, 8,
                            new EffectValue(CityEffect.RaidDefense, 0.4f)),
                    },
                },
                new BuildingConfig
                {
                    type = BuildingType.House, title = "Деревенский дом", shortLabel = "ДОМ", color = new Color(0.62f, 0.48f, 0.36f),
                    description = $"Дом на 3 {people}. Когда дома заполнены, город сам ставит новый.",
                    maxHp = 40, unique = false, buildable = false,
                    effects = { new EffectValue(CityEffect.Housing, 3) },
                },
                new BuildingConfig
                {
                    type = BuildingType.Bazaar, title = "Базар", shortLabel = "БАЗАР", color = new Color(0.95f, 0.75f, 0.25f),
                    description = $"+1 {gold} с каждой монеты на поле. Караваны меняют лишнее {production} на {gold}.",
                    maxHp = 60, buildable = false,
                    effects = { new EffectValue(CityEffect.GoldPerCoin, 1) },
                    upgrades =
                    {
                        new UpgradeConfig("market_rows", "Торговые ряды", $"+1 {gold} с каждой монеты", TechIds.Trade, 8,
                            new EffectValue(CityEffect.GoldPerCoin, 1)),
                    },
                },
                new BuildingConfig
                {
                    type = BuildingType.Barracks, title = "Казармы", shortLabel = "КАЗАРМЫ", color = new Color(0.80f, 0.34f, 0.28f),
                    description = "Гильдия ближнего боя: воины на поле. Прокачка воинов, монахов и богатыря — на вкладке «Войска».",
                    requiresAnyTech = new[] { TechIds.Warfare }, productionCost = 5, maxHp = 80, unitBranch = UnitClass.Warrior,
                    effects = { new EffectValue(CityEffect.UnlockWarriors, 1) },
                    upgrades =
                    {
                        new UpgradeConfig("drill", "Муштра", "«Рывок» воинов бьёт на 30% сильнее", "", 25,
                            new EffectValue(CityEffect.WarriorSkill, 0.3f)),
                        new UpgradeConfig("bronze_weapons", "Бронзовые мечи", "Воины +20% ХП и атаки", TechIds.Bronze, 30,
                            new EffectValue(CityEffect.WarriorStats, 0.2f)),
                        new UpgradeConfig("banner", "Боевое знамя", $"В боевом слоте появляются Знамёна {banner}. Знамя — джокер: считается буквой любого кластера рядом, и навык срабатывает чаще и сильнее", "", 30,
                            new EffectValue(CityEffect.BannerSymbols, 1)),
                        new UpgradeConfig("shields", "Щиты", "Воины берут щиты: ловят удары в лицо, а «Рывок» становится «Тараном щитом» — цель оглушена на ход", TechIds.Shields, 35,
                            new EffectValue(CityEffect.ShieldLevel, 1)) { gate = true },
                        new UpgradeConfig("iron_rims", "Окованные щиты", "+10% к шансу поймать удар целиком", "", 45,
                            new EffectValue(CityEffect.ShieldBlockBonus, 0.1f)) { tier = 2 },
                        new UpgradeConfig("regiment_banners", "Знамёна полков", $"Знамён {banner} (джокеров) в боевом слоте выпадает больше", "", 50,
                            new EffectValue(CityEffect.BannerSymbols, 1)) { tier = 2 },
                        new UpgradeConfig("tower_shields", "Ростовые щиты", "Блок чаще, щит прикрывает и бок, таран оглушает на 2 хода", TechIds.TowerShields, 60,
                            new EffectValue(CityEffect.ShieldLevel, 2)) { tier = 2, gate = true },
                        new UpgradeConfig("phalanx", "Фаланга", "Воины +25% ХП и атаки, «Рывок» на 30% сильнее", "", 70,
                            new EffectValue(CityEffect.WarriorStats, 0.25f), new EffectValue(CityEffect.WarriorSkill, 0.3f)) { tier = 3 },
                        new UpgradeConfig("fist_school", "Школа кулака", "Монахи +30% ХП и атаки", TechIds.MartialArts, 75,
                            new EffectValue(CityEffect.MonkStats, 0.3f)) { tier = 3 },
                        new UpgradeConfig("inner_fire", "Внутренний огонь", "«Огненные клинки» монахов на 50% сильнее", TechIds.MartialArts, 80,
                            new EffectValue(CityEffect.MonkSkill, 0.5f)) { tier = 3 },
                        new UpgradeConfig("bogatyr_outpost", "Богатырская застава", "Богатырь +30% ХП и атаки", TechIds.Epics, 90,
                            new EffectValue(CityEffect.HeroStats, 0.3f)) { tier = 3 },
                    },
                },
                new BuildingConfig
                {
                    type = BuildingType.ArcheryRange, title = "Стрельбище", shortLabel = "СТРЕЛЬБ.", color = new Color(0.42f, 0.66f, 0.30f),
                    description = "Гильдия дальнего боя: лучники на поле. Прокачка стрелков и подрывников — на вкладке «Войска».",
                    requiresAnyTech = new[] { TechIds.Archery }, productionCost = 5, maxHp = 70, unitBranch = UnitClass.Archer,
                    effects = { new EffectValue(CityEffect.UnlockArchers, 1) },
                    upgrades =
                    {
                        new UpgradeConfig("bowstring", "Тугая тетива", "Лучники +15% ХП и атаки", "", 25,
                            new EffectValue(CityEffect.ArcherStats, 0.15f)),
                        new UpgradeConfig("composite_bows", "Составные луки", "Лучники +25% ХП и атаки", TechIds.CompositeBow, 30,
                            new EffectValue(CityEffect.ArcherStats, 0.25f)),
                        new UpgradeConfig("sights", "Прицел", "В боевом слоте появляются ×2: кластер, который касается ×2, действует вдвое сильнее (удары и навыки)", "", 30,
                            new EffectValue(CityEffect.MultiplierSymbols, 1)),
                        new UpgradeConfig("crossbows", "Арбалеты", "Луки → арбалеты: стрелки +15%, болты вдвое реже ловят щитом", TechIds.Crossbows, 35,
                            new EffectValue(CityEffect.Crossbows, 1), new EffectValue(CityEffect.ArcherStats, 0.15f)) { gate = true },
                        new UpgradeConfig("heavy_bolts", "Тяжёлые болты", "Стрелки +20% ХП и атаки", "", 45,
                            new EffectValue(CityEffect.ArcherStats, 0.2f)) { tier = 2 },
                        new UpgradeConfig("volley_drill", "Залповый огонь", "«Залп» на 50% сильнее", "", 50,
                            new EffectValue(CityEffect.ArcherSkill, 0.5f)) { tier = 2 },
                        new UpgradeConfig("marksmen", "Меткие стрелки", "Множители в боевом слоте: ×2 → ×3", "", 60,
                            new EffectValue(CityEffect.MultiplierValue, 1)) { tier = 2 },
                        new UpgradeConfig("powder_kegs", "Пороховые бочки", "Подрывники +30% ХП и атаки", TechIds.Gunpowder, 70,
                            new EffectValue(CityEffect.BomberStats, 0.3f)) { tier = 2 },
                        new UpgradeConfig("dynamite_bundles", "Связки динамита", "«Динамит» подрывников на 50% сильнее", TechIds.Gunpowder, 80,
                            new EffectValue(CityEffect.BomberSkill, 0.5f)) { tier = 2 },
                    },
                },
                new BuildingConfig
                {
                    type = BuildingType.Sanctuary, title = "Святилище", shortLabel = "СВЯТИЛ.", color = new Color(0.58f, 0.42f, 0.86f),
                    description = "Гильдия магии: жрецы на поле и +1 перемешивание поля в день. Прокачка жрецов и магов — на вкладке «Войска».",
                    requiresAnyTech = new[] { TechIds.Mysticism }, productionCost = 5, maxHp = 60, unitBranch = UnitClass.Priest,
                    effects = { new EffectValue(CityEffect.UnlockPriests, 1), new EffectValue(CityEffect.Reshuffles, 1) },
                    upgrades =
                    {
                        new UpgradeConfig("herbs", "Лечебные травы", "Жрецы лечат на 50% сильнее", TechIds.Healing, 30,
                            new EffectValue(CityEffect.PriestHeal, 0.5f)),
                        new UpgradeConfig("psalter", "Молитвенник", "«Молитва» на 30% сильнее", "", 25,
                            new EffectValue(CityEffect.PriestSkill, 0.3f)),
                        new UpgradeConfig("candles", "Свечи", $"В боевом слоте появляются Свечи {candle}. 3+ свечи в любом месте слота — «Чудо»: все свои лечатся на 12% ХП и 2 шага получают на 25% меньше урона", "", 30,
                            new EffectValue(CityEffect.RelicSymbols, 1)),
                        new UpgradeConfig("staffs", "Посохи", "Жрецы с посохами: +25% ХП и атаки, лечение +25%", TechIds.Staffs, 35,
                            new EffectValue(CityEffect.PriestStats, 0.25f), new EffectValue(CityEffect.PriestHeal, 0.25f)) { gate = true },
                        new UpgradeConfig("cloisters", "Обители", "Жрецы +25% ХП и атаки", TechIds.Monasticism, 50,
                            new EffectValue(CityEffect.PriestStats, 0.25f)) { tier = 2 },
                        new UpgradeConfig("holy_relics", "Святые мощи", $"«Чудо» от Свечей {candle} лечит вдвое сильнее", "", 50,
                            new EffectValue(CityEffect.MiraclePower, 1)) { tier = 2 },
                        new UpgradeConfig("blessed_staffs", "Освящённые посохи", "«Молитва» на 50% сильнее", "", 60,
                            new EffectValue(CityEffect.PriestSkill, 0.5f)) { tier = 2 },
                        new UpgradeConfig("grimoires", "Гримуары", "Маги +30% ХП и атаки", TechIds.Sorcery, 70,
                            new EffectValue(CityEffect.MageStats, 0.3f)) { tier = 2 },
                        new UpgradeConfig("storm_staffs", "Грозовые посохи", "«Цепная молния» магов на 50% сильнее", TechIds.Sorcery, 80,
                            new EffectValue(CityEffect.MageSkill, 0.5f)) { tier = 2 },
                    },
                },
                new BuildingConfig
                {
                    type = BuildingType.Granary, title = "Амбар", shortLabel = "АМБАР", color = new Color(0.50f, 0.74f, 0.36f),
                    description = $"+1 {food} с каждого символа еды.",
                    requiresAnyTech = new[] { TechIds.Agriculture }, productionCost = 4, maxHp = 50,
                    effects = { new EffectValue(CityEffect.FoodPerSymbol, 1) },
                    upgrades =
                    {
                        new UpgradeConfig("storerooms", "Кладовые", "Рост города на 25% дешевле", TechIds.Pottery, 6,
                            new EffectValue(CityEffect.GrowthDiscount, 0.25f)),
                    },
                },
                new BuildingConfig
                {
                    type = BuildingType.Workshop, title = "Мастерская", shortLabel = "МАСТЕР.", color = new Color(0.52f, 0.57f, 0.66f),
                    description = $"+1 {production} с каждого символа производства.",
                    requiresAnyTech = new[] { TechIds.Crafts }, productionCost = 4, maxHp = 50,
                    effects = { new EffectValue(CityEffect.ProductionPerSymbol, 1) },
                    upgrades =
                    {
                        new UpgradeConfig("bronze_tools", "Бронзовые инструменты", $"Ещё +1 {production} с символа", TechIds.Bronze, 8,
                            new EffectValue(CityEffect.ProductionPerSymbol, 1)),
                    },
                },
                new BuildingConfig
                {
                    type = BuildingType.School, title = "Школа", shortLabel = "ШКОЛА", color = new Color(0.36f, 0.60f, 0.86f),
                    description = $"+50% очков знаний {knowledge}.",
                    requiresAnyTech = new[] { TechIds.Writing }, productionCost = 5, maxHp = 50,
                    effects = { new EffectValue(CityEffect.KnowledgePercent, 0.5f) },
                    upgrades =
                    {
                        new UpgradeConfig("library", "Библиотека", $"Ещё +50% {knowledge}", TechIds.Philosophy, 10,
                            new EffectValue(CityEffect.KnowledgePercent, 0.5f)),
                    },
                },
                // Чудеса света: куда тратить накопленное производство
                Wonder(BuildingType.Stonehenge, "Стоунхендж", "СТОУНХ.", 35, $"+1 перемешивание поля в день и +20% {knowledge}",
                    new[] { TechIds.Mysticism }, new EffectValue(CityEffect.Reshuffles, 1), new EffectValue(CityEffect.KnowledgePercent, 0.2f)),
                Wonder(BuildingType.Pyramids, "Пирамиды", "ПИРАМИДЫ", 45, "+6 символов на поле каждый день (и ходов больше)",
                    new[] { TechIds.Crafts }, new EffectValue(CityEffect.Reserve, 6)),
                Wonder(BuildingType.GreatWall, "Великая стена", "СТЕНА", 50, "Набеги наносят на 40% меньше урона",
                    new[] { TechIds.Masonry }, new EffectValue(CityEffect.RaidDefense, 0.4f)),
                Wonder(BuildingType.Tiltyard, "Ристалище", "РИСТАЛ.", 55, "Выжившие получают +2 опыта за каждый бой",
                    new[] { TechIds.Bronze }, new EffectValue(CityEffect.BattleXp, 2)),
                Wonder(BuildingType.Colossus, "Колосс", "КОЛОСС", 60, $"+1 {gold} с каждой монеты на поле",
                    new[] { TechIds.Trade }, new EffectValue(CityEffect.GoldPerCoin, 1)),
                Wonder(BuildingType.GreatLibrary, "Великая библиотека", "БИБЛИОТ.", 70, $"Ещё +50% {knowledge}",
                    new[] { TechIds.Philosophy }, new EffectValue(CityEffect.KnowledgePercent, 0.5f)),
                Wonder(BuildingType.TerracottaArmy, "Терракотовая армия", "ТЕРРАК.", 90, "Все отряды (и ополченцы) +15% ХП и атаки",
                    new[] { TechIds.TowerShields, TechIds.Crossbows, TechIds.Staffs }, new EffectValue(CityEffect.ArmyStats, 0.15f)),
            };
        }

        /// <summary>Чудо света: одно на город, дорогое в ⚒, без улучшений; разрушено набегом — не работает, пока не починят.</summary>
        private static BuildingConfig Wonder(BuildingType type, string title, string shortLabel, int productionCost, string description,
            string[] techs, params EffectValue[] effects)
        {
            return new BuildingConfig
            {
                type = type, title = title, shortLabel = shortLabel, color = new Color(0.86f, 0.80f, 0.58f),
                description = "Чудо света: " + description + ".",
                requiresAnyTech = techs, productionCost = productionCost, maxHp = 80,
                effects = effects.ToList(),
            };
        }

        // =====================================================================
        //  Эффекты
        // =====================================================================

        /// <summary>Сумма эффекта по всем целым зданиям и их улучшениям (с учётом изученной науки).</summary>
        public float Effect(CityEffect effect)
        {
            float total = 0f;
            foreach (Building building in placed)
            {
                if (building.IsRuined) continue;
                total += Sum(building.Config.effects, effect);
                foreach (UpgradeConfig upgrade in building.Config.upgrades)
                {
                    if (building.Upgrades.Contains(upgrade.id)) total += Sum(upgrade.effects, effect);
                }
            }
            return total;
        }

        private float Sum(List<EffectValue> effects, CityEffect effect)
        {
            float total = 0f;
            foreach (EffectValue value in effects)
            {
                if (value.effect == effect && IsResearched(value.requiredTech)) total += value.value;
            }
            return total;
        }

        /// <summary>Лучшее значение эффекта (для «уровней», которые не складываются — например, щитов).</summary>
        public float MaxEffect(CityEffect effect)
        {
            float best = 0f;
            foreach (Building building in placed)
            {
                if (building.IsRuined) continue;
                best = Mathf.Max(best, Max(building.Config.effects, effect));
                foreach (UpgradeConfig upgrade in building.Config.upgrades)
                {
                    if (building.Upgrades.Contains(upgrade.id)) best = Mathf.Max(best, Max(upgrade.effects, effect));
                }
            }
            return best;
        }

        private float Max(List<EffectValue> effects, CityEffect effect)
        {
            float best = 0f;
            foreach (EffectValue value in effects)
            {
                if (value.effect == effect && IsResearched(value.requiredTech)) best = Mathf.Max(best, value.value);
            }
            return best;
        }

        private bool IsResearched(string tech) => string.IsNullOrEmpty(tech) || (techTree != null && techTree.IsResearched(tech));

        /// <summary>Округление «половина вверх»: 7.5 → 8 (без банковского округления и мусора float).</summary>
        private static int Round(float value) => Mathf.FloorToInt(value + 0.5f);

        public int Housing => Round(Effect(CityEffect.Housing));
        public int Homeless => Mathf.Max(0, Population - Housing);
        public int BoardSize => Mathf.Clamp(baseBoardSize + Mathf.Max(0, Population - startPopulation) / citizensPerBoardSize,
            baseBoardSize, Mathf.Max(baseBoardSize, maxBoardSize));
        public int BoardReserve => baseReserve + reservePerCitizen * Population + Round(Effect(CityEffect.Reserve));
        public int GoldPerCoin => baseGoldPerCoin + Round(Effect(CityEffect.GoldPerCoin));
        public int ProductionPerSymbol => baseProductionPerSymbol + Round(Effect(CityEffect.ProductionPerSymbol));
        public int FoodPerSymbol => baseFoodPerSymbol + Round(Effect(CityEffect.FoodPerSymbol));
        public int FoodUpkeep => Mathf.CeilToInt(foodPerCitizen * Population - 0.001f);
        public int GrowthCost => Mathf.Max(1, Round((baseGrowthCost + growthCostPerCitizen * Population)
                                                    * (1f - Mathf.Clamp(Effect(CityEffect.GrowthDiscount), 0f, 0.9f))));
        public float RaidDamageMultiplier => 1f - Mathf.Clamp(Effect(CityEffect.RaidDefense), 0f, 0.9f);
        public float PriestHealMultiplier => 1f + Effect(CityEffect.PriestHeal);

        /// <summary>Щиты воинов: 0 — нет, 1 — обычные, 2 — ростовые.</summary>
        public int ShieldLevel => Mathf.Clamp(Round(MaxEffect(CityEffect.ShieldLevel)), 0, 2);

        /// <summary>Луки стали арбалетами: болт пробивает щиты.</summary>
        public bool HasCrossbows => Effect(CityEffect.Crossbows) > 0f;

        /// <summary>Символов за день: поле целиком + запас сверху.</summary>
        public int SymbolsPerDay => BoardSize * BoardSize + BoardReserve;

        /// <summary>Ходов на поле в день: база + 1 за каждые symbolsPerExtraAction символов сверх стартовых (позже — культура).</summary>
        public int ActionsPerDay
        {
            get
            {
                int startSymbols = baseBoardSize * baseBoardSize + baseReserve + reservePerCitizen * startPopulation;
                return actionsPerDay + Mathf.Max(0, SymbolsPerDay - startSymbols) / Mathf.Max(1, symbolsPerExtraAction);
            }
        }

        /// <summary>Перемешиваний поля в день — дают постройки веры (святилище).</summary>
        public int ReshufflesPerDay => Round(Effect(CityEffect.Reshuffles));

        /// <summary>Очки знаний до бонусов: жители + ратуша.</summary>
        public int KnowledgeBase => knowledgePerCitizen * Population + (IsTownHallRuined ? 0 : townHallKnowledge);
        public float KnowledgeMultiplier => 1f + Effect(CityEffect.KnowledgePercent);
        public int KnowledgePerDay => Round(KnowledgeBase * KnowledgeMultiplier);

        public bool IsClassUnlocked(UnitClass unitClass)
        {
            switch (unitClass)
            {
                case UnitClass.Warrior: return Effect(CityEffect.UnlockWarriors) > 0f;
                case UnitClass.Archer: return Effect(CityEffect.UnlockArchers) > 0f;
                case UnitClass.Priest: return Effect(CityEffect.UnlockPriests) > 0f;
                case UnitClass.Bomber:
                case UnitClass.Monk:
                case UnitClass.Mage: return IsHybridUnlocked(unitClass);
                case UnitClass.Hero: return HeroUnlocked;
                default: return true;
            }
        }

        /// <summary>Гибрид складывается на поле из двух классов: подрывник — «Порох», монах — «Боевые искусства», маг — «Чародейство».</summary>
        public bool IsHybridUnlocked(UnitClass hybrid)
        {
            switch (hybrid)
            {
                case UnitClass.Bomber: return IsResearched(TechIds.Gunpowder);
                case UnitClass.Monk: return IsResearched(TechIds.MartialArts);
                case UnitClass.Mage: return IsResearched(TechIds.Sorcery);
                default: return false;
            }
        }

        /// <summary>«Былины»: линия 5+ любых юнитов призывает богатыря.</summary>
        public bool HeroUnlocked => IsResearched(TechIds.Epics);

        /// <summary>Множитель ХП и атаки класса: улучшения его гильдии и чудеса, что усиливают всех.</summary>
        public float ClassStatMultiplier(UnitClass unitClass) => GuildStatMultiplier(unitClass) * (1f + Effect(CityEffect.ArmyStats));

        private float GuildStatMultiplier(UnitClass unitClass)
        {
            switch (unitClass)
            {
                case UnitClass.Warrior: return 1f + Effect(CityEffect.WarriorStats);
                case UnitClass.Archer: return 1f + Effect(CityEffect.ArcherStats);
                case UnitClass.Priest: return 1f + Effect(CityEffect.PriestStats);
                case UnitClass.Bomber:
                case UnitClass.Monk:
                case UnitClass.Mage:
                case UnitClass.Hero:
                    // Гибриды и богатырь пользуются улучшениями «родительских» гильдий (в среднем) и своими
                    return UnitClasses.Parents(unitClass).Average(GuildStatMultiplier) * (1f + Effect(OwnStats(unitClass)));
                default: return 1f;
            }
        }

        /// <summary>+опыта каждому выжившему за бой (Ристалище).</summary>
        public int BattleXpBonus => Round(Effect(CityEffect.BattleXp));

        /// <summary>Свои улучшения статов гибрида или богатыря.</summary>
        private static CityEffect OwnStats(UnitClass unitClass)
        {
            switch (unitClass)
            {
                case UnitClass.Bomber: return CityEffect.BomberStats;
                case UnitClass.Monk: return CityEffect.MonkStats;
                case UnitClass.Mage: return CityEffect.MageStats;
                default: return CityEffect.HeroStats;
            }
        }

        /// <summary>Сила массового навыка класса в бою: улучшения навыка × статы класса (у жрецов — лечение).</summary>
        public float SkillPower(UnitClass unitClass)
        {
            switch (unitClass)
            {
                case UnitClass.Warrior: return (1f + Effect(CityEffect.WarriorSkill)) * ClassStatMultiplier(unitClass);
                case UnitClass.Archer: return (1f + Effect(CityEffect.ArcherSkill)) * ClassStatMultiplier(unitClass);
                case UnitClass.Priest: return (1f + Effect(CityEffect.PriestSkill)) * PriestHealMultiplier;
                case UnitClass.Bomber: return 1f + Effect(CityEffect.BomberSkill);
                case UnitClass.Monk: return 1f + Effect(CityEffect.MonkSkill);
                case UnitClass.Mage: return 1f + Effect(CityEffect.MageSkill);
                default: return 1f;
            }
        }

        /// <summary>Уровень Знамён (джокеров) в боевом слоте.</summary>
        public int BannerLevel => Round(Effect(CityEffect.BannerSymbols));
        /// <summary>Уровень множителей в боевом слоте и их сила (×2, ×3).</summary>
        public int MultiplierLevel => Round(Effect(CityEffect.MultiplierSymbols));
        public int MultiplierValue => 2 + Round(Effect(CityEffect.MultiplierValue));
        /// <summary>Уровень Свечей в боевом слоте и сила «Чуда».</summary>
        public int RelicLevel => Round(Effect(CityEffect.RelicSymbols));
        public float MiracleMultiplier => 1f + Effect(CityEffect.MiraclePower);
        /// <summary>+шанс полного блока щитом у своих воинов.</summary>
        public float ShieldBlockBonus => Effect(CityEffect.ShieldBlockBonus);

        /// <summary>Гильдия класса (казармы, стрельбище, святилище) и её здание в городе (null — не построено).</summary>
        public BuildingConfig GuildConfig(UnitClass unitClass) => buildings.FirstOrDefault(b => b != null && b.unitBranch == unitClass && unitClass != UnitClass.None);
        public Building Guild(UnitClass unitClass) => placed.FirstOrDefault(b => b.Config.unitBranch == unitClass && unitClass != UnitClass.None);

        /// <summary>Сколько улучшений отрядов куплено во всех гильдиях (целых).</summary>
        public int UnitUpgradesBought => placed.Where(b => b.Config.unitBranch != UnitClass.None && !b.IsRuined).Sum(b => b.Upgrades.Count);

        /// <summary>«Уровень» военного дела: открытые классы + половина купленных улучшений отрядов (растит боевой слот).</summary>
        public int MilitaryLevel
        {
            get
            {
                int level = 0;
                foreach (UnitClass unitClass in new[] { UnitClass.Warrior, UnitClass.Archer, UnitClass.Priest })
                {
                    if (IsClassUnlocked(unitClass)) level++;
                }
                return level + UnitUpgradesBought / 2;
            }
        }

        // =====================================================================
        //  Фокус поля: каких ресурсов завтра больше
        // =====================================================================

        /// <summary>Вес ресурса на поле сегодня (его выбрали вчера).</summary>
        public int FocusToday(ResourceType resource) => activeFocus[(int)resource];

        /// <summary>Вес ресурса на поле завтра (можно менять весь день).</summary>
        public int FocusTomorrow(ResourceType resource) => plannedFocus[(int)resource];

        /// <summary>Доля ресурса среди символов-ресурсов сегодня (сумма по трём ресурсам — 1).</summary>
        public float FocusShare(ResourceType resource) => activeFocus[(int)resource] / (float)Mathf.Max(1, activeFocus.Sum());

        /// <summary>Кнопка фокуса: вес ресурса на завтра 1 → 2 → … → максимум → 1.</summary>
        public void CycleFocus(ResourceType resource)
        {
            int index = (int)resource;
            plannedFocus[index] = plannedFocus[index] % Mathf.Max(1, maxFocusWeight) + 1;
            NotifyChanged();
        }

        public void SetFocusTomorrow(int gold, int production, int food)
        {
            int max = Mathf.Max(1, maxFocusWeight);
            plannedFocus[(int)ResourceType.Gold] = Mathf.Clamp(gold, 1, max);
            plannedFocus[(int)ResourceType.Production] = Mathf.Clamp(production, 1, max);
            plannedFocus[(int)ResourceType.Food] = Mathf.Clamp(food, 1, max);
            NotifyChanged();
        }

        // =====================================================================
        //  Постройка, улучшение, ремонт
        // =====================================================================

        public BuildingConfig Config(BuildingType type) => buildings.FirstOrDefault(b => b != null && b.type == type);

        public Building BuildingAt(Vector2Int cell) => placed.FirstOrDefault(b => b.Cell == cell);

        public bool InMap(Vector2Int cell) => World != null && World.InBounds(cell);

        /// <summary>Клетка в границах города (квадрат радиуса TerritoryRadius вокруг ратуши).</summary>
        public bool InTerritory(Vector2Int cell) =>
            InMap(cell) && Mathf.Max(Mathf.Abs(cell.x - CityCenter.x), Mathf.Abs(cell.y - CityCenter.y)) <= TerritoryRadius;

        public TerrainType TerrainAt(Vector2Int cell) => World != null ? World.TerrainAt(cell) : TerrainType.Grass;

        /// <summary>Здесь можно ставить здание: в границах города, не вода и свободно.</summary>
        public bool CanPlaceOn(Vector2Int cell) => InTerritory(cell) && !World.IsWater(cell) && BuildingAt(cell) == null;

        public bool IsUnlocked(BuildingConfig config)
        {
            return config.requiresAnyTech == null || config.requiresAnyTech.Length == 0 || config.requiresAnyTech.Any(IsResearched);
        }

        public string RequiredTechTitles(BuildingConfig config)
        {
            if (config.requiresAnyTech == null) return string.Empty;
            return string.Join(" или ", config.requiresAnyTech.Select(id => techTree != null ? techTree.Title(id) : id));
        }

        /// <summary>Почему нельзя построить (null — можно).</summary>
        public string WhyCannotBuild(BuildingConfig config, Vector2Int cell)
        {
            if (!InMap(cell)) return "За краем карты";
            if (World.IsWater(cell)) return "На воде строить нельзя";
            if (!InTerritory(cell)) return "Вне границ города";
            if (BuildingAt(cell) != null) return "Клетка занята";
            if (!config.buildable) return "Это здание нельзя построить";
            if (!IsUnlocked(config)) return $"Нужна наука: {RequiredTechTitles(config)}";
            if (config.unique && placed.Any(b => b.Type == config.type)) return "Уже построено";
            if (Production < config.productionCost) return $"Нужно {config.productionCost} {GameVisuals.IconProduction}";
            return null;
        }

        public bool TryBuild(BuildingType type, Vector2Int cell, out string message)
        {
            BuildingConfig config = Config(type);
            message = config == null ? "Нет такого здания" : WhyCannotBuild(config, cell);
            if (message != null) return false;

            Production -= config.productionCost;
            placed.Add(new Building(config, cell));
            message = $"Построено: {config.title}";
            NotifyChanged();
            return true;
        }

        /// <summary>
        /// Цена улучшения. В гильдиях цены плавно растут вглубь ветки: первые ~30–45, последние ~90–115.
        /// Все цены покупок идут через этот метод — тут же можно сделать цену зависящей от состояния города.
        /// </summary>
        public int UpgradeCost(Building building, UpgradeConfig upgrade) => upgrade != null ? upgrade.goldCost : 0;

        /// <summary>В здании можно что-то купить прямо сейчас: золота хватает, наука изучена, ярус открыт (для подсветки).</summary>
        public bool CanUpgradeNow(Building building) =>
            building != null && building.Config.upgrades.Any(u => WhyCannotUpgrade(building, u) == null);

        /// <summary>Есть что купить: в гильдиях (guilds — вкладка «Войска») или в остальных зданиях города.</summary>
        public bool AnyUpgradeNow(bool guilds) => placed.Any(b => (b.Config.unitBranch != UnitClass.None) == guilds && CanUpgradeNow(b));

        public string WhyCannotUpgrade(Building building, UpgradeConfig upgrade)
        {
            if (building.Upgrades.Contains(upgrade.id)) return "Уже сделано";
            // Ярус открывается ключом прошлого яруса (щиты, арбалеты, посохи)
            UpgradeConfig previousGate = building.Config.upgrades.FirstOrDefault(u => u.gate && u.tier == upgrade.tier - 1);
            if (upgrade.tier > 1 && previousGate != null && !building.Upgrades.Contains(previousGate.id))
                return $"Сначала: {previousGate.title}";
            if (!IsResearched(upgrade.requiredTech))
                return $"Нужна наука: {(techTree != null ? techTree.Title(upgrade.requiredTech) : upgrade.requiredTech)}";
            if (upgrade.gate)
            {
                int bought = building.Config.upgrades.Count(u => !u.gate && u.tier == upgrade.tier && building.Upgrades.Contains(u.id));
                if (bought < upgrade.requiredInTier) return $"Нужно ещё {upgrade.requiredInTier - bought} улучш. этого яруса";
            }
            if (building.IsRuined) return "Сначала почините здание";
            int cost = UpgradeCost(building, upgrade);
            if (Gold < cost) return $"Нужно {cost} {GameVisuals.IconGold}";
            return null;
        }

        public bool TryUpgrade(Building building, string upgradeId, out string message)
        {
            UpgradeConfig upgrade = building?.Config.upgrades.FirstOrDefault(u => u.id == upgradeId);
            message = upgrade == null ? "Нет такого улучшения" : WhyCannotUpgrade(building, upgrade);
            if (message != null) return false;

            Gold -= UpgradeCost(building, upgrade);
            building.Upgrades.Add(upgrade.id);
            message = $"{building.Config.title}: {upgrade.title}";
            NotifyChanged();
            return true;
        }

        // =====================================================================
        //  Куда ещё девать производство: границы и караваны
        // =====================================================================

        /// <summary>Цена расширения границ на 1 клетку во все стороны (дома расширяют их сами и бесплатно).</summary>
        public int BorderExpansionCost => borderCostPerRadius * (TerritoryRadius + 1);

        public string WhyCannotExpandBorders()
        {
            if (TerritoryRadius >= maxCityRadius) return "Границы уже самые широкие";
            if (IsTownHallRuined) return "Сначала почините ратушу";
            if (Production < BorderExpansionCost) return $"Нужно {BorderExpansionCost} {GameVisuals.IconProduction}";
            return null;
        }

        public bool TryExpandBorders(out string message)
        {
            message = WhyCannotExpandBorders();
            if (message != null) return false;
            Production -= BorderExpansionCost;
            TerritoryRadius++;
            int border = TerritoryRadius * 2 + 1;
            message = $"Границы города расширены: {border}×{border}";
            NotifyChanged();
            return true;
        }

        /// <summary>Караван с базара: caravanProduction ⚒ → caravanGold 🪙 (сколько угодно раз).</summary>
        public string WhyCannotSendCaravan()
        {
            Building bazaar = placed.FirstOrDefault(b => b.Type == BuildingType.Bazaar);
            if (bazaar == null) return "Нет базара";
            if (bazaar.IsRuined) return "Сначала почините базар";
            if (Production < caravanProduction) return $"Нужно {caravanProduction} {GameVisuals.IconProduction}";
            return null;
        }

        public bool TrySendCaravan(out string message)
        {
            message = WhyCannotSendCaravan();
            if (message != null) return false;
            Production -= caravanProduction;
            Gold += caravanGold;
            message = $"Караван ушёл: −{caravanProduction} {GameVisuals.IconProduction}, +{caravanGold} {GameVisuals.IconGold}";
            NotifyChanged();
            return true;
        }

        public int RepairCost(Building building)
        {
            return building == null || !building.IsDamaged ? 0 : Mathf.CeilToInt((building.MaxHp - building.Hp) / (float)hpPerProduction);
        }

        public bool TryRepair(Building building, out string message)
        {
            int cost = RepairCost(building);
            if (cost <= 0)
            {
                message = "Здание целое";
                return false;
            }
            if (Production < cost)
            {
                message = $"Нужно {cost} {GameVisuals.IconProduction}";
                return false;
            }
            Production -= cost;
            building.Hp = building.MaxHp;
            message = $"Починено: {building.Config.title}";
            NotifyChanged();
            return true;
        }

        // =====================================================================
        //  Ресурсы
        // =====================================================================

        public void AddProduction(int amount)
        {
            Production = Mathf.Max(0, Production + amount);
            NotifyChanged();
        }

        public void AddFood(int amount)
        {
            Food = Mathf.Max(0, Food + amount);
            NotifyChanged();
        }

        public void AddGold(int amount)
        {
            Gold = Mathf.Max(0, Gold + amount);
            NotifyChanged();
        }

        public bool TrySpendGold(int amount)
        {
            if (Gold < amount) return false;
            Gold -= amount;
            NotifyChanged();
            return true;
        }

        public void AddKnowledge(int amount)
        {
            Knowledge = Mathf.Max(0, Knowledge + amount);
            NotifyChanged();
        }

        public bool TrySpendKnowledge(int amount)
        {
            if (Knowledge < amount) return false;
            Knowledge -= amount;
            NotifyChanged();
            return true;
        }

        // =====================================================================
        //  Утро: знания, еда, рост
        // =====================================================================

        /// <summary>
        /// Начало нового дня: приходят очки знаний; жители едят, излишек растит население
        /// (если дома заполнены — ставится новый), нехватка — голод (минус житель).
        /// Фокус поля на сегодня — тот, что выбрали вчера. Возвращает строку-отчёт.
        /// </summary>
        public string ProcessNewDay()
        {
            var report = new List<string>();
            int knowledge = KnowledgePerDay;
            Knowledge += knowledge;
            report.Add($"+{knowledge} {GameVisuals.IconKnowledge}");

            int upkeep = FoodUpkeep;
            if (Food < upkeep)
            {
                Food = 0;
                if (Population > 1)
                {
                    Population--;
                    report.Add($"Голод! Не хватило {GameVisuals.IconFood} — жителей стало {Population}");
                }
                else
                {
                    report.Add($"Голод! Не хватило {GameVisuals.IconFood}");
                }
            }
            else
            {
                Food -= upkeep;
                int cost = GrowthCost;
                if (Food < cost)
                {
                    report.Add($"Жители съели {upkeep} {GameVisuals.IconFood}, до роста {Food}/{cost}");
                }
                else
                {
                    string blocked = MakeRoomForCitizen(out bool newHouse);
                    if (blocked == null)
                    {
                        Food -= cost;
                        Population++;
                        report.Add($"Город вырос: {Population} {GameVisuals.IconPopulation}" + (newHouse ? ", построен новый дом" : string.Empty));
                    }
                    else
                    {
                        Food = cost; // копить сверх цены роста некуда
                        report.Add(blocked);
                    }
                }
            }

            if (bordersGrew)
            {
                bordersGrew = false;
                report.Add("Границы города расширились");
            }

            Array.Copy(plannedFocus, activeFocus, ResourceCount);
            NotifyChanged();
            return string.Join(" · ", report);
        }

        /// <summary>Место для ещё одного жителя: если все дома заполнены — ставит новый. null — место есть.</summary>
        private string MakeRoomForCitizen(out bool newHouse)
        {
            newHouse = false;
            if (Population < Housing) return null;
            if (placed.Any(b => b.Type == BuildingType.House && b.IsRuined)) return "Город не растёт: почините разрушенные дома";
            if (!PlaceHouse()) return "Город не растёт: в границах города нет места для дома";
            newHouse = true;
            return null;
        }

        private bool PlaceHouse()
        {
            Vector2Int? cell = FindHouseCell();
            // Места нет — границы города расширяются (до maxCityRadius)
            while (cell == null && TerritoryRadius < maxCityRadius)
            {
                TerritoryRadius++;
                bordersGrew = true;
                cell = FindHouseCell();
            }
            if (cell == null) return false;
            Place(BuildingType.House, cell.Value);
            return true;
        }

        /// <summary>Свободная клетка суши в границах города для нового дома: рядом с городом и поближе к ратуше.</summary>
        private Vector2Int? FindHouseCell()
        {
            Vector2Int center = CityCenter;
            var free = new List<Vector2Int>();
            for (int y = center.y - TerritoryRadius; y <= center.y + TerritoryRadius; y++)
            {
                for (int x = center.x - TerritoryRadius; x <= center.x + TerritoryRadius; x++)
                {
                    var cell = new Vector2Int(x, y);
                    if (CanPlaceOn(cell)) free.Add(cell);
                }
            }
            if (free.Count == 0) return null;

            bool NextToCity(Vector2Int cell) =>
                new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right }.Any(d => BuildingAt(cell + d) != null);
            return free.OrderBy(c => NextToCity(c) ? 0 : 1)
                .ThenBy(c => (c - center).sqrMagnitude)
                .ThenBy(c => Mathf.Abs(c.y - center.y))
                .ThenBy(c => c.x)
                .First();
        }

        /// <summary>Сколько жителей в доме: дома заселяются по порядку постройки.</summary>
        public int ResidentsIn(Building house)
        {
            if (house == null || house.Type != BuildingType.House || house.IsRuined) return 0;
            int capacity = Round(Sum(house.Config.effects, CityEffect.Housing));
            int before = 0;
            foreach (Building building in placed)
            {
                if (building == house) break;
                if (building.Type == BuildingType.House && !building.IsRuined) before += capacity;
            }
            return Mathf.Clamp(Population - before, 0, capacity);
        }

        // =====================================================================
        //  Набеги
        // =====================================================================

        /// <summary>
        /// Набег: каждый уцелевший варвар бьёт по случайному зданию (ратушу — в последнюю очередь).
        /// Возвращает короткий итог: что разрушено и что повреждено (пусто — бить было нечего).
        /// </summary>
        public string ApplyRaid(IReadOnlyList<float> hits)
        {
            Dictionary<Building, int> hpBefore = placed.ToDictionary(b => b, b => b.Hp);
            float multiplier = RaidDamageMultiplier;
            foreach (float hit in hits)
            {
                List<Building> targets = placed.Where(b => !b.IsRuined && b.Type != BuildingType.TownHall).ToList();
                Building target = targets.Count > 0 ? targets[Random.Range(0, targets.Count)] : TownHall;
                if (target == null || target.IsRuined) continue;

                int damage = Mathf.Max(1, Mathf.RoundToInt(hit * multiplier));
                target.Hp = Mathf.Max(0, target.Hp - damage);
            }
            NotifyChanged();

            var parts = new List<string>();
            List<string> ruined = placed.Where(b => b.IsRuined && hpBefore[b] > 0).Select(b => b.Config.title).ToList();
            List<string> damaged = placed.Where(b => !b.IsRuined && b.Hp < hpBefore[b]).Select(b => $"{b.Config.title} {b.Hp}/{b.MaxHp}").ToList();
            if (ruined.Count > 0) parts.Add("разрушено: " + string.Join(", ", ruined));
            if (damaged.Count > 0) parts.Add("повреждено: " + string.Join(", ", damaged));
            return string.Join("; ", parts);
        }

        public Building TownHall => placed.FirstOrDefault(b => b.Type == BuildingType.TownHall);
        public bool IsTownHallRuined => TownHall == null || TownHall.IsRuined;

        // =====================================================================
        //  Новый забег
        // =====================================================================

        /// <summary>Новый забег: ратуша, базар и дома на стартовых жителей; ресурсы — стартовые, фокус 1:1:1.</summary>
        public void ResetCity()
        {
            placed.Clear();
            int seed = worldSeed != 0 ? worldSeed : UnityEngine.Random.Range(1, int.MaxValue);
            World = WorldMap.Generate(worldWidth, worldHeight, seed);
            CityCenter = World.FindCitySite();
            TerritoryRadius = Mathf.Min(cityRadius, maxCityRadius);
            bordersGrew = false;
            Place(BuildingType.TownHall, CityCenter);
            var below = new Vector2Int(CityCenter.x, CityCenter.y + 1);
            Place(BuildingType.Bazaar, CanPlaceOn(below) ? below : FindHouseCell() ?? below);
            Population = startPopulation;
            while (Housing < Population && PlaceHouse()) { }

            Food = startFood;
            Production = startProduction;
            Gold = startGold;
            Knowledge = startKnowledge;
            for (int i = 0; i < ResourceCount; i++)
            {
                plannedFocus[i] = 1;
                activeFocus[i] = 1;
            }
            NotifyChanged();
        }

        private void Place(BuildingType type, Vector2Int cell)
        {
            BuildingConfig config = Config(type);
            if (config != null && CanPlaceOn(cell)) placed.Add(new Building(config, cell));
        }

        /// <summary>Что даёт здание сейчас (для панели здания).</summary>
        public string DescribeBuilding(Building building)
        {
            var lines = new List<string>
            {
                building.IsRuined ? "РАЗРУШЕНО — не работает, пока не починят" : $"Прочность {building.Hp}/{building.MaxHp}",
                building.Config.description,
            };
            if (building.Type == BuildingType.House && !building.IsRuined)
                lines.Add($"Живут: {ResidentsIn(building)} из {Round(Sum(building.Config.effects, CityEffect.Housing))} {GameVisuals.IconPopulation}");
            var done = building.Config.upgrades.Where(u => building.Upgrades.Contains(u.id)).Select(u => u.title).ToList();
            if (done.Count > 0) lines.Add("Улучшения: " + string.Join(", ", done));
            return string.Join("\n", lines.Where(l => !string.IsNullOrEmpty(l)));
        }

        private void NotifyChanged() => Changed?.Invoke();
    }
}
