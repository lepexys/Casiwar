using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace Casiwar
{
    public enum Team
    {
        Player,
        Enemy,
    }

    /// <summary>
    /// Итог боя: победа, судьба каждого юнита игрока (погибшие пропадают навсегда, выжившие получают опыт)
    /// и сила уцелевших варваров (по ней считается урон зданиям при набеге).
    /// </summary>
    public sealed class BattleOutcome
    {
        public bool Victory;
        /// <summary>Сколько юнитов игрока дожило до конца боя.</summary>
        public int Survivors;
        /// <summary>
        /// Сумма звёзд убитых врагов — столько круток в «Трофеях» (★★ и ★★★ → 5). Считаются убитые, а не наши выжившие:
        /// большой армией по мелкому отряду много не набить.
        /// </summary>
        public int KilledStars;
        /// <summary>Каждый юнит игрока: жив ли и сколько врагов добил (→ BenchManager.ApplyBattleResults).</summary>
        public readonly List<UnitBattleResult> PlayerUnits = new List<UnitBattleResult>();
        /// <summary>Удар каждого уцелевшего варвара по городу.</summary>
        public readonly List<float> RaidHits = new List<float>();
        /// <summary>Уцелевшие враги.</summary>
        public readonly List<BenchUnit> EnemySurvivors = new List<BenchUnit>();
        /// <summary>Погибшие враги (их отряд на карте их теряет; кто не влез на арену — жив).</summary>
        public readonly List<BenchUnit> EnemyFallen = new List<BenchUnit>();
    }

    /// <summary>
    /// Итоги боя для настройки темпа: сколько он длился (по часам поля и по времени арены), сколько рядов собрал
    /// игрок и чей урон — от рядов поля или от бойцов самих (AutoBattleManager.Stats).
    /// </summary>
    public sealed class BattleStats
    {
        /// <summary>Обычные часы (что видит игрок), без пауз до и после боя.</summary>
        public float BoardSeconds;
        /// <summary>Время арены: медленнее при темпе меньше 1 и под «Тактикой».</summary>
        public float ArenaSeconds;
        /// <summary>Сколько секунд действовала «Тактика».</summary>
        public float SlowSeconds;
        /// <summary>Рядов игрока (и волн каскада).</summary>
        public int Matches;
        public int Supers;
        /// <summary>Урон наших от рядов поля: натиск, навыки, суперудары.</summary>
        public float BoardDamage;
        /// <summary>Урон наших сам по себе: удары по таймеру, навыки «сами», горение и раны.</summary>
        public float SelfDamage;
        /// <summary>Урон врага по нашим.</summary>
        public float EnemyDamage;
    }

    /// <summary>
    /// Бой на клетках с отрядом, который пришёл к городу или встретился в поле. Арена — сетка columns×rows
    /// (центр = этот объект), юниты стоят в клетках, ходят из клетки в клетку и бьют тех, до кого достают.
    /// Игрок внизу, враг вверху.
    ///
    /// Бой идёт В РЕАЛЬНОМ ВРЕМЕНИ: каждый боец сам идёт к ближайшему врагу (шаг — moveInterval) и бьёт, когда достаёт
    /// (удар — attackInterval; жрецы лечат раненых). А мы собираем три в ряд на поле боя (BattleBoard), и бойцы делают
    /// то, что собрано:
    ///   ⚔ натиск — все, кто достаёт, бьют разом; кто не достаёт — шаг к врагу;
    ///   ⏳ тактика — арена на несколько секунд медленнее (tacticsSlow), а поле — нет: успеть собрать больше рядов;
    ///   символ класса — его массовый навык (3 в ряд — ×1, 4 — ×1.5, 5 — ×2); 5+ в ряд — СУПЕРУДАР класса, если он открыт
    ///   приказом гильдии («Боевой клич», «Пли!», «Молебен»; гибридам — приказом любой родительской гильдии) и класс не
    ///   перезаряжается (superCooldownSeconds, «Выдержка» — быстрее): воины — «Землетрясение», лучники — «Огненный
    ///   дождь», жрецы — «Воскрешение», подрывники — «Пороховая бочка», монахи — «Вихрь», маги — «Ледяная буря»;
    ///   🕯 3 в ряд — «Чудо»; 🚩 и ×2 — джокеры (×2 усиливает ряд); каскад — каждая волна сильнее (cascadeBonus).
    /// Враг поля не имеет: навык случайного своего класса он применяет сам раз в enemySkillInterval секунд,
    ///   а с enemySuperDay — ещё и суперудар раз в enemySuperInterval.
    /// Вожак: богатырь в слоте вожака даёт всем бойцам своей линейки +ХП и +атаку (HeroTraits.LeadHp/LeadAttack);
    ///   вождь варваров так же ведёт своих. Богатырь своего символа не имеет — бьёт вместе с любым навыком своей стороны.
    /// Два времени: часы поля (обычные, ×кнопка скорости) — поле три в ряд, лимит боя и перезарядка наших суперударов;
    ///   время арены = часы поля × arenaTempo (× tacticsSlow под «Тактикой») — шаги, удары, навыки и суперудары врага,
    ///   оглушения и горение. Арена медленнее поля — у игрока есть время собирать ряды и смотреть на бой.
    /// Победа — когда у противника не осталось юнитов; время вышло (battleDuration) — решает доля оставшегося ХП.
    /// Если бой проигран, уцелевшие враги грабят город (BattleOutcome.RaidHits → CityManager.ApplyRaid).
    /// Погибшие юниты игрока не возвращаются, выжившие растут в уровнях (BattleOutcome.PlayerUnits).
    /// Враги растут с ходами: выше уровень, позже среди них бывают ★★ и ★★★,
    /// с 10-го хода — подрывники и маги (шаманы), с 15-го отряд ведёт вождь-богатырь.
    ///
    /// Массовые навыки (для обеих сторон):
    ///   Воины   «Рывок»       — каждый бросается к ближайшему врагу (до нескольких клеток) и бьёт: чем дальше
    ///                           пролетел, тем сильнее удар; не достал — разгон копится до следующего удара.
    ///                           Со щитами это «Таран щитом»: цель оглушена (ростовые щиты — вдвое дольше);
    ///   Лучники «Залп»        — все лучники разом стреляют (дальность не важна);
    ///   Жрецы   «Молитва»     — лечат всех своих и благословляют: меньше урона несколько секунд.
    ///   Подрывники «Динамит»  — каждый бросает динамит в самую плотную кучу врагов: взрыв бьёт всех вокруг;
    ///   Монахи  «Огненные клинки» — оружие ближнего боя у всех своих горит: удары сильнее несколько секунд;
    ///   Маги    «Цепная молния» — молния бьёт цель и перескакивает на ближайших врагов, щиты и броня не помогают.
    ///
    /// Щиты (у своих — после науки и улучшения казарм, у варваров — с определённого дня) ловят удар
    /// не всегда: иногда целиком («БЛОК!»), иногда частично, иногда никак. Стрелы ловятся чаще,
    /// арбалетные болты — реже. Обычный щит прикрывает только лицо, ростовой — и бок.
    /// </summary>
    public class AutoBattleManager : MonoBehaviour
    {
        private static readonly Vector2Int[] Directions8 =
        {
            new Vector2Int(1, 0), new Vector2Int(1, 1), new Vector2Int(0, 1), new Vector2Int(-1, 1),
            new Vector2Int(-1, 0), new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(1, -1),
        };

        [Header("Ссылки")]
        [Tooltip("Поле боя — три в ряд (пусто — найдётся само)")]
        public BattleBoard board;
        public CityManager city;

        [Header("UI (необязательно)")]
        [Tooltip("Сколько осталось времени боя")]
        public TMP_Text stepText;
        [Tooltip("Крупная надпись «ПОБЕДА!» / «ПОРАЖЕНИЕ»")]
        public TMP_Text resultText;
        [Tooltip("Что сделал враг")]
        public TMP_Text enemyTurnText;
        [Tooltip("Подпись кнопки скорости («Скорость ×1»)")]
        public TMP_Text speedButtonLabel;
        [Tooltip("Легенда рядом с полем боя: что даёт каждый ряд (пишется под армию и открытые символы)")]
        public TMP_Text legendText;
        [Tooltip("Подписи внизу экрана боя: наши юниты по шеренгам — атака и точное ХП")]
        public TMP_Text[] rosterLabels = new TMP_Text[0];

        [Header("Устарело: кнопки из старых сцен прячутся")]
        public Button spinButton;
        public TMP_Text autoSpinLabel;
        public TMP_Text moraleText;
        public Button[] abilityButtons = new Button[0];
        public Button[] orderButtons = new Button[0];

        [Header("Бой в реальном времени")]
        [Tooltip("Сколько секунд длится бой; время вышло — решает доля оставшегося ХП")]
        [Min(10f)] public float battleDuration = 150f;
        [Tooltip("Секунд между ударами бойца (стрелки — чуть реже, богатырь — чаще)")]
        [Min(0.2f)] public float attackInterval = 2.3f;
        [Tooltip("Секунд на шаг в соседнюю клетку")]
        [Min(0.1f)] public float moveInterval = 1.05f;
        [Tooltip("Оглушение «на ход» длится столько секунд")]
        [Min(0.1f)] public float stunSecondsPerTurn = 1.6f;
        [Tooltip("Темп арены: бойцы, враг и все их таймеры (1 — как раньше). Поле три в ряд, лимит боя и перезарядка " +
                 "наших суперударов идут по обычным часам — чем медленнее арена, тем больше рядов успевает игрок")]
        [Range(0.2f, 1f)] public float arenaTempo = 0.55f;
        [Tooltip("ХП всех бойцов на арене (обе стороны) × это, лечение — тоже: бой длиннее, а размен сторон тот же. " +
                 "На скамейке ХП показывается без множителя")]
        [Min(1f)] public float hpMultiplier = 3f;

        [Header("⏳ Тактика (ряд часов на поле боя)")]
        [Tooltip("Пока действует «Тактика», арена идёт во столько раз медленнее (поле — как обычно)")]
        [Range(0.1f, 1f)] public float tacticsSlow = 0.5f;
        [Tooltip("Секунд «Тактики» за ряд из трёх; за каждый символ сверх трёх — ещё tacticsSecondsPerExtra")]
        [Min(0.5f)] public float tacticsSeconds = 4f;
        [Min(0f)] public float tacticsSecondsPerExtra = 2f;
        [Tooltip("«Тактика» копится, но не дольше стольких секунд")]
        [Min(1f)] public float tacticsMaxSeconds = 12f;
        [Tooltip("Клетки арены под «Тактикой» подкрашиваются этим цветом")]
        public Color tacticsTint = new Color(0.45f, 0.55f, 1f);

        [Header("Поле боя: что даёт ряд")]
        [Tooltip("+сила за каждый символ ряда сверх трёх (4 в ряд — ×1.5, 5 — ×2)")]
        [Min(0f)] public float lengthBonus = 0.5f;
        [Tooltip("+сила за каждую волну каскада")]
        [Min(0f)] public float cascadeBonus = 0.25f;
        [Tooltip("Ряд ⚔ «натиск»: каждый, кто достаёт до врага, бьёт с этим множителем (× сила ряда)")]
        [Min(0f)] public float assaultPower = 1f;

        [Header("Суперудары (5 в ряд символов класса; у врага — сами)")]
        [Tooltip("После суперудара класс перезаряжается столько секунд")]
        [Min(0f)] public float superCooldownSeconds = 12f;
        [Tooltip("«Выдержка» стрельбища: перезарядка — эта доля")]
        [Range(0.1f, 1f)] public float fasterSupersShare = 0.55f;
        [Tooltip("Сила суперудара (длина ряда × каскад × ×2) — не выше этого")]
        [Min(1f)] public float superPowerCap = 1.6f;
        [Tooltip("«Землетрясение» воинов: враги в стольких клетках от воина получают атаку × сила и оглушены")]
        [Min(1)] public int quakeRadius = 2;
        [Min(0f)] public float quakePower = 1f;
        [Min(1)] public int quakeStunTurns = 1;
        [Tooltip("«Огненный дождь» лучников: столько волн, в каждой каждый лучник бьёт случайного врага")]
        [Min(1)] public int fireRainWaves = 3;
        [Min(0f)] public float fireRainPower = 0.6f;
        [Tooltip("«Воскрешение» жрецов: павших встаёт по числу жрецов, с этой долей ХП; павших нет — все лечатся на reviveHealShare")]
        [Range(0.1f, 1f)] public float reviveHp = 0.35f;
        [Range(0.05f, 1f)] public float reviveHealShare = 0.35f;
        [Tooltip("«Пороховая бочка» подрывников: взрыв радиусом в клетках; урон = сумма атак подрывников × сила")]
        [Min(1)] public int powderRadius = 2;
        [Min(0f)] public float powderPower = 1.2f;
        [Tooltip("«Вихрь» монахов: ударов огнём по каждому врагу вокруг монаха")]
        [Min(1)] public int whirlwindHits = 2;
        [Min(0f)] public float whirlwindPower = 0.8f;
        [Tooltip("«Ледяная буря» магов: урон всем врагам (сумма атак магов × сила, сквозь броню) и заморозка")]
        [Min(0f)] public float frostPower = 0.45f;
        [Min(1)] public int frostStunTurns = 1;
        [Tooltip("Сложность растёт так: «день угрозы» = 1 + (ход − 1) × это (50 ходов ≈ 23 дня старого темпа)")]
        [Min(0.05f)] public float difficultyPerTurn = 0.45f;

        [Header("Враг: навыки применяет сам")]
        [Tooltip("Раз в столько секунд (± разброс) — навык случайного своего класса")]
        [Min(1f)] public float enemySkillInterval = 9.5f;
        [Tooltip("Наши классы, которым не хватило места на поле (BattleBoard.maxClassSymbols), применяют навык сами — раз в столько секунд")]
        [Min(1f)] public float offBoardSkillInterval = 10f;
        [Min(0f)] public float enemySkillJitter = 2f;
        [Tooltip("С этого «дня угрозы» враг делает и суперудары — раз в столько секунд")]
        [Min(1)] public int enemySuperDay = 5;
        [Min(5f)] public float enemySuperInterval = 38f;

        [Header("Арена — сетка (центр арены = этот объект)")]
        [Min(3)] public int columns = 7;
        [Min(6)] public int rows = 8;
        [Min(0.1f)] public float cellSize = 0.66f;
        [Tooltip("Юнитов в шеренге при расстановке (шеренг у каждой стороны — 3)")]
        [Min(1)] public int unitsPerRow = 5;
        public Color cellColorA = new Color(0.20f, 0.29f, 0.20f);
        public Color cellColorB = new Color(0.17f, 0.25f, 0.17f);

        [Header("Ход боя")]
        [Tooltip("Множитель скорости боя (кнопка ×1/×2/×3): бойцы и враг; поле три в ряд — как есть")]
        [Min(0.1f)] public float battleSpeed = 1f;
        [Tooltip("«Армии строятся» — пауза перед боем")]
        [Min(0f)] public float startDelay = 1.5f;
        [Tooltip("Пауза после боя, чтобы игрок увидел результат")]
        [Min(0f)] public float endDelay = 2f;

        [Header("Направления")]
        [Tooltip("Удар по боку получает эту долю бонуса удара в спину")]
        [Range(0f, 1f)] public float sideBonusShare = 0.3f;

        [Header("Массовые навыки")]
        [Tooltip("«Залп»: урон каждого лучника = атака × множитель × сила")]
        [Min(0f)] public float volleyMultiplier = 1.5f;
        [Tooltip("«Рывок» воинов: на сколько клеток бросается воин")]
        [Min(1)] public int chargeRange = 3;
        [Tooltip("«Рывок»: +доля к удару за каждую пролетевшую клетку (× сила навыка)")]
        [Min(0f)] public float chargeBonusPerCell = 0.3f;
        [Tooltip("«Таран щитом» (у воинов есть щиты): цель пропускает столько своих ходов; у своих с ростовыми щитами — +1")]
        [Min(0)] public int shieldBashStunTurns = 1;
        [Tooltip("«Молитва»: лечение каждого союзника = лечение жреца × множитель × сила")]
        [Min(0f)] public float prayerHealMultiplier = 0.8f;
        [Tooltip("«Молитва»: благословение снижает входящий урон на эту долю")]
        [Range(0f, 0.9f)] public float blessingReduction = 0.25f;
        [Tooltip("Благословение длится столько секунд")]
        [Min(1)] public int blessingSeconds = 6;
        [Tooltip("«Динамит»: урон взрыва каждому врагу рядом с целью = атака подрывника × множитель × сила")]
        [Min(0f)] public float dynamiteMultiplier = 1.4f;
        [Tooltip("Обычный бросок подрывника задевает врагов рядом с целью этой долей урона")]
        [Range(0f, 1f)] public float bomberSplashShare = 0.5f;
        [Tooltip("«Огненные клинки»: +доля к урону ближнего боя у своих (× сила навыка)")]
        [Min(0f)] public float fireBladesBonus = 0.5f;
        [Tooltip("«Огненные клинки» горят столько секунд")]
        [Min(1)] public int fireBladesSeconds = 6;
        [Tooltip("«Цепная молния»: урон по первой цели = атака мага × множитель × сила; дальше слабее")]
        [Min(0f)] public float lightningMultiplier = 1.3f;
        [Min(0)] public int lightningJumps = 3;
        [Range(0f, 1f)] public float lightningFalloff = 0.7f;

        [Header("«Чудо» (3+ Свечи в ряд на поле боя)")]
        [Tooltip("Лечит каждого своего на эту долю его ХП (× сила «Чуда» × множитель каскада)")]
        [Range(0f, 1f)] public float miracleHealShare = 0.12f;
        [Tooltip("+к лечению за каждую Свечу сверх трёх")]
        [Min(0f)] public float miracleExtraPerRelic = 0.25f;

        [Header("Щиты (блок срабатывает не всегда)")]
        [Tooltip("Обычный щит: шанс поймать удар в лицо целиком")]
        [Range(0f, 1f)] public float shieldFullBlock = 0.2f;
        [Tooltip("Обычный щит: шанс поймать удар частично")]
        [Range(0f, 1f)] public float shieldPartialBlock = 0.35f;
        [Tooltip("Ростовой щит: шанс полного блока")]
        [Range(0f, 1f)] public float towerShieldFullBlock = 0.35f;
        [Tooltip("Ростовой щит: шанс частичного блока")]
        [Range(0f, 1f)] public float towerShieldPartialBlock = 0.4f;
        [Tooltip("Частичный блок гасит случайную долю урона в этих пределах")]
        [Range(0f, 1f)] public float partialBlockMin = 0.3f;
        [Range(0f, 1f)] public float partialBlockMax = 0.7f;
        [Tooltip("Удар в бок ростовой щит ловит с такой долей шансов")]
        [Range(0f, 1f)] public float sideCover = 0.5f;
        [Tooltip("Стрелу щит ловит целиком чаще: шанс полного блока × множитель")]
        [Min(1f)] public float arrowFullBlockMultiplier = 1.5f;
        [Tooltip("Арбалетный болт пробивает щит: все шансы блока × множитель")]
        [Range(0f, 1f)] public float crossbowBlockMultiplier = 0.5f;
        [Tooltip("С какого дня варвары-воины приходят со щитами и с ростовыми щитами")]
        [Min(1)] public int enemyShieldDay = 4;
        [Min(1)] public int enemyTowerShieldDay = 9;

        [Header("Враги (отряды лагерей и армии городов сильнее с каждым ходом)")]
        public List<UnitData> enemyPool = new List<UnitData>();
        [Min(1)] public int baseEnemyCount = 3;
        [Tooltip("+варваров за каждый день")]
        [Min(0f)] public float enemiesPerDay = 0.8f;
        [Min(1)] public int maxEnemies = 12;
        [Tooltip("+к статам варваров за день (0.04 = +4%) — сверху их уровня")]
        [Min(0f)] public float enemyStatsGrowthPerDay = 0.04f;
        [Tooltip("Уровень варваров растёт на 1 каждые N дней")]
        [Min(1)] public int enemyLevelEveryDays = 3;
        [Tooltip("Шанс, что варвар придёт на уровень выше остальных")]
        [Range(0f, 1f)] public float enemyLevelUpChance = 0.2f;
        [Tooltip("С этого дня среди варваров бывают ★★")]
        [FormerlySerializedAs("enemyCorpsDay")]
        [Min(1)] public int enemyTwoStarDay = 7;
        [Tooltip("Шанс ★★: +доля за каждый день с enemyTwoStarDay (не больше enemyMaxTwoStarChance)")]
        [Range(0f, 1f)] public float enemyTwoStarChancePerDay = 0.06f;
        [Range(0f, 1f)] public float enemyMaxTwoStarChance = 0.5f;
        [Tooltip("С этого дня среди варваров бывают ★★★")]
        [FormerlySerializedAs("enemyArmyDay")]
        [Min(1)] public int enemyThreeStarDay = 12;
        [Tooltip("Шанс ★★★: +доля за каждый день с enemyThreeStarDay (не больше enemyMaxThreeStarChance)")]
        [Range(0f, 1f)] public float enemyThreeStarChancePerDay = 0.04f;
        [Range(0f, 1f)] public float enemyMaxThreeStarChance = 0.3f;

        [Header("Особые варвары")]
        [Tooltip("Подрывники, маги (с enemyHybridDay) и вождь-богатырь (с enemyHeroDay). Нет в списке — берутся числа по умолчанию")]
        public List<UnitData> enemySpecials = new List<UnitData>();
        [Tooltip("С этого дня в волне бывают подрывники и маги")]
        [Min(1)] public int enemyHybridDay = 10;
        [Tooltip("Доля подрывников и магов в волне: в первый их день и прибавка за каждый следующий (не больше enemyMaxHybridShare)")]
        [Range(0f, 1f)] public float enemyHybridShare = 0.15f;
        [Range(0f, 1f)] public float enemyHybridSharePerDay = 0.02f;
        [Range(0f, 1f)] public float enemyMaxHybridShare = 0.35f;
        [Tooltip("С этого дня волну ведёт вождь-богатырь (один на волну)")]
        [Min(1)] public int enemyHeroDay = 15;

        [Header("Набег (если бой проигран)")]
        [Tooltip("Удар уцелевшего варвара по зданию = атака × множитель + оставшееся ХП × доля")]
        [Min(0f)] public float raidAttackMultiplier = 1.5f;
        [Range(0f, 1f)] public float raidHpShare = 0.15f;

        private readonly List<BattleUnit> units = new List<BattleUnit>();
        private BattleUnit[,] occupancy;
        private Transform unitsRoot;
        private Transform arenaRoot;
        private Action<BattleOutcome> onFinished;
        private float battleTime;
        /// <summary>Часы поля (обычное время × скорость боя): лимит боя и перезарядка наших суперударов.</summary>
        private float boardTime;
        /// <summary>Сколько ещё секунд (по часам поля) действует «Тактика».</summary>
        private float tacticsLeft;
        private bool tacticsShown;
        /// <summary>Сейчас применяется ряд с поля — его урон идёт в Stats.BoardDamage.</summary>
        private bool boardAction;
        private readonly List<(SpriteRenderer renderer, Color color)> arenaCells = new List<(SpriteRenderer, Color)>();

        /// <summary>Итоги текущего (или последнего) боя.</summary>
        public BattleStats Stats { get; private set; } = new BattleStats();
        /// <summary>Армии построились — бойцы ходят и бьют, поле принимает ряды.</summary>
        private bool fighting;
        private float enemySkillTimer;
        private float enemySuperTimer;
        private float stepTextTimer;
        private int boardClassesKey = -1;
        /// <summary>Чьи символы на поле (до BattleBoard.maxClassSymbols классов; вымерший уступает место следующему).</summary>
        private readonly List<UnitClass> boardClasses = new List<UnitClass>();
        private float offBoardTimer;
        private int offBoardTurn;
        private bool boardHooked;
        private float playerStartHp;
        private float enemyStartHp;
        private int bannerFrame = -1;
        private float rosterTimer;
        private readonly List<Vector2> bannersThisFrame = new List<Vector2>();
        /// <summary>С какой секунды боя класс стороны снова может сделать суперудар.</summary>
        private readonly Dictionary<(Team, UnitClass), float> superReadyAt = new Dictionary<(Team, UnitClass), float>();
        /// <summary>Вожаки каждой стороны: у нас — по одному на линейку (гарнизон, отряды), у врага — вождь.</summary>
        private readonly Dictionary<Team, List<BattleUnit>> leaders = new Dictionary<Team, List<BattleUnit>>();

        public bool IsRunning { get; private set; }
        /// <summary>Целые секунды боя: по ним считают длительность благословения и огненных клинков.</summary>
        public int CurrentStep => Mathf.FloorToInt(battleTime);
        public int Day { get; private set; } = 1;
        public IReadOnlyList<BattleUnit> Units => units;
        public int Columns => columns;
        public int Rows => rows;
        /// <summary>Сколько наших бойцов встаёт на арену: три шеренги во всю ширину.</summary>
        public int PlayerCapacity => columns * 3;

        private float EnemyStatMultiplier => 1f + (Day - 1) * enemyStatsGrowthPerDay;
        private int EnemyClassLevel => Day >= 5 ? 1 : 0;

        public static Vector2Int Forward(Team team) => team == Team.Player ? Vector2Int.up : Vector2Int.down;
        public static Team Opposite(Team team) => team == Team.Player ? Team.Enemy : Team.Player;

        // =====================================================================
        //  Запуск и завершение боя
        // =====================================================================

        /// <summary>
        /// Начать бой. army — наши бойцы (порядок = строй), enemies — враги (null — сгенерировать по ходу),
        /// finished — итог после боя. armyRows — наши линейки (гарнизон, отряды): вожак линейки усиливает только её бойцов.
        /// У врага вожак — его вождь, бонус всем.
        /// </summary>
        public void StartBattle(IReadOnlyList<BenchUnit> army, IReadOnlyList<BenchUnit> enemies, int turn, Action<BattleOutcome> finished,
            IEnumerable<ArmyRow> armyRows = null)
        {
            StopAllCoroutines();
            ClearUnits();
            EnsureArena();
            EnsureBoard();
            HookControls();

            Day = ThreatDay(turn);
            onFinished = finished;
            battleTime = 0f;
            boardTime = 0f;
            tacticsLeft = 0f;
            Stats = new BattleStats();
            SetTacticsLook(false);
            fighting = false;
            occupancy = new BattleUnit[columns, rows];

            leaders.Clear();
            SpawnArmy(army, Team.Player);
            EnsureRosterCapacity(units.Count);
            List<BenchUnit> foes = enemies != null ? enemies.Where(u => u != null && u.data != null).ToList() : GenerateEnemies(Day);
            if (foes.Count > unitsPerRow * 3) foes = foes.OrderByDescending(u => u.stars * 100 + u.level).Take(unitsPerRow * 3).ToList();
            foes.Sort((a, b) => FormationRank(a.data).CompareTo(FormationRank(b.data)));
            SpawnArmy(foes, Team.Enemy);
            if (armyRows != null)
            {
                foreach (ArmyRow row in armyRows)
                {
                    if (row != null) ApplyLeader(Team.Player, row.Leader, row.Units.ToList());
                }
            }
            ApplyLeader(Team.Enemy, foes.FirstOrDefault(u => u.IsHeroClass), foes);
            playerStartHp = TotalHp(Team.Player);
            enemyStartHp = TotalHp(Team.Enemy);

            superReadyAt.Clear();
            enemySkillTimer = enemySkillInterval * 0.6f;
            enemySuperTimer = enemySuperInterval * 0.7f;
            boardClassesKey = -1;
            boardClasses.Clear();
            offBoardTimer = offBoardSkillInterval * 0.6f;
            offBoardTurn = 0;
            UpdateBoardClassList();
            if (board != null) board.Begin(BoardClassesWithHero());
            RefreshBoardClasses();
            UpdateLegend();
            if (arenaRoot != null) arenaRoot.gameObject.SetActive(true);
            if (resultText != null) resultText.text = string.Empty;
            if (enemyTurnText != null) enemyTurnText.text = string.Empty;
            UpdateSpeedLabel();

            IsRunning = true;
            UpdateStepText();
            StartCoroutine(StartRoutine());
        }

        /// <summary>Поле боя: из ссылки, а нет её — найдётся на сцене; ряды поля идут в OnBoardMatched.</summary>
        private void EnsureBoard()
        {
            if (board == null) board = FindFirstObjectByType<BattleBoard>(FindObjectsInactive.Include);
            if (board == null || boardHooked) return;
            boardHooked = true;
            if (board.city == null) board.city = city;
            board.Matched += OnBoardMatched;
        }

        /// <summary>Кнопка скорости: ×1 → ×2 → ×3 → ×1.</summary>
        public void ToggleSpeed()
        {
            battleSpeed = battleSpeed < 1.5f ? 2f : (battleSpeed < 2.5f ? 3f : 1f);
            UpdateSpeedLabel();
        }

        // =====================================================================
        //  Бой в реальном времени: бойцы сами, мы — рядами на поле боя
        // =====================================================================

        /// <summary>У каких классов есть суперудар.</summary>
        public static readonly UnitClass[] AbilityClasses =
        {
            UnitClass.Warrior, UnitClass.Archer, UnitClass.Priest, UnitClass.Bomber, UnitClass.Monk, UnitClass.Mage,
        };

        /// <summary>Устарело: кнопки ручной крутки, паузы и способностей из старых сцен (прячутся, ничего не делают).</summary>
        public void RequestSpin() { }
        public void ToggleAutoSpin() { }
        public void OrderWarriors() { }
        public void OrderArchers() { }
        public void OrderPriests() { }
        public void UseAbility(int index) { }

        private bool controlsHooked;

        /// <summary>Кнопки старых сцен (крутить, пауза, способности, приказы, боевой дух) — прочь.</summary>
        private void HookControls()
        {
            if (controlsHooked) return;
            controlsHooked = true;
            if (moraleText != null) moraleText.gameObject.SetActive(false);
            if (spinButton != null) spinButton.gameObject.SetActive(false);
            Button autoButton = autoSpinLabel != null ? autoSpinLabel.GetComponentInParent<Button>(true) : null;
            if (autoButton != null) autoButton.gameObject.SetActive(false);
            foreach (Button button in (abilityButtons ?? new Button[0]).Concat(orderButtons ?? new Button[0]))
            {
                if (button != null) button.gameObject.SetActive(false);
            }
        }

        /// <summary>Суперспособность класса.</summary>
        public static string SuperName(UnitClass unitClass)
        {
            switch (unitClass)
            {
                case UnitClass.Warrior: return "Землетрясение";
                case UnitClass.Archer: return "Огненный дождь";
                case UnitClass.Priest: return "Воскрешение";
                case UnitClass.Bomber: return "Пороховая бочка";
                case UnitClass.Monk: return "Вихрь";
                case UnitClass.Mage: return "Ледяная буря";
                default: return string.Empty;
            }
        }

        /// <summary>Что делает суперспособность — коротко, для легенды.</summary>
        private string SuperEffect(UnitClass unitClass)
        {
            switch (unitClass)
            {
                case UnitClass.Warrior: return $"враги в {quakeRadius} кл. — урон и оглушение";
                case UnitClass.Archer: return $"{fireRainWaves} волны стрел";
                case UnitClass.Priest: return $"павшие встают ({Mathf.RoundToInt(reviveHp * 100f)}%{GameVisuals.IconHp})";
                case UnitClass.Bomber: return $"взрыв в {powderRadius} кл.";
                case UnitClass.Monk: return "бьют всех вокруг";
                case UnitClass.Mage: return "урон всем и заморозка";
                default: return string.Empty;
            }
        }

        /// <summary>«Армии строятся» — и бой пошёл.</summary>
        private IEnumerator StartRoutine()
        {
            yield return Wait(startDelay);
            if (!IsRunning) yield break;
            fighting = true;
            if (board != null) board.Unlock();
            UpdateStepText();
            CheckForWinner();
        }

        /// <summary>Кадр боя: время, бойцы (ходят и бьют по своим таймерам), навыки врага, проверка победы и времени.</summary>
        private void TickBattle(float dt)
        {
            battleTime += dt;
            foreach (BattleUnit unit in units.ToList())
            {
                if (!IsRunning) return;
                if (!unit.IsDead) unit.Tick(dt);
            }
            if (!IsRunning) return;
            EnemyAbilities(dt);
            OffBoardSkills(dt);
            CheckForWinner();
            Stats.ArenaSeconds = battleTime;
            if (IsRunning && boardTime >= battleDuration) FinishBattle(PlayerWinsOnTimeout(), timeout: true);
            if (IsRunning) RefreshBoardClasses();
        }

        /// <summary>Секунд между ударами бойца: стрелки — чуть реже, богатырь — чаще.</summary>
        public float AttackIntervalOf(BattleUnit unit)
        {
            float interval = attackInterval * (unit.AttackRange > 1 ? 1.15f : 1f);
            if (unit.Data != null && unit.Data.unitClass == UnitClass.Hero) interval *= 0.85f;
            if (unit.Team == Team.Player && city != null && unit.Data != null)
                interval *= 1f - Mathf.Clamp(city.ClassEffect(CityEffect.Haste, unit.Data.unitClass), 0f, 0.6f);
            return interval;
        }

        /// <summary>Класс вымер (или ожил) — поле боя меняет, чьи символы падают, легенда — что они делают.</summary>
        private void RefreshBoardClasses()
        {
            bool changed = UpdateBoardClassList();
            int key = 0;
            foreach (UnitClass unitClass in AliveClasses(Team.Player)) key |= 1 << (int)unitClass;
            if (key == boardClassesKey && !changed) return;
            boardClassesKey = key;
            if (board != null) board.SetClasses(BoardClassesWithHero());
            UpdateLegend();
        }

        /// <summary>
        /// Классы на поле: самые многочисленные, не больше BattleBoard.maxClassSymbols. Состав держится, пока класс жив,
        /// — вымер, и его место занимает следующий. true — состав поменялся.
        /// </summary>
        private bool UpdateBoardClassList()
        {
            List<UnitClass> alive = AliveClasses(Team.Player).Where(c => BattleBoard.SymbolFor(c) != BattleSymbol.Blank).ToList();
            bool changed = boardClasses.RemoveAll(c => !alive.Contains(c)) > 0;
            int max = board != null ? board.maxClassSymbols : 3;
            foreach (UnitClass unitClass in alive.Where(c => !boardClasses.Contains(c))
                         .OrderByDescending(c => CountAlive(Team.Player, c)).ThenBy(c => (int)c).ToList())
            {
                if (boardClasses.Count >= max) break;
                boardClasses.Add(unitClass);
                changed = true;
            }
            return changed;
        }

        /// <summary>Для поля: классы с символами + богатырь, если жив (его «Знамя» — джокеры).</summary>
        private List<UnitClass> BoardClassesWithHero()
        {
            var classes = new List<UnitClass>(boardClasses);
            if (CountAlive(Team.Player, UnitClass.Hero) > 0) classes.Add(UnitClass.Hero);
            return classes;
        }

        /// <summary>Классы, которым не хватило места на поле: применяют навык сами раз в offBoardSkillInterval.</summary>
        private List<UnitClass> OffBoardClasses() => AliveClasses(Team.Player)
            .Where(c => BattleBoard.SymbolFor(c) != BattleSymbol.Blank && !boardClasses.Contains(c)).OrderBy(c => (int)c).ToList();

        private void OffBoardSkills(float dt)
        {
            offBoardTimer -= dt;
            if (offBoardTimer > 0f) return;
            offBoardTimer = offBoardSkillInterval;
            List<UnitClass> classes = OffBoardClasses().Where(c => Ready(Team.Player, c).Count > 0).ToList();
            if (classes.Count == 0) return;
            UnitClass unitClass = classes[offBoardTurn++ % classes.Count];
            ActionTally done = Tallied(Team.Player, () => TriggerClassSkill(Team.Player, unitClass, SkillPower(Team.Player, unitClass)));
            if (board != null)
                board.ShowTopAction(SkillTitle(Team.Player, unitClass), GameVisuals.ClassColor(unitClass), enemy: false);
        }

        /// <summary>
        /// Ряд на поле боя: ⚔ — натиск, ⏳ — тактика, символ класса — его навык (5+ — суперудар), 🕯 — «Чудо».
        /// Сила — длина ряда × волна каскада × ×2 в ряду.
        /// </summary>
        private void OnBoardMatched(BoardMatch match)
        {
            if (!IsRunning || !fighting || match == null) return;
            float power = MatchPower(match);
            string text = string.Empty, word = string.Empty;
            bool super = false;
            if (match.Symbol != BattleSymbol.Blank) Stats.Matches++;
            ActionTally tally = Tallied(Team.Player, () =>
            {
            switch (match.Symbol)
            {
                case BattleSymbol.Attack:
                    bool hit = Assault(Team.Player, power);
                    text = hit ? $"{GameVisuals.IconAttack} Натиск{PowerTag(power)}!" : $"{GameVisuals.IconAttack} До врага не достают — шагают к нему";
                    word = hit ? "Натиск" : "Вперёд";
                    break;
                case BattleSymbol.Tactics:
                    // Арена медленнее, поле — нет: успеть собрать больше рядов и посмотреть на бой
                    float seconds = (tacticsSeconds + Mathf.Max(0, match.Length - 3) * tacticsSecondsPerExtra) * match.Multiplier;
                    tacticsLeft = Mathf.Min(tacticsMaxSeconds, tacticsLeft + seconds);
                    SetTacticsLook(true);
                    text = $"{GameVisuals.IconTurns} Тактика: бой медленнее ещё {Mathf.CeilToInt(tacticsLeft)} с";
                    word = "Тактика";
                    break;
                case BattleSymbol.Relic:
                    Miracle(Team.Player, MiraclePower(Team.Player, match.Length) * (1f + (match.Wave - 1) * cascadeBonus) * match.Multiplier);
                    text = $"{GameVisuals.IconCandle} Чудо{PowerTag(power)}!";
                    word = "Чудо";
                    break;
                case BattleSymbol.Blank:
                    break;
                default:
                    text = ClassMatch(BattleBoard.ClassOf(match.Symbol), match, power, out word, out super);
                    break;
            }
            }, fromBoard: true);
            if (super) Stats.Supers++;
            if (text.Length == 0) return;
            if (match.Wave > 1) text = $"<color=#ffd166>каскад ×{match.Wave}</color> {text}";
            // На поле — одно слово и вспышка, подробности (урон, добитые…) — строкой под полем
            // «Тактика» ничего не бьёт — её итог и есть замедление, без «без толку»
            string details = match.Symbol == BattleSymbol.Tactics ? string.Empty : tally.Text();
            if (board != null && board.resultText != null) board.resultText.text = $"{text}  <size=80%>{details}</size>";
            if (board != null) board.ShowOutcome(match.Cells, word, OutcomeColor(match.Symbol), super, tally.Empty && match.Symbol != BattleSymbol.Tactics);
            CheckForWinner();
        }

        /// <summary>
        /// Что сделало на арене одно действие (ряд с поля три в ряд или навык врага): урон, добитые, оглушённые,
        /// лечение… BattleUnit пишет сюда, пока действие идёт, — итог показывается табличкой прямо на поле.
        /// </summary>
        public sealed class ActionTally
        {
            private readonly Team side;
            private float damage, healed;
            private int kills, revived;
            private readonly HashSet<BattleUnit> stunned = new HashSet<BattleUnit>();
            private readonly HashSet<BattleUnit> burning = new HashSet<BattleUnit>();
            private readonly HashSet<BattleUnit> buffed = new HashSet<BattleUnit>();
            public int Moved;

            public ActionTally(Team side) => this.side = side;

            /// <summary>Ничего не вышло: ни урона, ни лечения, ни эффектов, никто не сдвинулся.</summary>
            public bool Empty => damage < 1f && healed < 1f && kills == 0 && revived == 0 && stunned.Count == 0 &&
                                 burning.Count == 0 && buffed.Count == 0 && Moved == 0;

            public void Hit(BattleUnit victim, float dealt, bool killed)
            {
                if (victim.Team == side) return;
                damage += dealt;
                if (killed) kills++;
            }

            public void Healed(BattleUnit unit, float amount) { if (unit.Team == side) healed += amount; }
            public void Revived(BattleUnit unit) { if (unit.Team == side) revived++; }
            public void Stunned(BattleUnit unit) { if (unit.Team != side) stunned.Add(unit); }
            public void Burned(BattleUnit unit) { if (unit.Team != side) burning.Add(unit); }
            public void Buffed(BattleUnit unit) { if (unit.Team == side) buffed.Add(unit); }

            /// <summary>«−124 урона · добито 2 · оглушено 3 · +60 лечения»; ничего не вышло — «без толку».</summary>
            public string Text()
            {
                var parts = new List<string>();
                if (damage >= 1f) parts.Add($"<color=#ff9e80>−{Mathf.RoundToInt(damage)} урона</color>");
                if (kills > 0) parts.Add($"<color=#ff6e6e>{GameVisuals.IconDead} {(side == Team.Player ? "добито" : "погибло")} {kills}</color>");
                if (stunned.Count > 0) parts.Add($"<color=#b3e5fc>оглушено {stunned.Count}</color>");
                if (burning.Count > 0) parts.Add($"<color=#ffb74d>горят {burning.Count}</color>");
                if (healed >= 1f) parts.Add($"<color=#a5f2a5>+{Mathf.RoundToInt(healed)} лечения</color>");
                if (revived > 0) parts.Add($"<color=#fff3b0>воскресли {revived}</color>");
                if (buffed.Count > 0) parts.Add($"<color=#fff3b0>усилены {buffed.Count}</color>");
                if (Moved > 0) parts.Add($"<color=#b3d9ff>{GameVisuals.IconMove} двинулись {Moved}</color>");
                return parts.Count > 0 ? string.Join(" · ", parts) : "<color=#9e9e9e>без толку</color>";
            }
        }

        /// <summary>Идёт действие, которое считается для таблички на поле (null — не считаем).</summary>
        public ActionTally Tally { get; private set; }

        /// <summary>Выполнить действие стороны и посчитать, что оно сделало (кто сдвинулся — по клеткам до и после).</summary>
        private ActionTally Tallied(Team team, Action action, bool fromBoard = false)
        {
            Dictionary<BattleUnit, Vector2Int> cells = Alive(team).ToDictionary(u => u, u => u.Cell);
            var tally = new ActionTally(team);
            ActionTally outer = Tally;
            bool outerBoard = boardAction;
            Tally = tally;
            boardAction = fromBoard;
            try
            {
                action();
            }
            finally
            {
                Tally = outer;
                boardAction = outerBoard;
            }
            tally.Moved = cells.Count(pair => !pair.Key.IsDead && pair.Key.Cell != pair.Value);
            return tally;
        }

        /// <summary>Цвет рамки таблички на поле: класс — его цвет.</summary>
        private static Color OutcomeColor(BattleSymbol symbol)
        {
            switch (symbol)
            {
                case BattleSymbol.Attack: return new Color(1f, 0.8f, 0.45f);
                case BattleSymbol.Tactics: return new Color(0.62f, 0.7f, 1f);
                case BattleSymbol.Relic: return new Color(1f, 0.95f, 0.7f);
                default: return GameVisuals.ClassColor(BattleBoard.ClassOf(symbol));
            }
        }

        /// <summary>Сила ряда: 3 — ×1, каждый символ сверх — +lengthBonus, волна каскада — +cascadeBonus, ×2 в ряду.</summary>
        private float MatchPower(BoardMatch match) =>
            (1f + Mathf.Max(0, match.Length - 3) * lengthBonus) * (1f + (match.Wave - 1) * cascadeBonus) * match.Multiplier;

        /// <summary>Ряд символов класса: навык; 5+ в ряд — суперудар (открыт приказом гильдии и не перезаряжается).</summary>
        private string ClassMatch(UnitClass unitClass, BoardMatch match, float power, out string word, out bool super)
        {
            string icon = GameVisuals.ClassIcon(unitClass);
            super = false;
            if (Ready(Team.Player, unitClass).Count == 0)
            {
                bool alive = CountAlive(Team.Player, unitClass) > 0;
                word = alive ? "Оглушены" : "Некому";
                return alive ? $"{icon} оглушены — навык пропал" : $"{icon} их в бою уже нет";
            }
            bool hasSuper = Array.IndexOf(AbilityClasses, unitClass) >= 0;
            int superLength = SuperLength(unitClass);
            CastRunes(unitClass);
            if (match.Length >= superLength && hasSuper && SuperUnlocked(Team.Player, unitClass) && SuperReady(Team.Player, unitClass))
            {
                float superPower = Mathf.Min(superPowerCap,
                    (1f + (match.Length - superLength) * lengthBonus) * (1f + (match.Wave - 1) * cascadeBonus) * match.Multiplier);
                superReadyAt[(Team.Player, unitClass)] = boardTime + SuperCooldown(Team.Player);
                SuperShow(Team.Player, unitClass);
                TriggerSuper(Team.Player, unitClass, superPower);
                word = SuperName(unitClass);
                super = true;
                return $"<color=#ffd166>{SuperName(unitClass)}!</color>";
            }
            TriggerClassSkill(Team.Player, unitClass, power * SkillPower(Team.Player, unitClass));
            word = SkillTitle(Team.Player, unitClass);
            string note = string.Empty;
            if (match.Length >= superLength && hasSuper)
                note = !SuperUnlocked(Team.Player, unitClass) ? " <size=80%>(суперудар — приказ в гильдии)</size>"
                    : !SuperReady(Team.Player, unitClass) ? " <size=80%>(суперудар перезаряжается)</size>" : string.Empty;
            return $"{icon} {SkillTitle(Team.Player, unitClass)}{PowerTag(power)}!{note}";
        }

        /// <summary>Сколько символов класса в ряд нужно на суперудар: 5, а с вершиной ветки в «Войсках» — 4.</summary>
        public int SuperLength(UnitClass unitClass) =>
            city != null && city.ClassEffect(CityEffect.SuperAt4, unitClass) > 0f ? 4 : 5;

        /// <summary>«Руны» магов: ряд их символов превращает несколько фишек поля боя в символы магов.</summary>
        private void CastRunes(UnitClass unitClass)
        {
            int runes = city != null ? Mathf.RoundToInt(city.ClassEffect(CityEffect.Runes, unitClass)) : 0;
            if (runes > 0 && board != null) board.Inject(BattleBoard.SymbolFor(unitClass), runes);
        }

        /// <summary>Боец добил врага: «Знамёна полков» — воин лечится.</summary>
        public void OnKill(BattleUnit killer, BattleUnit victim)
        {
            if (killer == null || killer.Team != Team.Player || city == null || killer.IsDead) return;
            float heal = city.ClassEffect(CityEffect.Bloodlust, killer.Data.unitClass);
            if (heal > 0f) killer.Heal(killer.MaxHp * heal);
        }

        /// <summary>Эффект дерева «Войск» для нашего бойца (по его классу); у врага — 0.</summary>
        public float PlayerEffect(BattleUnit unit, CityEffect effect) =>
            unit != null && unit.Team == Team.Player && city != null && unit.Data != null ? city.ClassEffect(effect, unit.Data.unitClass) : 0f;

        /// <summary>«Внутренний огонь»: под «Огненными клинками» наши лечатся на эту долю урона.</summary>
        public float Lifesteal => city != null ? city.ClassEffect(CityEffect.Lifesteal, UnitClass.None) : 0f;

        /// <summary>Рядом стоит живой союзник этого класса («Фаланга»).</summary>
        public bool HasAdjacentAlly(BattleUnit unit, UnitClass unitClass) =>
            units.Any(u => u != unit && !u.IsDead && u.Team == unit.Team && u.Data.unitClass == unitClass && Distance(u.Cell, unit.Cell) <= 1);

        /// <summary>Шанс нашего бойца ударить второй раз («Вихрь ударов» монахов).</summary>
        public float DoubleStrikeChance(BattleUnit unit) =>
            unit.Team == Team.Player && city != null ? Mathf.Clamp01(city.ClassEffect(CityEffect.DoubleStrike, unit.Data.unitClass)) : 0f;

        /// <summary>Натиск: все, кто достаёт до врага, бьют разом; кто не достаёт — шаг к врагу. false — бить было некому.</summary>
        private bool Assault(Team team, float power)
        {
            bool any = false;
            foreach (BattleUnit unit in Alive(team))
            {
                if (unit.IsStunned) continue;
                if (unit.HasTargetInRange())
                {
                    unit.AttackAction(power * assaultPower);
                    any = true;
                }
                else if (unit.WantsToMove())
                {
                    unit.MoveAction(1);
                }
            }
            if (any) SkillBanner(Alive(team), "НАТИСК!", new Color(1f, 0.85f, 0.55f));
            return any;
        }


        /// <summary>
        /// Враг применяет навыки сам: раз в enemySkillInterval (± разброс) — навык случайного своего класса,
        /// с enemySuperDay — ещё и суперудар самого многочисленного класса раз в enemySuperInterval.
        /// </summary>
        private void EnemyAbilities(float dt)
        {
            enemySkillTimer -= dt;
            if (enemySkillTimer <= 0f)
            {
                enemySkillTimer = Mathf.Max(1f, enemySkillInterval + Random.Range(-enemySkillJitter, enemySkillJitter));
                List<UnitClass> classes = AliveClasses(Team.Enemy).Where(c => SkillEffect(c).Length > 0 && Ready(Team.Enemy, c).Count > 0).ToList();
                if (classes.Count > 0)
                {
                    UnitClass unitClass = classes[Random.Range(0, classes.Count)];
                    ActionTally done = Tallied(Team.Enemy, () => TriggerClassSkill(Team.Enemy, unitClass, SkillPower(Team.Enemy, unitClass)));
                    EnemySays($"{GameVisuals.ClassIcon(unitClass)} {SkillTitle(Team.Enemy, unitClass)}!", SkillTitle(Team.Enemy, unitClass), done);
                }
            }
            if (Day < enemySuperDay) return;
            enemySuperTimer -= dt;
            if (enemySuperTimer > 0f) return;
            enemySuperTimer = Mathf.Max(5f, enemySuperInterval + Random.Range(-enemySkillJitter, enemySkillJitter));
            UnitClass best = AliveClasses(Team.Enemy).Where(c => Array.IndexOf(AbilityClasses, c) >= 0 && Ready(Team.Enemy, c).Count > 0)
                .OrderByDescending(c => CountAlive(Team.Enemy, c)).FirstOrDefault();
            if (best == UnitClass.None) return;
            ActionTally strike = Tallied(Team.Enemy, () =>
            {
                SuperShow(Team.Enemy, best);
                TriggerSuper(Team.Enemy, best, 1f);
            });
            EnemySays($"<color=#ffd166>{SuperName(best)}!</color>", SuperName(best), strike);
        }

        /// <summary>Навык врага: строкой у арены и табличкой сверху поля три в ряд (с тем, что он нам сделал).</summary>
        private void EnemySays(string text, string word, ActionTally done)
        {
            if (enemyTurnText != null) enemyTurnText.text = $"<color=#ff8a80>Враг:</color> {text} <size=80%>{done.Text()}</size>";
            if (board != null) board.ShowTopAction($"Враг: {word}", new Color(1f, 0.4f, 0.35f), enemy: true);
        }

        private static string PowerTag(float power) => power > 1.05f ? $" ×{power:0.#}" : string.Empty;

        /// <summary>Сила «Чуда»: свечи сверх трёх и «Святые мощи».</summary>
        private float MiraclePower(Team team, int relics) =>
            (1f + Mathf.Max(0, relics - 3) * miracleExtraPerRelic) * (team == Team.Player && city != null ? city.MiracleMultiplier : 1f);

        /// <summary>«Чудо»: все свои лечатся и получают благословение.</summary>
        private void Miracle(Team team, float power)
        {
            List<BattleUnit> allies = Alive(team);
            if (allies.Count == 0) return;
            foreach (BattleUnit ally in allies)
            {
                ally.Heal(ally.MaxHp * miracleHealShare * power);
                ally.ApplyBlessing(CurrentStep + blessingSeconds, blessingReduction);
                BattleVfx.Ring(ally.Position, 0.45f, new Color(1f, 0.95f, 0.7f, 0.7f), 0.5f);
            }
            SkillBanner(allies, "ЧУДО!", new Color(1f, 0.95f, 0.7f));
        }

        /// <summary>Суперудар класса открыт: у нас — приказом гильдии (гибридам — любой родительской), у врага — всегда.</summary>
        public bool SuperUnlocked(Team team, UnitClass unitClass)
        {
            if (team == Team.Enemy) return true;
            if (city == null) return false;
            return UnitClasses.IsHybrid(unitClass) ? UnitClasses.Parents(unitClass).Any(city.HasOrder) : city.HasOrder(unitClass);
        }

        /// <summary>Класс стороны не перезаряжается после прошлого суперудара.</summary>
        private bool SuperReady(Team team, UnitClass unitClass) =>
            !superReadyAt.TryGetValue((team, unitClass), out float at) || (team == Team.Player ? boardTime : battleTime) >= at;

        /// <summary>Перезарядка суперудара (наша «Выдержка» — быстрее).</summary>
        private float SuperCooldown(Team team) =>
            superCooldownSeconds * (team == Team.Player && city != null && city.FasterSupers ? fasterSupersShare : 1f);

        /// <summary>Суперудар: вспышка цвета класса на всю арену и крупное название (у врага — «ВРАГ:»).</summary>
        private void SuperShow(Team team, UnitClass unitClass)
        {
            Color color = Color.Lerp(GameVisuals.ClassColor(unitClass), Color.white, 0.35f);
            Vector2 center = transform.position;
            Color flash = team == Team.Player ? color : Color.Lerp(color, new Color(1f, 0.25f, 0.2f), 0.5f);
            flash.a = 0.32f;
            BattleVfx.Flash(center, new Vector2(columns * cellSize, rows * cellSize), flash, 0.7f);
            string title = $"{SuperName(unitClass).ToUpperInvariant()}!";
            BattleVfx.FloatingText(center + Vector2.up * 0.4f, team == Team.Player ? title : $"<size=70%>ВРАГ:</size>\n{title}",
                team == Team.Player ? new Color(1f, 0.88f, 0.4f) : new Color(1f, 0.6f, 0.55f), 4.6f, 1.8f, outline: true);
        }

        /// <summary>«День угрозы» хода: по нему растут враги (50 ходов — примерно 23 дня прежнего темпа).</summary>
        public int ThreatDay(int turn) => 1 + Mathf.FloorToInt(Mathf.Max(0, turn - 1) * difficultyPerTurn);

        /// <summary>Удары по зданиям, если город грабят без боя (армия в поле): как у уцелевших в бою врагов.</summary>
        public List<float> RaidHits(IEnumerable<BenchUnit> raiders, int turn)
        {
            float multiplier = 1f + (ThreatDay(turn) - 1) * enemyStatsGrowthPerDay;
            return raiders.Where(u => u?.data != null)
                .Select(u => (u.Attack * raidAttackMultiplier + u.MaxHp * raidHpShare) * multiplier)
                .ToList();
        }

        /// <summary>Название навыка класса для стороны: воины без щитов делают «Рывок», со щитами — «Таран щитом».</summary>
        public string SkillTitle(Team team, UnitClass unitClass)
        {
            if (unitClass == UnitClass.Warrior && ShieldLevelOf(team) == 0) return "Рывок";
            return GameVisuals.SkillName(unitClass);
        }

        /// <summary>Щиты воинов стороны: свои — из города, варвары берут щиты с определённого дня.</summary>
        public int ShieldLevelOf(Team team)
        {
            if (team == Team.Player) return city != null ? city.ShieldLevel : 0;
            return Day >= enemyTowerShieldDay ? 2 : Day >= enemyShieldDay ? 1 : 0;
        }

        /// <summary>
        /// Шансы щита для удара: полный блок и частичный. Обычный щит ловит удары в лицо, ростовой
        /// — ещё и в бок (хуже); в спину щит не помогает. Стрелы ловятся чаще, болты — реже.
        /// </summary>
        public void ShieldChances(int shieldLevel, HitSide side, bool ranged, bool pierce, out float full, out float partial,
            float blockBonus = 0f)
        {
            full = 0f;
            partial = 0f;
            if (shieldLevel <= 0 || side == HitSide.Back) return;
            bool tower = shieldLevel >= 2;
            float cover = side == HitSide.Front ? 1f : (tower ? sideCover : 0f);
            if (cover <= 0f) return;

            full = tower ? towerShieldFullBlock : shieldFullBlock;
            partial = tower ? towerShieldPartialBlock : shieldPartialBlock;
            if (ranged) full *= arrowFullBlockMultiplier;
            full += blockBonus;
            if (pierce)
            {
                full *= crossbowBlockMultiplier;
                partial *= crossbowBlockMultiplier;
            }
            full = Mathf.Clamp01(full * cover);
            partial = Mathf.Clamp(partial * cover, 0f, 1f - full);
        }

        /// <summary>Бросок щита: 1 — удар пойман целиком, доля (0..1) — частично, 0 — щит не помог.</summary>
        public float RollShieldBlock(BattleUnit defender, HitSide side, bool ranged, bool pierce)
        {
            ShieldChances(defender.ShieldLevel, side, ranged, pierce, out float full, out float partial, defender.ShieldBonus);
            float roll = Random.value;
            if (roll < full) return 1f;
            if (roll < full + partial) return Random.Range(partialBlockMin, Mathf.Max(partialBlockMin, partialBlockMax));
            return 0f;
        }

        private float SkillPower(Team team, UnitClass unitClass)
        {
            if (team == Team.Player) return city != null ? city.SkillPower(unitClass) : 1f;
            return 1f + 0.25f * EnemyClassLevel;
        }

        private void CheckForWinner()
        {
            if (!IsRunning) return;
            bool playerAlive = CountAlive(Team.Player) > 0;
            bool enemyAlive = CountAlive(Team.Enemy) > 0;
            if (!playerAlive || !enemyAlive) FinishBattle(playerAlive, timeout: false);
        }

        private bool PlayerWinsOnTimeout()
        {
            float playerShare = playerStartHp > 0f ? TotalHp(Team.Player) / playerStartHp : 0f;
            float enemyShare = enemyStartHp > 0f ? TotalHp(Team.Enemy) / enemyStartHp : 0f;
            return playerShare > enemyShare;
        }

        private void FinishBattle(bool victory, bool timeout)
        {
            if (!IsRunning) return;
            IsRunning = false;
            fighting = false;
            if (board != null) board.End();
            if (resultText != null) resultText.text = victory ? "ПОБЕДА!" : "ПОРАЖЕНИЕ";
            if (timeout && stepText != null) stepText.text = "Время вышло — решает оставшееся ХП";

            // Уцелевшие варвары идут грабить город: сила удара — от их атаки и оставшегося здоровья
            var outcome = new BattleOutcome
            {
                Victory = victory,
                Survivors = CountAlive(Team.Player),
                KilledStars = units.Where(u => u.IsDead && u.Team == Team.Enemy).Sum(u => u.Stars),
            };
            foreach (BattleUnit unit in units)
            {
                if (unit.Team == Team.Player && unit.Source != null)
                    outcome.PlayerUnits.Add(new UnitBattleResult { Unit = unit.Source, Alive = !unit.IsDead, Kills = unit.Kills });
            }
            foreach (BattleUnit enemy in units.Where(u => u.Team == Team.Enemy))
            {
                if (enemy.IsDead)
                {
                    if (enemy.Source != null) outcome.EnemyFallen.Add(enemy.Source);
                    continue;
                }
                if (enemy.Source != null) outcome.EnemySurvivors.Add(enemy.Source);
                if (!victory) outcome.RaidHits.Add(enemy.AttackDamage * raidAttackMultiplier + enemy.Hp / Mathf.Max(1f, hpMultiplier) * raidHpShare);
            }
            StartCoroutine(EndRoutine(outcome));
        }

        private IEnumerator EndRoutine(BattleOutcome outcome)
        {
            yield return Wait(endDelay);

            superReadyAt.Clear();
            leaders.Clear();
            if (board != null) board.End();
            ClearUnits();
            if (arenaRoot != null) arenaRoot.gameObject.SetActive(false);
            if (resultText != null) resultText.text = string.Empty;
            if (enemyTurnText != null) enemyTurnText.text = string.Empty;

            Action<BattleOutcome> callback = onFinished;
            onFinished = null;
            callback?.Invoke(outcome);
        }

        private IEnumerator Wait(float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime * battleSpeed;
                yield return null;
            }
        }

        // =====================================================================
        //  Массовые навыки
        // =====================================================================

        /// <summary>Суперудар класса (срабатывает сам). Богатырь бьёт вместе с ним.</summary>
        public void TriggerSuper(Team team, UnitClass unitClass, float power)
        {
            switch (unitClass)
            {
                case UnitClass.Warrior: Earthquake(team, power); break;
                case UnitClass.Archer: FireRain(team, power); break;
                case UnitClass.Priest: Resurrection(team, power); break;
                case UnitClass.Bomber: PowderKeg(team, power); break;
                case UnitClass.Monk: Whirlwind(team, power); break;
                case UnitClass.Mage: FrostStorm(team, power); break;
            }
            HeroesJoin(team);
        }

        /// <summary>«Землетрясение»: каждый воин бьёт оземь — враги рядом (quakeRadius) получают урон и оглушены.</summary>
        private void Earthquake(Team team, float power)
        {
            List<BattleUnit> warriors = Ready(team, UnitClass.Warrior);
            if (warriors.Count == 0) return;
            foreach (BattleUnit warrior in warriors)
                BattleVfx.Ring(warrior.Position, cellSize * (quakeRadius + 0.5f) * 2f, new Color(0.75f, 0.55f, 0.3f, 0.4f), 0.6f);
            foreach (BattleUnit enemy in Alive(Opposite(team)))
            {
                BattleUnit striker = warriors.Where(w => Distance(w.Cell, enemy.Cell) <= quakeRadius).OrderByDescending(w => w.AttackDamage).FirstOrDefault();
                if (striker == null) continue;
                striker.DealDamage(enemy, striker.AttackDamage * quakePower * power, ignoreArmor: false);
                enemy.Stun(quakeStunTurns);
            }
            SkillBanner(warriors, "ЗЕМЛЕТРЯСЕНИЕ!", new Color(0.95f, 0.7f, 0.4f));
        }

        /// <summary>«Огненный дождь»: несколько волн — в каждой каждый лучник бьёт горящей стрелой случайного врага (щит не ловит).</summary>
        private void FireRain(Team team, float power)
        {
            List<BattleUnit> archers = Ready(team, UnitClass.Archer);
            for (int wave = 0; wave < fireRainWaves; wave++)
            {
                foreach (BattleUnit archer in archers)
                {
                    List<BattleUnit> enemies = Alive(Opposite(team));
                    if (enemies.Count == 0) break;
                    BattleUnit target = enemies[Random.Range(0, enemies.Count)];
                    Vector2 sky = target.Position + new Vector2(Random.Range(-0.6f, 0.6f), 2.2f + wave * 0.4f);
                    BattleVfx.Bolt(sky, target.Position, new Color(1f, 0.55f, 0.15f, 0.95f), 0.09f, 0.3f + wave * 0.12f);
                    archer.DealDamage(target, archer.AttackDamage * fireRainPower * power, ignoreArmor: false);
                }
            }
            if (archers.Count > 0) SkillBanner(archers, "ОГНЕННЫЙ ДОЖДЬ!", new Color(1f, 0.6f, 0.2f));
        }

        /// <summary>«Воскрешение»: павшие встают (по одному на жреца, сильнейшие первыми); павших нет — все лечатся на reviveHealShare.</summary>
        private void Resurrection(Team team, float power)
        {
            List<BattleUnit> priests = Ready(team, UnitClass.Priest);
            if (priests.Count == 0) return;
            List<BattleUnit> fallen = units.Where(u => u.IsDead && u.Team == team)
                .OrderByDescending(u => u.Stars * 100 + u.Level).Take(priests.Count).ToList();
            int raised = 0;
            foreach (BattleUnit dead in fallen)
            {
                Vector2Int? cell = FreeCellNear(dead.Cell, team);
                if (!cell.HasValue) break;
                occupancy[cell.Value.x, cell.Value.y] = dead;
                dead.Revive(cell.Value, Mathf.Clamp01(reviveHp * power));
                raised++;
            }
            if (raised == 0)
            {
                foreach (BattleUnit ally in Alive(team))
                {
                    ally.Heal(ally.MaxHp * Mathf.Clamp01(reviveHealShare * power));
                    BattleVfx.Ring(ally.Position, 0.45f, new Color(1f, 0.95f, 0.7f, 0.8f), 0.5f);
                }
            }
            SkillBanner(priests, raised > 0 ? "ВОСКРЕШЕНИЕ!" : "БЛАГОДАТЬ!", new Color(1f, 0.95f, 0.7f));
        }

        /// <summary>Свободная клетка поближе к from (на своей половине арены — у края своей стороны). null — нет места.</summary>
        private Vector2Int? FreeCellNear(Vector2Int from, Team team)
        {
            Vector2Int? best = null;
            int bestScore = int.MaxValue;
            for (int x = 0; x < columns; x++)
            {
                for (int y = 0; y < rows; y++)
                {
                    var cell = new Vector2Int(x, y);
                    if (!IsFree(cell)) continue;
                    int depth = team == Team.Player ? y : rows - 1 - y; // чем ближе к своему краю, тем безопаснее
                    int score = Distance(cell, from) * 4 + depth;
                    if (score < bestScore)
                    {
                        best = cell;
                        bestScore = score;
                    }
                }
            }
            return best;
        }

        /// <summary>«Пороховая бочка»: подрывники катят бочку в самую плотную кучу врагов — огромный взрыв.</summary>
        private void PowderKeg(Team team, float power)
        {
            List<BattleUnit> bombers = Ready(team, UnitClass.Bomber);
            List<BattleUnit> enemies = Alive(Opposite(team));
            if (bombers.Count == 0 || enemies.Count == 0) return;
            BattleUnit center = enemies.OrderByDescending(e => enemies.Count(o => Distance(o.Cell, e.Cell) <= powderRadius)).First();
            float damage = bombers.Sum(b => b.AttackDamage) * powderPower * power;
            BattleVfx.Ring(center.Position, cellSize * (powderRadius * 2 + 1), new Color(1f, 0.5f, 0.1f, 0.85f), 0.7f);
            BattleVfx.Ring(center.Position, cellSize * powderRadius, new Color(1f, 0.85f, 0.3f, 0.9f), 0.5f);
            foreach (BattleUnit bomber in bombers) BattleVfx.Bolt(bomber.Position, center.Position, new Color(0.95f, 0.55f, 0.15f, 0.9f), 0.1f, 0.3f);
            BattleUnit owner = bombers[0];
            foreach (BattleUnit victim in enemies.Where(e => Distance(e.Cell, center.Cell) <= powderRadius).ToList())
                owner.DealDamage(victim, damage, ignoreArmor: false);
            SkillBanner(bombers, "ПОРОХОВАЯ БОЧКА!", new Color(1f, 0.6f, 0.2f));
        }

        /// <summary>«Вихрь»: каждый монах (подходит к врагу, если рядом никого) бьёт огнём всех врагов вокруг себя по нескольку раз.</summary>
        private void Whirlwind(Team team, float power)
        {
            List<BattleUnit> monks = Ready(team, UnitClass.Monk);
            foreach (BattleUnit monk in monks)
            {
                if (!Alive(Opposite(team)).Any(e => Distance(e.Cell, monk.Cell) <= 1)) monk.MoveAction();
                BattleVfx.Ring(monk.Position, cellSize * 3f, new Color(1f, 0.45f, 0.15f, 0.8f), 0.45f);
                foreach (BattleUnit enemy in Alive(Opposite(team)).Where(e => Distance(e.Cell, monk.Cell) <= 1).ToList())
                {
                    for (int hit = 0; hit < whirlwindHits; hit++) monk.DealDamage(enemy, monk.AttackDamage * whirlwindPower * power, ignoreArmor: false);
                }
            }
            if (monks.Count > 0) SkillBanner(monks, "ВИХРЬ!", new Color(1f, 0.55f, 0.2f));
        }

        /// <summary>«Ледяная буря»: все враги получают урон сквозь броню и замерзают (пропускают ход).</summary>
        private void FrostStorm(Team team, float power)
        {
            List<BattleUnit> mages = Ready(team, UnitClass.Mage);
            if (mages.Count == 0) return;
            float damage = mages.Sum(m => m.AttackDamage) * frostPower * power;
            BattleUnit owner = mages[0];
            foreach (BattleUnit enemy in Alive(Opposite(team)))
            {
                BattleVfx.Ring(enemy.Position, 0.55f, new Color(0.6f, 0.9f, 1f, 0.9f), 0.6f);
                owner.DealDamage(enemy, damage, ignoreArmor: true);
                enemy.Stun(frostStunTurns);
            }
            SkillBanner(mages, "ЛЕДЯНАЯ БУРЯ!", new Color(0.6f, 0.9f, 1f));
        }

        public void TriggerClassSkill(Team team, UnitClass unitClass, float power)
        {
            switch (unitClass)
            {
                case UnitClass.Archer: Volley(team, power); break;
                case UnitClass.Priest: Prayer(team, power); break;
                case UnitClass.Warrior: Charge(team, power); break;
                case UnitClass.Bomber: Dynamite(team, power); break;
                case UnitClass.Monk: FireBlades(team, power); break;
                case UnitClass.Mage: ChainLightning(team, power); break;
            }
            HeroesJoin(team);
        }

        /// <summary>Богатырь бьёт вместе с любым навыком своей стороны (до цели не достаёт — шагает к ней).</summary>
        private void HeroesJoin(Team team)
        {
            foreach (BattleUnit hero in Ready(team, UnitClass.Hero))
            {
                if (hero.HasTargetInRange()) hero.AttackAction();
                else hero.MoveAction();
                if (hero.RegenShare > 0f) hero.Heal(hero.MaxHp * hero.RegenShare); // «живая вода» от жрецов в его линии
            }
        }

        /// <summary>«Динамит»: каждый подрывник бросает динамит туда, где больше всего врагов рядом друг с другом.</summary>
        private void Dynamite(Team team, float power)
        {
            List<BattleUnit> bombers = Ready(team, UnitClass.Bomber);
            int shots = 1 + (team == Team.Player && city != null ? Mathf.RoundToInt(city.ClassEffect(CityEffect.ExtraDynamite, UnitClass.Bomber)) : 0);
            var struck = new List<Vector2Int>();
            foreach (BattleUnit bomber in bombers)
            for (int shot = 0; shot < shots; shot++)
            {
                List<BattleUnit> enemies = Alive(Opposite(team));
                if (enemies.Count == 0) break;
                if (shot == 0) struck.Clear();
                // Вторая шашка («Связки динамита») — в другую кучу, не рядом с первой
                BattleUnit target = enemies
                    .Where(e => struck.All(c => Distance(c, e.Cell) > 1))
                    .OrderByDescending(e => enemies.Count(o => Distance(o.Cell, e.Cell) <= 1))
                    .ThenBy(e => Distance(e.Cell, bomber.Cell))
                    .FirstOrDefault() ?? enemies[Random.Range(0, enemies.Count)];
                struck.Add(target.Cell);
                BattleVfx.Bolt(bomber.Position, target.Position, new Color(0.95f, 0.55f, 0.15f, 0.9f), 0.07f, 0.25f);
                BattleVfx.Ring(target.Position, cellSize * 1.4f, new Color(1f, 0.55f, 0.15f, 0.75f), 0.45f);
                float damage = bomber.AttackDamage * dynamiteMultiplier * power;
                bool shock = team == Team.Player && city != null && city.ClassEffect(CityEffect.Shockwave, UnitClass.Bomber) > 0f;
                foreach (BattleUnit victim in enemies.Where(e => Distance(e.Cell, target.Cell) <= 1).ToList())
                {
                    bomber.DealDamage(victim, damage, ignoreArmor: false);
                    if (shock) victim.Stun(1);
                }
            }
            if (bombers.Count > 0) SkillBanner(bombers, "ДИНАМИТ!", new Color(1f, 0.6f, 0.2f));
        }

        /// <summary>«Огненные клинки»: оружие ближнего боя у всех своих горит — удары сильнее несколько шагов.</summary>
        private void FireBlades(Team team, float power)
        {
            List<BattleUnit> monks = Ready(team, UnitClass.Monk);
            if (monks.Count == 0) return;
            int until = CurrentStep + fireBladesSeconds;
            foreach (BattleUnit ally in Alive(team).Where(u => u.AttackRange <= 1 && !u.IsHealer))
            {
                ally.ApplyFireBlades(until, fireBladesBonus * power);
                BattleVfx.Ring(ally.Position, 0.4f, new Color(1f, 0.5f, 0.15f, 0.6f), 0.45f);
            }
            SkillBanner(monks, "ОГНЕННЫЕ КЛИНКИ!", new Color(1f, 0.55f, 0.2f));
        }

        /// <summary>«Цепная молния»: бьёт цель и перескакивает на ближайших врагов; щиты и броня не помогают.</summary>
        private void ChainLightning(Team team, float power)
        {
            List<BattleUnit> mages = Ready(team, UnitClass.Mage);
            foreach (BattleUnit mage in mages)
            {
                BattleUnit target = mage.CurrentTarget != null && !mage.CurrentTarget.IsDead ? mage.CurrentTarget : FindNearestEnemy(mage);
                if (target == null) continue;
                var hit = new HashSet<BattleUnit>();
                Vector2 from = mage.Position;
                float damage = mage.AttackDamage * lightningMultiplier * power;
                int jumps = lightningJumps + (team == Team.Player && city != null ? Mathf.RoundToInt(city.ClassEffect(CityEffect.ExtraJumps, UnitClass.Mage)) : 0);
                float stunChance = team == Team.Player && city != null ? city.ClassEffect(CityEffect.ChainStun, UnitClass.Mage) : 0f;
                for (int jump = 0; jump <= jumps && target != null; jump++)
                {
                    BattleVfx.Bolt(from, target.Position, new Color(0.6f, 0.85f, 1f, 0.95f), 0.08f, 0.3f);
                    hit.Add(target);
                    from = target.Position;
                    Vector2Int cell = target.Cell;
                    mage.DealDamage(target, damage, ignoreArmor: true);
                    if (stunChance > 0f && !target.IsDead && Random.value < stunChance) target.Stun(1);
                    damage *= lightningFalloff;
                    target = Alive(Opposite(team)).Where(e => !hit.Contains(e) && Distance(e.Cell, cell) <= 2)
                        .OrderBy(e => Distance(e.Cell, cell)).FirstOrDefault();
                }
            }
            if (mages.Count > 0) SkillBanner(mages, "МОЛНИЯ!", new Color(0.6f, 0.85f, 1f));
        }

        /// <summary>«Залп»: все лучники стороны разом стреляют по своим целям (дальность не важна).</summary>
        private void Volley(Team team, float power)
        {
            List<BattleUnit> archers = Ready(team, UnitClass.Archer);
            foreach (BattleUnit archer in archers)
            {
                BattleUnit target = archer.CurrentTarget != null && !archer.CurrentTarget.IsDead ? archer.CurrentTarget : FindNearestEnemy(archer);
                if (target == null) continue;
                BattleVfx.Bolt(archer.Position, target.Position, new Color(1f, 0.92f, 0.6f, 0.95f), 0.1f, 0.35f);
                archer.Attack(target, volleyMultiplier * power);
                float burn = team == Team.Player && city != null ? city.ClassEffect(CityEffect.FireArrows, UnitClass.Archer) : 0f;
                if (burn > 0f && !target.IsDead) target.Ignite(archer, archer.AttackDamage * burn, 3f);
            }
            if (archers.Count > 0) SkillBanner(archers, "ЗАЛП!", new Color(1f, 0.92f, 0.6f));
        }

        /// <summary>«Молитва»: жрецы лечат всех своих и благословляют их — меньше урона несколько шагов.</summary>
        private void Prayer(Team team, float power)
        {
            List<BattleUnit> monks = Ready(team, UnitClass.Priest);
            if (monks.Count == 0) return;
            float heal = monks.Max(m => m.HealPower) * prayerHealMultiplier * power;
            float reduction = Mathf.Clamp(blessingReduction * power, 0f, 0.6f);

            foreach (BattleUnit ally in Alive(team))
            {
                ally.Heal(heal);
                ally.ApplyBlessing(CurrentStep + blessingSeconds, reduction);
                BattleVfx.Ring(ally.Position, 0.4f, new Color(1f, 0.92f, 0.55f, 0.6f), 0.45f);
            }
            SkillBanner(monks, "МОЛИТВА!", new Color(1f, 0.92f, 0.55f));
        }

        /// <summary>
        /// «Рывок» / «Таран щитом»: каждый воин бросается к ближайшему врагу (до chargeRange клеток) и бьёт —
        /// чем дальше пролетел, тем сильнее удар; не достал — разгон копится до следующего удара.
        /// Со щитами удар — таран: цель оглушена и пропускает свой ход (у своих с ростовыми щитами — два).
        /// </summary>
        private void Charge(Team team, float power)
        {
            List<BattleUnit> warriors = Ready(team, UnitClass.Warrior);
            if (warriors.Count == 0) return;
            int shields = ShieldLevelOf(team);
            int stun = shields > 0 ? shieldBashStunTurns + (team == Team.Player && shields >= 2 ? 1 : 0) : 0;

            // Ближние к врагу бросаются первыми, чтобы не загораживать путь задним
            int DistanceToEnemy(BattleUnit warrior)
            {
                BattleUnit enemy = FindNearestEnemy(warrior);
                return enemy != null ? Distance(enemy.Cell, warrior.Cell) : int.MaxValue;
            }
            int range = chargeRange + (team == Team.Player && city != null ? Mathf.RoundToInt(city.ClassEffect(CityEffect.ChargeRange, UnitClass.Warrior)) : 0);
            foreach (BattleUnit warrior in warriors.OrderBy(DistanceToEnemy).ToList())
                warrior.Charge(power, range, chargeBonusPerCell, stun);
            SkillBanner(warriors, SkillTitle(team, UnitClass.Warrior).ToUpperInvariant() + "!", new Color(0.55f, 0.85f, 1f));
        }

        // =====================================================================
        //  Спавн армий
        // =====================================================================

        private void SpawnArmy(IReadOnlyList<BenchUnit> army, Team team)
        {
            if (army == null) return;
            List<BenchUnit> valid = army.Where(u => u != null && u.data != null).ToList();
            // Гарнизон и отряд вместе — шеренги шире (до всей арены)
            int perRow = Mathf.Clamp(Mathf.Max(unitsPerRow, Mathf.CeilToInt(valid.Count / 3f)), 1, columns);

            for (int i = 0; i < valid.Count; i++)
            {
                int rowIndex = i / perRow; // 0 — передняя шеренга
                if (rowIndex >= 3)
                {
                    Debug.LogWarning($"[Battle] Не хватило клеток для {valid[i].data.unitName}");
                    continue;
                }
                int inRow = Mathf.Min(perRow, valid.Count - rowIndex * perRow);
                int x = (columns - inRow) / 2 + i % perRow;
                int y = team == Team.Player ? 2 - rowIndex : rows - 3 + rowIndex;
                var cell = new Vector2Int(x, y);

                var go = new GameObject();
                go.transform.SetParent(unitsRoot, false);
                var unit = go.AddComponent<BattleUnit>();
                bool player = team == Team.Player;
                float multiplier = player ? (city != null ? city.ClassStatMultiplier(valid[i].data.unitClass) : 1f) : EnemyStatMultiplier;
                float healMultiplier = player ? (city != null ? city.PriestHealMultiplier : 1f) : EnemyStatMultiplier;
                bool crossbows = player && city != null && city.HasCrossbows;
                float shieldBonus = player && city != null ? city.ShieldBlockBonus : 0f;
                unit.Setup(this, valid[i], team, multiplier, healMultiplier, ShieldLevelOf(team), shieldBonus, crossbows, cell, Forward(team));
                if (player && city != null) unit.AddRange(Mathf.RoundToInt(city.ClassEffect(CityEffect.RangeBonus, valid[i].data.unitClass)));
                occupancy[x, y] = unit;
                units.Add(unit);
            }
        }

        /// <summary>
        /// Вожак стороны: всем, кого он ведёт (кроме него самого), +доля ХП и атаки; над ним — надпись «ВОЖАК».
        /// </summary>
        private void ApplyLeader(Team team, BenchUnit leader, IEnumerable<BenchUnit> led)
        {
            if (leader == null || led == null) return;
            BattleUnit chief = units.FirstOrDefault(u => u.Team == team && u.Source == leader);
            if (chief == null) return;
            if (!leaders.TryGetValue(team, out List<BattleUnit> chiefs)) leaders[team] = chiefs = new List<BattleUnit>();
            chiefs.Add(chief);
            var followers = new HashSet<BenchUnit>(led);
            float boost = team == Team.Player && city != null ? 1f + city.ClassEffect(CityEffect.LeaderBoost, leader.data.unitClass) : 1f;
            float hp = leader.LeadHp * boost, attack = leader.LeadAttack * boost;
            foreach (BattleUnit unit in units.Where(u => u.Team == team && u != chief && u.Source != null && followers.Contains(u.Source)))
                unit.ApplyLeadership(hp, attack);
            BattleVfx.FloatingText(chief.Position + Vector2.up * 0.75f, "ВОЖАК", team == Team.Player ? new Color(1f, 0.85f, 0.35f) : new Color(1f, 0.55f, 0.5f), 2.6f, 2.2f);
        }

        /// <summary>Вожаки стороны в этом бою (пусто — нет).</summary>
        public IReadOnlyList<BattleUnit> LeadersOf(Team team) =>
            leaders.TryGetValue(team, out List<BattleUnit> chiefs) ? chiefs : (IReadOnlyList<BattleUnit>)Array.Empty<BattleUnit>();

        /// <summary>Первый вожак стороны (null — нет).</summary>
        public BattleUnit LeaderOf(Team team) => LeadersOf(team).FirstOrDefault();

        /// <summary>
        /// Отряд врагов на ход day: с каждым ходом их больше, уровень выше, позже — ★★ и ★★★;
        /// с enemyHybridDay среди них подрывники и маги, с enemyHeroDay отряд ведёт вождь-богатырь.
        /// levelBonus — сильнее обычного (армии городов), sizeMultiplier — больше обычного.
        /// </summary>
        public List<BenchUnit> GenerateEnemies(int day, int levelBonus = 0, float sizeMultiplier = 1f)
        {
            var enemies = new List<BenchUnit>();
            List<UnitData> pool = enemyPool.FindAll(unit => unit != null);
            if (pool.Count == 0)
            {
                Debug.LogWarning("[Battle] Enemy Pool пуст — варваров не будет.");
                return enemies;
            }

            int count = Mathf.Clamp(Mathf.RoundToInt((baseEnemyCount + Mathf.FloorToInt((day - 1) * enemiesPerDay)) * Mathf.Max(0.1f, sizeMultiplier)),
                1, Mathf.Min(maxEnemies, unitsPerRow * 3));
            int baseLevel = Mathf.Clamp(1 + (day - 1) / enemyLevelEveryDays + levelBonus, 1, UnitData.MaxLevel);
            float twoStarChance = day >= enemyTwoStarDay ? Mathf.Min(enemyMaxTwoStarChance, enemyTwoStarChancePerDay * (day - enemyTwoStarDay + 1)) : 0f;
            float threeStarChance = day >= enemyThreeStarDay ? Mathf.Min(enemyMaxThreeStarChance, enemyThreeStarChancePerDay * (day - enemyThreeStarDay + 1)) : 0f;
            float hybridShare = day >= enemyHybridDay ? Mathf.Min(enemyMaxHybridShare, enemyHybridShare + enemyHybridSharePerDay * (day - enemyHybridDay)) : 0f;

            // Вождь-богатырь: один на волну, звёзд не набирает
            if (day >= enemyHeroDay) enemies.Add(new BenchUnit(EnemySpecial(UnitClass.Hero), 1, baseLevel));

            while (enemies.Count < count)
            {
                UnitData data = Random.value < hybridShare
                    ? EnemySpecial(Random.value < 0.5f ? UnitClass.Bomber : UnitClass.Mage)
                    : pool[Random.Range(0, pool.Count)];
                int level = Mathf.Min(UnitData.MaxLevel, baseLevel + (day > 1 && Random.value < enemyLevelUpChance ? 1 : 0));
                int stars = 1;
                if (Random.value < twoStarChance) stars = 2;
                if (Random.value < threeStarChance) stars = 3;
                enemies.Add(new BenchUnit(data, stars, level));
            }

            // Варвары тоже стоят строем: рубаки впереди, лучники сзади
            enemies.Sort((a, b) => FormationRank(a.data).CompareTo(FormationRank(b.data)));
            return enemies;
        }

        private static int FormationRank(UnitData data)
        {
            switch (data.unitClass)
            {
                case UnitClass.Hero:
                case UnitClass.Warrior: return 0;
                case UnitClass.None:
                case UnitClass.Monk: return 1;
                case UnitClass.Priest:
                case UnitClass.Bomber: return 2;
                default: return 3;
            }
        }

        private readonly Dictionary<UnitClass, UnitData> fallbackEnemySpecials = new Dictionary<UnitClass, UnitData>();

        /// <summary>Особый варвар (подрывник, маг, вождь): из списка enemySpecials, а нет его там — с числами по умолчанию.</summary>
        private UnitData EnemySpecial(UnitClass unitClass)
        {
            UnitData unit = enemySpecials.FirstOrDefault(u => u != null && u.unitClass == unitClass);
            if (unit != null) return unit;
            if (!fallbackEnemySpecials.TryGetValue(unitClass, out unit))
            {
                unit = UnitData.CreateDefault(unitClass);
                unit.unitName = EnemySpecialName(unitClass);
                unit.sellPrice = 0;
                fallbackEnemySpecials[unitClass] = unit;
            }
            return unit;
        }

        /// <summary>Имена особых варваров (их же берёт сборщик сцены).</summary>
        public static string EnemySpecialName(UnitClass unitClass)
        {
            switch (unitClass)
            {
                case UnitClass.Bomber: return "Варвар-подрывник";
                case UnitClass.Mage: return "Шаман";
                case UnitClass.Hero: return "Вождь";
                default: return "Варвар";
            }
        }

        /// <summary>«Тактика» видна: клетки арены синеют, пока она действует.</summary>
        private void SetTacticsLook(bool on)
        {
            if (on == tacticsShown) return;
            tacticsShown = on;
            foreach ((SpriteRenderer renderer, Color color) in arenaCells)
            {
                if (renderer != null) renderer.color = on ? Color.Lerp(color, tacticsTint, 0.35f) : color;
            }
        }

        /// <summary>Урон по бойцу — в итоги боя (Stats): наш от рядов поля или сам по себе, либо урон врага.</summary>
        public void RecordDamage(BattleUnit victim, float dealt)
        {
            if (dealt <= 0f || victim == null) return;
            if (victim.Team == Team.Player) Stats.EnemyDamage += dealt;
            else if (boardAction) Stats.BoardDamage += dealt;
            else Stats.SelfDamage += dealt;
        }

        private void EnsureArena()
        {
            if (unitsRoot == null)
            {
                unitsRoot = new GameObject("Units").transform;
                unitsRoot.SetParent(transform, false);
            }
            if (arenaRoot != null && arenaRoot.childCount == columns * rows) return;
            if (arenaRoot != null) Destroy(arenaRoot.gameObject);

            arenaRoot = new GameObject("ArenaCells").transform;
            arenaRoot.SetParent(transform, false);
            arenaCells.Clear();
            tacticsShown = false;
            for (int x = 0; x < columns; x++)
            {
                for (int y = 0; y < rows; y++)
                {
                    var go = new GameObject($"Cell_{x}_{y}");
                    go.transform.SetParent(arenaRoot, false);
                    go.transform.position = CellToWorld(new Vector2Int(x, y));
                    go.transform.localScale = new Vector3(cellSize * 0.96f, cellSize * 0.96f, 1f);
                    var renderer = go.AddComponent<SpriteRenderer>();
                    renderer.sprite = GameVisuals.Square;
                    Color color = (x + y) % 2 == 0 ? cellColorA : cellColorB;
                    if (y <= 2) color = Color.Lerp(color, GameVisuals.PlayerColor, 0.08f);         // зона игрока
                    else if (y >= rows - 3) color = Color.Lerp(color, GameVisuals.EnemyColor, 0.08f); // зона врага
                    renderer.color = color;
                    renderer.sortingOrder = -30000;
                    arenaCells.Add((renderer, color));
                }
            }
        }

        private void Update()
        {
            if (IsRunning && fighting)
            {
                // Поле, лимит боя и наши суперудары — по обычным часам; арена — с темпом и «Тактикой»
                float boardDt = Time.deltaTime * battleSpeed;
                boardTime += boardDt;
                Stats.BoardSeconds = boardTime;
                float slow = 1f;
                if (tacticsLeft > 0f)
                {
                    slow = tacticsSlow;
                    Stats.SlowSeconds += Mathf.Min(boardDt, tacticsLeft);
                    tacticsLeft = Mathf.Max(0f, tacticsLeft - boardDt);
                }
                SetTacticsLook(tacticsLeft > 0f);
                TickBattle(boardDt * arenaTempo * slow);
            }
            if (IsRunning)
            {
                stepTextTimer -= Time.deltaTime;
                if (stepTextTimer <= 0f)
                {
                    stepTextTimer = 0.25f;
                    UpdateStepText();
                }
            }
            if (rosterLabels == null || rosterLabels.Length == 0) return;
            rosterTimer -= Time.deltaTime;
            if (rosterTimer > 0f) return;
            rosterTimer = 0.15f;
            UpdateRoster();
        }

        /// <summary>
        /// Легенда поля боя значками: что даёт ряд ⚔ и ➜, навыки классов этой армии и их суперудары (5 в ряд),
        /// открытые особые символы, каскад.
        /// </summary>
        private void UpdateLegend()
        {
            if (legendText == null) return;
            const string gold = "<color=#ffd166>";
            const string end = "</color>";
            const string muted = "<color=#8a8f9c>";
            var lines = new List<string>
            {
                $"{gold}3 одинаковых в ряд{end} <size=80%>(4 — ×1.5, 5 — ×2)</size>",
                $"<size=85%>{muted}круглые — действия, квадратные — бойцы; выберите фишку — запульсируют те, с кем она сложится{end}</size>",
                $"{GameVisuals.IconAttack} натиск: кто достаёт — бьют",
                $"{GameVisuals.IconTurns} тактика: бой медленнее, поле — нет",
                string.Empty,
            };
            List<UnitClass> classes = boardClasses.Where(c => SkillEffect(c).Length > 0).OrderBy(c => (int)c).ToList();
            foreach (UnitClass unitClass in classes)
            {
                lines.Add($"{GameVisuals.ClassIcon(unitClass)} {gold}{SkillTitle(Team.Player, unitClass)}{end} {SkillEffect(unitClass)}");
                if (Array.IndexOf(AbilityClasses, unitClass) < 0) continue;
                int superLength = SuperLength(unitClass);
                lines.Add(SuperUnlocked(Team.Player, unitClass)
                    ? $"<size=85%>  {superLength} в ряд: {gold}{SuperName(unitClass)}{end} {SuperEffect(unitClass)}</size>"
                    : $"<size=85%>{muted}  {superLength} в ряд: суперудар — приказ в гильдии{end}</size>");
            }
            List<UnitClass> offBoard = OffBoardClasses();
            if (offBoard.Count > 0)
                lines.Add($"<size=85%>{string.Concat(offBoard.Select(GameVisuals.ClassIcon))} не на поле — навык сами раз в {offBoardSkillInterval:0} с</size>");
            if (CountAlive(Team.Player, UnitClass.Hero) > 0) lines.Add($"{GameVisuals.ClassIcon(UnitClass.Hero)} бьёт с любым навыком");
            foreach (BattleUnit chief in LeadersOf(Team.Player))
            {
                if (chief.Source != null) lines.Add($"{gold}Вожак {chief.Source.Name}{end}: своей линейке {chief.Source.LeadText}");
            }

            lines.Add(string.Empty);
            List<BattleSymbol> symbols = board != null ? board.Symbols.ToList() : new List<BattleSymbol>();
            if (symbols.Contains(BattleSymbol.Banner)) lines.Add($"{GameVisuals.IconBanner} джокер — в любой ряд");
            if (symbols.Contains(BattleSymbol.Multiplier) && city != null) lines.Add($"{gold}×{city.MultiplierValue}{end} джокер, ряд ×{city.MultiplierValue}");
            if (symbols.Contains(BattleSymbol.Relic) && city != null)
            {
                int heal = Mathf.RoundToInt(miracleHealShare * 100f * city.MiracleMultiplier);
                lines.Add($"{GameVisuals.IconCandle}×3 = {gold}Чудо{end} +{heal}%{GameVisuals.IconHp}");
            }
            lines.Add($"каскад — каждая волна +{Mathf.RoundToInt(cascadeBonus * 100f)}%");
            lines.Add($"<size=85%><color=#ff8a80>Враг бьёт навыками сам</color></size>");
            legendText.text = string.Join("\n", lines);
        }

        /// <summary>Что делает навык класса — коротко, значками и цифрами.</summary>
        private string SkillEffect(UnitClass unitClass)
        {
            switch (unitClass)
            {
                case UnitClass.Warrior:
                    return $"{GameVisuals.IconMove}{chargeRange} +{Mathf.RoundToInt(chargeBonusPerCell * 100f)}%/кл." +
                           (ShieldLevelOf(Team.Player) > 0 ? ", оглушает" : string.Empty);
                case UnitClass.Archer: return $"все {GameVisuals.IconAttack}×{volleyMultiplier:0.#}";
                case UnitClass.Priest: return $"+{GameVisuals.IconHp} всем, −{Mathf.RoundToInt(blessingReduction * 100f)}% урона {blessingSeconds}с";
                case UnitClass.Bomber: return "по толпе";
                case UnitClass.Monk: return $"{GameVisuals.IconAttack}+{Mathf.RoundToInt(fireBladesBonus * 100f)}% вблизи {fireBladesSeconds}с";
                case UnitClass.Mage: return $"{lightningJumps + 1} цели сквозь броню";
                default: return string.Empty;
            }
        }

        /// <summary>Панель внизу: наши юниты в порядке строя — имя, звёзды, уровень, атака и ХП сейчас.</summary>
        private void UpdateRoster()
        {
            int index = 0;
            IReadOnlyList<BattleUnit> chiefs = LeadersOf(Team.Player);
            foreach (BattleUnit unit in units)
            {
                if (unit == null || unit.Team != Team.Player) continue;
                if (index >= rosterLabels.Length) break;
                if (rosterLabels[index] != null) rosterLabels[index].text = RosterEntry(unit, chiefs.Contains(unit));
                index++;
            }
            for (; index < rosterLabels.Length; index++)
            {
                if (rosterLabels[index] != null) rosterLabels[index].text = string.Empty;
            }
        }

        /// <summary>
        /// Строк в панели бойцов меньше, чем наших на арене (гарнизон + отряд) — в каждой колонке-шеренге
        /// добавляются строки (копии последней), колонки заполняются по очереди.
        /// </summary>
        private void EnsureRosterCapacity(int count)
        {
            if (rosterLabels == null || rosterLabels.Length == 0 || rosterLabels.Length >= count) return;
            List<Transform> ranks = rosterLabels.Where(l => l != null).Select(l => l.transform.parent).Distinct().ToList();
            if (ranks.Count == 0) return;
            int perRank = Mathf.CeilToInt(count / (float)ranks.Count);
            var all = new List<TMP_Text>();
            foreach (Transform rank in ranks)
            {
                List<TMP_Text> labels = rosterLabels.Where(l => l != null && l.transform.parent == rank).ToList();
                while (labels.Count < perRank)
                {
                    TMP_Text copy = Instantiate(labels[labels.Count - 1], rank);
                    copy.text = string.Empty;
                    labels.Add(copy);
                }
                all.AddRange(labels);
            }
            rosterLabels = all.ToArray();
        }

        /// <summary>Строка юнита; вожак — золотым именем.</summary>
        private static string RosterEntry(BattleUnit unit, bool leader)
        {
            string stars = unit.Stars > 1 ? " " + GameVisuals.Stars(unit.Stars) : string.Empty;
            string title = unit.Source != null ? unit.Source.Name : unit.Data.unitName;
            if (leader && !unit.IsDead) title = $"<color=#ffd166>{title}</color>";
            string name = $"{GameVisuals.ClassIcon(unit.Data.unitClass)} {title}{stars} <size=75%>ур.{unit.Level}</size>";
            if (unit.IsDead) return $"<color=#7a7f8c>{name} {GameVisuals.IconDead}</color>";
            float share = unit.MaxHp > 0f ? unit.Hp / unit.MaxHp : 0f;
            string hpColor = ColorUtility.ToHtmlStringRGB(Color.Lerp(new Color(0.95f, 0.35f, 0.3f), new Color(0.45f, 0.95f, 0.5f), share));
            string state = unit.IsStunned ? " <color=#ffe066>оглушён</color>" : string.Empty;
            return $"{name}  {GameVisuals.IconAttack}<color=#ffd166>{Mathf.RoundToInt(unit.AttackDamage)}</color>  " +
                   $"{GameVisuals.IconHp}<color=#{hpColor}>{Mathf.CeilToInt(unit.Hp)}/{Mathf.RoundToInt(unit.MaxHp)}</color>{state}";
        }

        private void ClearUnits()
        {
            foreach (BattleUnit unit in units)
            {
                if (unit != null) Destroy(unit.gameObject);
            }
            units.Clear();
        }

        // =====================================================================
        //  Сетка: клетки, путь, направления
        // =====================================================================

        public Vector3 CellToWorld(Vector2Int cell)
        {
            return transform.position + new Vector3((cell.x - (columns - 1) * 0.5f) * cellSize, (cell.y - (rows - 1) * 0.5f) * cellSize, 0f);
        }

        public bool InBounds(Vector2Int cell) => cell.x >= 0 && cell.y >= 0 && cell.x < columns && cell.y < rows;
        public bool IsFree(Vector2Int cell) => InBounds(cell) && occupancy != null && occupancy[cell.x, cell.y] == null;
        public BattleUnit UnitAt(Vector2Int cell) => InBounds(cell) && occupancy != null ? occupancy[cell.x, cell.y] : null;

        /// <summary>Переместить юнита в свободную клетку (единственный способ менять занятость).</summary>
        public void Relocate(BattleUnit unit, Vector2Int to, bool fast = false)
        {
            if (!IsFree(to)) return;
            if (occupancy[unit.Cell.x, unit.Cell.y] == unit) occupancy[unit.Cell.x, unit.Cell.y] = null;
            occupancy[to.x, to.y] = unit;
            unit.SetCell(to, fast);
        }

        public void OnUnitDied(BattleUnit unit)
        {
            if (occupancy != null && InBounds(unit.Cell) && occupancy[unit.Cell.x, unit.Cell.y] == unit)
                occupancy[unit.Cell.x, unit.Cell.y] = null;
            if (unit.Team != Team.Player || !IsRunning || !fighting || city == null) return;
            // «Святые мощи»: погиб наш боец — на поле боя падает Свеча
            int candles = Mathf.RoundToInt(city.Effect(CityEffect.Martyrs));
            if (candles > 0 && board != null) board.Inject(BattleSymbol.Relic, candles);
            // «Пороховые бочки»: погибая, подрывник взрывается
            float blast = city.ClassEffect(CityEffect.DeathBlast, unit.Data.unitClass);
            if (blast > 0f)
            {
                BattleVfx.Ring(unit.Position, cellSize * 2.6f, new Color(1f, 0.55f, 0.15f, 0.85f), 0.5f);
                BattleVfx.FloatingText(unit.Position + Vector2.up * 0.5f, "ВЗРЫВ!", new Color(1f, 0.6f, 0.2f), 3.2f);
                foreach (BattleUnit enemy in units.Where(u => !u.IsDead && u.Team != unit.Team && Distance(u.Cell, unit.Cell) <= 1).ToList())
                    unit.DealDamage(enemy, unit.AttackDamage * blast, ignoreArmor: false);
            }
            // «Обители»: павший рядом с жрецом раз за бой встаёт
            if (!unit.SecondWindUsed)
            {
                BattleUnit priest = units.FirstOrDefault(u => !u.IsDead && !u.IsStunned && u.Team == unit.Team && Distance(u.Cell, unit.Cell) <= 2
                                                            && city.ClassEffect(CityEffect.SecondWind, u.Data.unitClass) > 0f);
                if (priest != null)
                {
                    unit.SecondWindUsed = true;
                    StartCoroutine(SecondWindRoutine(unit, priest, city.ClassEffect(CityEffect.SecondWind, priest.Data.unitClass)));
                }
            }
        }

        /// <summary>«Обители»: павший встаёт рядом через полсекунды (если бой идёт и есть место).</summary>
        private IEnumerator SecondWindRoutine(BattleUnit unit, BattleUnit priest, float share)
        {
            yield return Wait(0.6f);
            if (!IsRunning || !unit.IsDead) yield break;
            Vector2Int? cell = IsFree(unit.Cell) ? unit.Cell : FreeCellNear(unit.Cell, unit.Team);
            if (!cell.HasValue) yield break;
            occupancy[cell.Value.x, cell.Value.y] = unit;
            unit.Revive(cell.Value, share);
            if (priest != null && !priest.IsDead) BattleVfx.Bolt(priest.Position, unit.Position, new Color(1f, 0.95f, 0.7f, 0.8f), 0.07f, 0.35f);
        }

        /// <summary>Кратчайший путь по свободным клеткам (8 направлений) до любой клетки, где isGoal. null — пути нет.</summary>
        public List<Vector2Int> FindPath(Vector2Int start, Predicate<Vector2Int> isGoal)
        {
            return FindPath(columns, rows, IsFree, start, isGoal);
        }

        public static List<Vector2Int> FindPath(int width, int height, Func<Vector2Int, bool> isFree, Vector2Int start, Predicate<Vector2Int> isGoal)
        {
            var cameFrom = new Dictionary<Vector2Int, Vector2Int> { [start] = start };
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                Vector2Int current = queue.Dequeue();
                foreach (Vector2Int direction in Directions8)
                {
                    Vector2Int next = current + direction;
                    if (next.x < 0 || next.y < 0 || next.x >= width || next.y >= height) continue;
                    if (cameFrom.ContainsKey(next) || !isFree(next)) continue;
                    cameFrom[next] = current;
                    if (isGoal(next))
                    {
                        var path = new List<Vector2Int>();
                        for (Vector2Int cell = next; cell != start; cell = cameFrom[cell]) path.Add(cell);
                        path.Reverse();
                        return path;
                    }
                    queue.Enqueue(next);
                }
            }
            return null;
        }

        /// <summary>
        /// Прыжок (flanker — задел под будущих убийц): любая свободная клетка в пределах MoveSpeed — даже через занятые.
        /// Выбирается та, откуда ближе всего (по свободным клеткам) до цели прыжка.
        /// </summary>
        public Vector2Int? BestLeapCell(BattleUnit unit, Predicate<Vector2Int> goal, BattleUnit target)
        {
            int[,] distance = DistanceField(goal);
            int currentBest = int.MaxValue;
            foreach (Vector2Int neighbor in Neighbors(unit.Cell))
            {
                if (IsFree(neighbor) && distance[neighbor.x, neighbor.y] < int.MaxValue)
                    currentBest = Mathf.Min(currentBest, distance[neighbor.x, neighbor.y] + 1);
            }

            Vector2Int? best = null;
            int bestDistance = int.MaxValue;
            int bestToTarget = int.MaxValue;
            int reach = unit.MoveSpeed;
            for (int dx = -reach; dx <= reach; dx++)
            {
                for (int dy = -reach; dy <= reach; dy++)
                {
                    var cell = new Vector2Int(unit.Cell.x + dx, unit.Cell.y + dy);
                    if (cell == unit.Cell || !IsFree(cell)) continue;
                    int d = distance[cell.x, cell.y];
                    int toTarget = Distance(cell, target.Cell);
                    if (d < bestDistance || (d == bestDistance && toTarget < bestToTarget))
                    {
                        best = cell;
                        bestDistance = d;
                        bestToTarget = toTarget;
                    }
                }
            }

            if (best.HasValue && bestDistance < int.MaxValue && bestDistance < currentBest) return best;
            return GreedyStep(unit, target.Cell, reach);
        }

        /// <summary>Расстояние (в шагах по свободным клеткам) от каждой клетки до ближайшей цели goal.</summary>
        private int[,] DistanceField(Predicate<Vector2Int> goal)
        {
            var distance = new int[columns, rows];
            var queue = new Queue<Vector2Int>();
            for (int x = 0; x < columns; x++)
            {
                for (int y = 0; y < rows; y++)
                {
                    var cell = new Vector2Int(x, y);
                    if (IsFree(cell) && goal(cell))
                    {
                        distance[x, y] = 0;
                        queue.Enqueue(cell);
                    }
                    else
                    {
                        distance[x, y] = int.MaxValue;
                    }
                }
            }
            while (queue.Count > 0)
            {
                Vector2Int current = queue.Dequeue();
                foreach (Vector2Int next in Neighbors(current))
                {
                    if (!IsFree(next) || distance[next.x, next.y] != int.MaxValue) continue;
                    distance[next.x, next.y] = distance[current.x, current.y] + 1;
                    queue.Enqueue(next);
                }
            }
            return distance;
        }

        /// <summary>Шаг «в лоб»: свободная клетка в пределах reach, которая ближе всего к цели.</summary>
        public Vector2Int? GreedyStep(BattleUnit unit, Vector2Int target, int reach = 1)
        {
            Vector2Int? best = null;
            int bestDistance = Distance(unit.Cell, target);
            for (int dx = -reach; dx <= reach; dx++)
            {
                for (int dy = -reach; dy <= reach; dy++)
                {
                    var cell = new Vector2Int(unit.Cell.x + dx, unit.Cell.y + dy);
                    if (!IsFree(cell)) continue;
                    int d = Distance(cell, target);
                    if (d < bestDistance)
                    {
                        best = cell;
                        bestDistance = d;
                    }
                }
            }
            return best;
        }

        public bool HasFreeBackCell(BattleUnit target)
        {
            foreach (Vector2Int cell in Neighbors(target.Cell))
            {
                if (IsFree(cell) && IsBackCell(target, cell)) return true;
            }
            return false;
        }

        /// <summary>Расстояние в ходах (по 8 направлениям).</summary>
        public static int Distance(Vector2Int a, Vector2Int b) => Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));

        /// <summary>Направление шага из from в to: компоненты -1..1.</summary>
        public static Vector2Int DirectionBetween(Vector2Int from, Vector2Int to)
        {
            return new Vector2Int(Math.Sign(to.x - from.x), Math.Sign(to.y - from.y));
        }

        public static IEnumerable<Vector2Int> Neighbors(Vector2Int cell)
        {
            foreach (Vector2Int direction in Directions8) yield return cell + direction;
        }

        /// <summary>С какой стороны от взгляда defender находится клетка attackerCell.</summary>
        public static HitSide SideOf(BattleUnit defender, Vector2Int attackerCell)
        {
            return SideOf(defender.Cell, defender.Facing, attackerCell);
        }

        public static HitSide SideOf(Vector2Int defenderCell, Vector2Int facing, Vector2Int attackerCell)
        {
            Vector2 toAttacker = attackerCell - defenderCell;
            Vector2 look = facing;
            if (toAttacker == Vector2.zero || look == Vector2.zero) return HitSide.Front;
            float dot = Vector2.Dot(toAttacker.normalized, look.normalized);
            if (dot >= 0.6f) return HitSide.Front; // лицо и 45° от него
            if (dot <= -0.6f) return HitSide.Back; // спина и 45° от неё
            return HitSide.Side;
        }

        /// <summary>Соседняя с целью клетка у неё за спиной (3 клетки сзади).</summary>
        public static bool IsBackCell(BattleUnit target, Vector2Int cell)
        {
            return Distance(cell, target.Cell) == 1 && SideOf(target, cell) == HitSide.Back;
        }

        // =====================================================================
        //  Запросы
        // =====================================================================

        public int CountAlive(Team team) => units.Count(u => !u.IsDead && u.Team == team);
        public int CountAlive(Team team, UnitClass unitClass) => units.Count(u => !u.IsDead && u.Team == team && u.Data.unitClass == unitClass);

        private List<BattleUnit> Alive(Team team) => units.Where(u => !u.IsDead && u.Team == team).ToList();
        private List<BattleUnit> Alive(Team team, UnitClass unitClass) => units.Where(u => !u.IsDead && u.Team == team && u.Data.unitClass == unitClass).ToList();
        /// <summary>Живые и не оглушённые юниты класса — те, кто может применить навык.</summary>
        private List<BattleUnit> Ready(Team team, UnitClass unitClass) => units.Where(u => !u.IsDead && !u.IsStunned && u.Team == team && u.Data.unitClass == unitClass).ToList();

        private IEnumerable<UnitClass> AliveClasses(Team team) => units.Where(u => !u.IsDead && u.Team == team).Select(u => u.Data.unitClass).Distinct();

        /// <summary>Ближайший враг (при равенстве — самый раненый).</summary>
        public BattleUnit FindNearestEnemy(BattleUnit seeker)
        {
            return units.Where(u => !u.IsDead && u.Team != seeker.Team)
                .OrderBy(u => Distance(u.Cell, seeker.Cell)).ThenBy(u => u.Hp).FirstOrDefault();
        }

        /// <summary>Самый раненый враг (при равенстве — ближайший): добыча тех, кто заходит в спину.</summary>
        public BattleUnit FindWeakestEnemy(BattleUnit seeker)
        {
            return units.Where(u => !u.IsDead && u.Team != seeker.Team)
                .OrderBy(u => u.Hp).ThenBy(u => Distance(u.Cell, seeker.Cell)).FirstOrDefault();
        }

        /// <summary>Самый раненый союзник целителя (доля ХП ниже порога), при равенстве — ближайший.</summary>
        public BattleUnit FindMostWounded(BattleUnit healer)
        {
            return units.Where(u => u != healer && u.Team == healer.Team && u.IsWounded)
                .OrderBy(u => u.Hp / u.MaxHp).ThenBy(u => Distance(u.Cell, healer.Cell)).FirstOrDefault();
        }

        private float TotalHp(Team team) => units.Where(u => !u.IsDead && u.Team == team).Sum(u => u.Hp);

        private static Vector2 Centroid(List<BattleUnit> list)
        {
            Vector2 sum = Vector2.zero;
            foreach (BattleUnit unit in list) sum += unit.Position;
            return list.Count > 0 ? sum / list.Count : Vector2.zero;
        }

        /// <summary>Крупная надпись навыка над его юнитами. Несколько навыков за один ход не налезают друг на друга.</summary>
        private void SkillBanner(List<BattleUnit> casters, string text, Color color)
        {
            if (bannerFrame != Time.frameCount)
            {
                bannerFrame = Time.frameCount;
                bannersThisFrame.Clear();
            }
            Vector2 position = Centroid(casters) + Vector2.up * 0.65f;
            while (bannersThisFrame.Any(b => Mathf.Abs(b.y - position.y) < 0.6f && Mathf.Abs(b.x - position.x) < 2.6f))
                position.y += 0.6f;
            bannersThisFrame.Add(position);
            BattleVfx.FloatingText(position, text, color, 4.2f);
        }

        private void UpdateStepText()
        {
            if (stepText == null) return;
            if (!fighting)
            {
                stepText.text = IsRunning ? "Армии строятся..." : string.Empty;
                return;
            }
            float left = Mathf.Max(0f, battleDuration - boardTime);
            string hint = tacticsLeft > 0f
                ? $"<color=#a9b8ff>Тактика {Mathf.CeilToInt(tacticsLeft)} с</color>"
                : "<color=#8fc1ff>Собирайте ряды!</color>";
            stepText.text = $"{GameVisuals.IconTurns}{Mathf.FloorToInt(left / 60f)}:{Mathf.FloorToInt(left % 60f):00}\n{hint}";
        }

        private void UpdateSpeedLabel()
        {
            if (speedButtonLabel != null) speedButtonLabel.text = $"{GameVisuals.IconSpeed}×{Mathf.RoundToInt(battleSpeed)}";
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(transform.position, new Vector3(columns * cellSize, rows * cellSize, 0f));
            Gizmos.color = new Color(1f, 1f, 0f, 0.25f);
            for (int x = 1; x < columns; x++)
            {
                float wx = transform.position.x + (x - columns * 0.5f) * cellSize;
                Gizmos.DrawLine(new Vector3(wx, transform.position.y - rows * cellSize * 0.5f), new Vector3(wx, transform.position.y + rows * cellSize * 0.5f));
            }
            for (int y = 1; y < rows; y++)
            {
                float wy = transform.position.y + (y - rows * 0.5f) * cellSize;
                Gizmos.DrawLine(new Vector3(transform.position.x - columns * cellSize * 0.5f, wy), new Vector3(transform.position.x + columns * cellSize * 0.5f, wy));
            }
        }
    }
}
