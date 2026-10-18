using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Casiwar
{
    public enum GamePhase
    {
        Preparation, // ход: вкладки «Город», «Наука», «Поле», «Войска» (начинается с «Города»)
        Battle,      // к городу пришёл отряд: бой на клетках со слотом
        Loot,        // трофеи после победы
        GameOver,
    }

    /// <summary>
    /// Главный цикл забега — ходы:
    ///   начало хода — мир делает шаг (WorldPowers.AdvanceTurn): отряды варваров ходят дозором и ищут добычу,
    ///             армии чужих городов идут к нам, стража городов бьёт варваров, караваны доходят и приносят золото,
    ///             появляются новые лагеря и города, наш отряд идёт по приказу дальше; приходят очки знаний;
    ///             жители едят, излишек еды растит население, новые дома ставятся сами (CityManager.ProcessNewDay);
    ///             и сам собой идёт сбор ресурсов (ResourceRain): самые большие кластеры 🪙 ⚒ 🍎 — в казну;
    ///   ход     — «Город»: карта мира — строим, улучшаем, шлём караваны, водим отряд и нападаем на отряды и лагеря
    ///             (бой в поле — посреди хода, после него ход продолжается). Армия — как в «Героях 3»: гарнизон защищает
    ///             город, отряды (у каждого слот богатыря-вожака, у гарнизона тоже) ходят по карте; отряд на ратуше
    ///             меняется бойцами с гарнизоном, а пока он в поле — на ратуше собирают следующий (панель армии видна
    ///             на «Поле» и на карте, когда выбраны ратуша или отряд);
    ///             «Наука»: изучаем технологии за очки знаний;
    ///             «Поле»: слот вербовки (RecruitSlot) — спины по населению, гильдиям и усилителям, линии дают бойцов;
    ///             тут же фокус сбора ресурсов на следующий ход;
    ///             «Войска»: прокачиваем отряды за золото в гильдиях (ближний бой, дальний бой, магия);
    ///   конец   — «Конец хода», а если враг стоит у нашего отряда в поле или в наших границах — «В БОЙ!» (бой обязателен);
    ///             у стен бьются гарнизон и отряды в границах; защищать некому — враг грабит город без боя;
    ///   бой     — в реальном времени: юниты сами идут и бьют, а мы собираем три в ряд на поле боя — навыки классов,
    ///             4 в ряд сильнее, 5 — суперудар (BattleBoard); враг применяет навыки сам;
    ///   победа  — трофеи (броня и оружие); продержались daysToWin ходов — победа,
    ///             но забег не кончается (бесконечный режим), пока стоит ратуша;
    ///   поражение у стен — уцелевшие враги бьют по зданиям и уходят.
    /// Здоровья у города нет — есть прочность зданий. Разрушена ратуша — забег окончен.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public enum PrepTab
        {
            Board,
            City,
            Tech,
            Units,
        }

        [Header("Системы")]
        public CityGridManager cityGrid;
        public LootSlotMachine lootSlot;
        public BenchManager bench;
        public AutoBattleManager battle;
        public CityManager city;
        public TechTree techTree;
        [Tooltip("Лагеря варваров, чужие города, отряды и караваны (нет в сцене — добавится сам)")]
        public WorldPowers powers;
        [Tooltip("Сбор ресурсов в начале хода (нет в сцене — добавится сам)")]
        public ResourceRain rain;
        [Tooltip("Вербовка на вкладке «Поле» (нет в сцене — добавится сама)")]
        public RecruitSlot recruit;

        [Header("Экраны (панели Canvas)")]
        public GameObject prepScreen;
        public GameObject boardTab;
        public GameObject cityTab;
        public GameObject techTab;
        public GameObject unitsTab;
        public GameObject battleScreen;
        public GameObject lootScreen;
        public GameObject gameOverScreen;
        [Tooltip("Армия (гарнизон и отряд): видна на поле, на трофеях и на карте, когда выбраны ратуша или отряд")]
        public GameObject benchPanel;
        [Tooltip("Высота панели армии на карте в горизонтальной раскладке (под панелью здания)")]
        [Min(200f)] public float mapBenchHeight = 400f;

        [Tooltip("Версия сборщика, которой собрана сцена (CasiwarSceneBuilder.SceneVersion). Старая — редактор предложит пересобрать.")]
        [HideInInspector] public int sceneVersion;

        [Header("Кнопки вкладок (подсветка активной)")]
        public Image boardTabButton;
        public Image cityTabButton;
        public Image techTabButton;
        public Image unitsTabButton;
        public Color tabActiveColor = new Color(0.36f, 0.42f, 0.56f);
        public Color tabIdleColor = new Color(0.20f, 0.22f, 0.29f);
        [Tooltip("Цвет главной кнопки: «Конец хода» и «В БОЙ!»")]
        public Color endTurnColor = new Color(0.24f, 0.50f, 0.72f);
        public Color fightColor = new Color(0.85f, 0.30f, 0.25f);

        [Header("Подсветка «сюда стоит нажать» (пусто — кнопки найдутся сами)")]
        [Tooltip("«Конец хода» / «В БОЙ!» — пульсирует, когда на поле больше ничего не сделать")]
        public Button fightButton;
        [Tooltip("«Дальше» на трофеях — пульсирует, когда крутки кончились")]
        public Button lootNextButton;

        [Header("HUD (необязательно)")]
        public TMP_Text dayText;
        public TMP_Text goldText;
        public TMP_Text productionText;
        public TMP_Text foodText;
        public TMP_Text populationText;
        public TMP_Text knowledgeText;
        public TMP_Text messageText;
        public GameObject messagePanel;
        public TMP_Text gameOverText;

        [Header("Забег (стартовые ресурсы — в CityManager)")]
        [Tooltip("Сколько ходов нужно продержаться, чтобы победить; дальше игра идёт без конца (бесконечный режим)")]
        [Min(1)] public int daysToWin = 50;
        [Tooltip("Юниты, с которыми игрок начинает забег")]
        public List<UnitData> startingUnits = new List<UnitData>();

        [Header("Награда за отбитое нападение")]
        [Min(0)] public int winGold = 4;

        [Header("Сообщения")]
        [Min(1)] public int maxMessages = 3;
        [Min(0.5f)] public float messageDuration = 2.5f;

        public GamePhase Phase { get; private set; }
        public PrepTab Tab { get; private set; }
        /// <summary>Номер хода.</summary>
        public int Turn { get; private set; } = 1;

        /// <summary>Казна города (золото хранит CityManager).</summary>
        public int Gold => city != null ? city.Gold : 0;

        private readonly List<(string text, float until)> messages = new List<(string text, float until)>();
        private TMP_Text fightLabel;
        /// <summary>Отряды, с которыми идёт бой у стен (после боя их убирают с карты).</summary>
        private List<Squad> fighting = new List<Squad>();
        /// <summary>Бой в поле (null — бой у стен): после него ход продолжается.</summary>
        private FieldBattle fieldBattle;
        /// <summary>После трофеев — вернуться в этот же ход (бой был в поле), а не начать следующий.</summary>
        private bool resumeAfterLoot;
        private CityMapView mapView;
        private AdaptiveLayout adaptive;

        private void Awake()
        {
            if (cityGrid == null) cityGrid = FindFirstObjectByType<CityGridManager>();
            if (lootSlot == null) lootSlot = FindFirstObjectByType<LootSlotMachine>();
            if (bench == null) bench = FindFirstObjectByType<BenchManager>();
            if (battle == null) battle = FindFirstObjectByType<AutoBattleManager>();
            if (city == null) city = FindFirstObjectByType<CityManager>();
            if (techTree == null) techTree = FindFirstObjectByType<TechTree>();
            if (powers == null) powers = FindFirstObjectByType<WorldPowers>();
            if (powers == null) powers = gameObject.AddComponent<WorldPowers>(); // сцена старой версии
            if (powers.city == null) powers.city = city;
            if (powers.battle == null) powers.battle = battle;
            if (powers.bench == null) powers.bench = bench;
            if (rain == null) rain = FindFirstObjectByType<ResourceRain>();
            if (rain == null) rain = gameObject.AddComponent<ResourceRain>(); // сцена старой версии
            if (rain.city == null) rain.city = city;
            if (rain.tilePrefab == null && cityGrid != null) rain.tilePrefab = cityGrid.tilePrefab;
            if (rain.font == null && messageText != null) rain.font = messageText.font;
            rain.Finished += OnRainFinished;
            if (recruit == null) recruit = FindFirstObjectByType<RecruitSlot>();
            if (recruit == null) recruit = gameObject.AddComponent<RecruitSlot>(); // сцена старой версии
            if (recruit.city == null) recruit.city = city;
            if (recruit.board == null) recruit.board = cityGrid;
            if (recruit.bench == null) recruit.bench = bench;
            if (recruit.game == null) recruit.game = this;
            recruit.Finished += OnRecruitFinished;

            if (lootSlot != null) lootSlot.Finished += OnLootFinished;
            if (city != null) city.Changed += OnCityChanged;
            if (powers != null) powers.Changed += OnWorldChanged;
            if (bench != null) bench.Changed += OnArmyChanged;
            mapView = FindFirstObjectByType<CityMapView>(FindObjectsInactive.Include);
            if (mapView != null) mapView.ArmyContextChanged += ApplyBenchPanel;
            adaptive = FindFirstObjectByType<AdaptiveLayout>();
            if (adaptive != null) adaptive.Applied += ApplyBenchPanel;
            ArrangeTabs();
            FindProgressButtons();
            Image messageBackground = messagePanel != null ? messagePanel.GetComponent<Image>() : null;
            if (messageBackground != null) messageBackground.color = new Color(0.03f, 0.03f, 0.05f, 0.95f); // не просвечивает сводку под собой
            if (GetComponent<KeyboardShortcuts>() == null) gameObject.AddComponent<KeyboardShortcuts>(); // горячие клавиши (ПК)
        }

        /// <summary>Кнопки «В БОЙ!» и «Дальше» — по их обработчику (сцены старой версии не знают этих полей).</summary>
        private void FindProgressButtons()
        {
            if (fightButton != null && lootNextButton != null)
            {
                fightLabel = fightButton.GetComponentInChildren<TMP_Text>(true);
                return;
            }
            foreach (Button button in FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                {
                    if (button.onClick.GetPersistentTarget(i) != this) continue;
                    string method = button.onClick.GetPersistentMethodName(i);
                    if ((method == nameof(StartBattle) || method == nameof(EndTurn)) && fightButton == null) fightButton = button;
                    else if (method == nameof(FinishLoot) && lootNextButton == null) lootNextButton = button;
                }
            }
            if (fightButton != null) fightLabel = fightButton.GetComponentInChildren<TMP_Text>(true);
        }

        /// <summary>Порядок вкладок «Город, Наука, Поле, Войска» — и в сценах, собранных старой версией.</summary>
        private void ArrangeTabs()
        {
            Image[] order = { cityTabButton, techTabButton, boardTabButton, unitsTabButton };
            int index = 0;
            foreach (Image tab in order)
            {
                if (tab != null) tab.transform.SetSiblingIndex(index++);
            }
        }

        private void Start() => StartRun();

        private void Update()
        {
            if (messages.RemoveAll(m => Time.time > m.until) > 0) RefreshMessages();
            UpdateProgressHighlights();
        }

        /// <summary>«Можно идти дальше»: «Конец хода» / «В БОЙ!» — когда на поле больше ничего не сделать, «Дальше» — когда крутки кончились.</summary>
        private void UpdateProgressHighlights()
        {
            // Пока на «Поле» есть спины вербовки, ход не кончить: пульсирует вкладка «Поле», а не «Конец хода»
            bool spinsLeft = Phase == GamePhase.Preparation && recruit != null && !recruit.Done;
            bool fight = Phase == GamePhase.Preparation && !spinsLeft;
            AttentionPulse.Set(boardTabButton, spinsLeft && Tab != PrepTab.Board);
            bool next = Phase == GamePhase.Loot && lootSlot != null && lootSlot.SpinsLeft <= 0 && !lootSlot.IsSpinning;
            if (fightButton != null) AttentionPulse.Set(fightButton.targetGraphic, fight);
            if (lootNextButton != null) AttentionPulse.Set(lootNextButton.targetGraphic, next);
        }

        /// <summary>Вкладки, где можно что-то купить: «Город» — улучшения зданий, «Войска» — гильдий, «Наука» — хватает на технологию.</summary>
        private void RefreshTabHighlights()
        {
            if (city == null) return;
            bool prep = Phase == GamePhase.Preparation;
            AttentionPulse.Set(cityTabButton, prep && Tab != PrepTab.City && city.AnyUpgradeNow(guilds: false));
            AttentionPulse.Set(unitsTabButton, prep && Tab != PrepTab.Units && city.AnyUpgradeNow(guilds: true));
            AttentionPulse.Set(techTabButton, prep && Tab != PrepTab.Tech && techTree != null &&
                                              techTree.techs.Any(t => t != null && !techTree.IsResearched(t.id) && techTree.PrerequisitesMet(t)
                                                                      && city.Knowledge >= t.knowledgeCost));
        }

        // =====================================================================
        //  Цикл забега
        // =====================================================================

        /// <summary>Новый забег (кнопка «Новый забег» на экране Game Over).</summary>
        public void StartRun()
        {
            Turn = 1;
            messages.Clear();
            RefreshMessages();
            if (techTree != null) techTree.ResetTree();
            if (city != null) city.ResetCity();
            if (powers != null) powers.ResetWorld();

            if (bench != null)
            {
                bench.ClearAll();
                foreach (UnitData unit in startingUnits)
                {
                    if (unit != null) bench.AddToHomeSquad(unit, 1); // стартовые бойцы — сразу в отряде на ратуше
                }
            }

            BeginTurn(firstTurn: true, news: null);
            ShowMessage($"На «Поле» крутится вербовка: ополченцы по линиям приходят в армию. Изучите за {GameVisuals.IconKnowledge} «Военное дело», " +
                        "«Стрельбу из лука» или «Мистицизм» и постройте гильдию — ополченцы станут бойцами, а гильдия даст ещё спин!", 6f);
            ShowMessage("Новобранцы приходят в гарнизон — он защищает город. Отряд ходит по карте: левый клик — выбрать, правый клик по клетке — куда идти. " +
                        "Отряд на ратуше меняется бойцами с гарнизоном (тяните бойца или подпись линейки); ушёл — собирайте следующий", 8f);
        }

        private void BeginTurn(bool firstTurn, List<string> news)
        {
            Phase = GamePhase.Preparation;
            if (!firstTurn && city != null) ShowMessage(city.ProcessNewDay(), 5f); // начало хода: знания, еда, рост города
            if (news != null)
            {
                foreach (string line in news) ShowMessage(line, 6f);
            }
            ApplyScreens();
            ShowCityTab(); // ход начинается в городе: карта, стройка → наука → поле → бой
            if (recruit != null) recruit.BeginTurn(); // вербовка: спины ждут вкладку «Поле»
            if (bench != null) bench.SetInteractable(true);
            UpdateHud();
            if (rain != null) rain.Play(Turn); // ресурсы приходят сами — поверх экрана, быстро
        }

        private void OnRainFinished(string summary)
        {
            if (Phase == GamePhase.Preparation) ShowMessage($"Сбор ресурсов: {summary}", 4f);
        }

        /// <summary>Вернуться в ход после боя в поле: тот же ход, оставшиеся спины вербовки, карта.</summary>
        private void ResumeTurn()
        {
            Phase = GamePhase.Preparation;
            ApplyScreens();
            ShowCityTab();
            if (recruit != null) recruit.Resume();
            if (bench != null) bench.SetInteractable(true);
            UpdateHud();
            if (powers != null && powers.MustFight)
                ShowMessage($"Рядом ещё враги — «{FightButtonText}» в конце хода, или уводите отряд", 5f);
        }

        public void ShowBoardTab() => SetTab(PrepTab.Board);
        public void ShowCityTab() => SetTab(PrepTab.City);
        public void ShowTechTab() => SetTab(PrepTab.Tech);
        public void ShowUnitsTab() => SetTab(PrepTab.Units);

        private void SetTab(PrepTab tab)
        {
            Tab = tab;
            if (boardTab != null) boardTab.SetActive(tab == PrepTab.Board);
            if (cityTab != null) cityTab.SetActive(tab == PrepTab.City);
            if (techTab != null) techTab.SetActive(tab == PrepTab.Tech);
            if (unitsTab != null) unitsTab.SetActive(tab == PrepTab.Units);
            if (boardTabButton != null) boardTabButton.color = tab == PrepTab.Board ? tabActiveColor : tabIdleColor;
            if (cityTabButton != null) cityTabButton.color = tab == PrepTab.City ? tabActiveColor : tabIdleColor;
            if (techTabButton != null) techTabButton.color = tab == PrepTab.Tech ? tabActiveColor : tabIdleColor;
            if (unitsTabButton != null) unitsTabButton.color = tab == PrepTab.Units ? tabActiveColor : tabIdleColor;
            ApplyBenchPanel();
            RefreshTabHighlights();
        }

        /// <summary>Изучить технологию за очки знаний (кнопка в дереве науки).</summary>
        public bool TryResearch(string techId)
        {
            if (Phase != GamePhase.Preparation || techTree == null || city == null) return false;
            TechConfig tech = techTree.Find(techId);
            if (tech == null || techTree.IsResearched(techId)) return false;

            string missing = techTree.MissingPrerequisites(tech);
            if (missing != null)
            {
                ShowMessage($"Сначала изучите: {missing}");
                return false;
            }
            if (!city.TrySpendKnowledge(tech.knowledgeCost))
            {
                ShowMessage($"Нужно {tech.knowledgeCost} {GameVisuals.IconKnowledge} — очки знаний приходят каждый ход (+{city.KnowledgePerDay})");
                return false;
            }

            techTree.MarkResearched(techId);
            ShowMessage($"Изучено: {tech.title}. {tech.description}", 4f);
            return true;
        }

        /// <summary>
        /// Кнопка «Конец хода» / «В БОЙ!» (Enter): пока на «Поле» есть спины вербовки — нельзя (крутится — сразу итог);
        /// враг в наших границах — бой с ним (обязательно), нет — ход кончается, мир делает шаг.
        /// </summary>
        public void EndTurn()
        {
            if (Phase != GamePhase.Preparation) return;
            if (rain != null && rain.IsPlaying)
            {
                rain.Skip();
                return;
            }
            if (recruit != null && recruit.IsPlaying)
            {
                recruit.ResolveRemaining(); // вербовка крутится — сразу итог, ход не кончаем
                return;
            }
            if (recruit != null && !recruit.Done)
            {
                // Не докрутили вербовку — ход не кончаем (вкладка «Поле» пульсирует)
                ShowMessage($"Сначала вербовка: на «Поле» осталось спинов — {recruit.SpinsLeft}", 4f);
                return;
            }
            if (powers != null)
            {
                // Сначала — враги у отрядов в поле (по одному бою), потом — у стен
                PlayerArmy engaged = powers.EngagedArmy;
                if (engaged != null)
                {
                    var ambush = new FieldBattle { Ambush = true, Army = engaged };
                    ambush.Squads.AddRange(powers.EngagersOf(engaged));
                    FightInField(ambush);
                    return;
                }
                if (powers.UnderAttack)
                {
                    if (powers.CityDefended) Fight(powers.Attackers);
                    else PlunderUndefended(powers.Attackers);
                    return;
                }
            }
            NextTurn();
        }

        /// <summary>
        /// Армия наступила на врага на карте (клик по отряду или лагерю): бой в поле посреди хода.
        /// После боя (и трофеев) ход продолжается. false — сейчас нельзя.
        /// </summary>
        public bool AttackFromMap(FieldBattle fight)
        {
            if (fight == null || Phase != GamePhase.Preparation) return false;
            FightInField(fight);
            return true;
        }

        /// <summary>Бой в поле: бьётся один наш отряд (его вожак усиливает его).</summary>
        private void FightInField(FieldBattle fight)
        {
            fieldBattle = fight;
            ArmyRow row = fight.Army != null ? fight.Army.Row : null;
            List<BenchUnit> squad = row != null ? row.Units.ToList() : new List<BenchUnit>();
            string who = fight.Army != null ? fight.Army.Title : "Отряд";
            StartBattleScreen(squad, row != null ? new[] { row } : null, fight.Enemies,
                fight.Ambush ? $"{GameVisuals.IconAttack} На {who.ToLowerInvariant()} напали: {fight.Title}" : $"{GameVisuals.IconAttack} {who}: {fight.Title}");
        }

        /// <summary>Гарнизон пуст и отряд в поле (или его нет) — вошедший в границы враг грабит город без боя и уходит.</summary>
        private void PlunderUndefended(List<Squad> attackers)
        {
            List<BenchUnit> raiders = attackers.SelectMany(s => s.Units).ToList();
            string raid = city != null && battle != null ? city.ApplyRaid(battle.RaidHits(raiders, Turn)) : string.Empty;
            string names = string.Join(", ", attackers.Select(s => s.Title));
            if (powers != null) powers.ResolveAttack(attackers);
            ShowMessage($"Город грабят без боя — гарнизон пуст{(powers != null && powers.ArmyPresent ? ", отряды в поле" : string.Empty)}! {names}" +
                        (raid.Length > 0 ? $" — {raid}" : string.Empty), 7f);
            if (city != null && city.IsTownHallRuined)
            {
                EndRun(Turn > daysToWin
                    ? $"РАТУША РАЗРУШЕНА\n\nПобеда была на {daysToWin}-м ходу, а город продержался до {Turn}-го"
                    : $"РАТУША РАЗРУШЕНА\n\nГород пал на {Turn}-м ходу");
                return;
            }
            NextTurn();
        }

        /// <summary>Старое имя кнопки (сцены старой версии): то же, что «Конец хода».</summary>
        public void StartBattle() => EndTurn();

        /// <summary>
        /// Бой у стен с отрядами, которые стоят в наших границах (их бойцы — одним строем). Бьётся гарнизон, а наши
        /// отряды в границах — вместе с ним (впереди; сколько влезет на арену). Вожак каждой линейки усиливает свою линейку.
        /// </summary>
        private void Fight(List<Squad> attackers)
        {
            fighting = attackers;
            fieldBattle = null;
            var rows = new List<ArmyRow>();
            if (powers != null) rows.AddRange(powers.DefendingArmies(battle != null ? battle.PlayerCapacity : 21).Select(a => a.Row));
            int squadsIn = rows.Count;
            if (bench != null) rows.Add(bench.GarrisonRow);
            List<BenchUnit> defenders = rows.SelectMany(r => r.Units).ToList();
            string who = squadsIn == 0 ? " — бьётся гарнизон" : bench != null && bench.GarrisonCount > 0 ? " — гарнизон и отряды" : " — бьются отряды";
            StartBattleScreen(defenders, rows, attackers.SelectMany(s => s.Units).ToList(),
                $"{GameVisuals.IconWalls} {string.Join(", ", attackers.Select(s => s.Title))}{who}");
        }

        /// <summary>Экран боя: army — наши бойцы по порядку строя, rows — их линейки (вожак линейки усиливает её бойцов).</summary>
        private void StartBattleScreen(List<BenchUnit> army, IEnumerable<ArmyRow> rows, List<BenchUnit> enemies, string title)
        {
            if (battle == null)
            {
                Debug.LogError("[GameManager] Не назначен AutoBattleManager.");
                return;
            }
            Phase = GamePhase.Battle;
            if (recruit != null) recruit.Pause();
            if (bench != null) bench.SetInteractable(false);
            messages.Clear(); // сообщения хода не должны висеть над боем
            RefreshMessages();
            ApplyScreens();
            UpdateHud();
            battle.StartBattle(army, enemies, Turn, OnBattleFinished, rows);
            ShowMessage(title, 4f);
        }

        private void OnBattleFinished(BattleOutcome outcome)
        {
            // Погибшие пропадают навсегда, выжившие получают опыт и уровни
            string report = bench != null ? string.Join(" · ", bench.ApplyBattleResults(outcome.PlayerUnits, outcome.Victory)) : string.Empty;
            if (fieldBattle != null)
            {
                OnFieldBattleFinished(outcome, report);
                return;
            }
            string enemyName = fighting.Count > 0 ? string.Join(", ", fighting.Select(s => s.Title)) : "Враг";
            if (powers != null) powers.ResolveAttack(fighting);
            fighting = new List<Squad>();

            if (outcome.Victory)
            {
                int reward = winGold + Turn / 2;
                AddGold(reward);
                ShowMessage($"Нападение отбито! +{reward} {GameVisuals.IconGold}", 3.5f);
                ShowMessage(report, 6f);
                resumeAfterLoot = false;
                EnterLoot(outcome.KilledStars);
                return;
            }

            // Поражение: уцелевшие враги бьют по зданиям и уходят
            string raid = city != null ? city.ApplyRaid(outcome.RaidHits) : string.Empty;
            ShowMessage($"Город разграблен! {enemyName} ({outcome.RaidHits.Count})" + (raid.Length > 0 ? $" — {raid}" : string.Empty), 6f);
            ShowMessage(report, 6f);
            if (city != null && city.IsTownHallRuined)
            {
                EndRun(Turn > daysToWin
                    ? $"РАТУША РАЗРУШЕНА\n\nПобеда была на {daysToWin}-м ходу, а город продержался до {Turn}-го"
                    : $"РАТУША РАЗРУШЕНА\n\nГород пал на {Turn}-м ходу");
                return;
            }
            NextTurn();
        }

        /// <summary>Бой в поле кончился: победа — золото (за лагерь больше) и трофеи; поражение — враг теряет погибших. Ход продолжается.</summary>
        private void OnFieldBattleFinished(BattleOutcome outcome, string report)
        {
            FieldBattle fight = fieldBattle;
            fieldBattle = null;
            int bounty = fight.Camp != null && powers != null ? powers.CampBounty : 0;
            if (powers != null) powers.ResolveFieldBattle(fight, outcome.Victory, outcome.EnemyFallen);
            if (outcome.Victory)
            {
                int reward = winGold + Turn / 2 + bounty;
                AddGold(reward);
                ShowMessage(fight.Camp != null
                    ? $"Лагерь «{fight.Camp.Name}» сожжён! +{reward} {GameVisuals.IconGold}"
                    : $"Победа в поле! +{reward} {GameVisuals.IconGold}", 4f);
                ShowMessage(report, 6f);
                resumeAfterLoot = true;
                EnterLoot(outcome.KilledStars);
                return;
            }
            ShowMessage(fight.Army == null || fight.Army.IsEmpty
                ? $"{fight.Army?.Title ?? "Отряд"} разбит. Новый соберите из гарнизона — на ратуше"
                : $"Бой проигран — враг устоял ({outcome.EnemySurvivors.Count} осталось)", 6f);
            ShowMessage(report, 6f);
            ResumeTurn();
        }

        /// <summary>
        /// Трофеи: круток столько, сколько звёзд у убитых врагов (★ — 1, ★★ — 2, ★★★ — 3). Крутятся сами, клик — сразу итог,
        /// после последней крутки экран закрывается сам (OnLootFinished).
        /// </summary>
        private void EnterLoot(int killedStars)
        {
            Phase = GamePhase.Loot;
            ApplyScreens();
            if (bench != null) bench.SetInteractable(true);
            int spins = Mathf.Max(1, killedStars);
            if (lootSlot != null) lootSlot.BeginLoot(spins);
            ShowMessage($"Трофеи: {GameVisuals.IconSpin}{spins} ({GameVisuals.IconStar} убитого врага = {GameVisuals.IconSpin}1) · клик — сразу итог", 4f);
            UpdateHud();
        }

        /// <summary>Кнопка «Дальше» (Enter) на трофеях: крутки ещё идут — сразу итог (экран закроется сам), иначе — дальше.</summary>
        public void FinishLoot()
        {
            if (Phase != GamePhase.Loot) return;
            if (lootSlot != null && lootSlot.IsActive)
            {
                lootSlot.Skip(); // → OnLootFinished
                return;
            }
            LeaveLoot();
        }

        /// <summary>Уйти с трофеев: после боя в поле — тот же ход, после боя у стен — следующий.</summary>
        private void LeaveLoot()
        {
            if (lootSlot != null) lootSlot.EndLoot();
            if (resumeAfterLoot)
            {
                resumeAfterLoot = false;
                ResumeTurn();
                return;
            }
            NextTurn();
        }

        /// <summary>Конец хода: мир делает шаг (отряды идут, караваны доходят, появляются лагеря и города), новый ход.</summary>
        private void NextTurn()
        {
            Turn++;
            List<string> news = powers != null ? powers.AdvanceTurn(Turn) : new List<string>();
            if (Turn == daysToWin + 1)
                news.Insert(0, $"ПОБЕДА! Город продержался {daysToWin} ходов. Дальше — бесконечный режим: сколько ещё продержитесь?");
            BeginTurn(firstTurn: false, news);
        }

        private void EndRun(string text)
        {
            Phase = GamePhase.GameOver;
            resumeAfterLoot = false;
            fieldBattle = null;
            if (recruit != null) recruit.Pause();
            if (lootSlot != null) lootSlot.EndLoot();
            if (bench != null) bench.SetInteractable(false);
            if (gameOverText != null) gameOverText.text = text;
            ApplyScreens();
            UpdateHud();
        }

        /// <summary>Все спины вербовки сделаны: кто пришёл — сообщением.</summary>
        private void OnRecruitFinished(string summary)
        {
            if (Phase == GamePhase.Preparation) ShowMessage($"Вербовка: {summary}", 5f);
        }

        /// <summary>Трофеи докрутились (или пропущены): итог — сообщением, экран закрывается сам.</summary>
        private void OnLootFinished(string summary)
        {
            if (Phase != GamePhase.Loot) return;
            ShowMessage($"Трофеи: {summary}", 5f);
            LeaveLoot();
        }

        private void OnWorldChanged()
        {
            UpdateHud();
            if (bench != null) bench.OnWorldChanged(); // отряд пришёл на ратушу или ушёл с неё
        }

        /// <summary>
        /// Бойцов переставили, пришли новобранцы, кто-то погиб — фигурки отрядов на карте меняются: опустевший в поле
        /// отряд пропадает, заполненный на ратуше выходит на карту, а там встаёт новый пустой.
        /// </summary>
        private void OnArmyChanged()
        {
            if (powers == null) return;
            powers.SyncArmies();
            powers.NotifyChanged();
        }

        /// <summary>
        /// Панель армии: на «Поле», на трофеях и на карте, когда выбраны ратуша или отряд (тогда снизу — он). На карте
        /// в горизонтальной раскладке она встаёт под панелью здания (её ширины), иначе — на своё место из раскладки.
        /// </summary>
        private void ApplyBenchPanel()
        {
            if (benchPanel == null) return;
            bool onMap = Phase == GamePhase.Preparation && Tab == PrepTab.City && mapView != null && mapView.ArmyContext;
            bool show = Phase == GamePhase.Loot || (Phase == GamePhase.Preparation && Tab == PrepTab.Board) || onMap;
            if (bench != null) bench.Focus(onMap ? mapView.SelectedArmy : null); // снизу — выбранный на карте отряд
            benchPanel.SetActive(show);
            var rect = (RectTransform)benchPanel.transform;
            if (adaptive != null && adaptive.TryGetPose(rect, out AdaptiveLayout.Pose pose)) pose.ApplyTo(rect);
            bool compact = onMap && adaptive != null && adaptive.IsLandscape && mapView.PanelWidth > 0f;
            if (compact)
            {
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f);
                rect.anchoredPosition = new Vector2(-mapView.PanelRightMargin, 0f);
                rect.sizeDelta = new Vector2(mapView.PanelWidth, mapBenchHeight);
            }
            if (mapView != null) mapView.SetBenchReserve(compact ? mapBenchHeight + 10f : 0f);
            EnsureMapMessageDock();
            if (mapMessageMarker != null) mapMessageMarker.SetActive(compact);
        }

        private GameObject mapMessageMarker;

        /// <summary>Панель армии на карте занимает низ правой колонки — сообщения тогда идут вниз карты, а не на неё.</summary>
        private void EnsureMapMessageDock()
        {
            if (mapMessageMarker != null || adaptive == null || adaptive.messagePanel == null || benchPanel == null) return;
            mapMessageMarker = new GameObject("MapMessageDock", typeof(RectTransform));
            mapMessageMarker.transform.SetParent(benchPanel.transform, false);
            mapMessageMarker.SetActive(false);
            adaptive.AddMessageDockFirst(new AdaptiveLayout.Dock
            {
                whenActive = mapMessageMarker,
                pose = new AdaptiveLayout.Pose
                {
                    anchorMin = Vector2.zero,
                    anchorMax = Vector2.zero,
                    pivot = Vector2.zero,
                    anchoredPosition = new Vector2(20f, 16f),
                    sizeDelta = new Vector2(1100f, 100f),
                    scale = 1f,
                },
            });
        }

        /// <summary>Подпись главной кнопки: враг у армии или у стен — «В БОЙ!», иначе «Конец хода».</summary>
        private string FightButtonText => powers != null && powers.MustFight ? "В БОЙ!" : "Конец хода";

        private void OnCityChanged()
        {
            UpdateHud();
            RefreshTabHighlights();
            // Новое здание с классом (казармы, святилище) — сразу видно на поле в оставшихся символах
        }

        // =====================================================================
        //  Золото (хранит город; здесь — для скамейки, полей и наград)
        // =====================================================================

        public void AddGold(int amount)
        {
            if (city != null) city.AddGold(amount);
        }

        public bool TrySpendGold(int amount) => city != null && city.TrySpendGold(amount);

        // =====================================================================
        //  UI
        // =====================================================================

        public void ShowMessage(string text, float duration = -1f)
        {
            if (string.IsNullOrEmpty(text)) return;
            messages.Add((text, Time.time + (duration > 0f ? duration : messageDuration)));
            while (messages.Count > maxMessages) messages.RemoveAt(0);
            RefreshMessages();
        }

        private void RefreshMessages()
        {
            if (messageText != null) messageText.text = string.Join("\n", messages.Select(m => m.text));
            if (messagePanel != null) messagePanel.SetActive(messages.Count > 0);
        }

        private void ApplyScreens()
        {
            if (prepScreen != null) prepScreen.SetActive(Phase == GamePhase.Preparation);
            if (battleScreen != null) battleScreen.SetActive(Phase == GamePhase.Battle);
            if (lootScreen != null) lootScreen.SetActive(Phase == GamePhase.Loot);
            if (gameOverScreen != null) gameOverScreen.SetActive(Phase == GamePhase.GameOver);
            ApplyBenchPanel();
            RefreshTabHighlights();
        }

        private void UpdateHud()
        {
            if (dayText != null) dayText.text = Turn <= daysToWin ? $"{GameVisuals.IconTurns} {Turn}/{daysToWin}" : $"{GameVisuals.IconTurns} {Turn} ★";
            if (fightLabel != null) fightLabel.text = FightButtonText;
            if (fightButton != null && fightButton.targetGraphic != null)
                fightButton.targetGraphic.color = powers != null && powers.MustFight ? fightColor : endTurnColor;
            if (goldText != null) goldText.text = $"{GameVisuals.IconGold} {Gold}";
            if (city == null) return;
            if (productionText != null) productionText.text = $"{GameVisuals.IconProduction} {city.Production}";
            if (foodText != null) foodText.text = $"{GameVisuals.IconFood} {city.Food}/{city.GrowthCost}";
            if (populationText != null) populationText.text = $"{GameVisuals.IconPopulation} {city.Population}";
            if (knowledgeText != null) knowledgeText.text = $"{GameVisuals.IconKnowledge} {city.Knowledge}";
        }
    }
}
