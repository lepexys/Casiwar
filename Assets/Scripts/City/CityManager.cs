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
        // Усилители юнитов и постройки особых мест: местность решает, где их ставить
        Smelter,   // плавильня — только камни: больше воинов на поле
        Sawmill,   // лесопилка — только лес: больше лучников
        Temple,    // храм — обычная суша: больше жрецов
        Monastery, // монастырь — только гора: без него нет монахов
        MageTower, // башня магов — только волшебный источник: без неё нет магов
    }

    /// <summary>Что дают здания и улучшения.</summary>
    public enum CityEffect
    {
        Housing,             // мест для жителей
        GoldPerCoin,         // +золота к каждому собранному кластеру монет (раньше — к каждой монете)
        FoodPerSymbol,       // +еды к каждому собранному кластеру еды
        Reserve,             // +спинов вербовки за ход (раньше — символы в запасе поля армии)
        UnlockWarriors,      // воины падают на поле
        UnlockArchers,       // лучники падают на поле
        UnlockPriests,         // жрецы падают на поле
        WarriorStats,        // +доля к ХП и атаке воинов
        ArcherStats,         // +доля к ХП и атаке лучников
        PriestHeal,            // +доля к лечению жрецов
        RaidDefense,         // −доля урона от набегов
        ProductionPerSymbol, // +⚒ к каждому собранному кластеру производства
        KnowledgePercent,    // +доля к очкам знаний
        GrowthDiscount,      // −доля еды на рост города
        PriestStats,           // +доля к ХП и атаке жрецов
        ShieldLevel,         // щиты воинов: 1 — обычные, 2 — ростовые (берётся лучший, не сумма)
        Crossbows,           // луки стали арбалетами: болт пробивает щиты
        BoardActions,        // +спинов вербовки за ход (раньше — действия на поле армии)
        WarriorSkill,        // +сила «Рывка» / «Тарана щитом»
        ArcherSkill,         // +сила «Залпа»
        PriestSkill,           // +сила «Молитвы»
        ShieldBlockBonus,    // +шанс поймать удар щитом целиком
        BannerSymbols,       // Знамёна (джокеры) на поле боя — уровень
        MultiplierSymbols,   // ×2 (джокер-множитель) на поле боя — уровень
        MultiplierValue,     // +1 к множителю (×2 → ×3)
        RelicSymbols,        // Свечи (3 в ряд — «Чудо») на поле боя — уровень
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
        WarriorBoost,        // часть нейтралов (Н) на поле становится воинами (плавильня)
        ArcherBoost,         // … лучниками (лесопилка)
        PriestBoost,         // … жрецами (храм)
        UnlockMonks,         // воин + жрец складываются в монаха (монастырь на горе)
        UnlockMages,         // лучник + жрец складываются в мага (башня магов на волшебном источнике)
        OrderWarriors,       // приказ гильдии: открывает суперудар воинов (5 в ряд на поле боя)
        OrderArchers,        // … лучников
        OrderPriests,        // … жрецов
        HoldColumns,         // «Выдержка»: суперудары перезаряжаются быстрее (раньше — удержание столбцов слота)
        // Дальше — эффекты для класса EffectValue.target (дерево «Войск»)
        RecruitShare,        // в спине гильдии класса символов класса больше (доля к весу)
        ExtraGuildSpin,      // +спин гильдии класса за ход
        RecruitLevel,        // новобранцы класса приходят на столько уровней выше
        SuperAt4,            // суперудар класса — уже с 4 в ряд
        Haste,               // бойцы класса бьют чаще (доля)
        BoardSymbols,        // символов класса на поле боя больше (доля к весу)
        Bloodlust,           // боец класса добил врага — лечится на долю ХП
        FireArrows,          // «Залп» поджигает: цель горит (доля атаки стрелка в секунду, 3 с)
        Martyrs,             // погиб наш боец — на поле боя падает Свеча (без класса)
        Shockwave,           // «Динамит» оглушает
        DoubleStrike,        // шанс ударить второй раз
        Runes,               // ряд символов класса превращает столько фишек поля боя в его символы
        HeroBanner,          // богатырь в бою — на поле боя падают Знамёна (без класса)
        ChargeRange,         // «Рывок» дальше на столько клеток
        Bleed,               // удар ранит: цель теряет долю атаки бойца в секунду, 3 с
        ShieldWall,          // боец рядом с бойцом своего класса получает меньше урона (доля)
        RangeBonus,          // +дальность стрельбы (клеток)
        Pierce,              // стрела пробивает цель и бьёт врага за ней (доля урона)
        HealSplash,          // лечение задевает соседей цели (доля)
        Cleanse,             // лечение снимает оглушение, горение и раны
        PriestChain,         // удар посохом перескакивает на второго врага (доля урона)
        SecondWind,          // союзник, погибший рядом с бойцом класса, раз за бой встаёт с долей ХП
        DeathBlast,          // погибая, боец взрывается: урон врагам рядом (× его атака)
        ExtraDynamite,       // «Динамит»: ещё столько шашек в другие кучи
        Dodge,               // шанс увернуться от удара
        Lifesteal,           // под «Огненными клинками» боец лечится на долю нанесённого урона (без класса)
        ChainStun,           // удар оглушает с шансом
        ExtraJumps,          // «Цепная молния» перескакивает ещё на столько целей
        HeroCleave,          // удар бьёт и всех врагов вокруг (доля урона)
        Toughness,           // получает меньше урона (доля)
        LeaderBoost,         // бонус вожака своей линейке больше (доля)
    }

    [Serializable]
    public class EffectValue
    {
        public CityEffect effect;
        public float value = 1f;
        [Tooltip("Эффект работает, только если изучена эта технология (пусто — всегда)")]
        public string requiredTech = "";

        public EffectValue() { }

        [Tooltip("Для каких бойцов (эффекты дерева «Войск»: спины, суперудар с 4, скорость…); None — для всех")]
        public UnitClass target = UnitClass.None;

        public EffectValue(CityEffect effect, float value, string requiredTech = "")
        {
            this.effect = effect;
            this.value = value;
            this.requiredTech = requiredTech;
        }

        public EffectValue(CityEffect effect, float value, UnitClass target, string requiredTech = "")
        {
            this.effect = effect;
            this.value = value;
            this.target = target;
            this.requiredTech = requiredTech;
        }
    }

    /// <summary>
    /// Улучшение здания: покупается за золото, может требовать науку.
    /// У гильдий (казармы, стрельбище, святилище) улучшения — узлы общего дерева «Войск» (как в tower defense):
    /// узлу нужны купленные узлы requires — в том числе из другой гильдии (гибриды стоят между ветками своих
    /// «родителей», богатырь — под гибридами); requiresAny — хватает любого. treePos — место узла в дереве,
    /// gate — «ключ» (крупный узел, где сходятся пути). Старые tier / requiredInTier — для улучшений без requires.
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
        [Tooltip("Нужны эти улучшения (id, в любой гильдии); пусто — первый узел ветки")]
        public string[] requires = new string[0];
        [Tooltip("Хватает любого из requires, а не всех")]
        public bool requiresAny;
        [Tooltip("Место в дереве «Войск»: x — столбец (0 — центр, ветки на −3, 0, +3), y — строка сверху")]
        public Vector2 treePos;
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
        [Tooltip("Где можно строить (подходит любая из местностей; пусто — обычная суша: трава, лес, камни)")]
        public TerrainType[] placeOn = new TerrainType[0];
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
    /// • золото, производство и еда приходят в начале хода сами (ResourceRain): символы падают в слот, самый большой
    ///   кластер идёт в казну, и так столько раз, сколько жителей (+ратуша). Фокус (например 2:1:1) решает,
    ///   каких ресурсов в следующий ход упадёт больше;
    /// • армию вербует слот на вкладке «Поле» (RecruitSlot): спинов — по населению, плюс по спину от гильдий и усилителей;
    /// • жители каждый день едят; излишек еды растит население → больше сборов ресурсов и спинов вербовки.
    ///   В доме живут 3 жителя; когда дома заполнены, новый дом ставится сам;
    /// • очки знаний приходят каждое утро — от жителей и ратуши, школа и библиотека их умножают;
    /// • лишнее производство уходит в чудеса света (дорогие уникальные постройки с сильным бонусом),
    ///   расширение границ в ратуше и караваны с базара в торговые города (⚒ → 🪙, WorldPowers);
    /// • местность решает, что где строить: плавильня (больше воинов) — только на камнях, лесопилка (лучники) —
    ///   в лесу, храм (жрецы) — где угодно на суше. Каждый усилитель — ещё спин вербовки, где бойцов его класса много;
    ///   усилителей всего три (любых).
    ///   Монахам нужен монастырь на редкой горе, магам — башня на редком волшебном источнике у воды:
    ///   ради них расширяют границы (⚒ в ратуше) или покупают клетки у границы за 🪙;
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

        public List<BuildingConfig> buildings = DefaultBuildings();

        [Header("Покупка клеток за золото (у границы города)")]
        [Min(0)] public int cellGoldBase = 10;
        [Tooltip("Каждая следующая купленная клетка дороже на столько")]
        [Min(0)] public int cellGoldStep = 5;

        [Header("Население и еда")]
        [Min(1)] public int startPopulation = 3;
        [Tooltip("Еды на жителя в ход (всего округляется вверх). Сбор даёт по кластеру на жителя — еды немного")]
        [Min(0f)] public float foodPerCitizen = 0.5f;
        [Tooltip("Еды на рост = база + за каждого жителя")]
        [Min(1)] public int baseGrowthCost = 4;
        [Min(0)] public int growthCostPerCitizen = 2;

        [Header("Поле армии (растёт с населением)")]
        [Min(3)] public int baseBoardSize = 4;
        [Min(3)] public int maxBoardSize = 7;
        [Tooltip("+1 к размеру поля за каждых N жителей сверх стартовых")]
        [Min(1)] public int citizensPerBoardSize = 2;
        [Tooltip("Символов в запасе на день = база + за каждого жителя (+ улучшения ратуши)")]
        [Min(0)] public int baseReserve = 5;
        [Min(0)] public int reservePerCitizen = 2;

        [Header("Сбор ресурсов: символ = 1 ресурс, здания дают +N к каждому собранному кластеру")]
        [Tooltip("Фокус сбора: вес каждого ресурса от 1 до этого числа (1:1:1 … 3:1:1)")]
        [Min(1)] public int maxFocusWeight = 3;

        [Header("Знания")]
        [Min(0)] public int startKnowledge = 5;
        [Tooltip("Очков знаний в день от каждого жителя")]
        [Min(0)] public int knowledgePerCitizen = 1;
        [Tooltip("Очков знаний в день от ратуши")]
        [Min(0)] public int townHallKnowledge = 2;

        [Header("Действия на поле армии")]
        [Tooltip("Действий на поле за ход при стартовом поле и запасе")]
        [Min(1)] public int actionsPerDay = 8;
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
        /// <summary>Клетки, купленные за золото сверх квадрата границ.</summary>
        private readonly HashSet<Vector2Int> boughtCells = new HashSet<Vector2Int>();
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
        /// <summary>Радиус границ города (квадрат вокруг ратуши; купленные клетки — сверху).</summary>
        public int TerritoryRadius { get; private set; }
        /// <summary>Растёт при любом изменении границ (вид перерисовывает рамку).</summary>
        public int TerritoryVersion { get; private set; }

        private bool bordersGrew;

        /// <summary>Чья-то земля (лагерь варваров, чужой город): вернёт причину, почему клетку не купить. Ставит WorldPowers.</summary>
        public Func<Vector2Int, string> ForeignLand;

        /// <summary>Границы на карте поменялись не у нас (появился чужой город) — перерисовать рамки.</summary>
        public void NotifyTerritoryChanged()
        {
            TerritoryVersion++;
            NotifyChanged();
        }

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
            string OrderText(UnitClass unitClass, string skill) =>
                $"Открывает суперудар «{AutoBattleManager.SuperName(unitClass)}»: 5 в ряд символов {GameVisuals.ClassIcon(unitClass)} на поле боя " +
                $"(без приказа 5 в ряд — просто «{skill}» вдвое сильнее)";
            return new List<BuildingConfig>
            {
                new BuildingConfig
                {
                    type = BuildingType.TownHall, title = "Ратуша", shortLabel = "РАТУША", color = new Color(0.78f, 0.62f, 0.32f),
                    description = $"Сердце города: +2 {knowledge} за ход. Здесь же — расширение границ за {production}. Разрушена — забег окончен.",
                    maxHp = 120, buildable = false,
                    upgrades =
                    {
                        new UpgradeConfig("bookkeeping", "Учёт", "+1 спин вербовки на «Поле» каждый ход", TechIds.Mathematics, 40,
                            new EffectValue(CityEffect.Reserve, 1)),
                        new UpgradeConfig("palisade", "Частокол", "Набеги наносят на 40% меньше урона", TechIds.Masonry, 35,
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
                    description = $"Отсюда уходят караваны в торговые города: {production} сейчас → {gold} по прибытии. «Торговые ряды» — больше золота со сбора.",
                    maxHp = 60, buildable = false,
                    upgrades =
                    {
                        new UpgradeConfig("market_rows", "Торговые ряды", $"+1 {gold} к каждому собранному кластеру монет", TechIds.Trade, 45,
                            new EffectValue(CityEffect.GoldPerCoin, 1)),
                    },
                },
                new BuildingConfig
                {
                    type = BuildingType.Barracks, title = "Казармы", shortLabel = "КАЗАРМЫ", color = new Color(0.80f, 0.34f, 0.28f),
                    description = "Гильдия ближнего боя: +1 спин вербовки за ход, в нём много воинов; ополченцы по прямым линиям становятся воинами. Прокачка воинов, монахов и богатыря — на вкладке «Войска».",
                    requiresAnyTech = new[] { TechIds.Warfare }, productionCost = 5, maxHp = 80, unitBranch = UnitClass.Warrior,
                    effects = { new EffectValue(CityEffect.UnlockWarriors, 1) },
                    upgrades =
                    {
                        // Воины — левая ветка (x = −3)
                        U("drill", "Муштра", "«Рывок» воинов на 2 клетки дальше и на 30% сильнее", "", 25, -3.6f, 1f, null,
                            new EffectValue(CityEffect.ChargeRange, 2f, UnitClass.Warrior), new EffectValue(CityEffect.WarriorSkill, 0.3f)),
                        U("recruiters_w", "Вербовщики", "В спине казарм на «Поле» вдвое больше воинов", "", 25, -2.4f, 1f, null,
                            new EffectValue(CityEffect.RecruitShare, 1f, UnitClass.Warrior)),
                        U("bronze_weapons", "Бронзовые мечи", "Удар воина ранит: враг истекает кровью 3 секунды (четверть атаки воина в секунду)",
                            TechIds.Bronze, 30, -3.6f, 2f, new[] { "drill" },
                            new EffectValue(CityEffect.Bleed, 0.25f, UnitClass.Warrior)),
                        U("war_cry", "Боевой клич", OrderText(UnitClass.Warrior, "Рывок"), "", 30, -2.4f, 2f, new[] { "recruiters_w" },
                            new EffectValue(CityEffect.OrderWarriors, 1)),
                        Key(U("shields", "Щиты", "Воины берут щиты: ловят удары в лицо, «Рывок» → «Таран щитом» (оглушает)", TechIds.Shields, 40,
                            -3f, 3f, new[] { "bronze_weapons", "war_cry" }, new EffectValue(CityEffect.ShieldLevel, 1)), any: true),
                        U("tower_shields", "Ростовые щиты", "Блок чаще (+10% целиком), щит прикрывает и бок, таран оглушает вдвое дольше",
                            TechIds.TowerShields, 50, -3.6f, 4f, new[] { "shields" },
                            new EffectValue(CityEffect.ShieldLevel, 2), new EffectValue(CityEffect.ShieldBlockBonus, 0.1f)),
                        U("levy", "Набор в полки", "+1 спин казарм на «Поле» каждый ход", "", 45, -2.4f, 4f, new[] { "shields" },
                            new EffectValue(CityEffect.ExtraGuildSpin, 1f, UnitClass.Warrior)),
                        U("phalanx", "Фаланга", "Воин рядом с другим воином получает на 30% меньше урона — держите строй; «Рывок» ещё на 30% сильнее",
                            "", 65, -3.6f, 5f, new[] { "tower_shields" },
                            new EffectValue(CityEffect.ShieldWall, 0.3f, UnitClass.Warrior), new EffectValue(CityEffect.WarriorSkill, 0.3f)),
                        U("regiment_banners", "Знамёна полков", $"На поле боя падают Знамёна {banner} (джокеры); воин, добивший врага, лечится на 20% ХП",
                            "", 60, -2.4f, 5f, new[] { "levy" },
                            new EffectValue(CityEffect.BannerSymbols, 1), new EffectValue(CityEffect.Bloodlust, 0.2f, UnitClass.Warrior)),
                        Key(U("blade_masters", "Мастера клинка", "Суперудар воинов — уже с 4 в ряд; воины бьют на 20% чаще", "", 85,
                            -3f, 6f, new[] { "phalanx", "regiment_banners" },
                            new EffectValue(CityEffect.SuperAt4, 1f, UnitClass.Warrior), new EffectValue(CityEffect.Haste, 0.2f, UnitClass.Warrior))),
                        // Монахи — внизу посередине: из вершин воинов или жрецов
                        AnyOf(U("fist_school", "Школа кулака", "Монах уворачивается от каждого четвёртого удара; символов монахов на поле боя больше",
                            TechIds.MartialArts, 70, 0f, 7.6f, new[] { "blade_masters", "prophets" },
                            new EffectValue(CityEffect.Dodge, 0.25f, UnitClass.Monk), new EffectValue(CityEffect.BoardSymbols, 0.5f, UnitClass.Monk))),
                        U("inner_fire", "Внутренний огонь", "Под «Огненными клинками» бойцы лечатся на 20% нанесённого урона; клинки на 30% сильнее",
                            TechIds.MartialArts, 75, 0f, 8.6f, new[] { "fist_school" },
                            new EffectValue(CityEffect.Lifesteal, 0.2f), new EffectValue(CityEffect.MonkSkill, 0.3f)),
                        U("whirl_strikes", "Вихрь ударов", "Монах бьёт второй раз с шансом 30%; суперудар монахов — с 4 в ряд", TechIds.MartialArts, 85,
                            0f, 9.6f, new[] { "inner_fire" },
                            new EffectValue(CityEffect.DoubleStrike, 0.3f, UnitClass.Monk), new EffectValue(CityEffect.SuperAt4, 1f, UnitClass.Monk)),
                        // Богатырь — в самом низу: из вершины любого гибрида
                        Key(U("bogatyr_outpost", "Богатырская застава", "Удар богатыря задевает всех врагов вокруг; богатырь получает на 30% меньше урона",
                            TechIds.Epics, 80, 0f, 10.9f, new[] { "powder_blast", "runes", "whirl_strikes" },
                            new EffectValue(CityEffect.HeroCleave, 0.6f, UnitClass.Hero), new EffectValue(CityEffect.Toughness, 0.3f, UnitClass.Hero)), any: true),
                        U("hero_banner", "Знамя богатыря", $"Пока богатырь в бою, на поле боя падают Знамёна {banner} (джокеры); вожак даёт своей линейке вдвое больше",
                            TechIds.Epics, 90, 0f, 11.9f, new[] { "bogatyr_outpost" },
                            new EffectValue(CityEffect.HeroBanner, 1), new EffectValue(CityEffect.LeaderBoost, 1f, UnitClass.Hero)),
                    },
                },
                new BuildingConfig
                {
                    type = BuildingType.ArcheryRange, title = "Стрельбище", shortLabel = "СТРЕЛЬБ.", color = new Color(0.42f, 0.66f, 0.30f),
                    description = "Гильдия дальнего боя: +1 спин вербовки за ход, в нём много лучников; ополченцы по линиям-углам становятся лучниками. Прокачка стрелков и подрывников — на вкладке «Войска».",
                    requiresAnyTech = new[] { TechIds.Archery }, productionCost = 5, maxHp = 70, unitBranch = UnitClass.Archer,
                    effects = { new EffectValue(CityEffect.UnlockArchers, 1) },
                    upgrades =
                    {
                        // Лучники — средняя ветка (x = 0)
                        U("bowstring", "Тугая тетива", "Лучники стреляют на клетку дальше", "", 25, -0.6f, 1f, null,
                            new EffectValue(CityEffect.RangeBonus, 1f, UnitClass.Archer)),
                        U("recruiters_a", "Вербовщики", "В спине стрельбища на «Поле» вдвое больше лучников", "", 25, 0.6f, 1f, null,
                            new EffectValue(CityEffect.RecruitShare, 1f, UnitClass.Archer)),
                        U("composite_bows", "Составные луки", "Стрела пробивает цель и бьёт врага за ней (60% урона)", TechIds.CompositeBow, 30,
                            -0.6f, 2f, new[] { "bowstring" },
                            new EffectValue(CityEffect.Pierce, 0.6f, UnitClass.Archer)),
                        U("fire_command", "Команда «Пли!»", OrderText(UnitClass.Archer, "Залп"), "", 30, 0.6f, 2f, new[] { "recruiters_a" },
                            new EffectValue(CityEffect.OrderArchers, 1)),
                        Key(U("crossbows", "Арбалеты", "Луки → арбалеты: стрелки +15%, болты вдвое реже ловят щитом", TechIds.Crossbows, 40,
                            0f, 3f, new[] { "composite_bows", "fire_command" },
                            new EffectValue(CityEffect.Crossbows, 1), new EffectValue(CityEffect.ArcherStats, 0.15f)), any: true),
                        U("sights", "Прицел", "На поле боя ×2 — джокер: ряд с ним вдвое сильнее", "", 45, -0.6f, 4f, new[] { "crossbows" },
                            new EffectValue(CityEffect.MultiplierSymbols, 1)),
                        U("archer_levy", "Набор стрелков", "+1 спин стрельбища на «Поле» каждый ход", "", 45, 0.6f, 4f, new[] { "crossbows" },
                            new EffectValue(CityEffect.ExtraGuildSpin, 1f, UnitClass.Archer)),
                        U("marksmen", "Меткие стрелки", "×2 на поле боя → ×3; лучники стреляют на 20% чаще", "", 60, -0.6f, 5f, new[] { "sights" },
                            new EffectValue(CityEffect.MultiplierValue, 1), new EffectValue(CityEffect.Haste, 0.2f, UnitClass.Archer)),
                        U("fire_arrows", "Огненные стрелы", "«Залп» на 50% сильнее и поджигает: цель горит 3 секунды", "", 60, 0.6f, 5f, new[] { "archer_levy" },
                            new EffectValue(CityEffect.ArcherSkill, 0.5f), new EffectValue(CityEffect.FireArrows, 0.3f, UnitClass.Archer)),
                        Key(U("snipers", "Снайперы", "Суперудар лучников — уже с 4 в ряд; все суперудары перезаряжаются почти вдвое быстрее", "", 85,
                            0f, 6f, new[] { "marksmen", "fire_arrows" },
                            new EffectValue(CityEffect.SuperAt4, 1f, UnitClass.Archer), new EffectValue(CityEffect.HoldColumns, 1))),
                        // Подрывники — между воинами и лучниками: из ключей обеих веток
                        U("powder_kegs", "Пороховые бочки", "Погибая, подрывник взрывается — врагам рядом вдвое больше его атаки; символов подрывников на поле боя больше",
                            TechIds.Gunpowder, 70, -1.5f, 4f, new[] { "shields", "crossbows" },
                            new EffectValue(CityEffect.DeathBlast, 2f, UnitClass.Bomber), new EffectValue(CityEffect.BoardSymbols, 0.5f, UnitClass.Bomber)),
                        U("dynamite_bundles", "Связки динамита", "«Динамит»: каждый подрывник бросает вторую шашку в другую кучу врагов; взрыв на 30% сильнее",
                            TechIds.Gunpowder, 75, -1.5f, 5f, new[] { "powder_kegs" },
                            new EffectValue(CityEffect.ExtraDynamite, 1f, UnitClass.Bomber), new EffectValue(CityEffect.BomberSkill, 0.3f)),
                        U("powder_blast", "Ударная волна", "«Динамит» оглушает; суперудар подрывников — с 4 в ряд", TechIds.Gunpowder, 85, -1.5f, 6f, new[] { "dynamite_bundles" },
                            new EffectValue(CityEffect.Shockwave, 1f, UnitClass.Bomber), new EffectValue(CityEffect.SuperAt4, 1f, UnitClass.Bomber)),
                    },
                },
                new BuildingConfig
                {
                    type = BuildingType.Sanctuary, title = "Святилище", shortLabel = "СВЯТИЛ.", color = new Color(0.58f, 0.42f, 0.86f),
                    description = "Гильдия магии: +1 спин вербовки за ход, в нём много жрецов; ополченцы по линиям-зигзагам становятся жрецами. Прокачка жрецов и магов — на вкладке «Войска».",
                    requiresAnyTech = new[] { TechIds.Mysticism }, productionCost = 5, maxHp = 60, unitBranch = UnitClass.Priest,
                    effects = { new EffectValue(CityEffect.UnlockPriests, 1) },
                    upgrades =
                    {
                        // Жрецы — правая ветка (x = +3)
                        U("psalter", "Молитвенник", "Лечение жреца задевает и соседей цели (половиной); «Молитва» на 30% сильнее", "", 25, 2.4f, 1f, null,
                            new EffectValue(CityEffect.HealSplash, 0.5f, UnitClass.Priest), new EffectValue(CityEffect.PriestSkill, 0.3f)),
                        U("recruiters_p", "Вербовщики", "В спине святилища на «Поле» вдвое больше жрецов", "", 25, 3.6f, 1f, null,
                            new EffectValue(CityEffect.RecruitShare, 1f, UnitClass.Priest)),
                        U("herbs", "Лечебные травы", "Лечение жреца снимает оглушение, горение и раны; жрецы лечат на 50% сильнее", TechIds.Healing, 30,
                            2.4f, 2f, new[] { "psalter" },
                            new EffectValue(CityEffect.Cleanse, 1f, UnitClass.Priest), new EffectValue(CityEffect.PriestHeal, 0.5f)),
                        U("moleben", "Молебен", OrderText(UnitClass.Priest, "Молитва"), "", 30, 3.6f, 2f, new[] { "recruiters_p" },
                            new EffectValue(CityEffect.OrderPriests, 1)),
                        Key(U("staffs", "Посохи", "Когда лечить некого, жрец бьёт посохом — молния перескакивает на второго врага; лечение +25%",
                            TechIds.Staffs, 40, 3f, 3f, new[] { "herbs", "moleben" },
                            new EffectValue(CityEffect.PriestChain, 0.6f, UnitClass.Priest), new EffectValue(CityEffect.PriestHeal, 0.25f)), any: true),
                        U("candles", "Свечи", $"На поле боя падают Свечи {candle}: 3 в ряд — «Чудо» (все свои лечатся и меньше получают урона)", "", 45,
                            2.4f, 4f, new[] { "staffs" }, new EffectValue(CityEffect.RelicSymbols, 1)),
                        U("pilgrims", "Паломники", "+1 спин святилища на «Поле» каждый ход", "", 45, 3.6f, 4f, new[] { "staffs" },
                            new EffectValue(CityEffect.ExtraGuildSpin, 1f, UnitClass.Priest)),
                        U("holy_relics", "Святые мощи", $"«Чудо» вдвое сильнее; погиб наш боец — на поле боя падает Свеча {candle}", "", 60,
                            2.4f, 5f, new[] { "candles" },
                            new EffectValue(CityEffect.MiraclePower, 1), new EffectValue(CityEffect.Martyrs, 1)),
                        U("cloisters", "Обители", "Союзник, павший рядом с жрецом, раз за бой встаёт с 30% ХП; «Молитва» на 50% сильнее",
                            TechIds.Monasticism, 65, 3.6f, 5f, new[] { "pilgrims" },
                            new EffectValue(CityEffect.SecondWind, 0.3f, UnitClass.Priest), new EffectValue(CityEffect.PriestSkill, 0.5f)),
                        Key(U("prophets", "Пророки", "Суперудар жрецов — уже с 4 в ряд; жрецы приходят с вербовки на уровень выше", "", 85,
                            3f, 6f, new[] { "holy_relics", "cloisters" },
                            new EffectValue(CityEffect.SuperAt4, 1f, UnitClass.Priest), new EffectValue(CityEffect.RecruitLevel, 1f, UnitClass.Priest))),
                        // Маги — между лучниками и жрецами: из ключей обеих веток
                        U("grimoires", "Гримуары", "Молния магов оглушает каждую третью цель; символов магов на поле боя больше",
                            TechIds.Sorcery, 70, 1.5f, 4f, new[] { "crossbows", "staffs" },
                            new EffectValue(CityEffect.ChainStun, 0.33f, UnitClass.Mage), new EffectValue(CityEffect.BoardSymbols, 0.5f, UnitClass.Mage)),
                        U("storm_staffs", "Грозовые посохи", "«Цепная молния» перескакивает ещё на 2 цели и бьёт на 30% сильнее",
                            TechIds.Sorcery, 75, 1.5f, 5f, new[] { "grimoires" },
                            new EffectValue(CityEffect.ExtraJumps, 2f, UnitClass.Mage), new EffectValue(CityEffect.MageSkill, 0.3f)),
                        U("runes", "Руны", "Ряд магов превращает 2 фишки поля боя в символы магов; суперудар магов — с 4 в ряд", TechIds.Sorcery, 85,
                            1.5f, 6f, new[] { "storm_staffs" },
                            new EffectValue(CityEffect.Runes, 2f, UnitClass.Mage), new EffectValue(CityEffect.SuperAt4, 1f, UnitClass.Mage)),
                    },
                },
                new BuildingConfig
                {
                    type = BuildingType.Granary, title = "Амбар", shortLabel = "АМБАР", color = new Color(0.50f, 0.74f, 0.36f),
                    description = $"+1 {food} к каждому собранному кластеру еды.",
                    requiresAnyTech = new[] { TechIds.Agriculture }, productionCost = 4, maxHp = 50,
                    effects = { new EffectValue(CityEffect.FoodPerSymbol, 1) },
                    upgrades =
                    {
                        new UpgradeConfig("storerooms", "Кладовые", "Рост города на 25% дешевле", TechIds.Pottery, 30,
                            new EffectValue(CityEffect.GrowthDiscount, 0.25f)),
                    },
                },
                new BuildingConfig
                {
                    type = BuildingType.Workshop, title = "Мастерская", shortLabel = "МАСТЕР.", color = new Color(0.52f, 0.57f, 0.66f),
                    description = $"+1 {production} к каждому собранному кластеру производства.",
                    requiresAnyTech = new[] { TechIds.Crafts }, productionCost = 4, maxHp = 50,
                    effects = { new EffectValue(CityEffect.ProductionPerSymbol, 1) },
                    upgrades =
                    {
                        new UpgradeConfig("bronze_tools", "Бронзовые инструменты", $"Ещё +1 {production} к каждому кластеру производства", TechIds.Bronze, 40,
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
                        new UpgradeConfig("library", "Библиотека", $"Ещё +50% {knowledge}", TechIds.Philosophy, 55,
                            new EffectValue(CityEffect.KnowledgePercent, 0.5f)),
                    },
                },
                // Чудеса света: куда тратить накопленное производство
                Wonder(BuildingType.Stonehenge, "Стоунхендж", "СТОУНХ.", 35, $"+1 спин вербовки за ход и +20% {knowledge}",
                    new[] { TechIds.Mysticism }, new EffectValue(CityEffect.BoardActions, 1), new EffectValue(CityEffect.KnowledgePercent, 0.2f)),
                Wonder(BuildingType.Pyramids, "Пирамиды", "ПИРАМИДЫ", 45, "+2 спина вербовки каждый ход",
                    new[] { TechIds.Crafts }, new EffectValue(CityEffect.Reserve, 2)),
                Wonder(BuildingType.GreatWall, "Великая стена", "СТЕНА", 50, "Набеги наносят на 40% меньше урона",
                    new[] { TechIds.Masonry }, new EffectValue(CityEffect.RaidDefense, 0.4f)),
                Wonder(BuildingType.Tiltyard, "Ристалище", "РИСТАЛ.", 55, "Выжившие получают +2 опыта за каждый бой",
                    new[] { TechIds.Bronze }, new EffectValue(CityEffect.BattleXp, 2)),
                Wonder(BuildingType.Colossus, "Колосс", "КОЛОСС", 60, $"+1 {gold} к каждому собранному кластеру монет",
                    new[] { TechIds.Trade }, new EffectValue(CityEffect.GoldPerCoin, 1)),
                Wonder(BuildingType.GreatLibrary, "Великая библиотека", "БИБЛИОТ.", 70, $"Ещё +50% {knowledge}",
                    new[] { TechIds.Philosophy }, new EffectValue(CityEffect.KnowledgePercent, 0.5f)),
                Wonder(BuildingType.TerracottaArmy, "Терракотовая армия", "ТЕРРАК.", 90, "Все отряды (и ополченцы) +15% ХП и атаки",
                    new[] { TechIds.TowerShields, TechIds.Crossbows, TechIds.Staffs }, new EffectValue(CityEffect.ArmyStats, 0.15f)),
                // Усилители юнитов: часть символов дня — всегда нужного класса (взамен ресурсов и других юнитов)
                Booster(BuildingType.Smelter, "Плавильня", "ПЛАВИЛ.", new Color(0.74f, 0.42f, 0.26f), TechIds.Bronze,
                    CityEffect.WarriorBoost, "воинами", "Только на камнях, нужны казармы", TerrainType.Rocks),
                Booster(BuildingType.Sawmill, "Лесопилка", "ЛЕСОП.", new Color(0.56f, 0.43f, 0.27f), TechIds.CompositeBow,
                    CityEffect.ArcherBoost, "лучниками", "Только в лесу, нужно стрельбище", TerrainType.Forest),
                Booster(BuildingType.Temple, "Храм", "ХРАМ", new Color(0.84f, 0.80f, 0.93f), TechIds.Healing,
                    CityEffect.PriestBoost, "жрецами", "Нужно святилище"),
                // Особые места: без этих построек гибриды не складываются
                new BuildingConfig
                {
                    type = BuildingType.Monastery, title = "Монастырь", shortLabel = "МОНАСТ.", color = new Color(0.86f, 0.52f, 0.30f),
                    description = "Без него нет монахов: линии воинов и жрецов в одном спине вербовки дают монаха, только пока стоит монастырь. " +
                                  "Строится только на горе — горы редкие, ищите их среди камней за границей города.",
                    requiresAnyTech = new[] { TechIds.MartialArts }, productionCost = 15, maxHp = 70,
                    placeOn = new[] { TerrainType.Mountain },
                    effects = { new EffectValue(CityEffect.UnlockMonks, 1) },
                },
                new BuildingConfig
                {
                    type = BuildingType.MageTower, title = "Башня магов", shortLabel = "БАШНЯ", color = new Color(0.36f, 0.52f, 0.92f),
                    description = "Без неё нет магов: линии лучников и жрецов в одном спине вербовки дают мага, только пока стоит башня. " +
                                  "Строится только на волшебном источнике — он редкий, бывает у рек и озёр.",
                    requiresAnyTech = new[] { TechIds.Sorcery }, productionCost = 15, maxHp = 60,
                    placeOn = new[] { TerrainType.Spring },
                    effects = { new EffectValue(CityEffect.UnlockMages, 1) },
                },
            };
        }

        /// <summary>Узел дерева «Войск»: место (x, y) и какие узлы нужны раньше (null — первый узел ветки).</summary>
        private static UpgradeConfig U(string id, string title, string description, string tech, int cost, float x, float y, string[] requires,
            params EffectValue[] effects)
        {
            return new UpgradeConfig(id, title, description, tech, cost, effects)
            {
                treePos = new Vector2(x, y),
                requires = requires ?? new string[0],
            };
        }

        /// <summary>Ключ — крупный узел, где сходятся пути ветки (any — хватает любого из входящих).</summary>
        private static UpgradeConfig Key(UpgradeConfig upgrade, bool any = false)
        {
            upgrade.gate = true;
            upgrade.requiresAny = any;
            return upgrade;
        }

        /// <summary>Хватает любого из входящих узлов.</summary>
        private static UpgradeConfig AnyOf(UpgradeConfig upgrade)
        {
            upgrade.requiresAny = true;
            return upgrade;
        }

        /// <summary>Нейтралы (Н) делятся на столько частей: каждый усилитель заменяет одну часть своим классом.</summary>
        public const int NeutralParts = 3;

        /// <summary>
        /// Усилитель юнитов: треть нейтралов (Н) на поле становится его классом. Усилителей всего NeutralParts (любых,
        /// можно и одинаковых): когда стоят все, нейтралов на поле нет совсем, и больше ставить нечего.
        /// </summary>
        private static BuildingConfig Booster(BuildingType type, string title, string shortLabel, Color color, string tech,
            CityEffect boost, string classInstrumental, string where, params TerrainType[] placeOn)
        {
            return new BuildingConfig
            {
                type = type, title = title, shortLabel = shortLabel, color = color,
                description = $"+1 спин вербовки за ход, где очень много символов этого класса (бойцы становятся {classInstrumental}). {where}. " +
                              "Усилителей (плавильня, лесопилка, храм) всего три.",
                requiresAnyTech = new[] { tech }, productionCost = 8, maxHp = 50, unique = false,
                placeOn = placeOn,
                effects = { new EffectValue(boost, 1) },
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

        /// <summary>Сумма эффекта для бойцов класса target (эффекты дерева «Войск») по целым зданиям.</summary>
        public float ClassEffect(CityEffect effect, UnitClass target)
        {
            float total = 0f;
            foreach (Building building in placed)
            {
                if (building.IsRuined) continue;
                foreach (UpgradeConfig upgrade in building.Config.upgrades)
                {
                    if (!building.Upgrades.Contains(upgrade.id)) continue;
                    foreach (EffectValue value in upgrade.effects)
                    {
                        if (value.effect == effect && value.target == target && IsResearched(value.requiredTech)) total += value.value;
                    }
                }
            }
            return total;
        }

        /// <summary>Узел дерева «Войск» куплен (в любой гильдии, даже разрушенной).</summary>
        public bool HasUnitUpgrade(string id) => placed.Any(b => b.Upgrades.Contains(id));

        /// <summary>Улучшение гильдии по id и чья это гильдия (null — нет такого).</summary>
        public UpgradeConfig FindUnitUpgrade(string id, out BuildingConfig owner)
        {
            foreach (BuildingConfig config in buildings)
            {
                if (config == null || config.unitBranch == UnitClass.None) continue;
                UpgradeConfig upgrade = config.upgrades.FirstOrDefault(u => u.id == id);
                if (upgrade == null) continue;
                owner = config;
                return upgrade;
            }
            owner = null;
            return null;
        }

        private string UpgradeTitle(string id) => FindUnitUpgrade(id, out _)?.title ?? id;

        /// <summary>Округление «половина вверх»: 7.5 → 8 (без банковского округления и мусора float).</summary>
        private static int Round(float value) => Mathf.FloorToInt(value + 0.5f);

        public int Housing => Round(Effect(CityEffect.Housing));
        public int Homeless => Mathf.Max(0, Population - Housing);
        public int BoardSize => Mathf.Clamp(baseBoardSize + Mathf.Max(0, Population - startPopulation) / citizensPerBoardSize,
            baseBoardSize, Mathf.Max(baseBoardSize, maxBoardSize));
        public int BoardReserve => baseReserve + reservePerCitizen * Population + Round(Effect(CityEffect.Reserve));
        /// <summary>Сбор ресурсов: +к каждому собранному кластеру (амбар, мастерская, торговые ряды, колосс). Символ сам — 1 ресурс.</summary>
        public int GoldPerCluster => Round(Effect(CityEffect.GoldPerCoin));
        public int ProductionPerCluster => Round(Effect(CityEffect.ProductionPerSymbol));
        public int FoodPerCluster => Round(Effect(CityEffect.FoodPerSymbol));

        public int ClusterBonus(ResourceType resource)
        {
            switch (resource)
            {
                case ResourceType.Gold: return GoldPerCluster;
                case ResourceType.Production: return ProductionPerCluster;
                default: return FoodPerCluster;
            }
        }
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

        /// <summary>
        /// Действий на поле армии за ход: база + 1 за каждые symbolsPerExtraAction символов сверх стартовых
        /// + постройки (Стоунхендж).
        /// </summary>
        public int ActionsPerDay
        {
            get
            {
                int startSymbols = baseBoardSize * baseBoardSize + baseReserve + reservePerCitizen * startPopulation;
                return actionsPerDay + Mathf.Max(0, SymbolsPerDay - startSymbols) / Mathf.Max(1, symbolsPerExtraAction)
                       + Round(Effect(CityEffect.BoardActions));
            }
        }

        /// <summary>
        /// Обычных спинов вербовки за ход (без гильдий и усилителей): 1 + жители / perSpin, плюс «Учёт», Стоунхендж
        /// и Пирамиды (каждый эффект — по спину).
        /// </summary>
        public int RecruitSpinsBase(int perSpin) =>
            1 + Population / Mathf.Max(1, perSpin) + Round(Effect(CityEffect.BoardActions)) + Round(Effect(CityEffect.Reserve));

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

        /// <summary>
        /// Гибрид складывается на поле из двух классов: подрывник — «Порох»; монах — «Боевые искусства» и монастырь
        /// на горе; маг — «Чародейство» и башня магов на волшебном источнике.
        /// </summary>
        public bool IsHybridUnlocked(UnitClass hybrid)
        {
            switch (hybrid)
            {
                case UnitClass.Bomber: return IsResearched(TechIds.Gunpowder);
                case UnitClass.Monk: return IsResearched(TechIds.MartialArts) && Effect(CityEffect.UnlockMonks) > 0f;
                case UnitClass.Mage: return IsResearched(TechIds.Sorcery) && Effect(CityEffect.UnlockMages) > 0f;
                default: return false;
            }
        }

        // =====================================================================
        //  Усилители юнитов: нейтралы (Н) на поле заменяются нужным классом
        // =====================================================================

        private static CityEffect BoostEffect(UnitClass unitClass)
        {
            switch (unitClass)
            {
                case UnitClass.Warrior: return CityEffect.WarriorBoost;
                case UnitClass.Archer: return CityEffect.ArcherBoost;
                default: return CityEffect.PriestBoost;
            }
        }

        /// <summary>Класс, который усиливает здание (None — не усилитель).</summary>
        public static UnitClass BoostedClass(BuildingConfig config)
        {
            foreach (EffectValue value in config.effects)
            {
                if (value.effect == CityEffect.WarriorBoost) return UnitClass.Warrior;
                if (value.effect == CityEffect.ArcherBoost) return UnitClass.Archer;
                if (value.effect == CityEffect.PriestBoost) return UnitClass.Priest;
            }
            return UnitClass.None;
        }

        /// <summary>Сколько усилителей стоит в городе (и разрушенные: место за ними).</summary>
        public int Boosters => placed.Count(b => BoostedClass(b.Config) != UnitClass.None);

        /// <summary>Сколько частей нейтралов сейчас становятся этим классом (целые усилители при стоящей гильдии).</summary>
        public int BoostParts(UnitClass unitClass) =>
            UnitClasses.IsBase(unitClass) && IsClassUnlocked(unitClass) ? Round(Effect(BoostEffect(unitClass))) : 0;

        /// <summary>Шанс, что выпавший нейтрал (Н) заменяется этим классом.</summary>
        public float NeutralReplaceChance(UnitClass unitClass) => Mathf.Clamp01(BoostParts(unitClass) / (float)NeutralParts);

        /// <summary>Какая доля нейтралов уже заменена (0…1).</summary>
        public float NeutralsReplaced => Mathf.Clamp01(UnitClasses.Base.Sum(BoostParts) / (float)NeutralParts);

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

        /// <summary>Уровень Знамён (джокеров) на поле боя.</summary>
        public int BannerLevel => Round(Effect(CityEffect.BannerSymbols));
        /// <summary>Уровень множителей на поле боя и их сила (×2, ×3).</summary>
        public int MultiplierLevel => Round(Effect(CityEffect.MultiplierSymbols));
        public int MultiplierValue => 2 + Round(Effect(CityEffect.MultiplierValue));
        /// <summary>Уровень Свечей на поле боя и сила «Чуда».</summary>
        public int RelicLevel => Round(Effect(CityEffect.RelicSymbols));
        public float MiracleMultiplier => 1f + Effect(CityEffect.MiraclePower);
        /// <summary>Приказ класса в бою куплен (и гильдия цела).</summary>
        public bool HasOrder(UnitClass unitClass)
        {
            switch (unitClass)
            {
                case UnitClass.Warrior: return Effect(CityEffect.OrderWarriors) > 0f;
                case UnitClass.Archer: return Effect(CityEffect.OrderArchers) > 0f;
                case UnitClass.Priest: return Effect(CityEffect.OrderPriests) > 0f;
                default: return false;
            }
        }

        /// <summary>В бою можно удерживать столбцы своего слота.</summary>
        /// <summary>«Выдержка»: суперудары наших классов перезаряжаются быстрее.</summary>
        public bool FasterSupers => Effect(CityEffect.HoldColumns) > 0f;

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

        /// <summary>Клетка в границах города: квадрат радиуса TerritoryRadius вокруг ратуши или купленная клетка.</summary>
        public bool InTerritory(Vector2Int cell) =>
            InMap(cell) && (Mathf.Max(Mathf.Abs(cell.x - CityCenter.x), Mathf.Abs(cell.y - CityCenter.y)) <= TerritoryRadius ||
                            boughtCells.Contains(cell));

        public TerrainType TerrainAt(Vector2Int cell) => World != null ? World.TerrainAt(cell) : TerrainType.Grass;

        /// <summary>Здесь можно ставить дом или обычное здание: в границах города, обычная суша (не вода, гора или источник) и свободно.</summary>
        public bool CanPlaceOn(Vector2Int cell) => InTerritory(cell) && World.IsPlainLand(cell) && BuildingAt(cell) == null;

        /// <summary>Здание подходит к местности клетки: обычные — трава, лес, камни; особые — только своя местность.</summary>
        public bool FitsTerrain(BuildingConfig config, Vector2Int cell)
        {
            if (World == null) return false;
            if (config.placeOn == null || config.placeOn.Length == 0) return World.IsPlainLand(cell);
            return config.placeOn.Contains(World.TerrainAt(cell));
        }

        /// <summary>Где можно строить здание: «камни», «гора»… (пусто — обычная суша).</summary>
        public static string PlaceOnText(BuildingConfig config) =>
            config.placeOn == null || config.placeOn.Length == 0
                ? string.Empty
                : string.Join(" или ", config.placeOn.Select(t => WorldMap.TerrainName(t).ToLowerInvariant()));

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
            if (!FitsTerrain(config, cell))
            {
                string where = PlaceOnText(config);
                return where.Length > 0 ? $"Строится только здесь: {where}" : $"{WorldMap.TerrainName(World.TerrainAt(cell))} — не для этого здания";
            }
            if (!IsUnlocked(config)) return $"Нужна наука: {RequiredTechTitles(config)}";
            if (config.unique && placed.Any(b => b.Type == config.type)) return "Уже построено";
            if (BoostedClass(config) != UnitClass.None && Boosters >= NeutralParts)
                return $"Нейтралов не осталось: все {NeutralParts} усилителя уже стоят";
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
            if (upgrade.requires != null && upgrade.requires.Length > 0)
            {
                // Узел дерева «Войск»: нужны узлы выше (все или любой), в том числе из другой гильдии
                List<string> missing = upgrade.requires.Where(r => !HasUnitUpgrade(r)).ToList();
                bool met = upgrade.requiresAny ? missing.Count < upgrade.requires.Length : missing.Count == 0;
                if (!met) return $"Сначала: {string.Join(upgrade.requiresAny ? " или " : ", ", missing.Select(UpgradeTitle))}";
            }
            else
            {
                // Ярус открывается ключом прошлого яруса (улучшения без связей — старые сцены)
                UpgradeConfig previousGate = building.Config.upgrades.FirstOrDefault(u => u.gate && u.tier == upgrade.tier - 1);
                if (upgrade.tier > 1 && previousGate != null && !building.Upgrades.Contains(previousGate.id))
                    return $"Сначала: {previousGate.title}";
                if (upgrade.gate)
                {
                    int bought = building.Config.upgrades.Count(u => !u.gate && u.tier == upgrade.tier && building.Upgrades.Contains(u.id));
                    if (bought < upgrade.requiredInTier) return $"Нужно ещё {upgrade.requiredInTier - bought} улучш. этого яруса";
                }
            }
            if (!IsResearched(upgrade.requiredTech))
                return $"Нужна наука: {(techTree != null ? techTree.Title(upgrade.requiredTech) : upgrade.requiredTech)}";
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

        /// <summary>Что принесёт расширение границ: «Новые клетки: трава 9, лес 4, камни 2, гора 1».</summary>
        public string NextBorderRingText()
        {
            if (World == null) return string.Empty;
            var counts = new Dictionary<string, int>();
            var order = new List<string>();
            foreach (Vector2Int cell in WorldMap.Ring(CityCenter, TerritoryRadius + 1))
            {
                if (!World.InBounds(cell) || boughtCells.Contains(cell)) continue;
                string name = World.IsWater(cell) ? "вода" : WorldMap.TerrainName(World.TerrainAt(cell)).ToLowerInvariant();
                if (!counts.ContainsKey(name)) order.Add(name);
                counts.TryGetValue(name, out int count);
                counts[name] = count + 1;
            }
            // Редкие места (гора, волшебный источник) — золотом, чтобы их было видно
            string rare1 = WorldMap.TerrainName(TerrainType.Mountain).ToLowerInvariant();
            string rare2 = WorldMap.TerrainName(TerrainType.Spring).ToLowerInvariant();
            return "Новые клетки: " + string.Join(", ", order.Select(name => name == rare1 || name == rare2
                ? $"<color=#FFD866>{name} {counts[name]}</color>"
                : $"{name} {counts[name]}"));
        }

        /// <summary>Цена следующей клетки за золото: каждая следующая дороже на cellGoldStep.</summary>
        public int CellPrice => cellGoldBase + cellGoldStep * boughtCells.Count;

        /// <summary>Клетка касается границ города (сторонами или углом).</summary>
        public bool TouchesTerritory(Vector2Int cell)
        {
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if ((dx != 0 || dy != 0) && InTerritory(new Vector2Int(cell.x + dx, cell.y + dy))) return true;
                }
            return false;
        }

        public string WhyCannotBuyCell(Vector2Int cell)
        {
            if (!InMap(cell)) return "За краем карты";
            if (InTerritory(cell)) return "Клетка уже в границах города";
            if (!TouchesTerritory(cell)) return "Только у самой границы города";
            string foreign = ForeignLand?.Invoke(cell);
            if (foreign != null) return foreign;
            if (IsTownHallRuined) return "Сначала почините ратушу";
            if (Gold < CellPrice) return $"Нужно {CellPrice} {GameVisuals.IconGold}";
            return null;
        }

        /// <summary>Купить одну клетку у границы за золото — она становится частью города.</summary>
        public bool TryBuyCell(Vector2Int cell, out string message)
        {
            message = WhyCannotBuyCell(cell);
            if (message != null) return false;
            Gold -= CellPrice;
            boughtCells.Add(cell);
            TerritoryVersion++;
            message = $"Клетка куплена: {WorldMap.TerrainName(World.TerrainAt(cell))}";
            NotifyChanged();
            return true;
        }

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
            TerritoryVersion++;
            int border = TerritoryRadius * 2 + 1;
            message = $"Границы города расширены: {border}×{border}";
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
                TerritoryVersion++;
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
            // Лес и камни дома занимают в последнюю очередь: там место лесопилкам и плавильням
            return free.OrderBy(c => TerrainAt(c) == TerrainType.Grass ? 0 : 1)
                .ThenBy(c => NextToCity(c) ? 0 : 1)
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
            boughtCells.Clear();
            TerritoryVersion++;
            // Гора (монастырь) и волшебный источник (башня магов) — за стартовыми границами, но в пределах расширения
            int near = TerritoryRadius + 1;
            int far = Mathf.Max(near, maxCityRadius - 1);
            World.EnsureFeatureNear(TerrainType.Mountain, CityCenter, near, far);
            World.EnsureFeatureNear(TerrainType.Spring, CityCenter, near, far);
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
            UnitClass boosted = BoostedClass(building.Config);
            if (boosted != UnitClass.None && !building.IsRuined)
            {
                lines.Add(IsClassUnlocked(boosted)
                    ? $"Работает: +1 спин вербовки за ход (усилителей {Boosters} из {NeutralParts})"
                    : $"Не работает: нужно здание «{GuildConfig(boosted)?.title ?? "гильдия"}»");
            }
            if (Sum(building.Config.effects, CityEffect.UnlockMonks) > 0f && !building.IsRuined)
                lines.Add(IsHybridUnlocked(UnitClass.Monk) ? "Монахи приходят: линии воинов и жрецов в одном спине" : "Монахов пока нет: нужна наука «Боевые искусства»");
            if (Sum(building.Config.effects, CityEffect.UnlockMages) > 0f && !building.IsRuined)
                lines.Add(IsHybridUnlocked(UnitClass.Mage) ? "Маги приходят: линии лучников и жрецов в одном спине" : "Магов пока нет: нужна наука «Чародейство»");
            var done = building.Config.upgrades.Where(u => building.Upgrades.Contains(u.id)).Select(u => u.title).ToList();
            if (done.Count > 0) lines.Add("Улучшения: " + string.Join(", ", done));
            return string.Join("\n", lines.Where(l => !string.IsNullOrEmpty(l)));
        }

        private void NotifyChanged() => Changed?.Invoke();
    }
}
