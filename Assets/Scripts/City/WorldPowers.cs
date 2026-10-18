using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Casiwar
{
    /// <summary>Кто живёт на карте кроме нас.</summary>
    public enum PowerKind
    {
        BarbarianCamp, // лагерь варваров: одна клетка, выпускает отряды
        City,          // чужой город: торгует с нами караванами или шлёт армии; его стража бьёт варваров
    }

    /// <summary>Лагерь варваров или чужой город.</summary>
    public sealed class Power
    {
        public int Id;
        public PowerKind Kind;
        public string Name;
        public Vector2Int Cell;
        /// <summary>Город враждебный (шлёт армии, его стража нападает на нашу армию) или торговый. Лагерь — всегда враг.</summary>
        public bool Hostile;
        public int AppearedTurn;
        /// <summary>Лагерь — когда выйдет следующий отряд; город — когда выйдет новая стража.</summary>
        public int NextSquadTurn;
        /// <summary>Враждебный город: когда соберёт армию на нас.</summary>
        public int NextArmyTurn;
        /// <summary>Город разграблен варварами до этого хода: караваны везут вдвое меньше, армий он не собирает.</summary>
        public int PlunderedUntil;

        public bool IsCamp => Kind == PowerKind.BarbarianCamp;
        public bool IsCity => Kind == PowerKind.City;
        public bool CanTrade => IsCity && !Hostile;
        public bool IsPlundered(int turn) => turn < PlunderedUntil;
        public string Title => IsCamp ? $"Лагерь «{Name}»" : $"Город {Name}";
    }

    public enum SquadKind
    {
        Raiders, // отряд варваров из лагеря
        Army,    // армия враждебного города — идёт на нас
        Caravan, // наш караван в торговый город
        Guard,   // стража чужого города: ходит дозором вокруг него и бьёт варваров
    }

    /// <summary>Что отряд сейчас делает.</summary>
    public enum SquadMission
    {
        Patrol, // бродит вокруг дома
        Raid,   // идёт грабить город — наш (RaidTarget == null) или чужой
        Chase,  // гонится за отрядом или за нашей армией
        Return, // возвращается домой после грабежа (и там распускается)
    }

    /// <summary>Отряд на карте: идёт по клеткам несколько клеток за ход.</summary>
    public sealed class Squad
    {
        public int Id;
        public SquadKind Kind;
        /// <summary>Откуда (лагерь или город); у каравана — куда.</summary>
        public Power Home;
        /// <summary>Вокруг какой клетки ходит дозором (лагерь, город; лагерь сожгли — там, где он стоял).</summary>
        public Vector2Int Anchor;
        public Vector2Int Cell;
        public int Speed;
        public readonly List<BenchUnit> Units = new List<BenchUnit>();
        /// <summary>Каравану: сколько золота привезёт.</summary>
        public int Gold;
        public SquadMission Mission;
        /// <summary>Набег: на какой чужой город (null — на наш).</summary>
        public Power RaidTarget;
        /// <summary>Погоня: за каким отрядом.</summary>
        public Squad ChaseTarget;
        /// <summary>Погоня за нашим отрядом (null — не за ним).</summary>
        public PlayerArmy ChaseArmy;
        public bool ChasingArmy => ChaseArmy != null;
        /// <summary>Дозор: куда идёт сейчас.</summary>
        public Vector2Int? Waypoint;
        /// <summary>Отряд стоит в наших границах: в конце хода — бой (или грабёж, если армии в городе нет).</summary>
        public bool Attacking;
        /// <summary>Оставшийся путь до цели (для пунктира на карте и «через N ходов»).</summary>
        public readonly List<Vector2Int> Path = new List<Vector2Int>();

        public bool IsCaravan => Kind == SquadKind.Caravan;
        public bool IsBarbarian => Kind == SquadKind.Raiders;
        /// <summary>Враг нам: варвары, армии враждебных городов и их стража.</summary>
        public bool IsHostile => Kind == SquadKind.Raiders || Kind == SquadKind.Army || (Kind == SquadKind.Guard && Home != null && Home.Hostile);
        /// <summary>Идёт на наш город.</summary>
        public bool RaidsUs => IsHostile && Mission == SquadMission.Raid && RaidTarget == null;

        public string Title
        {
            get
            {
                switch (Kind)
                {
                    case SquadKind.Army: return $"Армия города {Home?.Name}";
                    case SquadKind.Caravan: return $"Караван в {Home?.Name}";
                    case SquadKind.Guard: return $"Стража города {Home?.Name}";
                    default: return Home != null ? $"Варвары «{Home.Name}»" : "Варвары";
                }
            }
        }
    }

    /// <summary>
    /// Наш отряд на карте — как герой в «Героях»: своя линейка бойцов (вожак + места, ArmyRow), клетка, шаги и приказ.
    /// Отрядов может быть несколько (WorldPowers.maxSquads); пустой отряд стоит на ратуше — его собирают из гарнизона.
    /// </summary>
    public sealed class PlayerArmy
    {
        public int Id;
        /// <summary>Номер для подписей: «Отряд 2».</summary>
        public int Number;
        public readonly ArmyRow Row;
        public Vector2Int Cell;
        /// <summary>Оставшийся путь приказа (отряд пойдёт по нему и в следующие ходы).</summary>
        public readonly List<Vector2Int> Path = new List<Vector2Int>();
        public int MovesLeft;
        /// <summary>Уже нападал в этот ход (нападать можно раз в ход).</summary>
        public bool AttackedThisTurn;
        /// <summary>Приказ: напасть на отряд, на лагерь, встретить свой отряд или просто дойти до клетки.</summary>
        public Squad TargetSquad;
        public Power TargetCamp;
        public PlayerArmy TargetArmy;
        public Vector2Int? Destination;

        public PlayerArmy(int slots) => Row = new ArmyRow(slots);

        public bool IsEmpty => Row.IsEmpty;
        public BenchUnit Leader => Row.Leader;
        public string Title => $"Отряд {Number}";
        public bool HasOrder => Destination.HasValue || TargetSquad != null || TargetCamp != null || TargetArmy != null;
    }

    /// <summary>Бой в поле: наша армия против отрядов на карте (или против лагеря с его гарнизоном).</summary>
    public sealed class FieldBattle
    {
        public readonly List<Squad> Squads = new List<Squad>();
        /// <summary>Штурм лагеря: победа — лагерь сожжён.</summary>
        public Power Camp;
        /// <summary>Гарнизон лагеря (бьётся вместе с отрядами, что стоят в лагере).</summary>
        public readonly List<BenchUnit> Garrison = new List<BenchUnit>();
        /// <summary>Напали на нас (а не мы).</summary>
        public bool Ambush;
        /// <summary>Какой наш отряд бьётся.</summary>
        public PlayerArmy Army;

        public List<BenchUnit> Enemies => Squads.SelectMany(s => s.Units).Concat(Garrison).ToList();

        public string Title => Camp != null
            ? $"Штурм лагеря «{Camp.Name}»"
            : string.Join(", ", Squads.Select(s => s.Title));
    }

    /// <summary>
    /// Мир вокруг города: лагеря варваров, чужие города, отряды на марше, наши караваны и наша армия.
    /// • Лагеря появляются сами по одному (раз в newCampEveryTurns ходов, не больше maxCamps) и раз в campSquadEveryTurns
    ///   ходов выпускают отряд (не больше maxSquadsPerCamp сразу). Отряд обычно ходит дозором вокруг лагеря и смотрит
    ///   на sightRadius клеток: увидел нашу армию — гонится за ней, наши границы — идёт грабить город, стражу чужого
    ///   города — дерётся с ней, наш караван — гонится и грабит, сам чужой город — грабит его. Иногда (raidChance) отряд
    ///   сразу идёт в набег на ближний город. Караван, прошедший рядом с варварами, разграблен — золото пропало.
    /// • Чужие города появляются на ходах cityTurns: враждебный (раз в armyEveryTurns ходов шлёт армию на нас) или
    ///   торговый (с базара к нему ходят караваны: ⚒ сейчас → 🪙 по прибытии). У каждого города своя стража: ходит
    ///   вокруг и бьёт варваров. Бои чужих отрядов между собой и набеги на чужие города решаются сами (по силе отрядов).
    /// • Наши отряды (как герои в «Героях», до maxSquads) — фигурки на карте: идут, куда скажут (armyMoves клеток за ход),
    ///   нападают на отряд или лагерь (бой на арене; сожжённый лагерь — золото). Враг рядом с отрядом — в конце хода бой.
    ///   Город защищает гарнизон (и отряды в границах); нет никого — вошедший в границы враг грабит здания без боя.
    ///   Отряд на ратуше меняется бойцами с гарнизоном; пока он в поле, на ратуше собирают следующий (пустой отряд всегда
    ///   стоит там, если отрядов меньше предела). Два своих отряда рядом меняются бойцами; на одной клетке не стоят.
    /// Всё двигается в начале каждого хода (AdvanceTurn). Вид — CityMapView. GameManager добавляет компонент сам, если его нет.
    /// </summary>
    public class WorldPowers : MonoBehaviour
    {
        private static readonly string[] CampNames =
        {
            "Волчья пасть", "Кабанья тропа", "Чёрный ворон", "Медвежий лог", "Красный череп", "Гнилая топь", "Сломанный клык",
            "Змеиный брод", "Седой курган",
        };

        private static readonly string[] CityNames = { "Белоград", "Златоград", "Ярград", "Китеж", "Велеград", "Чернигород", "Синегорье" };

        private static readonly Vector2Int[] Steps8 =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
            new Vector2Int(1, 1), new Vector2Int(-1, 1), new Vector2Int(1, -1), new Vector2Int(-1, -1),
        };

        [Header("Ссылки")]
        public CityManager city;
        public AutoBattleManager battle;
        [Tooltip("Скамейка = наша армия на карте (пусто — найдётся сама)")]
        public BenchManager bench;

        [Header("Лагеря варваров")]
        [Min(0)] public int startCamps = 1;
        [Tooltip("Лагерь стоит на таком расстоянии от нашего города (клеток)")]
        [Min(3)] public int campMinDistance = 7;
        [Min(3)] public int campMaxDistance = 13;
        [Tooltip("Новый лагерь — раз в столько ходов")]
        [Min(1)] public int newCampEveryTurns = 6;
        [Min(1)] public int maxCamps = 5;
        [Tooltip("Лагерь выпускает новый отряд раз в столько ходов")]
        [Min(1)] public int campSquadEveryTurns = 4;
        [Tooltip("Сколько отрядов одного лагеря может ходить по карте сразу")]
        [Min(1)] public int maxSquadsPerCamp = 2;
        [Tooltip("Клеток за ход у отрядов варваров")]
        [Min(1)] public int raiderSpeed = 2;
        [Tooltip("Отряд набега больше обычной волны во столько раз; дозор — patrolSizeMultiplier")]
        [Min(0.1f)] public float raidSizeMultiplier = 1.15f;
        [Min(0.1f)] public float patrolSizeMultiplier = 0.9f;
        [Tooltip("Дозор ходит не дальше стольких клеток от лагеря")]
        [Min(1)] public int patrolRadius = 4;
        [Tooltip("На сколько клеток отряд видит вокруг себя (нашу армию, наши границы, чужие отряды и города)")]
        [Min(1)] public int sightRadius = 3;
        [Tooltip("Шанс, что новый отряд сразу идёт в набег на ближний город: в начале и прибавка за ход (не больше максимума)")]
        [Range(0f, 1f)] public float raidChance = 0.2f;
        [Range(0f, 0.1f)] public float raidChancePerTurn = 0.008f;
        [Range(0f, 1f)] public float maxRaidChance = 0.55f;
        [Tooltip("Гарнизон лагеря (с ним бьёмся при штурме) — во столько раз меньше обычной волны")]
        [Min(0.1f)] public float campGarrisonSize = 0.8f;
        [Tooltip("Золото за сожжённый лагерь: база + за каждый ход")]
        [Min(0)] public int campBounty = 10;
        [Min(0f)] public float campBountyPerTurn = 0.6f;

        [Header("Чужие города")]
        [Tooltip("На каких ходах появляются новые города")]
        public int[] cityTurns = { 4, 10, 17, 26, 36 };
        [Min(3)] public int cityMinDistance = 10;
        [Min(3)] public int cityMaxDistance = 17;
        [Tooltip("Шанс, что новый город враждебный (первый всегда торговый)")]
        [Range(0f, 1f)] public float hostileCityChance = 0.5f;
        [Tooltip("Враждебный город шлёт армию раз в столько ходов")]
        [Min(1)] public int armyEveryTurns = 8;
        [Min(1)] public int armySpeed = 2;
        [Tooltip("Армия города — как отряд варваров на столько уровней выше")]
        [Min(0)] public int armyLevelBonus = 1;
        [Min(0.1f)] public float armySizeMultiplier = 1.3f;
        [Tooltip("Стража города: выходит раз в столько ходов (если прежнюю разбили), ходит не дальше guardRadius")]
        [Min(1)] public int guardEveryTurns = 5;
        [Min(1)] public int guardRadius = 4;
        [Min(0.1f)] public float guardSizeMultiplier = 1f;
        [Tooltip("Сила гарнизона чужого города против набега (× сила обычной волны)")]
        [Min(0.1f)] public float garrisonMultiplier = 1.2f;
        [Tooltip("Сколько ходов разграбленный город приходит в себя")]
        [Min(1)] public int plunderTurns = 5;
        [Tooltip("Земля вокруг чужого города (радиус): тут нельзя покупать клетки")]
        [Min(0)] public int cityRadius = 1;

        [Header("Караваны (с базара в торговый город)")]
        [Min(1)] public int caravanProduction = 12;
        [Min(0)] public int caravanBaseGold = 6;
        [Tooltip("+золота за каждую клетку пути")]
        [Min(0f)] public float caravanGoldPerCell = 0.8f;
        [Tooltip("Клеток за ход: караван быстрый")]
        [Min(1)] public int caravanSpeed = 6;

        [Header("Местность: проходимость (для всех, кто ходит по карте)")]
        [Tooltip("Лес: войти можно с любым остатком хода, но ход на этом кончается — по лесу 1 клетка за ход")]
        public bool forestEndsMove = true;
        [Tooltip("Река (брод): очков хода за клетку")]
        [Min(1)] public int riverCost = 2;
        [Tooltip("Поиск пути: лесная клетка «стоит» столько обычных — лес обходят, если обход ненамного длиннее")]
        [Min(1f)] public float forestPathWeight = 3f;

        [Header("Наши отряды на карте")]
        [Tooltip("Клеток за ход")]
        [Min(1)] public int armyMoves = 3;
        [Tooltip("Сколько отрядов может быть сразу (как героев в «Героях»): новый собирают на ратуше, пока прежние в поле")]
        [Min(1)] public int maxSquads = 4;

        [Header("Журнал событий мира")]
        [Min(1)] public int logSize = 8;

        private readonly List<Power> powers = new List<Power>();
        private readonly List<Squad> squads = new List<Squad>();
        private readonly List<string> log = new List<string>();
        private readonly List<PlayerArmy> armies = new List<PlayerArmy>();
        private int nextId = 1;
        private int nextArmyId = 1;
        private int namesUsed;

        public IReadOnlyList<Power> Powers => powers;
        public IReadOnlyList<Squad> Squads => squads;
        /// <summary>Последние события мира (бои чужих отрядов, набеги на чужие города) — для панели карты.</summary>
        public IReadOnlyList<string> Log => log;
        /// <summary>Растёт при любом изменении (вид перерисовывает метки).</summary>
        public int Version { get; private set; }
        /// <summary>Ход, на котором мир сейчас.</summary>
        public int Turn { get; private set; } = 1;

        public event Action Changed;

        /// <summary>Отряды в наших границах — с ними бой в этот ход (или грабёж, если армия ушла).</summary>
        public List<Squad> Attackers => squads.Where(s => s.IsHostile && s.Attacking).ToList();
        public bool UnderAttack => squads.Any(s => s.IsHostile && s.Attacking);

        /// <summary>Наши отряды на карте (в линейке кто-то есть).</summary>
        public IEnumerable<PlayerArmy> Armies => armies.Where(a => !a.IsEmpty);
        /// <summary>Все наши отряды, и пустой на ратуше (его собирают из гарнизона).</summary>
        public IReadOnlyList<PlayerArmy> AllArmies => armies;
        public int ArmyCount => armies.Count(a => !a.IsEmpty);
        /// <summary>Хоть один отряд на карте.</summary>
        public bool ArmyPresent => armies.Any(a => !a.IsEmpty);
        /// <summary>Отряд на ратуше: тот, что там стоит, или новый пустой; null — все maxSquads отрядов в поле.</summary>
        public PlayerArmy HomeArmy => city == null
            ? armies.FirstOrDefault()
            : armies.Where(a => a.Cell == city.CityCenter).OrderBy(a => a.IsEmpty).FirstOrDefault();

        /// <summary>Отряд в наших границах: защищает город вместе с гарнизоном.</summary>
        public bool InCity(PlayerArmy army) => city != null && army != null && city.InTerritory(army.Cell);
        /// <summary>Отряд стоит на ратуше: меняется бойцами с гарнизоном.</summary>
        public bool AtTownHall(PlayerArmy army) => city == null || (army != null && army.Cell == city.CityCenter);
        /// <summary>Наш отряд на этой клетке (null — нет).</summary>
        public PlayerArmy ArmyAt(Vector2Int cell) => armies.FirstOrDefault(a => !a.IsEmpty && a.Cell == cell);
        /// <summary>Свой отряд рядом (встреча, как у героев): с ним можно меняться бойцами. null — никого.</summary>
        public PlayerArmy MeetingWith(PlayerArmy army) =>
            army == null || army.IsEmpty
                ? null
                : Armies.Where(a => a != army && Distance(a.Cell, army.Cell) <= 1).OrderBy(a => Distance(a.Cell, army.Cell)).ThenBy(a => a.Number).FirstOrDefault();

        /// <summary>Хоть один отряд в границах — защищает город.</summary>
        public bool ArmiesDefend => Armies.Any(InCity);
        /// <summary>Город есть кому защищать: гарнизон или отряды в границах.</summary>
        public bool CityDefended => (bench != null && bench.GarrisonCount > 0) || ArmiesDefend;

        /// <summary>Враги рядом с этим отрядом в поле — в конце хода бой с ними.</summary>
        public List<Squad> EngagersOf(PlayerArmy army) =>
            army != null && !army.IsEmpty && !InCity(army)
                ? squads.Where(s => s.IsHostile && !s.Attacking && Distance(s.Cell, army.Cell) <= 1).ToList()
                : new List<Squad>();

        /// <summary>Первый наш отряд в поле, рядом с которым враги (с ним бой в конце хода); null — таких нет.</summary>
        public PlayerArmy EngagedArmy => Armies.OrderBy(a => a.Number).FirstOrDefault(a => EngagersOf(a).Count > 0);

        /// <summary>В конце хода обязательный бой: враг у отряда в поле или в наших границах.</summary>
        public bool MustFight => UnderAttack || EngagedArmy != null;

        private void Awake()
        {
            if (city == null) city = FindFirstObjectByType<CityManager>();
            if (battle == null) battle = FindFirstObjectByType<AutoBattleManager>();
            if (bench == null) bench = FindFirstObjectByType<BenchManager>();
        }

        // =====================================================================
        //  Новый забег и новый ход
        // =====================================================================

        /// <summary>Новый забег: пустой отряд на ратуше, стартовые лагеря (первый отряд выйдет на следующем ходу).</summary>
        public void ResetWorld()
        {
            powers.Clear();
            squads.Clear();
            log.Clear();
            nextId = 1;
            namesUsed = Random.Range(0, 100);
            Turn = 1;
            if (city != null) city.ForeignLand = ForeignLandAt;
            armies.Clear();
            nextArmyId = 1;
            SyncArmies(); // первый отряд — на ратуше
            for (int i = 0; i < startCamps; i++) AddCamp(1);
            NotifyChanged();
        }

        /// <summary>
        /// Начало хода turn: отряды смотрят вокруг и идут, чужие отряды дерутся между собой, варвары грабят чужие города,
        /// караваны доходят, лагеря и города выпускают отряды, появляются новые лагеря и города, наша армия идёт
        /// по приказу дальше. Возвращает сообщения для игрока (то, что касается нас).
        /// </summary>
        public List<string> AdvanceTurn(int turn)
        {
            Turn = turn;
            var news = new List<string>();
            if (city == null || city.World == null) return news;

            // 1) Отряды: посмотрели вокруг, выбрали цель, пошли
            foreach (Squad squad in squads.ToList())
            {
                if (!squads.Contains(squad) || squad.IsCaravan) continue;
                Think(squad);
                Walk(squad);
            }

            // 2) Встречи: набеги на чужие города, бои чужих отрядов, грабёж караванов
            ResolveMeetings(news);

            // 3) Караваны
            foreach (Squad caravan in squads.Where(s => s.IsCaravan).ToList()) MoveCaravan(caravan, news);

            // 4) Новые лагеря и города
            if (turn > 1 && turn % newCampEveryTurns == 0 && powers.Count(p => p.IsCamp) < maxCamps)
            {
                Power camp = AddCamp(turn);
                if (camp != null) news.Add($"Новый лагерь варваров «{camp.Name}» — смотри карту");
            }
            if (cityTurns != null && cityTurns.Contains(turn))
            {
                Power newCity = AddCity(turn);
                if (newCity != null)
                    news.Add(newCity.Hostile
                        ? $"Новый город {newCity.Name} — <color=#FF7A6E>враг</color>"
                        : $"Новый город {newCity.Name} — <color=#7FE3E8>торговля</color> (караваны с базара)");
            }

            // 5) Лагеря и города выпускают отряды
            foreach (Power power in powers.ToList()) Spawn(power, turn, news);

            // 6) Наши отряды идут по приказам дальше (нападают — по клику игрока)
            foreach (PlayerArmy army in armies.ToList())
            {
                army.MovesLeft = armyMoves;
                army.AttackedThisTurn = false;
                if (!army.IsEmpty && army.HasOrder) MoveArmy(army, attack: false);
            }
            SyncArmies();

            // 7) Кто у наших стен, кто напал на отряды
            MarkAttackers();
            foreach (Squad attacker in Attackers)
                news.Add($"{GameVisuals.IconWalls} {attacker.Title} у стен! {Composition(attacker)} → «В БОЙ!»" +
                         (CityDefended
                             ? (ArmiesDefend ? " (бьются гарнизон и отряды в границах)" : " (бьётся гарнизон)")
                             : " <color=#FF7A6E>Гарнизон пуст, отряды в поле — город грабят без боя!</color>"));
            foreach (PlayerArmy army in Armies)
            {
                foreach (Squad engager in EngagersOf(army))
                    news.Add($"{GameVisuals.IconAttack} {engager.Title} напали на {army.Title.ToLowerInvariant()}! {Composition(engager)} → «В БОЙ!» (или уводи отряд)");
            }
            NotifyChanged();
            return news;
        }

        /// <summary>Отряды в наших границах — «у стен».</summary>
        private void MarkAttackers()
        {
            foreach (Squad squad in squads)
            {
                if (squad.IsHostile && !squad.Attacking && city.InTerritory(squad.Cell))
                {
                    squad.Attacking = true;
                    squad.Path.Clear();
                }
            }
        }

        /// <summary>Бой в наших границах закончился (или защищать было некому — город пограбили): эти отряды уходят с карты.</summary>
        public void ResolveAttack(IEnumerable<Squad> attackers)
        {
            foreach (Squad squad in attackers.ToList()) RemoveSquad(squad);
            NotifyChanged();
        }

        /// <summary>
        /// Бой в поле закончился. Победа — отряды разбиты, лагерь сожжён. Поражение — враг теряет только погибших.
        /// Наш отряд разбит целиком — он пропадает с карты (новый соберут на ратуше).
        /// </summary>
        public void ResolveFieldBattle(FieldBattle fight, bool victory, ICollection<BenchUnit> enemyFallen)
        {
            if (fight == null) return;
            foreach (Squad squad in fight.Squads)
            {
                if (victory) RemoveSquad(squad);
                else
                {
                    if (enemyFallen != null) squad.Units.RemoveAll(enemyFallen.Contains);
                    if (squad.Units.Count == 0) RemoveSquad(squad);
                }
            }
            if (victory && fight.Camp != null) DestroyCamp(fight.Camp);
            SyncArmies(); // отряд разбит целиком — пропал с карты
            NotifyChanged();
        }

        /// <summary>Золото за сожжённый лагерь.</summary>
        public int CampBounty => campBounty + Mathf.RoundToInt(Turn * campBountyPerTurn);

        private void DestroyCamp(Power camp)
        {
            powers.Remove(camp);
            // Его отряды остаются бродить там, где стоял лагерь
            foreach (Squad squad in squads.Where(s => s.Home == camp)) squad.Anchor = camp.Cell;
            AddLog($"Лагерь «{camp.Name}» сожжён");
            Version++;
        }

        private void RemoveSquad(Squad squad)
        {
            if (!squads.Remove(squad)) return;
            foreach (Squad other in squads)
            {
                if (other.ChaseTarget != squad) continue;
                other.ChaseTarget = null;
                other.Mission = SquadMission.Patrol;
                other.Waypoint = null;
            }
            foreach (PlayerArmy army in armies)
            {
                if (army.TargetSquad == squad) ClearOrder(army);
            }
            if (squad.Home == null) return;
            if (squad.Kind == SquadKind.Guard) squad.Home.NextSquadTurn = Mathf.Max(squad.Home.NextSquadTurn, Turn + guardEveryTurns);
            if (squad.Kind == SquadKind.Army) squad.Home.NextArmyTurn = Mathf.Max(squad.Home.NextArmyTurn, Turn + armyEveryTurns);
        }

        // =====================================================================
        //  Отряды: что видят и куда идут
        // =====================================================================

        private void Think(Squad squad)
        {
            if (squad.Attacking) return;
            switch (squad.Kind)
            {
                case SquadKind.Raiders: ThinkRaiders(squad); break;
                case SquadKind.Army: ThinkArmy(squad); break;
                case SquadKind.Guard: ThinkGuard(squad); break;
            }
        }

        /// <summary>
        /// Варвары: в погоне и в набеге — продолжают; иначе смотрят вокруг: наша армия → погоня, чужие отряды и наши
        /// караваны → погоня, наши границы → набег на нас, чужой город → набег на него; никого — дозор вокруг лагеря.
        /// </summary>
        private void ThinkRaiders(Squad squad)
        {
            if (squad.Mission == SquadMission.Return) return;
            if (squad.Mission == SquadMission.Chase && ChaseValid(squad)) return;
            if (squad.Mission == SquadMission.Raid && RaidValid(squad)) return;

            PlayerArmy seen = SeenArmy(squad, outsideCity: false);
            if (seen != null)
            {
                StartChaseArmy(squad, seen);
                return;
            }
            Squad prey = squads.Where(o => o != squad && AtWar(squad, o) && Distance(o.Cell, squad.Cell) <= sightRadius)
                .OrderBy(o => Distance(o.Cell, squad.Cell)).FirstOrDefault();
            if (prey != null)
            {
                squad.Mission = SquadMission.Chase;
                squad.ChaseTarget = prey;
                squad.ChaseArmy = null;
                return;
            }
            if (SeesOurLand(squad))
            {
                StartRaid(squad, null);
                return;
            }
            Power town = powers.Where(p => p.IsCity && !p.IsPlundered(Turn) && Distance(p.Cell, squad.Cell) <= sightRadius + cityRadius)
                .OrderBy(p => Distance(p.Cell, squad.Cell)).FirstOrDefault();
            if (town != null)
            {
                StartRaid(squad, town);
                return;
            }
            Patrol(squad, patrolRadius);
        }

        /// <summary>Армия враждебного города идёт на нас; увидела наш отряд в поле — перехватывает его.</summary>
        private void ThinkArmy(Squad squad)
        {
            if (squad.Mission == SquadMission.Chase && ChaseValid(squad)) return;
            PlayerArmy seen = SeenArmy(squad, outsideCity: true);
            if (seen != null)
            {
                StartChaseArmy(squad, seen);
                return;
            }
            StartRaid(squad, null);
        }

        /// <summary>Стража города: гонит варваров, которых увидела (стража врага — и нашу армию), иначе ходит дозором.</summary>
        private void ThinkGuard(Squad squad)
        {
            if (squad.Mission == SquadMission.Chase && ChaseValid(squad) && Distance(squad.Cell, squad.Anchor) <= guardRadius + sightRadius) return;
            Squad prey = squads.Where(o => o.IsBarbarian && Distance(o.Cell, squad.Cell) <= sightRadius)
                .OrderBy(o => Distance(o.Cell, squad.Cell)).FirstOrDefault();
            if (prey != null)
            {
                squad.Mission = SquadMission.Chase;
                squad.ChaseTarget = prey;
                squad.ChaseArmy = null;
                return;
            }
            PlayerArmy seen = squad.IsHostile ? SeenArmy(squad, outsideCity: false) : null;
            if (seen != null && Distance(seen.Cell, squad.Anchor) <= guardRadius + 1)
            {
                StartChaseArmy(squad, seen);
                return;
            }
            Patrol(squad, guardRadius);
        }

        /// <summary>Ближайший наш отряд, которого видит отряд (outsideCity — только вне наших границ); null — никого.</summary>
        private PlayerArmy SeenArmy(Squad squad, bool outsideCity) =>
            Armies.Where(a => Distance(a.Cell, squad.Cell) <= sightRadius && (!outsideCity || !InCity(a)))
                .OrderBy(a => Distance(a.Cell, squad.Cell)).FirstOrDefault();

        private bool SeesOurLand(Squad squad)
        {
            for (int dx = -sightRadius; dx <= sightRadius; dx++)
            {
                for (int dy = -sightRadius; dy <= sightRadius; dy++)
                {
                    if (city.InTerritory(squad.Cell + new Vector2Int(dx, dy))) return true;
                }
            }
            return false;
        }

        private void StartChaseArmy(Squad squad, PlayerArmy army)
        {
            squad.Mission = SquadMission.Chase;
            squad.ChaseTarget = null;
            squad.ChaseArmy = army;
        }

        private void StartRaid(Squad squad, Power target)
        {
            squad.Mission = SquadMission.Raid;
            squad.RaidTarget = target;
            squad.ChaseTarget = null;
            squad.ChaseArmy = null;
        }

        /// <summary>Погоня ещё имеет смысл: цель жива и не ушла слишком далеко.</summary>
        private bool ChaseValid(Squad squad)
        {
            if (squad.ChaseArmy != null)
                return !squad.ChaseArmy.IsEmpty && armies.Contains(squad.ChaseArmy) && Distance(squad.ChaseArmy.Cell, squad.Cell) <= sightRadius * 2;
            return squad.ChaseTarget != null && squads.Contains(squad.ChaseTarget) && Distance(squad.ChaseTarget.Cell, squad.Cell) <= sightRadius * 2;
        }

        private bool RaidValid(Squad squad) => squad.RaidTarget == null || (powers.Contains(squad.RaidTarget) && !squad.RaidTarget.IsPlundered(Turn));

        /// <summary>Дозор: идёт к случайной клетке суши рядом с домом; дошёл — выбирает новую.</summary>
        private void Patrol(Squad squad, int radius)
        {
            squad.Mission = SquadMission.Patrol;
            squad.ChaseTarget = null;
            squad.ChaseArmy = null;
            if (squad.Waypoint.HasValue && squad.Waypoint.Value != squad.Cell && Distance(squad.Waypoint.Value, squad.Anchor) <= radius) return;
            var options = new List<Vector2Int>();
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    Vector2Int cell = squad.Anchor + new Vector2Int(dx, dy);
                    if (cell == squad.Cell || !city.World.IsPlainLand(cell) || city.InTerritory(cell) || PowerAt(cell) != null) continue;
                    options.Add(cell);
                }
            }
            squad.Waypoint = options.Count > 0 ? options[Random.Range(0, options.Count)] : (Vector2Int?)null;
        }

        /// <summary>Куда отряд идёт сейчас (null — стоит).</summary>
        private Predicate<Vector2Int> GoalOf(Squad squad)
        {
            switch (squad.Mission)
            {
                case SquadMission.Raid:
                    if (squad.RaidTarget == null) return c => city.InTerritory(c);
                    Vector2Int town = squad.RaidTarget.Cell;
                    return c => Distance(c, town) <= cityRadius;
                case SquadMission.Chase:
                    if (squad.ChaseArmy != null)
                    {
                        Vector2Int army = squad.ChaseArmy.Cell;
                        return c => Distance(c, army) <= 1;
                    }
                    if (squad.ChaseTarget == null) return null;
                    Vector2Int prey = squad.ChaseTarget.Cell;
                    return c => Distance(c, prey) <= 1;
                case SquadMission.Return:
                    Vector2Int home = squad.Anchor;
                    return c => Distance(c, home) <= 0;
                default:
                    if (!squad.Waypoint.HasValue) return null;
                    Vector2Int waypoint = squad.Waypoint.Value;
                    return c => c == waypoint;
            }
        }

        /// <summary>Путь до цели (без шагов): для пунктира и «через N ходов».</summary>
        private void RefreshPath(Squad squad)
        {
            squad.Path.Clear();
            Predicate<Vector2Int> goal = GoalOf(squad);
            if (goal == null) return;
            List<Vector2Int> path = FindPath(squad.Cell, goal);
            if (path != null) squad.Path.AddRange(path);
        }

        private void Walk(Squad squad)
        {
            if (squad.Attacking) return;
            RefreshPath(squad);
            Predicate<Vector2Int> goal = GoalOf(squad);
            int moves = squad.Speed;
            while (moves > 0 && squad.Path.Count > 0)
            {
                squad.Cell = squad.Path[0];
                squad.Path.RemoveAt(0);
                moves = Mathf.Max(0, moves - StepCost(squad.Cell)); // лес — ход кончился, река — дороже
                if (squad.IsHostile && city.InTerritory(squad.Cell)) break; // вошли в наши границы — дальше не идут
            }
            if (goal == null || !goal(squad.Cell)) return;
            if (squad.Mission == SquadMission.Return)
            {
                RemoveSquad(squad); // вернулись в лагерь с добычей
                return;
            }
            if (squad.Mission == SquadMission.Patrol) squad.Waypoint = null; // дошли — следующий ход выберут новую точку
            RefreshPath(squad);
        }

        // =====================================================================
        //  Встречи: набеги на чужие города, бои чужих отрядов (решаются сами)
        // =====================================================================

        /// <summary>Варвары против стражи и армий городов и против наших караванов (города между собой не воюют).</summary>
        private static bool AtWar(Squad a, Squad b)
        {
            if (a.IsCaravan || b.IsCaravan) return a.IsBarbarian || b.IsBarbarian;
            return a.IsBarbarian != b.IsBarbarian;
        }

        private void ResolveMeetings(List<string> news)
        {
            foreach (Squad raider in squads.Where(s => s.IsBarbarian && s.Mission == SquadMission.Raid && s.RaidTarget != null).ToList())
            {
                if (squads.Contains(raider) && Distance(raider.Cell, raider.RaidTarget.Cell) <= cityRadius) RaidCity(raider, raider.RaidTarget);
            }

            for (int guard = 0; guard < 50; guard++)
            {
                (Squad a, Squad b) pair = default;
                foreach (Squad a in squads)
                {
                    pair.b = squads.FirstOrDefault(b => b != a && AtWar(a, b) && Distance(a.Cell, b.Cell) <= 1);
                    if (pair.b == null) continue;
                    pair.a = a;
                    break;
                }
                if (pair.a == null) break;
                if (pair.a.IsCaravan || pair.b.IsCaravan) RobCaravan(pair.a.IsCaravan ? pair.a : pair.b, pair.a.IsCaravan ? pair.b : pair.a, news);
                else AutoFight(pair.a, pair.b);
            }
        }

        /// <summary>Варвары догнали караван: золото пропало, добыча уходит в лагерь.</summary>
        private void RobCaravan(Squad caravan, Squad raider, List<string> news)
        {
            squads.Remove(caravan);
            raider.Mission = SquadMission.Return;
            raider.ChaseTarget = null;
            raider.RaidTarget = null;
            foreach (Squad other in squads.Where(s => s.ChaseTarget == caravan))
            {
                other.ChaseTarget = null;
                other.Mission = SquadMission.Patrol;
            }
            news.Add($"<color=#FF7A6E>{caravan.Title} разграблен: {raider.Title} забрали {caravan.Gold} {GameVisuals.IconGold}</color>");
            AddLog($"{raider.Title} разграбили караван в {caravan.Home?.Name}");
        }

        /// <summary>Бой двух чужих отрядов: шанс победы — по силе; победитель теряет часть бойцов, проигравший разбит.</summary>
        private void AutoFight(Squad a, Squad b)
        {
            float powerA = Strength(a.Units), powerB = Strength(b.Units);
            float chanceA = WinChance(powerA, powerB);
            Squad winner = Random.value < chanceA ? a : b;
            Squad loser = winner == a ? b : a;
            LoseUnits(winner, Mathf.Clamp(Strength(loser.Units) / Mathf.Max(1f, Strength(winner.Units)) * 0.7f, 0.1f, 0.9f));
            RemoveSquad(loser);
            AddLog($"{winner.Title} разбили: {loser.Title}");
        }

        /// <summary>Варвары дошли до чужого города: бьются с гарнизоном. Победили — город разграблен, добыча уходит в лагерь.</summary>
        private void RaidCity(Squad raider, Power town)
        {
            float garrison = GarrisonStrength(town);
            if (Random.value < WinChance(Strength(raider.Units), garrison))
            {
                town.PlunderedUntil = Turn + plunderTurns;
                LoseUnits(raider, 0.3f);
                raider.Mission = SquadMission.Return;
                raider.RaidTarget = null;
                AddLog($"{raider.Title} разграбили город {town.Name}");
            }
            else
            {
                RemoveSquad(raider);
                AddLog($"Город {town.Name} отбился от варваров");
            }
        }

        private float GarrisonStrength(Power town) =>
            battle != null ? Strength(battle.GenerateEnemies(battle.ThreatDay(Turn), armyLevelBonus, 1f)) * garrisonMultiplier : 100f;

        private static float WinChance(float mine, float theirs)
        {
            float a = Mathf.Pow(Mathf.Max(1f, mine), 1.5f), b = Mathf.Pow(Mathf.Max(1f, theirs), 1.5f);
            return a / (a + b);
        }

        private static void LoseUnits(Squad squad, float share)
        {
            int lost = Mathf.Min(squad.Units.Count - 1, Mathf.RoundToInt(squad.Units.Count * share));
            for (int i = 0; i < lost; i++) squad.Units.RemoveAt(Random.Range(0, squad.Units.Count));
        }

        /// <summary>Сила отряда для сравнения на карте: сумма √(ХП × атака) бойцов.</summary>
        public static float Strength(IEnumerable<BenchUnit> units)
        {
            float total = 0f;
            foreach (BenchUnit unit in units)
            {
                if (unit?.data == null) continue;
                total += Mathf.Sqrt(Mathf.Max(1, unit.MaxHp) * Mathf.Max(1, unit.Attack + unit.Heal));
            }
            return total;
        }

        private void AddLog(string text)
        {
            log.Add($"{GameVisuals.IconTurns}{Turn} {text}");
            while (log.Count > logSize) log.RemoveAt(0);
        }

        // =====================================================================
        //  Появление отрядов
        // =====================================================================

        private void Spawn(Power power, int turn, List<string> news)
        {
            if (battle == null) return;
            int day = battle.ThreatDay(turn + 2);
            if (power.IsCamp)
            {
                if (turn < power.NextSquadTurn || squads.Count(s => s.Home == power && s.IsBarbarian) >= maxSquadsPerCamp) return;
                power.NextSquadTurn = turn + campSquadEveryTurns;
                Power target = Random.value < Mathf.Min(maxRaidChance, raidChance + raidChancePerTurn * turn) ? RaidTargetFor(power) : power;
                bool raid = target != power;
                Squad squad = NewSquad(SquadKind.Raiders, power, battle.GenerateEnemies(day, 0, raid ? raidSizeMultiplier : patrolSizeMultiplier), raiderSpeed);
                if (squad == null) return;
                if (!raid)
                {
                    Patrol(squad, patrolRadius);
                }
                else
                {
                    StartRaid(squad, target);
                    if (target == null)
                    {
                        RefreshPath(squad);
                        int eta = TurnsToArrive(squad);
                        news.Add($"{GameVisuals.IconAttack} «{power.Name}» идут в набег на нас: {Composition(squad)} → {GameVisuals.IconWalls} через {eta} {TurnWord(eta)}");
                    }
                    else
                    {
                        AddLog($"Варвары «{power.Name}» пошли грабить город {target.Name}");
                    }
                }
                RefreshPath(squad);
                return;
            }

            if (turn >= power.NextSquadTurn && !squads.Any(s => s.Home == power && s.Kind == SquadKind.Guard))
            {
                Squad guard = NewSquad(SquadKind.Guard, power, battle.GenerateEnemies(day, power.Hostile ? armyLevelBonus : 0, guardSizeMultiplier), armySpeed);
                if (guard != null)
                {
                    Patrol(guard, guardRadius);
                    RefreshPath(guard);
                }
                power.NextSquadTurn = turn + guardEveryTurns;
            }
            if (power.Hostile && !power.IsPlundered(turn) && turn >= power.NextArmyTurn && !squads.Any(s => s.Home == power && s.Kind == SquadKind.Army))
            {
                Squad army = NewSquad(SquadKind.Army, power, battle.GenerateEnemies(day, armyLevelBonus, armySizeMultiplier), armySpeed);
                power.NextArmyTurn = turn + armyEveryTurns;
                if (army == null) return;
                StartRaid(army, null);
                RefreshPath(army);
                int eta = TurnsToArrive(army);
                news.Add($"{GameVisuals.IconAttack} {power.Name} шлёт армию: {Composition(army)} → {GameVisuals.IconWalls} через {eta} {TurnWord(eta)}");
            }
        }

        /// <summary>Ближний город для набега: наш или чужой (неразграбленный). null — наш.</summary>
        private Power RaidTargetFor(Power camp)
        {
            Power nearest = powers.Where(p => p.IsCity && !p.IsPlundered(Turn)).OrderBy(p => Distance(p.Cell, camp.Cell)).FirstOrDefault();
            int toUs = Distance(city.CityCenter, camp.Cell);
            return nearest != null && Distance(nearest.Cell, camp.Cell) < toUs ? nearest : null;
        }

        private Squad NewSquad(SquadKind kind, Power home, List<BenchUnit> units, int speed)
        {
            if (units == null || units.Count == 0) return null;
            var squad = new Squad
            {
                Id = nextId++,
                Kind = kind,
                Home = home,
                Anchor = home.Cell,
                Cell = home.Cell,
                Speed = speed,
            };
            squad.Units.AddRange(units);
            squads.Add(squad);
            return squad;
        }

        // =====================================================================
        //  Караваны
        // =====================================================================

        private void MoveCaravan(Squad caravan, List<string> news)
        {
            int moves = caravan.Speed;
            while (moves > 0 && caravan.Path.Count > 0)
            {
                caravan.Cell = caravan.Path[0];
                caravan.Path.RemoveAt(0);
                moves = Mathf.Max(0, moves - StepCost(caravan.Cell));
                // Прошёл рядом с варварами — грабят
                Squad raider = squads.FirstOrDefault(s => s.IsBarbarian && Distance(s.Cell, caravan.Cell) <= 1);
                if (raider != null)
                {
                    RobCaravan(caravan, raider, news);
                    return;
                }
            }
            if (caravan.Path.Count > 0 && caravan.Cell != caravan.Home.Cell) return;
            squads.Remove(caravan);
            bool plundered = caravan.Home.IsPlundered(Turn);
            int gold = plundered ? caravan.Gold / 2 : caravan.Gold;
            city.AddGold(gold);
            news.Add($"Караван → {caravan.Home.Name}: +{gold} {GameVisuals.IconGold}" + (plundered ? " (город разграблен — торговля хуже)" : string.Empty));
        }

        /// <summary>Через сколько ходов отряд будет у наших стен или у цели (0 — уже стоит).</summary>
        public int TurnsToArrive(Squad squad)
        {
            if (squad.Attacking) return 0;
            int turns = TurnsFor(squad.Path, squad.Speed, squad.Speed);
            return squad.IsCaravan ? turns : Mathf.Max(1, turns);
        }

        public static string TurnWord(int turns)
        {
            int n = Mathf.Abs(turns) % 100;
            if (n >= 11 && n <= 14) return "ходов";
            switch (n % 10)
            {
                case 1: return "ход";
                case 2:
                case 3:
                case 4: return "хода";
                default: return "ходов";
            }
        }

        /// <summary>Сколько золота привезёт караван в этот город (чем дальше — тем больше; разграбленный город — вдвое меньше).</summary>
        public int CaravanGold(Power target) => target == null ? 0 : caravanBaseGold + Mathf.RoundToInt(CaravanPathLength(target) * caravanGoldPerCell);

        /// <summary>Сколько ходов идёт караван.</summary>
        public int CaravanTurns(Power target)
        {
            if (target == null || city == null) return 1;
            List<Vector2Int> path = FindPath(city.CityCenter, c => c == target.Cell);
            return path != null ? Mathf.Max(1, TurnsFor(path, caravanSpeed, caravanSpeed)) : 1;
        }

        private int CaravanPathLength(Power target)
        {
            if (target == null || city == null) return 0;
            List<Vector2Int> path = FindPath(city.CityCenter, c => c == target.Cell);
            return path?.Count ?? Distance(city.CityCenter, target.Cell);
        }

        public string WhyCannotSendCaravan(Power target)
        {
            if (target == null || !target.CanTrade) return "С этим городом не торгуют";
            Building bazaar = city.Buildings.FirstOrDefault(b => b.Type == BuildingType.Bazaar);
            if (bazaar == null) return "Нет базара";
            if (bazaar.IsRuined) return "Сначала почините базар";
            if (city.Production < caravanProduction) return $"Нужно {caravanProduction} {GameVisuals.IconProduction}";
            return null;
        }

        public bool TrySendCaravan(Power target, out string message)
        {
            message = WhyCannotSendCaravan(target);
            if (message != null) return false;
            int gold = CaravanGold(target);
            int turns = CaravanTurns(target);
            city.AddProduction(-caravanProduction);
            var caravan = new Squad
            {
                Id = nextId++,
                Kind = SquadKind.Caravan,
                Home = target,
                Anchor = city.CityCenter,
                Cell = city.CityCenter,
                Speed = caravanSpeed,
                Gold = gold,
            };
            List<Vector2Int> path = FindPath(city.CityCenter, c => c == target.Cell);
            if (path != null) caravan.Path.AddRange(path);
            squads.Add(caravan);
            message = $"Караван → {target.Name}: −{caravanProduction}{GameVisuals.IconProduction}, через {turns} {TurnWord(turns)} +{gold}{GameVisuals.IconGold}";
            NotifyChanged();
            return true;
        }

        // =====================================================================
        //  Наши отряды
        // =====================================================================

        /// <summary>
        /// Порядок в отрядах: пустые отряды в поле пропадают; на ратуше всегда стоит отряд — пустой, новый (его собирают
        /// из гарнизона), если там никого нет и отрядов меньше maxSquads.
        /// </summary>
        public void SyncArmies()
        {
            if (city == null) return;
            Vector2Int home = city.CityCenter;
            List<PlayerArmy> gone = armies.Where(a => a.IsEmpty && a.Cell != home).ToList();
            if (armies.Any(a => !a.IsEmpty && a.Cell == home)) gone.AddRange(armies.Where(a => a.IsEmpty));
            else gone.AddRange(armies.Where(a => a.IsEmpty && a.Cell == home).Skip(ArmyCount < maxSquads ? 1 : 0));
            foreach (PlayerArmy army in gone.Distinct()) RemoveArmy(army);
            if (ArmyCount < maxSquads && !armies.Any(a => a.Cell == home)) armies.Add(NewArmy(home));
        }

        private void RemoveArmy(PlayerArmy army)
        {
            if (!armies.Remove(army)) return;
            foreach (PlayerArmy other in armies)
            {
                if (other.TargetArmy == army) ClearOrder(other);
            }
            foreach (Squad squad in squads)
            {
                if (squad.ChaseArmy != army) continue;
                squad.ChaseArmy = null;
                squad.Mission = SquadMission.Patrol;
                squad.Waypoint = null;
            }
        }

        private PlayerArmy NewArmy(Vector2Int cell)
        {
            int number = 1;
            while (armies.Any(a => a.Number == number)) number++;
            return new PlayerArmy(bench != null ? bench.rowSlots : 7)
            {
                Id = nextArmyId++,
                Number = number,
                Cell = cell,
                MovesLeft = armyMoves,
            };
        }

        /// <summary>Сила линейки (с бонусом вожака) для сравнения с отрядами на карте.</summary>
        public static float RowStrength(ArmyRow row) => row == null ? 0f : Strength(row.Units) * LeaderFactor(row.Leader);

        public static float ArmyStrength(PlayerArmy army) => army != null ? RowStrength(army.Row) : 0f;

        /// <summary>Сила защитников города: гарнизон и отряды в границах.</summary>
        public float DefenseStrength =>
            (bench != null ? RowStrength(bench.GarrisonRow) : 0f) + DefendingArmies(int.MaxValue).Sum(a => RowStrength(a.Row));

        /// <summary>
        /// Отряды, которые бьются у стен вместе с гарнизоном: в наших границах, ближние к ратуше — первыми, целиком
        /// и сколько влезет на арену (capacity — мест на арене вместе с гарнизоном).
        /// </summary>
        public List<PlayerArmy> DefendingArmies(int capacity)
        {
            var result = new List<PlayerArmy>();
            if (city == null) return result;
            long room = (long)capacity - (bench != null ? bench.GarrisonCount : 0);
            foreach (PlayerArmy army in Armies.Where(InCity).OrderBy(a => Distance(a.Cell, city.CityCenter)).ThenBy(a => a.Number))
            {
                int count = army.Row.Count;
                if (count > room) continue;
                result.Add(army);
                room -= count;
            }
            return result;
        }

        /// <summary>Вожак усиливает отряд: множитель силы (≈ корень из бонусов ХП и атаки).</summary>
        public static float LeaderFactor(BenchUnit leader) =>
            leader == null ? 1f : Mathf.Sqrt((1f + leader.LeadHp) * (1f + leader.LeadAttack));

        /// <summary>
        /// Приказ отряду: клетка с врагом (отряд, лагерь) — идти к нему и напасть; со своим отрядом — встать рядом
        /// (встреча: меняются бойцами); иначе — дойти до клетки. Отряд сразу идёт (сколько осталось шагов), остаток пути —
        /// в следующие ходы. Дошёл до врага — fight: бой (нападать можно раз в ход). Возвращает сообщение (null — всё в порядке).
        /// </summary>
        public string OrderArmy(PlayerArmy army, Vector2Int target, out FieldBattle fight)
        {
            fight = null;
            if (army == null || city == null || !city.InMap(target)) return null;
            if (army.IsEmpty) return "Отряд пуст — переставьте в него бойцов из гарнизона (панель армии внизу)";
            Squad enemy = HostileSquadsAt(target).FirstOrDefault();
            Power place = PowerAt(target);
            PlayerArmy friend = ArmyAt(target);
            bool plainMove = enemy == null && !(place != null && place.IsCamp) && (friend == null || friend == army);
            if (plainMove)
            {
                // Нельзя туда — прежний приказ остаётся
                if (target == army.Cell) return null;
                if (city.World.TerrainAt(target) == TerrainType.Lake) return "Через озеро не пройти";
                if (city.World.TerrainAt(target) == TerrainType.Mountain) return "На гору отряду не подняться";
                if (place != null) return place.Hostile ? $"{place.Name} слишком крепок для штурма — бейте его армии в поле" : $"Это город {place.Name}";
            }
            ClearOrder(army);
            if (enemy != null) army.TargetSquad = enemy;
            else if (place != null && place.IsCamp) army.TargetCamp = place;
            else if (friend != null && friend != army) army.TargetArmy = friend;
            else army.Destination = target;
            fight = MoveArmy(army, attack: true);
            SyncArmies(); // ушёл с ратуши — там встанет новый пустой отряд
            NotifyChanged();
            if (fight != null) return null;
            if (friend != null && friend != army && Distance(army.Cell, friend.Cell) <= 1)
                return $"Встреча: {army.Title} и {friend.Title} — меняйтесь бойцами в панели армии";
            if (!army.HasOrder) return null;
            if (army.Destination == null && army.TargetArmy == null && Distance(army.Cell, TargetCell(army)) <= 1)
                return $"{army.Title} уже бился в этот ход — напасть можно в следующий";
            int turns = Mathf.Max(1, TurnsFor(army.Path, armyMoves, armyMoves));
            return army.MovesLeft == 0 ? $"{army.Title} в пути: дойдёт через {turns} {TurnWord(turns)}" : $"{army.Title} остановился: рядом враг";
        }

        public void ClearOrder(PlayerArmy army)
        {
            if (army == null) return;
            army.TargetSquad = null;
            army.TargetCamp = null;
            army.TargetArmy = null;
            army.Destination = null;
            army.Path.Clear();
        }

        /// <summary>Клетка цели приказа.</summary>
        private static Vector2Int TargetCell(PlayerArmy army) =>
            army.TargetSquad != null ? army.TargetSquad.Cell
            : army.TargetCamp != null ? army.TargetCamp.Cell
            : army.TargetArmy != null ? army.TargetArmy.Cell
            : army.Destination ?? army.Cell;

        /// <summary>На клетке стоит другой наш отряд (два отряда на одной клетке не стоят).</summary>
        private bool Occupied(Vector2Int cell, PlayerArmy except) => armies.Any(a => a != except && !a.IsEmpty && a.Cell == cell);

        /// <summary>
        /// Шаги отряда по приказу. Рядом враг (не цель) — отряд останавливается; клетку цели занял свой отряд — встаёт рядом.
        /// Дошёл до врага и attack — бой.
        /// </summary>
        private FieldBattle MoveArmy(PlayerArmy army, bool attack)
        {
            army.Path.Clear();
            if (!army.HasOrder) return null;
            if ((army.TargetSquad != null && !squads.Contains(army.TargetSquad)) ||
                (army.TargetCamp != null && !powers.Contains(army.TargetCamp)) ||
                (army.TargetArmy != null && (army.TargetArmy.IsEmpty || !armies.Contains(army.TargetArmy))))
            {
                ClearOrder(army);
                return null;
            }
            bool hunt = army.TargetSquad != null || army.TargetCamp != null;
            bool meet = army.TargetArmy != null;
            Vector2Int goalCell = TargetCell(army);
            Predicate<Vector2Int> goal = hunt || meet ? c => Distance(c, goalCell) <= 1 && !Occupied(c, army) : c => c == goalCell;
            List<Vector2Int> path = FindPath(army.Cell, goal);
            if (path == null)
            {
                ClearOrder(army);
                return null;
            }
            army.Path.AddRange(path);
            while (army.MovesLeft > 0 && army.Path.Count > 0)
            {
                if (army.Path.Count == 1 && Occupied(army.Path[0], army))
                {
                    ClearOrder(army); // туда уже встал свой отряд — дальше не идём
                    return null;
                }
                army.Cell = army.Path[0];
                army.Path.RemoveAt(0);
                army.MovesLeft = Mathf.Max(0, army.MovesLeft - StepCost(army.Cell)); // лес — ход кончился, река — дороже
                // Враг рядом — отряд встаёт (обойти его не дадут)
                if (squads.Any(s => s.IsHostile && s != army.TargetSquad && Distance(s.Cell, army.Cell) <= 1)) break;
            }

            if (meet || !hunt)
            {
                if (meet ? Distance(army.Cell, goalCell) <= 1 : army.Cell == goalCell) ClearOrder(army);
                return null;
            }
            if (Distance(army.Cell, goalCell) > 1 || !attack || army.AttackedThisTurn) return null;

            var fight = new FieldBattle { Camp = army.TargetCamp, Army = army };
            fight.Squads.AddRange(squads.Where(s => s.IsHostile && s.Cell == goalCell));
            if (fight.Camp != null && battle != null)
                fight.Garrison.AddRange(battle.GenerateEnemies(battle.ThreatDay(Turn), 0, campGarrisonSize));
            army.AttackedThisTurn = true;
            army.MovesLeft = 0;
            ClearOrder(army);
            return fight.Enemies.Count > 0 ? fight : null;
        }

        /// <summary>Враги рядом с отрядом, на которых можно напасть кликом (для панели отряда).</summary>
        public List<Squad> HostileNear(PlayerArmy army) => squads.Where(s => s.IsHostile && Distance(s.Cell, army.Cell) <= 1).ToList();

        /// <summary>Лагерь рядом с отрядом.</summary>
        public Power CampNear(PlayerArmy army) => powers.FirstOrDefault(p => p.IsCamp && Distance(p.Cell, army.Cell) <= 1);

        // =====================================================================
        //  Появление лагерей и городов
        // =====================================================================

        private Power AddCamp(int turn)
        {
            Vector2Int? cell = FindSite(campMinDistance, campMaxDistance, 5);
            if (!cell.HasValue) return null;
            var camp = new Power
            {
                Id = nextId++,
                Kind = PowerKind.BarbarianCamp,
                Name = CampNames[namesUsed++ % CampNames.Length],
                Cell = cell.Value,
                Hostile = true,
                NextSquadTurn = turn + 1,
                AppearedTurn = turn,
            };
            powers.Add(camp);
            Version++;
            return camp;
        }

        private Power AddCity(int turn)
        {
            Vector2Int? cell = FindSite(cityMinDistance, cityMaxDistance, 6);
            if (!cell.HasValue) return null;
            bool firstCity = !powers.Any(p => p.Kind == PowerKind.City);
            var other = new Power
            {
                Id = nextId++,
                Kind = PowerKind.City,
                Name = CityNames[powers.Count(p => p.Kind == PowerKind.City) % CityNames.Length],
                Cell = cell.Value,
                Hostile = !firstCity && Random.value < hostileCityChance,
                AppearedTurn = turn,
                NextSquadTurn = turn,
            };
            other.NextArmyTurn = other.Hostile ? turn + 3 : int.MaxValue;
            powers.Add(other);
            city.NotifyTerritoryChanged(); // чужая земля рисуется рамкой
            Version++;
            return other;
        }

        /// <summary>
        /// Место для лагеря или города: суша на расстоянии min…max от нашего города, далеко от других,
        /// не в наших будущих границах, и до нас можно дойти.
        /// </summary>
        private Vector2Int? FindSite(int minDistance, int maxDistance, int spacing)
        {
            if (city == null || city.World == null) return null;
            WorldMap world = city.World;
            Vector2Int center = city.CityCenter;
            int safe = city.maxCityRadius + 2;
            var candidates = new List<Vector2Int>();
            for (int r = Mathf.Max(minDistance, safe); r <= maxDistance; r++)
            {
                foreach (Vector2Int cell in WorldMap.Ring(center, r))
                {
                    if (!world.IsPlainLand(cell) || city.InTerritory(cell)) continue;
                    if (powers.Any(p => Distance(p.Cell, cell) < spacing)) continue;
                    candidates.Add(cell);
                }
            }
            for (int attempt = 0; attempt < 12 && candidates.Count > 0; attempt++)
            {
                int pick = Random.Range(0, candidates.Count);
                Vector2Int cell = candidates[pick];
                candidates.RemoveAt(pick);
                if (FindPath(cell, c => city.InTerritory(c)) != null) return cell;
            }
            return null;
        }

        // =====================================================================
        //  Запросы для карты
        // =====================================================================

        public Power PowerAt(Vector2Int cell) => powers.FirstOrDefault(p => p.Cell == cell);

        public List<Squad> SquadsAt(Vector2Int cell) => squads.Where(s => s.Cell == cell).ToList();

        public List<Squad> HostileSquadsAt(Vector2Int cell) => squads.Where(s => s.Cell == cell && s.IsHostile).ToList();

        /// <summary>Клетка — чужая земля (вокруг чужого города или сам лагерь)? Пусто — нет.</summary>
        public string ForeignLandAt(Vector2Int cell)
        {
            foreach (Power power in powers)
            {
                int radius = power.IsCamp ? 0 : cityRadius;
                if (Distance(power.Cell, cell) <= radius) return power.IsCamp ? "Здесь лагерь варваров" : $"Земля города {power.Name}";
            }
            return null;
        }

        /// <summary>Клетка в земле чужого города (для рамки на карте): сам город или null.</summary>
        public Power CityLandAt(Vector2Int cell) =>
            powers.FirstOrDefault(p => p.Kind == PowerKind.City && Distance(p.Cell, cell) <= cityRadius);

        /// <summary>Состав отряда значками классов: «[меч]×4 ур.3  [лук]★★ ур.3».</summary>
        public static string Composition(IEnumerable<BenchUnit> units)
        {
            return string.Join("  ", units
                .Where(u => u?.data != null)
                .GroupBy(u => (u.data.unitClass, u.stars, u.level))
                .OrderByDescending(g => g.Count())
                .Select(g => $"{GameVisuals.ClassIcon(g.Key.unitClass)}{(g.Key.stars > 1 ? GameVisuals.Stars(g.Key.stars) : string.Empty)}" +
                             (g.Count() > 1 ? $"×{g.Count()}" : string.Empty) + $" <size=80%>ур.{g.Key.level}</size>"));
        }

        public static string Composition(Squad squad) => Composition(squad.Units);

        /// <summary>Что отряд делает — для панели и подписи на карте.</summary>
        public string MissionText(Squad squad)
        {
            if (squad.IsCaravan)
            {
                int left = TurnsToArrive(squad);
                return $"везёт {squad.Gold} {GameVisuals.IconGold}, дойдёт через {left} {TurnWord(left)}";
            }
            if (squad.Attacking) return $"<color=#FF7A6E>{GameVisuals.IconWalls} у наших стен!</color>";
            switch (squad.Mission)
            {
                case SquadMission.Raid:
                    if (squad.RaidTarget != null) return $"идёт грабить город {squad.RaidTarget.Name}";
                    int eta = TurnsToArrive(squad);
                    return $"<color=#FF7A6E>идёт на наш город: {GameVisuals.IconWalls} через {eta} {TurnWord(eta)}</color>";
                case SquadMission.Chase:
                    return squad.ChaseArmy != null ? $"<color=#FF7A6E>гонится за: {squad.ChaseArmy.Title.ToLowerInvariant()}</color>" : $"гонится за: {squad.ChaseTarget?.Title}";
                case SquadMission.Return:
                    return "уносит добычу в лагерь";
                default:
                    return "ходит дозором";
            }
        }

        /// <summary>Ближайшая угроза: какой отряд раньше всех будет у стен (для сводки и сообщений).</summary>
        public Squad NextThreat => squads.Where(s => s.Attacking || s.RaidsUs).OrderBy(TurnsToArrive).FirstOrDefault();

        // =====================================================================
        //  Путь по карте
        // =====================================================================

        /// <summary>
        /// Сколько очков хода стоит войти в клетку: лес — весь остаток хода (по лесу 1 клетка за ход), река — riverCost,
        /// остальное — 1. С хоть одним очком войти можно всегда. Горы и озёра не проходят (стоимость — только для расчётов).
        /// </summary>
        public int StepCost(Vector2Int cell)
        {
            if (city == null || city.World == null) return 1;
            switch (city.World.TerrainAt(cell))
            {
                case TerrainType.Forest: return forestEndsMove ? 99 : 2;
                case TerrainType.River: return riverCost;
                case TerrainType.Lake:
                case TerrainType.Mountain: return 99;
                default: return 1;
            }
        }

        /// <summary>По клетке можно идти: не гора и не озеро.</summary>
        public bool Passable(Vector2Int cell)
        {
            if (city == null || city.World == null || !city.World.InBounds(cell)) return false;
            TerrainType type = city.World.TerrainAt(cell);
            return type != TerrainType.Lake && type != TerrainType.Mountain;
        }

        /// <summary>«Цена» клетки в поиске пути: лес и брод обходят, если обход ненамного длиннее.</summary>
        private float PathWeight(Vector2Int cell)
        {
            switch (city.World.TerrainAt(cell))
            {
                case TerrainType.Forest: return forestEndsMove ? forestPathWeight : 2f;
                case TerrainType.River: return riverCost;
                default: return 1f;
            }
        }

        /// <summary>
        /// За сколько ходов пройден путь при speed очков за ход (в первый ход — firstMoves): 1 — в этот (первый) ход.
        /// Лес съедает остаток хода, река — riverCost очков. Пустой путь — 0.
        /// </summary>
        public int TurnsFor(IReadOnlyList<Vector2Int> path, int speed, int firstMoves)
        {
            if (path == null || path.Count == 0) return 0;
            int turn = 1, moves = firstMoves;
            foreach (Vector2Int cell in path)
            {
                if (moves <= 0)
                {
                    turn++;
                    moves = Mathf.Max(1, speed);
                }
                moves = Mathf.Max(0, moves - StepCost(cell));
            }
            return turn;
        }

        /// <summary>Где на пути отряд встанет в конце хода (для крупных точек пунктира): true — тут ход кончается.</summary>
        public List<bool> TurnEnds(IReadOnlyList<Vector2Int> path, int speed, int firstMoves)
        {
            var ends = new List<bool>(path.Count);
            int moves = firstMoves;
            foreach (Vector2Int cell in path)
            {
                if (moves <= 0) moves = Mathf.Max(1, speed);
                moves = Mathf.Max(0, moves - StepCost(cell));
                ends.Add(moves == 0);
            }
            return ends;
        }

        /// <summary>Через сколько ходов отряд дойдёт до клетки рядом с cell: 0 — в этот ход, −1 — не дойти.</summary>
        public int ArmyTurnsTo(PlayerArmy army, Vector2Int cell)
        {
            if (army == null) return -1;
            List<Vector2Int> path = FindPath(army.Cell, c => Distance(c, cell) <= 1);
            if (path == null) return -1;
            return path.Count == 0 ? 0 : TurnsFor(path, armyMoves, army.MovesLeft) - 1;
        }

        /// <summary>Что местность делает с отрядами — для панели клетки.</summary>
        public static string MoveNote(TerrainType terrain)
        {
            switch (terrain)
            {
                case TerrainType.Forest: return "Лес: отряды идут по нему по 1 клетке за ход";
                case TerrainType.River: return "Река: переход вброд — 2 шага вместо одного";
                case TerrainType.Mountain: return "Гора: отряды здесь не пройдут";
                case TerrainType.Lake: return "Озеро: не пройти";
                default: return string.Empty;
            }
        }

        /// <summary>
        /// Кратчайший путь (8 направлений) до клетки, где goal: лес и брод — дороже (обходят, если обход ненамного длиннее),
        /// горы, озёра и чужие города — в обход (город может быть только целью). null — пути нет.
        /// </summary>
        public List<Vector2Int> FindPath(Vector2Int start, Predicate<Vector2Int> goal)
        {
            if (city == null || city.World == null) return null;
            WorldMap world = city.World;
            if (goal(start)) return new List<Vector2Int>();
            var cameFrom = new Dictionary<Vector2Int, Vector2Int> { [start] = start };
            var cost = new Dictionary<Vector2Int, float> { [start] = 0f };
            var done = new HashSet<Vector2Int>();
            var open = new CellHeap();
            open.Push(start, 0f);
            while (open.Count > 0)
            {
                Vector2Int current = open.Pop();
                if (!done.Add(current)) continue;
                if (current != start && goal(current))
                {
                    var path = new List<Vector2Int>();
                    for (Vector2Int cell = current; cell != start; cell = cameFrom[cell]) path.Add(cell);
                    path.Reverse();
                    return path;
                }
                if (current != start && (!Passable(current) || IsCityCell(current))) continue; // цель-город: дальше от него не идут
                foreach (Vector2Int step in Steps8)
                {
                    Vector2Int next = current + step;
                    if (!world.InBounds(next) || done.Contains(next)) continue;
                    bool reached = goal(next);
                    if (!Passable(next) && !(reached && IsCityCell(next))) continue;
                    if (IsCityCell(next) && !reached) continue;
                    float total = cost[current] + PathWeight(next);
                    if (cost.TryGetValue(next, out float known) && known <= total) continue;
                    cost[next] = total;
                    cameFrom[next] = current;
                    open.Push(next, total);
                }
            }
            return null;
        }

        /// <summary>Очередь клеток по «цене» (двоичная куча) для поиска пути.</summary>
        private sealed class CellHeap
        {
            private readonly List<(float cost, Vector2Int cell)> items = new List<(float cost, Vector2Int cell)>();

            public int Count => items.Count;

            public void Push(Vector2Int cell, float cost)
            {
                items.Add((cost, cell));
                int i = items.Count - 1;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (items[parent].cost <= items[i].cost) break;
                    (items[parent], items[i]) = (items[i], items[parent]);
                    i = parent;
                }
            }

            public Vector2Int Pop()
            {
                Vector2Int top = items[0].cell;
                int last = items.Count - 1;
                items[0] = items[last];
                items.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int left = i * 2 + 1, right = left + 1, smallest = i;
                    if (left < items.Count && items[left].cost < items[smallest].cost) smallest = left;
                    if (right < items.Count && items[right].cost < items[smallest].cost) smallest = right;
                    if (smallest == i) break;
                    (items[smallest], items[i]) = (items[i], items[smallest]);
                    i = smallest;
                }
                return top;
            }
        }

        private bool IsCityCell(Vector2Int cell)
        {
            foreach (Power power in powers)
            {
                if (power.IsCity && power.Cell == cell) return true;
            }
            return false;
        }

        public static int Distance(Vector2Int a, Vector2Int b) => Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));

        public void NotifyChanged()
        {
            Version++;
            Changed?.Invoke();
        }
    }
}
