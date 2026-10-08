using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Casiwar
{
    /// <summary>Символ боевого слота.</summary>
    public enum BattleSymbol
    {
        Blank,      // пусто
        Attack,     // ⚔ обычная атака
        Move,       // ➜ перемещение
        Warrior,    // массовый навык воинов — «Рывок» / «Таран щитом»
        Archer,     // массовый навык лучников — «Залп»
        Priest,     // массовый навык жрецов — «Молитва»
        Banner,     // 🚩 Знамя — джокер: становится частью любого соседнего кластера
        Multiplier, // ×2 (×3) — соседний кластер действует сильнее; множители рядом перемножаются
        Relic,      // 🕯 Свеча — 3+ в любом месте поля: «Чудо»
        Bomber,     // массовый навык подрывников — «Динамит»
        Monk,       // массовый навык монахов — «Огненные клинки»
        Mage,       // массовый навык магов — «Цепная молния»
    }

    /// <summary>Связная группа одинаковых символов (соседи по сторонам); Знамёна в неё входят.</summary>
    public sealed class SlotCluster
    {
        public BattleSymbol Symbol;
        public readonly List<Vector2Int> Cells = new List<Vector2Int>();
        /// <summary>Множители, которые касаются кластера.</summary>
        public readonly List<Vector2Int> MultiplierCells = new List<Vector2Int>();
        /// <summary>Сила от множителей рядом: ×1, ×2, ×4…</summary>
        public int Multiplier = 1;
        public int Size => Cells.Count;
    }

    /// <summary>Одна волна каскада: расклад, кластеры, что сработало и что лопнет.</summary>
    public sealed class SlotWave
    {
        /// <summary>Номер волны = множитель каскада (×1, ×2, ×3…).</summary>
        public int Index;
        public BattleSymbol[,] Grid;
        public readonly List<SlotCluster> Clusters = new List<SlotCluster>();
        /// <summary>Самый большой ⚔ — столько юнитов бьют.</summary>
        public SlotCluster Attack;
        /// <summary>Самый большой ➜ — столько юнитов ходят.</summary>
        public SlotCluster Move;
        /// <summary>Навыки классов, набравшие порог.</summary>
        public readonly Dictionary<UnitClass, SlotCluster> Skills = new Dictionary<UnitClass, SlotCluster>();
        public readonly List<Vector2Int> Relics = new List<Vector2Int>();
        public bool Miracle;
        /// <summary>Клетки, которые лопнут: сработавшие кластеры от минимального размера, их множители и свечи «Чуда».</summary>
        public readonly HashSet<Vector2Int> Burst = new HashSet<Vector2Int>();
        /// <summary>Лопнул хотя бы один боевой кластер — будет следующая волна (свечи сами каскад не запускают).</summary>
        public bool Cascades;

        public SlotCluster Largest(BattleSymbol symbol)
        {
            SlotCluster best = null;
            foreach (SlotCluster cluster in Clusters)
            {
                if (cluster.Symbol == symbol && (best == null || cluster.Size > best.Size)) best = cluster;
            }
            return best;
        }

        public int LargestSize(BattleSymbol symbol) => Largest(symbol)?.Size ?? 0;

        /// <summary>Итоговая сила кластера: множитель каскада × множители рядом.</summary>
        public int PowerOf(SlotCluster cluster) => Index * (cluster != null ? cluster.Multiplier : 1);

        public IEnumerable<SlotCluster> Fired()
        {
            if (Attack != null) yield return Attack;
            if (Move != null) yield return Move;
            foreach (SlotCluster skill in Skills.Values) yield return skill;
        }
    }

    /// <summary>Итог одного спина: волны каскада (первая — то, что выпало сразу).</summary>
    public sealed class SlotSpinResult
    {
        public readonly List<SlotWave> Waves = new List<SlotWave>();
        public BattleSymbol[,] Grid => Waves.Count > 0 ? Waves[0].Grid : null;
        public SlotCluster Largest(BattleSymbol symbol) => Waves.Count > 0 ? Waves[0].Largest(symbol) : null;
        public int LargestSize(BattleSymbol symbol) => Largest(symbol)?.Size ?? 0;
    }

    /// <summary>Что может выпасть в слоте стороны и как считаются волны.</summary>
    public sealed class SlotSetup
    {
        public int Size = 4;
        public readonly List<UnitClass> Classes = new List<UnitClass>();
        /// <summary>Уровень класса → +вес его символа навыка.</summary>
        public Func<UnitClass, int> ClassLevel;
        /// <summary>Сколько символов нужно навыку класса (по умолчанию 3).</summary>
        public Func<UnitClass, int> Threshold;
        public float AttackWeight = 1.5f;
        public float MoveWeight = 1.2f;
        public float BlankWeight = 0.8f;
        public float SkillWeight = 1.2f;
        public float SkillWeightPerLevel = 0.2f;
        public float BannerWeight;
        public float MultiplierWeight;
        public float RelicWeight;
        public int MultiplierValue = 2;
        public int BurstMin = 5;
        public int MaxWaves = 3;
        public int RelicsForMiracle = 3;
        public int MaxMultiplier = 8;
    }

    /// <summary>
    /// Боевой слот («бонуска»). Каждый шаг боя поле символов «дропается» сверху, затем считаются кластеры —
    /// группы одинаковых символов, соседних по сторонам:
    ///   ⚔  самый большой кластер атаки = сколько СЛУЧАЙНЫХ юнитов бьют;
    ///   ➜  самый большой кластер движения = сколько случайных юнитов делают шаг;
    ///   В/Л/М — массовый навык класса, если в кластере символов не меньше, чем юнитов класса в бою (и не меньше 3).
    /// Особые символы (открываются в ветках «Войск»):
    ///   🚩 Знамя — джокер, входит в любой соседний кластер;
    ///   ×2 — кластер рядом действует вдвое сильнее (множители рядом перемножаются; «Меткие стрелки» дают ×3);
    ///   🕯 Свеча — 3+ в любом месте поля: «Чудо» (лечит и благословляет всю армию).
    /// КАСКАДЫ: сработавшие кластеры от 5 символов лопаются, остальное падает вниз, сверху досыпаются новые символы —
    /// следующая волна с множителем ×2, потом ×3 (не больше maxWaves волн). Свечи «Чуда» лопаются, но каскад не запускают.
    /// Размер поля растёт с военным уровнем города. У варваров такой же слот, только скрытый и без особых символов.
    /// </summary>
    public class BattleSlotMachine : MonoBehaviour
    {
        private static readonly BattleSymbol[] ClusterSymbols =
        {
            BattleSymbol.Attack, BattleSymbol.Move, BattleSymbol.Warrior, BattleSymbol.Archer, BattleSymbol.Priest,
            BattleSymbol.Bomber, BattleSymbol.Monk, BattleSymbol.Mage,
        };

        private static readonly Vector2Int[] Sides = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

        [Header("Ссылки")]
        [Tooltip("UI-область поля слота (квадратный RectTransform). Заполняется кодом.")]
        public RectTransform gridContainer;
        [Tooltip("Префаб клетки (тот же TileView, что у поля города)")]
        public TileView cellPrefab;
        public CityManager city;
        [Tooltip("Строка с итогом спина")]
        public TMP_Text resultText;

        [Header("Размер поля слота")]
        [Min(3)] public int baseSize = 4;
        [Min(3)] public int maxSize = 7;
        [Tooltip("+1 к размеру за каждые N уровней военного дела (открытые классы и улучшения отрядов)")]
        [Min(1)] public int militaryLevelsPerSize = 2;

        [Header("Веса символов")]
        [Min(0f)] public float attackWeight = 1.5f;
        [Min(0f)] public float moveWeight = 1.2f;
        [Min(0f)] public float blankWeight = 0.8f;
        [Tooltip("Вес символа навыка (у каждого класса в армии свой символ)")]
        [Min(0f)] public float skillWeight = 1.2f;
        [Tooltip("+вес символа навыка, если класс улучшен в городе")]
        [Min(0f)] public float skillWeightPerLevel = 0.2f;

        [Header("Особые символы (уровни — из веток «Войск»)")]
        [Min(0f)] public float bannerWeightPerLevel = 0.3f;
        [Min(0f)] public float multiplierWeightPerLevel = 0.3f;
        [Min(0f)] public float relicWeightPerLevel = 0.35f;
        [Tooltip("Сколько Свечей в любом месте поля дают «Чудо»")]
        [Min(1)] public int relicsForMiracle = 3;
        [Tooltip("Множители рядом перемножаются, но не больше этого")]
        [Min(1)] public int maxClusterMultiplier = 8;

        [Header("Массовые навыки и каскады")]
        [Tooltip("Минимальный кластер навыка. Кроме того, символов должно быть не меньше, чем юнитов класса в бою")]
        [Min(1)] public int minSkillCluster = 3;
        [Tooltip("Сработавший кластер от этого размера лопается — сверху падают новые символы")]
        [Min(2)] public int burstMinSize = 5;
        [Tooltip("Сколько волн каскада максимум за спин (множители ×1, ×2, ×3…)")]
        [Min(1)] public int maxWaves = 3;

        [Header("Вид и анимация")]
        [Min(0f)] public float tileSpacing = 6f;
        [Tooltip("Скорость падения символов, клеток в секунду")]
        [Min(1f)] public float dropSpeed = 18f;
        [Tooltip("Сколько секунд показывается волна")]
        [Min(0f)] public float showDuration = 0.45f;
        [Min(0.01f)] public float popDuration = 0.18f;

        private TileView[,] views;
        private int size;
        private float step;
        private Vector2 lastContainerSize;
        private readonly List<UnitClass> playerClasses = new List<UnitClass>();
        private Coroutine spinRoutine;

        public int Size => size;
        public bool IsSpinning { get; private set; }
        public SlotSpinResult LastResult { get; private set; }

        /// <summary>Спин игрока выпал (первая волна подсвечена).</summary>
        public event Action<SlotSpinResult> Spun;
        /// <summary>Волна каскада показана (перед тем как сработать).</summary>
        public event Action<SlotWave> WaveShown;

        // =====================================================================
        //  Подготовка
        // =====================================================================

        /// <summary>Перед боем: какие классы в армии игрока → какие символы на ленте; размер — по военному уровню.</summary>
        public void Prepare(IEnumerable<UnitClass> classesInArmy)
        {
            playerClasses.Clear();
            foreach (UnitClass unitClass in classesInArmy.Distinct())
            {
                if (SymbolFor(unitClass) != BattleSymbol.Blank) playerClasses.Add(unitClass); // у богатыря своего символа нет
            }

            int levels = city != null ? city.MilitaryLevel : 0;
            int newSize = SizeFor(levels);
            if (views == null || newSize != size) Build(newSize);
            ShowIdle();
            if (resultText != null) resultText.text = LegendLine();
        }

        private string LegendLine()
        {
            var parts = new List<string> { $"{GameVisuals.IconAttack} бьют", $"{GameVisuals.IconMove} ходят" };
            parts.AddRange(playerClasses.Select(c => $"{GameVisuals.ClassLetter(c)} — {SkillTitle(c)}"));
            if (city != null && city.BannerLevel > 0) parts.Add($"{GameVisuals.IconBanner} джокер");
            if (city != null && city.MultiplierLevel > 0) parts.Add($"×{city.MultiplierValue} множитель");
            if (city != null && city.RelicLevel > 0) parts.Add($"3 {GameVisuals.IconCandle} — чудо");
            return string.Join(" · ", parts);
        }

        /// <summary>Навык класса игрока: воины без щитов делают «Рывок», со щитами — «Таран щитом».</summary>
        private string SkillTitle(UnitClass unitClass)
        {
            return unitClass == UnitClass.Warrior && (city == null || city.ShieldLevel == 0) ? "Рывок" : GameVisuals.SkillName(unitClass);
        }

        public int SizeFor(int militaryLevel)
        {
            return Mathf.Clamp(baseSize + militaryLevel / Mathf.Max(1, militaryLevelsPerSize), baseSize, Mathf.Max(baseSize, maxSize));
        }

        /// <summary>Слот стороны: размер, классы, пороги навыков и особые символы.</summary>
        public SlotSetup MakeSetup(int slotSize, IEnumerable<UnitClass> classes, Func<UnitClass, int> classLevel, Func<UnitClass, int> threshold,
            int bannerLevel = 0, int multiplierLevel = 0, int relicLevel = 0, int multiplierValue = 2)
        {
            var setup = new SlotSetup
            {
                Size = slotSize,
                ClassLevel = classLevel,
                Threshold = threshold,
                AttackWeight = attackWeight,
                MoveWeight = moveWeight,
                BlankWeight = blankWeight,
                SkillWeight = skillWeight,
                SkillWeightPerLevel = skillWeightPerLevel,
                BannerWeight = bannerWeightPerLevel * bannerLevel,
                MultiplierWeight = multiplierWeightPerLevel * multiplierLevel,
                RelicWeight = relicWeightPerLevel * relicLevel,
                MultiplierValue = Mathf.Max(2, multiplierValue),
                BurstMin = burstMinSize,
                MaxWaves = maxWaves,
                RelicsForMiracle = relicsForMiracle,
                MaxMultiplier = maxClusterMultiplier,
            };
            setup.Classes.AddRange(classes.Where(c => SymbolFor(c) != BattleSymbol.Blank).Distinct());
            return setup;
        }

        private SlotSetup PlayerSetup(AutoBattleManager battle)
        {
            return MakeSetup(size, playerClasses,
                c => city != null && city.SkillPower(c) > 1f ? 1 : 0,
                c => battle != null ? battle.SkillThreshold(Team.Player, c) : minSkillCluster,
                city != null ? city.BannerLevel : 0,
                city != null ? city.MultiplierLevel : 0,
                city != null ? city.RelicLevel : 0,
                city != null ? city.MultiplierValue : 2);
        }

        // =====================================================================
        //  Спин игрока (с анимацией и каскадами)
        // =====================================================================

        /// <summary>
        /// Спин игрока с анимацией: каждая волна каскада показывается и сразу срабатывает
        /// (AutoBattleManager.ResolveWave). Бой ждёт корутину и читает LastResult.
        /// </summary>
        public Coroutine StartSpin(AutoBattleManager battle)
        {
            StopSpin();
            spinRoutine = StartCoroutine(SpinRoutine(battle));
            return spinRoutine;
        }

        public void StopSpin()
        {
            if (spinRoutine != null) StopCoroutine(spinRoutine);
            spinRoutine = null;
            IsSpinning = false;
        }

        public void ResetGrid()
        {
            StopSpin();
            ShowIdle();
            if (resultText != null) resultText.text = string.Empty;
        }

        private IEnumerator SpinRoutine(AutoBattleManager battle)
        {
            IsSpinning = true;
            float speed = battle != null ? battle.battleSpeed : 1f;
            SlotSetup setup = PlayerSetup(battle);
            SlotSpinResult result = RollCascade(setup);
            LastResult = result;
            var summaries = new List<string>();

            for (int w = 0; w < result.Waves.Count; w++)
            {
                SlotWave wave = result.Waves[w];
                if (views != null)
                {
                    if (w == 0) DropIn(wave.Grid, setup.MultiplierValue, speed);
                    else Tumble(result.Waves[w - 1], wave.Grid, setup.MultiplierValue, speed);
                    if (w == 0 && resultText != null) resultText.text = "...";
                    yield return WaitSettled();
                    Highlight(wave);
                }
                if (w == 0) Spun?.Invoke(result);
                WaveShown?.Invoke(wave);

                if (battle != null && battle.IsRunning) summaries.Add(battle.ResolveWave(Team.Player, wave));
                if (resultText != null) resultText.text = string.Join("  |  ", summaries);
                yield return Wait(showDuration, speed);

                bool battleOver = battle != null && (!battle.IsRunning || battle.CountAlive(Team.Enemy) == 0 || battle.CountAlive(Team.Player) == 0);
                if (battleOver || w + 1 >= result.Waves.Count) break;
                if (views != null)
                {
                    foreach (Vector2Int cell in wave.Burst) views[cell.x, cell.y].PlayPop(popDuration);
                    yield return Wait(popDuration, speed);
                }
            }
            IsSpinning = false;
            spinRoutine = null;
        }

        /// <summary>Скрытый спин (для варваров): те же волны, без анимации и без особых символов.</summary>
        public SlotSpinResult SimulateSpin(int slotSize, IEnumerable<UnitClass> classes, Func<UnitClass, int> classLevel, Func<UnitClass, int> threshold)
        {
            return RollCascade(MakeSetup(slotSize, classes, classLevel, threshold));
        }

        public void SetResultText(string text)
        {
            if (resultText != null) resultText.text = text;
        }

        // =====================================================================
        //  Логика (чистые функции — их проверяют тесты)
        // =====================================================================

        /// <summary>Спин с каскадами: волна → лопнуло сработавшее → упало и досыпалось → следующая волна (×2, ×3…).</summary>
        public static SlotSpinResult RollCascade(SlotSetup setup)
        {
            var result = new SlotSpinResult();
            BattleSymbol[,] grid = RollGrid(setup);
            for (int index = 1; index <= Mathf.Max(1, setup.MaxWaves); index++)
            {
                SlotWave wave = Evaluate(grid, index, setup);
                result.Waves.Add(wave);
                if (!wave.Cascades || index >= setup.MaxWaves) break;
                grid = Tumble(grid, wave.Burst, setup);
            }
            return result;
        }

        public static BattleSymbol[,] RollGrid(SlotSetup setup)
        {
            var grid = new BattleSymbol[setup.Size, setup.Size];
            for (int x = 0; x < setup.Size; x++)
            {
                for (int y = 0; y < setup.Size; y++) grid[x, y] = RandomSymbol(setup);
            }
            return grid;
        }

        private static BattleSymbol RandomSymbol(SlotSetup setup)
        {
            var symbols = new List<BattleSymbol> { BattleSymbol.Blank, BattleSymbol.Attack, BattleSymbol.Move, BattleSymbol.Banner, BattleSymbol.Multiplier, BattleSymbol.Relic };
            var weights = new List<float> { setup.BlankWeight, setup.AttackWeight, setup.MoveWeight, setup.BannerWeight, setup.MultiplierWeight, setup.RelicWeight };
            foreach (UnitClass unitClass in setup.Classes)
            {
                symbols.Add(SymbolFor(unitClass));
                weights.Add(setup.SkillWeight + setup.SkillWeightPerLevel * (setup.ClassLevel != null ? setup.ClassLevel(unitClass) : 0));
            }
            float roll = Random.value * Mathf.Max(0.0001f, weights.Sum());
            for (int i = 0; i < symbols.Count; i++)
            {
                roll -= weights[i];
                if (roll < 0f) return symbols[i];
            }
            return BattleSymbol.Attack;
        }

        /// <summary>Разобрать волну: кластеры, множители, что сработает (⚔, ➜, навыки, «Чудо») и что лопнет.</summary>
        public static SlotWave Evaluate(BattleSymbol[,] grid, int index, SlotSetup setup)
        {
            var wave = new SlotWave { Index = index, Grid = (BattleSymbol[,])grid.Clone() };
            wave.Clusters.AddRange(FindClusters(grid));
            foreach (SlotCluster cluster in wave.Clusters) AttachMultipliers(grid, cluster, setup.MultiplierValue, setup.MaxMultiplier);

            wave.Attack = wave.Largest(BattleSymbol.Attack);
            wave.Move = wave.Largest(BattleSymbol.Move);
            foreach (UnitClass unitClass in setup.Classes)
            {
                SlotCluster cluster = wave.Largest(SymbolFor(unitClass));
                int need = setup.Threshold != null ? setup.Threshold(unitClass) : 3;
                if (cluster != null && cluster.Size >= need) wave.Skills[unitClass] = cluster;
            }

            int width = grid.GetLength(0), height = grid.GetLength(1);
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (grid[x, y] == BattleSymbol.Relic) wave.Relics.Add(new Vector2Int(x, y));
                }
            }
            wave.Miracle = wave.Relics.Count >= setup.RelicsForMiracle;

            foreach (SlotCluster fired in wave.Fired())
            {
                if (fired.Size < setup.BurstMin) continue;
                foreach (Vector2Int cell in fired.Cells) wave.Burst.Add(cell);
                foreach (Vector2Int cell in fired.MultiplierCells) wave.Burst.Add(cell);
                wave.Cascades = true;
            }
            if (wave.Miracle)
            {
                foreach (Vector2Int cell in wave.Relics) wave.Burst.Add(cell);
            }
            return wave;
        }

        /// <summary>Кластеры символов ⚔, ➜ и навыков. Знамя — джокер: входит в любой соседний кластер (и сразу в несколько).</summary>
        public static List<SlotCluster> FindClusters(BattleSymbol[,] grid)
        {
            int width = grid.GetLength(0);
            int height = grid.GetLength(1);
            var clusters = new List<SlotCluster>();
            var stack = new Stack<Vector2Int>();

            foreach (BattleSymbol symbol in ClusterSymbols)
            {
                var seen = new bool[width, height];
                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        if (seen[x, y] || grid[x, y] != symbol) continue;

                        var cluster = new SlotCluster { Symbol = symbol };
                        seen[x, y] = true;
                        stack.Push(new Vector2Int(x, y));
                        while (stack.Count > 0)
                        {
                            Vector2Int cell = stack.Pop();
                            cluster.Cells.Add(cell);
                            foreach (Vector2Int side in Sides)
                            {
                                Vector2Int next = cell + side;
                                if (next.x < 0 || next.y < 0 || next.x >= width || next.y >= height || seen[next.x, next.y]) continue;
                                BattleSymbol other = grid[next.x, next.y];
                                if (other != symbol && other != BattleSymbol.Banner) continue;
                                seen[next.x, next.y] = true;
                                stack.Push(next);
                            }
                        }
                        clusters.Add(cluster);
                    }
                }
            }
            return clusters;
        }

        /// <summary>Множители, касающиеся кластера: сила перемножается (×2 × ×2 = ×4), но не больше max.</summary>
        private static void AttachMultipliers(BattleSymbol[,] grid, SlotCluster cluster, int value, int max)
        {
            int width = grid.GetLength(0), height = grid.GetLength(1);
            foreach (Vector2Int cell in cluster.Cells)
            {
                foreach (Vector2Int side in Sides)
                {
                    Vector2Int next = cell + side;
                    if (next.x < 0 || next.y < 0 || next.x >= width || next.y >= height) continue;
                    if (grid[next.x, next.y] == BattleSymbol.Multiplier && !cluster.MultiplierCells.Contains(next)) cluster.MultiplierCells.Add(next);
                }
            }
            int multiplier = 1;
            for (int i = 0; i < cluster.MultiplierCells.Count; i++) multiplier = Mathf.Min(Mathf.Max(1, max), multiplier * Mathf.Max(2, value));
            cluster.Multiplier = multiplier;
        }

        /// <summary>Лопнувшие клетки пустеют, символы падают вниз (y растёт вниз), сверху досыпаются новые.</summary>
        public static BattleSymbol[,] Tumble(BattleSymbol[,] grid, ICollection<Vector2Int> burst, SlotSetup setup)
        {
            int width = grid.GetLength(0), height = grid.GetLength(1);
            var next = new BattleSymbol[width, height];
            for (int x = 0; x < width; x++)
            {
                int write = height - 1;
                for (int y = height - 1; y >= 0; y--)
                {
                    if (burst.Contains(new Vector2Int(x, y))) continue;
                    next[x, write--] = grid[x, y];
                }
                for (int y = write; y >= 0; y--) next[x, y] = RandomSymbol(setup);
            }
            return next;
        }

        public static BattleSymbol SymbolFor(UnitClass unitClass)
        {
            switch (unitClass)
            {
                case UnitClass.Warrior: return BattleSymbol.Warrior;
                case UnitClass.Archer: return BattleSymbol.Archer;
                case UnitClass.Priest: return BattleSymbol.Priest;
                case UnitClass.Bomber: return BattleSymbol.Bomber;
                case UnitClass.Monk: return BattleSymbol.Monk;
                case UnitClass.Mage: return BattleSymbol.Mage;
                default: return BattleSymbol.Blank;
            }
        }

        public static UnitClass ClassOf(BattleSymbol symbol)
        {
            switch (symbol)
            {
                case BattleSymbol.Warrior: return UnitClass.Warrior;
                case BattleSymbol.Archer: return UnitClass.Archer;
                case BattleSymbol.Priest: return UnitClass.Priest;
                case BattleSymbol.Bomber: return UnitClass.Bomber;
                case BattleSymbol.Monk: return UnitClass.Monk;
                case BattleSymbol.Mage: return UnitClass.Mage;
                default: return UnitClass.None;
            }
        }

        // =====================================================================
        //  Визуал
        // =====================================================================

        private void Build(int newSize)
        {
            if (views != null)
            {
                foreach (TileView view in views)
                {
                    if (view != null) Destroy(view.gameObject);
                }
            }
            views = null;
            size = newSize;
            if (gridContainer == null || cellPrefab == null) return; // без UI слот всё равно работает

            views = new TileView[size, size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    TileView view = Instantiate(cellPrefab, gridContainer);
                    view.Init(null, new Vector2Int(x, y));
                    views[x, y] = view;
                }
            }
            LayoutViews();
        }

        private void LayoutViews()
        {
            if (views == null || gridContainer == null) return;
            Rect rect = gridContainer.rect;
            lastContainerSize = rect.size;
            float side = Mathf.Min(rect.width, rect.height);
            if (side < 1f) side = 600f;
            step = side / size;
            float cellSize = Mathf.Max(8f, step - tileSpacing);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var home = new Vector2((x - (size - 1) * 0.5f) * step, ((size - 1) * 0.5f - y) * step);
                    views[x, y].Place(home, cellSize, dropSpeed * step);
                }
            }
        }

        private void Update()
        {
            // Окно поменяло размер (или раскладку) — переставляем символы
            if (views != null && !IsSpinning && gridContainer != null && gridContainer.rect.size != lastContainerSize)
                LayoutViews();
        }

        /// <summary>Первая волна: все символы падают сверху, колонки с небольшой задержкой.</summary>
        private void DropIn(BattleSymbol[,] grid, int multiplierValue, float speed)
        {
            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++)
                {
                    TileView view = views[x, y];
                    view.Show(GameVisuals.LookForSlot(grid[x, y], multiplierValue));
                    view.SetMoveSpeed(dropSpeed * step * speed);
                    view.AnimateFrom(view.Home + Vector2.up * step * (size + x * 0.6f));
                }
            }
        }

        /// <summary>Каскад: уцелевшие символы падают со своих мест, новые — из-за верхнего края.</summary>
        private void Tumble(SlotWave previous, BattleSymbol[,] grid, int multiplierValue, float speed)
        {
            for (int x = 0; x < size; x++)
            {
                var survivors = new List<int>(); // старые строки уцелевших, снизу вверх
                for (int y = size - 1; y >= 0; y--)
                {
                    if (!previous.Burst.Contains(new Vector2Int(x, y))) survivors.Add(y);
                }
                int fresh = size - survivors.Count;
                for (int y = 0; y < size; y++)
                {
                    TileView view = views[x, y];
                    view.Show(GameVisuals.LookForSlot(grid[x, y], multiplierValue));
                    view.SetMoveSpeed(dropSpeed * step * speed);
                    int fromRow = y < fresh ? y - fresh : survivors[size - 1 - y];
                    if (fromRow != y) view.AnimateFrom(view.Home + Vector2.up * step * (y - fromRow));
                }
            }
        }

        /// <summary>Подсветка того, что сработает в волне (⚔, ➜, навыки, «Чудо» и их множители).</summary>
        private void Highlight(SlotWave wave)
        {
            var active = new HashSet<Vector2Int>();
            foreach (SlotCluster cluster in wave.Fired())
            {
                foreach (Vector2Int cell in cluster.Cells) active.Add(cell);
                foreach (Vector2Int cell in cluster.MultiplierCells) active.Add(cell);
            }
            if (wave.Miracle)
            {
                foreach (Vector2Int cell in wave.Relics) active.Add(cell);
            }
            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++)
                {
                    bool on = active.Contains(new Vector2Int(x, y));
                    views[x, y].SetHint(on);
                    views[x, y].SetDimmed(!on);
                }
            }
        }

        private void ShowIdle()
        {
            if (views == null) return;
            foreach (TileView view in views)
            {
                view.Show(GameVisuals.LookForSlot(BattleSymbol.Blank));
                view.SetDimmed(false);
            }
        }

        private IEnumerator WaitSettled()
        {
            float timeout = 3f;
            while (timeout > 0f && !AllSettled())
            {
                timeout -= Time.deltaTime;
                yield return null;
            }
        }

        private static IEnumerator Wait(float seconds, float speed)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime * speed;
                yield return null;
            }
        }

        private bool AllSettled()
        {
            foreach (TileView view in views)
            {
                if (!view.IsSettled) return false;
            }
            return true;
        }
    }
}
