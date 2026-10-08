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
        Preparation, // день: вкладки «Город», «Наука», «Поле», «Войска» (начинается с «Города»)
        Battle,      // набег варваров: бой на клетках со слотом
        Loot,        // трофеи после победы
        GameOver,
    }

    /// <summary>
    /// Главный цикл забега — дни:
    ///   утро    — приходят очки знаний; жители едят, излишек еды растит население → поле больше,
    ///             новые дома ставятся сами (CityManager.ProcessNewDay); начинает действовать вчерашний фокус поля;
    ///   день    — «Поле»: складываем юнитов и ресурсы (золото, производство, еда), выбираем фокус на завтра;
    ///             «Город»: строим здания за производство, улучшаем их за золото;
    ///             «Наука»: изучаем технологии за очки знаний;
    ///             «Войска»: прокачиваем отряды за золото в гильдиях (ближний бой, дальний бой, магия);
    ///   вечер   — «В БОЙ!»: приходит волна варваров;
    ///   победа  — трофеи (броня и оружие) → следующий день; пережили daysToWin дней — победа,
    ///             но набеги идут дальше (бесконечный режим), пока стоит ратуша;
    ///   поражение — уцелевшие варвары бьют по зданиям и уходят → следующий день.
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

        [Header("Экраны (панели Canvas)")]
        public GameObject prepScreen;
        public GameObject boardTab;
        public GameObject cityTab;
        public GameObject techTab;
        public GameObject unitsTab;
        public GameObject battleScreen;
        public GameObject lootScreen;
        public GameObject gameOverScreen;
        [Tooltip("Скамейка: видна на поле и на трофеях")]
        public GameObject benchPanel;

        [Tooltip("Версия сборщика, которой собрана сцена (CasiwarSceneBuilder.SceneVersion). Старая — редактор предложит пересобрать.")]
        [HideInInspector] public int sceneVersion;

        [Header("Кнопки вкладок (подсветка активной)")]
        public Image boardTabButton;
        public Image cityTabButton;
        public Image techTabButton;
        public Image unitsTabButton;
        public Color tabActiveColor = new Color(0.36f, 0.42f, 0.56f);
        public Color tabIdleColor = new Color(0.20f, 0.22f, 0.29f);

        [Header("Подсветка «сюда стоит нажать» (пусто — кнопки найдутся сами)")]
        [Tooltip("«В БОЙ!» — пульсирует, когда на поле больше ничего не сделать")]
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
        [Tooltip("Сколько дней нужно продержаться, чтобы победить; дальше набеги идут без конца (бесконечный режим)")]
        [Min(1)] public int daysToWin = 20;
        [Tooltip("Юниты, с которыми игрок начинает забег")]
        public List<UnitData> startingUnits = new List<UnitData>();

        [Header("Награда за отбитый набег")]
        [Min(0)] public int winGold = 4;

        [Header("Сообщения")]
        [Min(1)] public int maxMessages = 3;
        [Min(0.5f)] public float messageDuration = 2.5f;

        public GamePhase Phase { get; private set; }
        public PrepTab Tab { get; private set; }
        public int Day { get; private set; } = 1;

        /// <summary>Казна города (золото хранит CityManager).</summary>
        public int Gold => city != null ? city.Gold : 0;

        private readonly List<(string text, float until)> messages = new List<(string text, float until)>();

        private void Awake()
        {
            if (cityGrid == null) cityGrid = FindFirstObjectByType<CityGridManager>();
            if (lootSlot == null) lootSlot = FindFirstObjectByType<LootSlotMachine>();
            if (bench == null) bench = FindFirstObjectByType<BenchManager>();
            if (battle == null) battle = FindFirstObjectByType<AutoBattleManager>();
            if (city == null) city = FindFirstObjectByType<CityManager>();
            if (techTree == null) techTree = FindFirstObjectByType<TechTree>();

            if (cityGrid != null) cityGrid.Exhausted += OnCityBoardExhausted;
            if (lootSlot != null) lootSlot.Finished += OnLootFinished;
            if (city != null) city.Changed += OnCityChanged;
            ArrangeTabs();
            FindProgressButtons();
        }

        /// <summary>Кнопки «В БОЙ!» и «Дальше» — по их обработчику (сцены старой версии не знают этих полей).</summary>
        private void FindProgressButtons()
        {
            if (fightButton != null && lootNextButton != null) return;
            foreach (Button button in FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                {
                    if (button.onClick.GetPersistentTarget(i) != this) continue;
                    string method = button.onClick.GetPersistentMethodName(i);
                    if (method == nameof(StartBattle) && fightButton == null) fightButton = button;
                    else if (method == nameof(FinishLoot) && lootNextButton == null) lootNextButton = button;
                }
            }
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

        /// <summary>«Можно идти дальше»: «В БОЙ!» — когда на поле больше ничего не сделать, «Дальше» — когда крутки кончились.</summary>
        private void UpdateProgressHighlights()
        {
            bool fight = Phase == GamePhase.Preparation && cityGrid != null && cityGrid.IsExhausted && !cityGrid.IsBusy;
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
            Day = 1;
            messages.Clear();
            RefreshMessages();
            if (techTree != null) techTree.ResetTree();
            if (city != null) city.ResetCity();

            if (bench != null)
            {
                bench.ClearAll();
                foreach (UnitData unit in startingUnits)
                {
                    if (unit != null) bench.TryAddUnit(unit, 1);
                }
            }

            BeginDay(firstDay: true);
            ShowMessage($"Пока все без класса. Изучите за {GameVisuals.IconKnowledge} «Военное дело», «Стрельбу из лука» или «Мистицизм» " +
                        "и постройте казармы или святилище!", 6f);
        }

        private void BeginDay(bool firstDay)
        {
            Phase = GamePhase.Preparation;
            if (!firstDay && city != null) ShowMessage(city.ProcessNewDay(), 5f); // утро: знания, еда, рост города
            ApplyScreens();
            ShowCityTab(); // день начинается в городе: стройка → наука → поле → бой
            if (cityGrid != null && city != null) cityGrid.BeginPhase(city.BoardSize, city.BoardReserve, city.ActionsPerDay, city.ReshufflesPerDay);
            if (bench != null) bench.SetInteractable(true);
            UpdateHud();
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
            if (benchPanel != null) benchPanel.SetActive(Phase == GamePhase.Loot || (Phase == GamePhase.Preparation && tab == PrepTab.Board));
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
                ShowMessage($"Нужно {tech.knowledgeCost} {GameVisuals.IconKnowledge} — очки знаний приходят каждое утро (+{city.KnowledgePerDay})");
                return false;
            }

            techTree.MarkResearched(techId);
            ShowMessage($"Изучено: {tech.title}. {tech.description}", 4f);
            return true;
        }

        /// <summary>Кнопка «В БОЙ!»: волна варваров против армии со скамейки.</summary>
        public void StartBattle()
        {
            if (Phase != GamePhase.Preparation) return;
            if (cityGrid != null && cityGrid.IsBusy)
            {
                ShowMessage("Дождитесь окончания каскада");
                return;
            }

            List<BenchUnit> army = bench != null ? bench.GetArmy() : new List<BenchUnit>();
            if (army.Count == 0 && cityGrid != null && !cityGrid.IsExhausted)
            {
                ShowMessage("Армия пуста — соберите юнитов на поле");
                return;
            }
            if (battle == null)
            {
                Debug.LogError("[GameManager] Не назначен AutoBattleManager.");
                return;
            }

            Phase = GamePhase.Battle;
            if (cityGrid != null) cityGrid.EndPhase();
            if (bench != null) bench.SetInteractable(false);
            messages.Clear(); // сообщения дня не должны висеть над боем
            RefreshMessages();
            ApplyScreens();
            UpdateHud();
            battle.StartBattle(army, Day, OnBattleFinished);
        }

        private void OnBattleFinished(BattleOutcome outcome)
        {
            // Погибшие пропадают навсегда, выжившие получают опыт и уровни
            string report = bench != null ? string.Join(" · ", bench.ApplyBattleResults(outcome.PlayerUnits, outcome.Victory)) : string.Empty;

            if (outcome.Victory)
            {
                int reward = winGold + Day / 2;
                AddGold(reward);
                ShowMessage($"Набег отбит! +{reward} {GameVisuals.IconGold}", 3.5f);
                ShowMessage(report, 6f);
                if (Day == daysToWin)
                {
                    // Победа засчитана, но забег не кончается: набеги идут дальше, пока стоит ратуша
                    ShowMessage($"ПОБЕДА! Город пережил {daysToWin} дней набегов. Дальше — бесконечный режим: сколько ещё продержитесь?", 8f);
                }
                EnterLoot(outcome.SurvivorStars);
                return;
            }

            // Поражение: уцелевшие варвары бьют по зданиям и уходят
            string raid = city != null ? city.ApplyRaid(outcome.RaidHits) : string.Empty;
            ShowMessage($"Набег! Прорвались варвары ({outcome.RaidHits.Count})" + (raid.Length > 0 ? $" — {raid}" : string.Empty), 6f);
            ShowMessage(report, 6f);
            if (city != null && city.IsTownHallRuined)
            {
                EndRun(Day > daysToWin
                    ? $"РАТУША РАЗРУШЕНА\n\nПобеда была на {daysToWin}-й день, а город продержался до {Day}-го"
                    : $"РАТУША РАЗРУШЕНА\n\nГород пал на {Day}-й день");
                return;
            }
            NextDay();
        }

        /// <summary>Трофеи: круток столько, сколько звёзд у выживших (★ — 1, ★★ — 2, ★★★ — 3).</summary>
        private void EnterLoot(int survivorStars)
        {
            Phase = GamePhase.Loot;
            ApplyScreens();
            if (bench != null) bench.SetInteractable(true);
            int spins = Mathf.Max(1, survivorStars);
            if (lootSlot != null) lootSlot.BeginLoot(spins);
            ShowMessage($"Трофеи! Круток: {spins} — по звёздам выживших ({GameVisuals.IconStar} = 1 крутка). Фигура линии решает, чей предмет", 4f);
            UpdateHud();
        }

        /// <summary>Кнопка «Дальше» на экране трофеев.</summary>
        public void FinishLoot()
        {
            if (Phase != GamePhase.Loot) return;
            if (lootSlot != null)
            {
                if (lootSlot.IsSpinning)
                {
                    ShowMessage("Дождитесь, пока барабаны остановятся");
                    return;
                }
                if (lootSlot.SpinsLeft > 0)
                {
                    ShowMessage($"Осталось круток: {lootSlot.SpinsLeft} — жмите «Крутить» или «Авто»");
                    return;
                }
                lootSlot.EndLoot();
            }
            NextDay();
        }

        private void NextDay()
        {
            Day++;
            BeginDay(firstDay: false);
        }

        private void EndRun(string text)
        {
            Phase = GamePhase.GameOver;
            if (cityGrid != null) cityGrid.EndPhase();
            if (lootSlot != null) lootSlot.EndLoot();
            if (bench != null) bench.SetInteractable(false);
            if (gameOverText != null) gameOverText.text = text;
            ApplyScreens();
            UpdateHud();
        }

        private void OnCityBoardExhausted()
        {
            if (Phase == GamePhase.Preparation && cityGrid != null)
                ShowMessage($"{cityGrid.ExhaustReason}! Стройтесь в «Городе», учите «Науку» и жмите «В БОЙ!»", 4f);
        }

        private void OnLootFinished()
        {
            if (Phase == GamePhase.Loot) ShowMessage("Крутки кончились — жмите «Дальше»", 4f);
        }

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
            if (benchPanel != null) benchPanel.SetActive(Phase == GamePhase.Loot || (Phase == GamePhase.Preparation && Tab == PrepTab.Board));
            RefreshTabHighlights();
        }

        private void UpdateHud()
        {
            if (dayText != null) dayText.text = Day <= daysToWin ? $"День {Day}/{daysToWin}" : $"День {Day} · сверх победы";
            if (goldText != null) goldText.text = $"{GameVisuals.IconGold} {Gold}";
            if (city == null) return;
            if (productionText != null) productionText.text = $"{GameVisuals.IconProduction} {city.Production}";
            if (foodText != null) foodText.text = $"{GameVisuals.IconFood} {city.Food}/{city.GrowthCost}";
            if (populationText != null) populationText.text = $"{GameVisuals.IconPopulation} {city.Population}";
            if (knowledgeText != null) knowledgeText.text = $"{GameVisuals.IconKnowledge} {city.Knowledge}";
        }
    }
}
