using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace Casiwar
{
    /// <summary>Символ слота вербовки.</summary>
    public enum RecruitSymbol
    {
        Civilian, // горожанин — не платит, мешает линиям
        Militia,  // ополченец — линия из них даёт бойца по форме линии; к символам гильдий подходит как джокер
        Warrior,
        Archer,
        Priest,
    }

    /// <summary>Откуда спин: город (по населению), гильдия или усилитель — у них больше символов своего класса.</summary>
    public sealed class RecruitSpin
    {
        public string Key;
        public string Title;
        /// <summary>Чьих символов больше (None — обычный спин: только горожане и ополченцы).</summary>
        public UnitClass Boost;
        public float CivilianWeight;
        public float MilitiaWeight;
        public float BoostWeight;
    }

    /// <summary>Выигрышная линия вербовки.</summary>
    public sealed class RecruitLine
    {
        /// <summary>Индекс в LootSlotMachine.Paylines (те же 9 линий, что в трофеях).</summary>
        public int Payline;
        public int Length;
        /// <summary>Кто выходит: класс символов линии, а у линии из одних ополченцев — класс её формы (None — ополченец).</summary>
        public UnitClass Class;

        public IEnumerable<Vector2Int> Cells
        {
            get
            {
                int[] rows = LootSlotMachine.Paylines[Payline].Rows;
                for (int x = 0; x < Length; x++) yield return new Vector2Int(x, rows[x]);
            }
        }
    }

    /// <summary>
    /// Вербовка на вкладке «Поле» — слот 5×3 с теми же 9 линиями, что в трофеях (вместо прежнего 3-в-ряд):
    /// • спинов за ход: от населения (1 + жители / populationPerSpin, плюс чудеса и «Учёт») — в них только горожане
    ///   и ополченцы; каждая целая гильдия (казармы, стрельбище, святилище) — ещё спин, где много её бойцов,
    ///   каждый усилитель (плавильня, лесопилка, храм) — спин, где их ещё больше. Построили посреди хода — спин сразу;
    /// • выигрыш — 3+ одинаковых подряд по линии с левого барабана (горожане рвут линию, ополченец подходит к символу
    ///   гильдии). Боец — класса символов линии; у линии из одних ополченцев — класса её формы, если гильдия стоит:
    ///   прямые → воин, углы → лучник, зигзаги → жрец (иначе — ополченец). Длина 3 / 4 / 5 → уровень 1 / 2 / 3;
    /// • за спин каждый класс даёт одного бойца (по самой длинной линии). Две линии разных классов в одном спине → их
    ///   гибрид, если он открыт (воин + лучник — подрывник, воин + жрец — монах, лучник + жрец — маг). Все три класса
    ///   разом — ДЖЕКПОТ: богатырь (если изучены «Былины» и богатырей меньше предела);
    /// • новобранцы идут в гарнизон (BenchManager.AddRecruit); гарнизон полон — продаются за золото.
    /// Крутится сам, пока открыта вкладка; клик по барабанам — сразу итог. Не докрутили — «Конец хода» досчитает.
    /// Окно строится кодом в рамке прежнего поля (CityGridManager.gridContainer) — сцену пересобирать не надо.
    /// </summary>
    public class RecruitSlot : MonoBehaviour, ITileInputHandler
    {
        public const int Reels = 5;
        public const int Rows = 3;

        [Header("Ссылки (пусто — найдутся сами)")]
        public CityManager city;
        [Tooltip("Прежнее поле армии: его рамка, префаб клетки, пул юнитов и подписи под полем")]
        public CityGridManager board;
        public BenchManager bench;
        public GameManager game;

        [Header("Спины")]
        [Tooltip("Обычных спинов за ход: 1 + жители / это (плюс чудеса и улучшения)")]
        [Min(1)] public int populationPerSpin = 3;

        [Header("Символы обычного спина (веса)")]
        [Min(0f)] public float civilianWeight = 0.9f;
        [Min(0f)] public float militiaWeight = 1.1f;

        [Header("Спин гильдии")]
        [Min(0f)] public float guildCivilianWeight = 0.8f;
        [Min(0f)] public float guildMilitiaWeight = 0.9f;
        [Min(0f)] public float guildClassWeight = 1f;

        [Header("Спин усилителя")]
        [Min(0f)] public float boosterCivilianWeight = 0.5f;
        [Min(0f)] public float boosterMilitiaWeight = 0.6f;
        [Min(0f)] public float boosterClassWeight = 1.7f;

        [Header("Правила")]
        [Range(1, UnitData.MaxLevel)] public int maxRecruitLevel = 3;
        [Tooltip("Черты богатыря из джекпота — по стольким символам его линий (длинные первыми)")]
        [Min(3)] public int heroTraitSymbols = 7;

        [Header("Анимация (темп как у ресурсного слота)")]
        [Tooltip("Все спины хода вместе — примерно столько секунд")]
        [Min(0.5f)] public float totalDuration = 3.2f;
        [Tooltip("Один спин (падение + показ линий) — не быстрее и не дольше")]
        [Min(0.05f)] public float minStep = 0.25f;
        [Min(0.05f)] public float maxStep = 0.6f;
        [Min(1f)] public float dropSpeed = 16f;
        [Min(0f)] public float reelDelay = 0.9f;
        [Tooltip("Пауза после последнего спина (видно линии и кто пришёл)")]
        [Min(0f)] public float showDuration = 0.9f;
        [Min(0f)] public float autoStartDelay = 0.5f;
        [Min(0f)] public float tileSpacing = 8f;
        [Min(1f)] public float lineThickness = 10f;

        private readonly List<RecruitSpin> pending = new List<RecruitSpin>();
        private readonly HashSet<string> granted = new HashSet<string>();
        private readonly List<string> log = new List<string>();
        private readonly List<string> recruitedThisTurn = new List<string>();
        private int spinsTotal;
        private int spinsDone;
        private int soldGold;
        private bool active;
        private bool autoSpin = true;
        private float autoSpinAt;
        private bool wasVisible;
        private bool spinAwarded = true;
        private RecruitSymbol[,] lastGrid;
        private RecruitSpin current;

        private RectTransform root;
        private RectTransform reels;
        private RectTransform linesRoot;
        private TMP_Text titleText;
        private TMP_Text legendText;
        private TMP_Text logText;
        private TileView[,] views;
        private float step;
        private Vector2 lastSize;

        public bool IsSpinning { get; private set; }
        /// <summary>Спинов осталось в этом ходу.</summary>
        public int SpinsLeft => pending.Count;
        /// <summary>Вербовка этого хода закончена (все спины сделаны).</summary>
        public bool Done => !IsSpinning && pending.Count == 0;
        /// <summary>Идёт показ (вкладка открыта, барабаны крутятся сами).</summary>
        public bool IsPlaying => active && (IsSpinning || (pending.Count > 0 && autoSpin && IsVisible));
        public bool AutoSpin => autoSpin;

        /// <summary>Все спины хода сделаны: итог «+3 бойца: …».</summary>
        public event Action<string> Finished;

        private bool IsVisible => root != null && root.gameObject.activeInHierarchy;

        private void Awake()
        {
            if (city == null) city = FindFirstObjectByType<CityManager>();
            if (board == null) board = FindFirstObjectByType<CityGridManager>();
            if (bench == null) bench = FindFirstObjectByType<BenchManager>();
            if (game == null) game = FindFirstObjectByType<GameManager>();
            if (city != null) city.Changed += OnCityChanged;
        }

        private void OnDestroy()
        {
            if (city != null) city.Changed -= OnCityChanged;
        }

        private void Update()
        {
            if (root == null) return;
            if (reels != null && reels.rect.size != lastSize && !IsSpinning) LayoutViews();
            bool visible = IsVisible;
            if (visible && !wasVisible) autoSpinAt = Time.time + autoStartDelay; // вкладку открыли — крутим чуть погодя
            wasVisible = visible;
            if (active && visible && autoSpin && !IsSpinning && pending.Count > 0 && Time.time >= autoSpinAt) Spin();
        }

        // =====================================================================
        //  Ход
        // =====================================================================

        /// <summary>Начало хода: спины по населению, гильдиям и усилителям.</summary>
        public void BeginTurn()
        {
            StopAllCoroutines();
            EnsureUi();
            IsSpinning = false;
            spinAwarded = true;
            active = true;
            autoSpin = true;
            autoSpinAt = Time.time + autoStartDelay;
            pending.Clear();
            granted.Clear();
            recruitedThisTurn.Clear();
            soldGold = 0;
            log.Clear();
            foreach (RecruitSpin spin in PlanSpins())
            {
                pending.Add(spin);
                granted.Add(spin.Key);
            }
            spinsTotal = pending.Count;
            spinsDone = 0;
            ClearPaylines();
            ShowIdle();
            UpdateUi();
        }

        /// <summary>Бой или трофеи: вербовка ждёт (спины не пропадают).</summary>
        public void Pause()
        {
            if (IsSpinning) ResolveRemaining(); // не бросаем спин на середине
            active = false;
            UpdateUi();
        }

        /// <summary>Вернулись в ход после боя в поле.</summary>
        public void Resume()
        {
            active = true;
            autoSpinAt = Time.time + autoStartDelay;
            UpdateUi();
        }

        /// <summary>Кнопка «Авто» (A): крутить самому или стоять.</summary>
        public void ToggleAuto()
        {
            autoSpin = !autoSpin;
            autoSpinAt = Time.time + 0.1f;
            UpdateUi();
        }

        /// <summary>Сколько спинов даёт ход (для сводки на карте).</summary>
        public int SpinsPerTurn => PlanSpins().Count;

        /// <summary>
        /// Спины хода: обычные (1 + жители / populationPerSpin + чудеса и «Учёт»), по спину на каждую целую гильдию
        /// и на каждый усилитель при целой гильдии своего класса.
        /// </summary>
        public List<RecruitSpin> PlanSpins()
        {
            var spins = new List<RecruitSpin>();
            if (city == null) return spins;
            int basic = city.RecruitSpinsBase(populationPerSpin);
            for (int i = 0; i < basic; i++)
            {
                spins.Add(new RecruitSpin
                {
                    Key = $"base:{i}", Title = "Город", Boost = UnitClass.None,
                    CivilianWeight = civilianWeight, MilitiaWeight = militiaWeight,
                });
            }
            foreach (Building building in city.Buildings)
            {
                if (building.IsRuined) continue;
                UnitClass guild = building.Config.unitBranch;
                if (guild != UnitClass.None)
                {
                    // «Вербовщики» — символов класса больше; «Набор в полки» и др. — ещё спины гильдии
                    float share = 1f + city.ClassEffect(CityEffect.RecruitShare, guild);
                    int count = 1 + Mathf.RoundToInt(city.ClassEffect(CityEffect.ExtraGuildSpin, guild));
                    for (int i = 0; i < count; i++)
                    {
                        spins.Add(new RecruitSpin
                        {
                            Key = i == 0 ? $"guild:{guild}" : $"guild:{guild}:{i}", Title = building.Config.title, Boost = guild,
                            CivilianWeight = guildCivilianWeight, MilitiaWeight = guildMilitiaWeight, BoostWeight = guildClassWeight * share,
                        });
                    }
                    continue;
                }
                UnitClass boosted = CityManager.BoostedClass(building.Config);
                if (boosted == UnitClass.None || !city.IsClassUnlocked(boosted)) continue;
                spins.Add(new RecruitSpin
                {
                    Key = $"booster:{building.Cell.x}:{building.Cell.y}", Title = building.Config.title, Boost = boosted,
                    CivilianWeight = boosterCivilianWeight, MilitiaWeight = boosterMilitiaWeight, BoostWeight = boosterClassWeight,
                });
            }
            return spins;
        }

        /// <summary>Построили гильдию или усилитель посреди хода — их спин добавляется сразу.</summary>
        private void OnCityChanged()
        {
            if (!active) return;
            foreach (RecruitSpin spin in PlanSpins())
            {
                if (spin.Key.StartsWith("base:") || granted.Contains(spin.Key)) continue;
                granted.Add(spin.Key);
                pending.Add(spin);
                spinsTotal++;
                if (game != null) game.ShowMessage($"{spin.Title}: +1 спин вербовки на «Поле» — в нём больше {ClassGenitive(spin.Boost)}");
            }
            UpdateUi();
        }

        // =====================================================================
        //  Спин
        // =====================================================================

        public void Spin()
        {
            if (IsSpinning || pending.Count == 0 || views == null) return;
            StartCoroutine(SpinRoutine());
        }

        /// <summary>Клик по барабанам — сразу итог.</summary>
        public void OnTileClicked(Vector2Int cell) => ResolveRemaining();
        public void OnTileSwiped(Vector2Int cell, Vector2Int direction) { }

        /// <summary>Досчитать все оставшиеся спины без показа (клик, «Конец хода»).</summary>
        public void ResolveRemaining()
        {
            if (Done) return;
            StopAllCoroutines();
            if (!spinAwarded && lastGrid != null && current != null) ShowResult(lastGrid, Resolve(lastGrid, current));
            IsSpinning = false;
            while (pending.Count > 0)
            {
                current = pending[0];
                pending.RemoveAt(0);
                spinsDone++;
                lastGrid = Roll(current);
                List<RecruitLine> lines = Resolve(lastGrid, current);
                if (views != null) ShowResult(lastGrid, lines);
            }
            Complete();
        }

        private IEnumerator SpinRoutine()
        {
            IsSpinning = true;
            current = pending[0];
            pending.RemoveAt(0);
            spinsDone++;
            ClearPaylines();
            RecruitSymbol[,] grid = Roll(current);
            lastGrid = grid;
            spinAwarded = false;
            UpdateUi();

            // Темп как у ресурсного слота: все спины хода — около totalDuration, падение — меньше половины спина
            float pace = Mathf.Clamp(totalDuration / Mathf.Max(1, spinsTotal), minStep, maxStep);
            float speed = Mathf.Max(dropSpeed, (Rows + (Reels - 1) * reelDelay) / Mathf.Max(0.05f, pace * 0.45f));
            for (int x = 0; x < Reels; x++)
            {
                for (int y = 0; y < Rows; y++)
                {
                    TileView view = views[x, y];
                    view.Show(LookFor(grid[x, y]));
                    view.SetMoveSpeed(speed * step);
                    view.AnimateFrom(view.Home + Vector2.up * step * (Rows + x * reelDelay));
                }
            }
            float timeout = 4f;
            while (timeout > 0f && !AllSettled())
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            ShowResult(grid, Resolve(grid, current));
            float hold = pending.Count == 0 ? Mathf.Max(showDuration, pace * 0.55f) : pace * 0.55f;
            for (float t = 0f; t < hold; t += Time.deltaTime) yield return null;
            IsSpinning = false;
            UpdateUi();
            if (pending.Count == 0) Complete();
        }

        private void Complete()
        {
            IsSpinning = false;
            spinAwarded = true;
            UpdateUi();
            Finished?.Invoke(Summary);
        }

        /// <summary>«+3: Воин ур.2, Подрывник, Ополченец» (или «никто не пришёл»).</summary>
        public string Summary
        {
            get
            {
                string people = recruitedThisTurn.Count > 0 ? $"+{recruitedThisTurn.Count}: {string.Join(", ", recruitedThisTurn)}" : "никто не пришёл";
                return soldGold > 0 ? $"{people} (гарнизон полон — лишних продали: +{soldGold} {GameVisuals.IconGold})" : people;
            }
        }

        // =====================================================================
        //  Логика
        // =====================================================================

        private RecruitSymbol[,] Roll(RecruitSpin spin)
        {
            var grid = new RecruitSymbol[Reels, Rows];
            float boost = spin.Boost != UnitClass.None ? spin.BoostWeight : 0f;
            float total = Mathf.Max(0.0001f, spin.CivilianWeight + spin.MilitiaWeight + boost);
            RecruitSymbol boostSymbol = SymbolOf(spin.Boost);
            for (int x = 0; x < Reels; x++)
            {
                for (int y = 0; y < Rows; y++)
                {
                    float roll = Random.value * total;
                    grid[x, y] = roll < spin.CivilianWeight ? RecruitSymbol.Civilian
                        : roll < spin.CivilianWeight + spin.MilitiaWeight ? RecruitSymbol.Militia
                        : boostSymbol;
                }
            }
            return grid;
        }

        /// <summary>
        /// Выигрышные линии: 3+ подряд с левого барабана, горожане рвут линию, ополченец подходит к символу гильдии.
        /// Класс линии — её символ гильдии, а у одних ополченцев — класс формы линии (если гильдия стоит), иначе None.
        /// </summary>
        public List<RecruitLine> FindLines(RecruitSymbol[,] grid)
        {
            var lines = new List<RecruitLine>();
            for (int index = 0; index < LootSlotMachine.Paylines.Length; index++)
            {
                int[] rows = LootSlotMachine.Paylines[index].Rows;
                UnitClass lineClass = UnitClass.None;
                int length = 0;
                for (int x = 0; x < Mathf.Min(Reels, rows.Length); x++)
                {
                    RecruitSymbol symbol = grid[x, rows[x]];
                    if (symbol == RecruitSymbol.Civilian) break;
                    UnitClass symbolClass = ClassOf(symbol);
                    if (symbolClass != UnitClass.None)
                    {
                        if (lineClass == UnitClass.None) lineClass = symbolClass;
                        else if (lineClass != symbolClass) break;
                    }
                    length++;
                }
                if (length < 3) continue;
                if (lineClass == UnitClass.None)
                {
                    UnitClass shape = LootSlotMachine.Paylines[index].Class;
                    if (city != null && city.IsClassUnlocked(shape)) lineClass = shape;
                }
                lines.Add(new RecruitLine { Payline = index, Length = length, Class = lineClass });
            }
            return lines;
        }

        /// <summary>
        /// Награда за спин: каждый класс — один боец (по самой длинной линии); две разных — гибрид; все три — богатырь.
        /// </summary>
        private List<RecruitLine> Resolve(RecruitSymbol[,] grid, RecruitSpin spin)
        {
            spinAwarded = true;
            List<RecruitLine> lines = FindLines(grid);
            var best = new Dictionary<UnitClass, RecruitLine>();
            foreach (RecruitLine line in lines)
            {
                if (!best.TryGetValue(line.Class, out RecruitLine old) || line.Length > old.Length) best[line.Class] = line;
            }
            var parts = new List<string>();
            List<UnitClass> bases = UnitClasses.Base.Where(best.ContainsKey).ToList();

            // Джекпот: все три класса разом — богатырь
            if (bases.Count == 3 && city != null && city.HeroUnlocked && bench != null && !bench.HeroLimitReached && board != null)
            {
                List<TileData> traitTiles = lines.Where(l => l.Class != UnitClass.None)
                    .OrderByDescending(l => l.Length)
                    .SelectMany(l => Enumerable.Repeat(l.Class, l.Length))
                    .Take(heroTraitSymbols)
                    .Select(c => TileData.ForUnit(board.RandomUnit(c)))
                    .Where(t => t.Unit != null)
                    .ToList();
                int baseLines = lines.Count(l => l.Class != UnitClass.None);
                int stars = Mathf.Clamp(1 + (baseLines - 3) / 2, 1, UnitData.MaxStars);
                parts.Add(Recruit(board.SpecialUnit(UnitClass.Hero), 1, stars, HeroTraits.From(traitTiles), "ДЖЕКПОТ! "));
                foreach (UnitClass unitClass in UnitClasses.Base) best.Remove(unitClass);
                bases.Clear();
            }

            // Две линии разных классов — гибрид (если открыт)
            foreach (UnitClass hybrid in UnitClasses.Hybrids)
            {
                UnitClass[] parents = UnitClasses.Parents(hybrid);
                if (parents.Length < 2 || !bases.Contains(parents[0]) || !bases.Contains(parents[1])) continue;
                if (city == null || !city.IsHybridUnlocked(hybrid) || board == null) continue;
                int level = WithBonus(Mathf.Max(LevelOf(best[parents[0]]), LevelOf(best[parents[1]])), hybrid);
                parts.Add(Recruit(board.SpecialUnit(hybrid), level, 1, null, string.Empty));
                best.Remove(parents[0]);
                best.Remove(parents[1]);
                bases.Remove(parents[0]);
                bases.Remove(parents[1]);
            }

            // Остальные — бойцы своего класса (ополченцы — ополченцы)
            foreach (KeyValuePair<UnitClass, RecruitLine> pair in best.OrderBy(p => p.Key == UnitClass.None ? 1 : 0))
            {
                UnitData unit = board != null ? board.RandomUnit(pair.Key) : null;
                if (unit != null) parts.Add(Recruit(unit, WithBonus(LevelOf(pair.Value), pair.Key), 1, null, string.Empty));
            }

            string title = spin.Boost != UnitClass.None ? $"{spin.Title}: " : string.Empty;
            AddLog(parts.Count > 0 ? title + string.Join(", ", parts.Where(p => p.Length > 0)) : $"{title}пусто");
            return lines;
        }

        private int LevelOf(RecruitLine line) => Mathf.Clamp(line.Length - 2, 1, Mathf.Max(1, maxRecruitLevel));

        /// <summary>Уровень новобранца с бонусом дерева «Войск» («Пророки» — жрецы на уровень выше).</summary>
        private int WithBonus(int level, UnitClass unitClass) =>
            Mathf.Clamp(level + (city != null ? Mathf.RoundToInt(city.ClassEffect(CityEffect.RecruitLevel, unitClass)) : 0), 1, UnitData.MaxLevel);

        /// <summary>Новобранец — в гарнизон; места нет — продаётся. Возвращает строку для журнала.</summary>
        private string Recruit(UnitData unit, int level, int stars, HeroTraits traits, string prefix)
        {
            if (unit == null) return string.Empty;
            BenchUnit added = bench != null ? bench.AddRecruit(unit, level, stars, traits) : null;
            string name = traits != null ? $"{unit.unitName} {GameVisuals.Stars(stars)}" : unit.unitName;
            string levelTag = level > 1 ? $" ур.{level}" : string.Empty;
            if (added != null)
            {
                recruitedThisTurn.Add($"{name}{levelTag}");
                string hybrid = UnitClasses.IsHybrid(unit.unitClass)
                    ? $" ({string.Join(" + ", UnitClasses.Parents(unit.unitClass).Select(GameVisuals.ClassName))})"
                    : string.Empty;
                return $"{prefix}{GameVisuals.ClassIcon(unit.unitClass)} {name}{levelTag}{hybrid}";
            }
            int gold = unit.GetSellPrice(stars, level);
            if (game != null) game.AddGold(gold);
            soldGold += gold;
            return $"{prefix}{GameVisuals.ClassIcon(unit.unitClass)} {name} — мест нет, +{gold} {GameVisuals.IconGold}";
        }

        private static RecruitSymbol SymbolOf(UnitClass unitClass)
        {
            switch (unitClass)
            {
                case UnitClass.Warrior: return RecruitSymbol.Warrior;
                case UnitClass.Archer: return RecruitSymbol.Archer;
                case UnitClass.Priest: return RecruitSymbol.Priest;
                default: return RecruitSymbol.Militia;
            }
        }

        private static UnitClass ClassOf(RecruitSymbol symbol)
        {
            switch (symbol)
            {
                case RecruitSymbol.Warrior: return UnitClass.Warrior;
                case RecruitSymbol.Archer: return UnitClass.Archer;
                case RecruitSymbol.Priest: return UnitClass.Priest;
                default: return UnitClass.None;
            }
        }

        private static string ClassGenitive(UnitClass unitClass)
        {
            switch (unitClass)
            {
                case UnitClass.Warrior: return "воинов";
                case UnitClass.Archer: return "лучников";
                case UnitClass.Priest: return "жрецов";
                default: return "ополченцев";
            }
        }

        // =====================================================================
        //  Вид
        // =====================================================================

        private SymbolLook LookFor(RecruitSymbol symbol)
        {
            if (symbol == RecruitSymbol.Civilian) return GameVisuals.LookFor(TileData.ForCivilian());
            UnitData unit = board != null ? board.RandomUnit(ClassOf(symbol), deterministic: true) : null;
            return unit != null ? GameVisuals.LookFor(TileData.ForUnit(unit)) : GameVisuals.LookFor(TileData.ForCivilian());
        }

        /// <summary>Выигрыш на барабанах: клетки линий светятся, остальные тускнеют, линии рисуются цветом класса.</summary>
        private void ShowResult(RecruitSymbol[,] grid, List<RecruitLine> lines)
        {
            if (views == null) return;
            ClearPaylines();
            var winning = new HashSet<Vector2Int>(lines.SelectMany(l => l.Cells));
            for (int x = 0; x < Reels; x++)
            {
                for (int y = 0; y < Rows; y++)
                {
                    TileView view = views[x, y];
                    view.Show(LookFor(grid[x, y]));
                    view.AnimateFrom(view.Home);
                    bool on = winning.Contains(new Vector2Int(x, y));
                    view.SetHint(on);
                    view.SetDimmed(lines.Count > 0 && !on);
                }
            }
            foreach (RecruitLine line in lines) DrawPayline(line);
        }

        private void ShowIdle()
        {
            if (views == null) return;
            for (int x = 0; x < Reels; x++)
            {
                for (int y = 0; y < Rows; y++)
                {
                    views[x, y].Show(LookFor((x + y) % 2 == 0 ? RecruitSymbol.Militia : RecruitSymbol.Civilian));
                    views[x, y].SetDimmed(true);
                    views[x, y].SetHint(false);
                }
            }
        }

        private void DrawPayline(RecruitLine line)
        {
            if (linesRoot == null || views == null) return;
            Color color = line.Class == UnitClass.None ? new Color(0.85f, 0.85f, 0.8f) : GameVisuals.ClassColor(line.Class);
            color.a = 0.9f;
            Vector2Int[] cells = line.Cells.ToArray();
            for (int i = 1; i < cells.Length; i++)
            {
                Vector2 from = views[cells[i - 1].x, cells[i - 1].y].Home;
                Vector2 to = views[cells[i].x, cells[i].y].Home;
                Vector2 delta = to - from;
                RectTransform rect = NewRect("Segment", linesRoot);
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(delta.magnitude + lineThickness, lineThickness);
                rect.anchoredPosition = (from + to) * 0.5f;
                rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
                var image = rect.gameObject.AddComponent<Image>();
                image.color = color;
                image.raycastTarget = false;
            }
        }

        private void ClearPaylines()
        {
            if (linesRoot == null) return;
            for (int i = linesRoot.childCount - 1; i >= 0; i--) Destroy(linesRoot.GetChild(i).gameObject);
        }

        private bool AllSettled()
        {
            if (views == null) return true;
            foreach (TileView view in views)
            {
                if (view != null && !view.IsSettled) return false;
            }
            return true;
        }

        private void AddLog(string line)
        {
            log.Add(line);
            while (log.Count > 5) log.RemoveAt(0);
        }

        private void UpdateUi()
        {
            if (titleText != null)
            {
                string source = current != null && IsSpinning && current.Boost != UnitClass.None
                    ? $" · <color=#ffd166>{current.Title}: больше {ClassGenitive(current.Boost)}</color>"
                    : string.Empty;
                titleText.text = Done && spinsTotal > 0
                    ? $"Вербовка хода окончена · спинов было {spinsTotal}"
                    : $"Вербовка · спин {Mathf.Min(spinsDone, spinsTotal)}/{spinsTotal}{source}";
            }
            if (logText != null)
                logText.text = log.Count > 0 ? string.Join("\n", log) : "Клик по барабанам — сразу итог · новобранцы идут в гарнизон";
            if (board != null && board.reserveText != null)
                board.reserveText.text = pending.Count > 0 ? $"{GameVisuals.IconSpin} спинов: {pending.Count}" : "Спины кончились";
            if (board != null && board.autoPlayLabel != null) board.autoPlayLabel.text = autoSpin ? "Авто: ВКЛ" : "Авто: ВЫКЛ";
        }

        /// <summary>Окно вербовки в рамке прежнего поля: заголовок, подсказка про линии, барабаны 5×3 и журнал.</summary>
        private void EnsureUi()
        {
            if (root != null || board == null || board.gridContainer == null || board.tilePrefab == null) return;
            RectTransform host = board.gridContainer;
            for (int i = host.childCount - 1; i >= 0; i--) host.GetChild(i).gameObject.SetActive(false); // клетки старого поля
            root = NewRect("RecruitSlot", host);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;

            TMP_Text font = board.reserveText;
            titleText = NewText("Title", root, font, 34f, new Color(1f, 0.82f, 0.3f));
            TopBand(titleText.rectTransform, 12f, 52f);
            legendText = NewText("Legend", root, font, 24f, new Color(0.82f, 0.85f, 0.92f));
            TopBand(legendText.rectTransform, 66f, 64f);
            legendText.enableWordWrapping = true;
            legendText.text =
                $"Ополченцы по линии: прямые → {GameVisuals.ClassIcon(UnitClass.Warrior)}, углы → {GameVisuals.ClassIcon(UnitClass.Archer)}, " +
                $"зигзаги → {GameVisuals.ClassIcon(UnitClass.Priest)} (нужна гильдия). Две линии разных классов → гибрид, все три → богатырь";

            reels = NewRect("Reels", root);
            reels.anchorMin = new Vector2(0f, 1f);
            reels.anchorMax = new Vector2(1f, 1f);
            reels.pivot = new Vector2(0.5f, 1f);
            reels.gameObject.AddComponent<RectMask2D>();
            views = new TileView[Reels, Rows];
            for (int y = 0; y < Rows; y++)
            {
                for (int x = 0; x < Reels; x++)
                {
                    TileView view = Instantiate(board.tilePrefab, reels);
                    view.Init(this, new Vector2Int(x, y));
                    views[x, y] = view;
                }
            }
            linesRoot = NewRect("Paylines", reels);
            linesRoot.anchorMin = Vector2.zero;
            linesRoot.anchorMax = Vector2.one;
            linesRoot.offsetMin = linesRoot.offsetMax = Vector2.zero;

            logText = NewText("Log", root, font, 26f, Color.white);
            logText.enableWordWrapping = true;
            logText.alignment = TextAlignmentOptions.Top;
            LayoutViews();
        }

        /// <summary>Барабаны во всю ширину рамки (5:3), журнал — под ними.</summary>
        private void LayoutViews()
        {
            if (root == null || reels == null || views == null) return;
            float width = Mathf.Max(100f, root.rect.width - 24f);
            float height = width * Rows / Reels;
            reels.anchoredPosition = new Vector2(0f, -136f);
            reels.sizeDelta = new Vector2(-24f, height);
            lastSize = reels.rect.size;
            step = width / Reels;
            float tileSize = Mathf.Max(8f, step - tileSpacing);
            for (int y = 0; y < Rows; y++)
            {
                for (int x = 0; x < Reels; x++)
                {
                    var home = new Vector2((x - (Reels - 1) * 0.5f) * step, ((Rows - 1) * 0.5f - y) * step);
                    views[x, y].Place(home, tileSize, dropSpeed * step);
                }
            }
            if (logText != null)
            {
                RectTransform rect = logText.rectTransform;
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.offsetMin = new Vector2(16f, 8f);
                rect.offsetMax = new Vector2(-16f, -(136f + height + 14f));
            }
        }

        private static void TopBand(RectTransform rect, float top, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(16f, -top - height);
            rect.offsetMax = new Vector2(-16f, -top);
        }

        private static TMP_Text NewText(string name, RectTransform parent, TMP_Text fontSource, float size, Color color)
        {
            RectTransform rect = NewRect(name, parent);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (fontSource != null) text.font = fontSource.font;
            text.fontSize = size;
            text.enableAutoSizing = true;
            text.fontSizeMin = 12f;
            text.fontSizeMax = size;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.color = color;
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            return text;
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
