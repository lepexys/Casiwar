#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Casiwar
{
    /// <summary>
    /// Временный бот теста экономики: играет настоящую игру в play mode (только в редакторе и только с аргументом
    /// -casiwarEcon &lt;папка&gt; [забегов] [ходов]). Наука, стройка и улучшения — в случайном порядке: бот берёт
    /// случайную доступную цель и копит на неё. В бою делает ходы на поле боя. Пишет CSV в папку.
    /// </summary>
    public class EconomyBot : MonoBehaviour
    {
        private const string Arg = "-casiwarEcon";
        private static string outDir;
        private static int runsTotal = 10;
        private static int turnsPerRun = 50;
        /// <summary>Варианты темпа боя (arenaTempo:tacticsSlow); пусто — как в сцене. Аргумент -casiwarVariants "1:1,0.5:0.5".</summary>
        private static readonly List<(float tempo, float slow)> variants = new List<(float, float)>();
        /// <summary>Множитель ХП на арене для каждого варианта (третье число: "0.5:0.5:3"); −1 — как в сцене.</summary>
        private static readonly List<float> variantHp = new List<float>();
        private static float moveEveryArg = -1f;
        private (float tempo, float slow) variant = (-1f, -1f);

        public float timeScale = 12f;
        [Tooltip("Секунд боя между ходами бота на поле боя")]
        public float moveEvery = 2f;
        public float watchdogSeconds = 300f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, Arg);
            if (i < 0 || i + 1 >= args.Length) return;
            outDir = args[i + 1];
            if (i + 2 < args.Length && int.TryParse(args[i + 2], out int runs)) runsTotal = runs;
            if (i + 3 < args.Length && int.TryParse(args[i + 3], out int turns)) turnsPerRun = turns;
            int v = Array.IndexOf(args, "-casiwarVariants");
            if (v >= 0 && v + 1 < args.Length)
            {
                foreach (string pair in args[v + 1].Split(','))
                {
                    string[] parts = pair.Split(':');
                    variants.Add((float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture)));
                    variantHp.Add(parts.Length > 2 ? float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture) : -1f);
                }
            }
            int m = Array.IndexOf(args, "-casiwarMoveEvery");
            if (m >= 0 && m + 1 < args.Length) moveEveryArg = float.Parse(args[m + 1], System.Globalization.CultureInfo.InvariantCulture);
            Directory.CreateDirectory(outDir);
            new GameObject("EconomyBot").AddComponent<EconomyBot>();
        }

        private sealed class Option
        {
            public string Key;
            public string Kind;
            public string Title;
            public int Cost;
            public Func<bool> Buy;
        }

        private GameManager game;
        private CityManager city;
        private TechTree tree;
        private WorldPowers powers;
        private BenchManager bench;
        private RecruitSlot recruit;
        private ResourceRain rain;
        private BattleBoard board;

        private int run;
        private string label = "start";
        private int lastGold, lastProd, lastFood, lastKnow;
        private int phaseTurn = -1;
        private float lastProgress;

        private string techTarget;
        private int techPicked;
        private string prodTarget;
        private int prodPicked;
        private string goldTarget;
        private int goldPicked;

        private readonly Dictionary<string, int> available = new Dictionary<string, int>();
        // (ход, метка) → [+🪙, −🪙, +⚒, −⚒, +🍎, −🍎, +📖, −📖]
        private readonly Dictionary<(int, string), int[]> flows = new Dictionary<(int, string), int[]>();
        private readonly Dictionary<int, string> turnRows = new Dictionary<int, string>();
        private readonly StringBuilder buys = new StringBuilder();
        private readonly StringBuilder battles = new StringBuilder();
        private readonly StringBuilder wants = new StringBuilder();

        private float nextBeat;

        private void Update()
        {
            if (game == null || Time.realtimeSinceStartup < nextBeat) return;
            nextBeat = Time.realtimeSinceStartup + 5f;
            AutoBattleManager arena = FindFirstObjectByType<AutoBattleManager>(FindObjectsInactive.Include);
            File.AppendAllText(Path.Combine(outDir, "progress.txt"),
                $"  beat real={Time.realtimeSinceStartup:F0} frame={Time.frameCount} time={Time.time:F0} ts={Time.timeScale} phase={game.Phase} turn={game.Turn} " +
                $"rain={rain != null && rain.IsPlaying} recruitDone={recruit == null || recruit.Done} " +
                $"battle={(arena != null ? $"{arena.IsRunning} P{arena.CountAlive(Team.Player)} E{arena.CountAlive(Team.Enemy)} active={arena.isActiveAndEnabled}" : "none")}\n");
        }

        private IEnumerator Start()
        {
            for (int i = 0; i < 5; i++) yield return null;
            game = FindFirstObjectByType<GameManager>();
            city = FindFirstObjectByType<CityManager>();
            tree = FindFirstObjectByType<TechTree>();
            powers = FindFirstObjectByType<WorldPowers>();
            bench = FindFirstObjectByType<BenchManager>();
            recruit = FindFirstObjectByType<RecruitSlot>();
            rain = FindFirstObjectByType<ResourceRain>();
            board = FindFirstObjectByType<BattleBoard>(FindObjectsInactive.Include);
            city.Changed += OnCityChanged;
            Time.timeScale = timeScale;
            // В batchmode у рисунка местности (TerrainLayer, создаётся кодом) нет CanvasRenderer — исключение каждый кадр
            foreach (UnityEngine.UI.Graphic graphic in FindObjectsByType<UnityEngine.UI.Graphic>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (graphic.GetComponent<CanvasRenderer>() != null) continue;
                graphic.gameObject.AddComponent<CanvasRenderer>();
                // Graphic уже закэшировал «пустой» CanvasRenderer — сбросить кэш, чтобы взял новый
                typeof(UnityEngine.UI.Graphic).GetField("m_CanvasRenderer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.SetValue(graphic, null);
                graphic.SetAllDirty();
            }
            // лог оставляем: нужны исключения
            WriteCatalog();
            File.WriteAllText(Path.Combine(outDir, "turns.csv"),
                "run,turn,pop,gold,prod,food,know,units,garrison,home,buildings,techs,upgrades,radius,kpd\n");
            File.WriteAllText(Path.Combine(outDir, "flows.csv"), "run,turn,label,gold_in,gold_out,prod_in,prod_out,food_in,food_out,know_in,know_out\n");
            File.WriteAllText(Path.Combine(outDir, "buys.csv"), "run,turn,kind,key,title,cost,available,picked,balance\n");
            File.WriteAllText(Path.Combine(outDir, "available.csv"), "run,key,turn\n");
            File.WriteAllText(Path.Combine(outDir, "battles.csv"), "run,turn,victory,our_before,our_after,enemies,enemy_stars,seconds," +
                "tempo,slow,hp,board_s,arena_s,slow_s,matches,supers,board_dmg,self_dmg,enemy_dmg\n");
            File.WriteAllText(Path.Combine(outDir, "runs.csv"), "run,end_turn,result,realtime,tempo,slow\n");
            File.WriteAllText(Path.Combine(outDir, "wants.csv"), "run,turn,res,key,cost,balance\n");

            if (moveEveryArg > 0f) moveEvery = moveEveryArg;
            if (variants.Count == 0)
            {
                variants.Add((-1f, -1f));
                variantHp.Add(-1f);
            }
            AutoBattleManager arenaSettings = FindFirstObjectByType<AutoBattleManager>(FindObjectsInactive.Include);
            run = 0;
            for (int vi = 0; vi < variants.Count; vi++)
            {
                (float tempo, float slow) next = variants[vi];
                variant = next;
                if (arenaSettings != null && next.tempo > 0f)
                {
                    arenaSettings.arenaTempo = next.tempo;
                    arenaSettings.tacticsSlow = next.slow;
                }
                if (arenaSettings != null && variantHp[vi] > 0f) arenaSettings.hpMultiplier = variantHp[vi];
                for (int r = 1; r <= runsTotal; r++)
                {
                    run++;
                    float started = Time.realtimeSinceStartup;
                    string result = "survived";
                    yield return PlayRun(res => result = res);
                    FlushRun(result, Time.realtimeSinceStartup - started);
                    File.AppendAllText(Path.Combine(outDir, "progress.txt"),
                        $"run {run} (tempo {next.tempo}, slow {next.slow}) {r}/{runsTotal}: {result}, turn {game.Turn}, {Time.realtimeSinceStartup - started:F0}s\n");
                }
            }
            File.WriteAllText(Path.Combine(outDir, "done.txt"), "ok");
            UnityEditor.EditorApplication.Exit(0);
        }

        // =====================================================================
        //  Забег
        // =====================================================================

        private IEnumerator PlayRun(Action<string> done)
        {
            available.Clear();
            flows.Clear();
            turnRows.Clear();
            techTarget = prodTarget = goldTarget = null;
            label = "start";
            resetting = true;
            game.StartRun();
            resetting = false;
            Snapshot();
            phaseTurn = -1;
            int seenTurn = 0;
            lastProgress = Time.realtimeSinceStartup;
            GamePhase lastPhase = game.Phase;

            while (true)
            {
                if (game.Phase != lastPhase || game.Turn != seenTurn) lastProgress = Time.realtimeSinceStartup;
                lastPhase = game.Phase;
                if (Time.realtimeSinceStartup - lastProgress > watchdogSeconds)
                {
                    done($"stuck:{game.Phase}");
                    yield break;
                }
                if (game.Phase == GamePhase.GameOver)
                {
                    done("fell");
                    yield break;
                }
                if (game.Phase == GamePhase.Battle)
                {
                    yield return Battle();
                    continue;
                }
                if (game.Phase == GamePhase.Loot)
                {
                    if (phaseTurn != game.Turn) phaseTurn = game.Turn;
                    label = "endturn";
                    game.FinishLoot();
                    yield return null;
                    continue;
                }
                if (rain != null && rain.IsPlaying)
                {
                    rain.Skip();
                    yield return null;
                    continue;
                }
                if (game.Turn > turnsPerRun)
                {
                    done("survived");
                    yield break;
                }
                if (game.Turn != seenTurn)
                {
                    seenTurn = game.Turn;
                    File.AppendAllText(Path.Combine(outDir, "progress.txt"), $"run {run} turn {game.Turn} t={Time.realtimeSinceStartup:F0}\n");
                    MarkAvailable();
                }
                if (recruit != null && !recruit.Done)
                {
                    label = "recruit";
                    recruit.ResolveRemaining();
                    yield return null;
                    continue;
                }
                ManageArmy();
                Spend();
                RecordTurn();
                label = "endturn";
                int turn = game.Turn;
                game.EndTurn();
                yield return null;
                if (game.Phase == GamePhase.Preparation && game.Turn == turn && recruit != null && recruit.Done)
                {
                    // Ход не кончился и вербовка тут ни при чём — подождём кадр и попробуем снова
                    yield return null;
                }
            }
        }

        private IEnumerator Battle()
        {
            phaseTurn = game.Turn;
            int turn = game.Turn;
            int before = bench.UnitCount;
            List<Squad> attackers = powers.Attackers;
            int enemies = attackers.Sum(s => s.Units.Count);
            int stars = attackers.Sum(s => s.Units.Sum(u => u.stars));
            float started = Time.time;
            float nextMove = Time.time + moveEvery;
            label = "battle";
            AutoBattleManager arena = FindFirstObjectByType<AutoBattleManager>(FindObjectsInactive.Include);
            float nextReport = Time.realtimeSinceStartup + 3f;
            int frames = 0;
            while (game.Phase == GamePhase.Battle)
            {
                frames++;
                if (false && Time.realtimeSinceStartup >= nextReport && arena != null)
                {
                    nextReport = Time.realtimeSinceStartup + 3f;
                    File.AppendAllText(Path.Combine(outDir, "progress.txt"),
                        $"  battle t={Time.time - started:F0} running={arena.IsRunning} P={arena.CountAlive(Team.Player)} E={arena.CountAlive(Team.Enemy)} frames={frames} ts={Time.timeScale}\n");
                }
                if (board != null && board.Interactable && Time.time >= nextMove)
                {
                    if (board.TryFindMove(out Vector2Int a, out Vector2Int b)) board.OnTileSwiped(a, b - a);
                    nextMove = Time.time + moveEvery;
                }
                if (Time.realtimeSinceStartup - lastProgress > watchdogSeconds) yield break;
                yield return null;
            }
            bool victory = game.Phase == GamePhase.Loot;
            BattleStats st = arena != null ? arena.Stats : new BattleStats();
            string F(float x) => x.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            battles.AppendLine($"{run},{turn},{(victory ? 1 : 0)},{before},{bench.UnitCount},{enemies},{stars},{F(Time.time - started)}," +
                               $"{F(arena != null ? arena.arenaTempo : 0f)},{F(arena != null ? arena.tacticsSlow : 0f)},{F(arena != null ? arena.hpMultiplier : 0f)},{F(st.BoardSeconds)},{F(st.ArenaSeconds)},{F(st.SlowSeconds)}," +
                               $"{st.Matches},{st.Supers},{F(st.BoardDamage)},{F(st.SelfDamage)},{F(st.EnemyDamage)}");
        }

        /// <summary>Гарнизон полон — бойцы переходят в отряд на ратуше (он тоже защищает город), чтобы новобранцы не продавались зря.</summary>
        private void ManageArmy()
        {
            if (bench == null || powers == null) return;
            PlayerArmy home = powers.HomeArmy;
            if (home == null || !powers.AtTownHall(home)) return;
            if (!bench.GarrisonRow.HasFreeSlot && home.Row.HasFreeSlot)
            {
                label = "army";
                bench.Focus(null);
                bench.MoveAll(PanelRow.Top, PanelRow.Bottom);
            }
        }

        // =====================================================================
        //  Покупки: случайная доступная цель, копим на неё
        // =====================================================================

        private void Spend()
        {
            label = "spend";
            for (int guard = 0; guard < 20; guard++)
            {
                if (techTarget == null || tree.IsResearched(techTarget))
                {
                    List<TechConfig> open = tree.techs.Where(t => t != null && !tree.IsResearched(t.id) && tree.PrerequisitesMet(t)).ToList();
                    if (open.Count == 0) break;
                    techTarget = open[Random.Range(0, open.Count)].id;
                    techPicked = game.Turn;
                }
                TechConfig tech = tree.Find(techTarget);
                if (city.Knowledge < tech.knowledgeCost)
                {
                    wants.AppendLine($"{run},{game.Turn},know,tech:{tech.id},{tech.knowledgeCost},{city.Knowledge}");
                    break;
                }
                int balance = city.Knowledge;
                if (!game.TryResearch(techTarget)) break;
                LogBuy("tech", "tech:" + tech.id, tech.title, tech.knowledgeCost, techPicked, balance);
                techTarget = null;
            }

            if (Repairs()) BuyLoop(ref prodTarget, ref prodPicked, ProductionOptions, "prod", () => city.Production);
            BuyLoop(ref goldTarget, ref goldPicked, GoldOptions, "gold", () => city.Gold);
        }

        private void BuyLoop(ref string target, ref int picked, Func<List<Option>> options, string resource, Func<int> balance)
        {
            for (int guard = 0; guard < 30; guard++)
            {
                List<Option> list = options();
                string key = target;
                Option current = list.FirstOrDefault(o => o.Key == key);
                if (current == null)
                {
                    if (list.Count == 0)
                    {
                        target = null;
                        return;
                    }
                    current = list[Random.Range(0, list.Count)];
                    target = current.Key;
                    picked = game.Turn;
                }
                int have = balance();
                if (have < current.Cost)
                {
                    wants.AppendLine($"{run},{game.Turn},{resource},{current.Key},{current.Cost},{have}");
                    return;
                }
                if (!current.Buy())
                {
                    target = null;
                    return;
                }
                LogBuy(current.Kind, current.Key, current.Title, current.Cost, picked, have);
                target = null;
            }
        }

        /// <summary>Починка: сначала ратуша и разрушенные. Разрушенное не на что чинить — копим ⚒ (false).</summary>
        private bool Repairs()
        {
            foreach (Building building in city.Buildings.Where(b => b.IsDamaged)
                         .OrderBy(b => b.Type == BuildingType.TownHall ? 0 : 1).ThenBy(b => b.IsRuined ? 0 : 1).ToList())
            {
                int cost = city.RepairCost(building);
                if (city.Production >= cost)
                {
                    int have = city.Production;
                    if (city.TryRepair(building, out _)) LogBuy("repair", "repair:" + building.Type, building.Config.title, cost, game.Turn, have);
                }
                else if (building.IsRuined && building.Type != BuildingType.House) return false;
            }
            return true;
        }

        private List<Option> ProductionOptions()
        {
            var list = new List<Option>();
            foreach (BuildingConfig config in city.buildings)
            {
                if (config == null || !config.buildable || !city.IsUnlocked(config)) continue;
                if (config.unique && city.Buildings.Any(b => b.Type == config.type)) continue;
                UnitClass boosted = CityManager.BoostedClass(config);
                if (boosted != UnitClass.None && (city.Boosters >= CityManager.NeutralParts || city.Guild(boosted) == null)) continue;
                if (BestCell(config) == null) continue;
                BuildingConfig c = config;
                list.Add(new Option
                {
                    Key = "build:" + c.type, Kind = IsWonder(c) ? "wonder" : boosted != UnitClass.None ? "booster" : "build",
                    Title = c.title, Cost = c.productionCost,
                    Buy = () =>
                    {
                        Vector2Int? cell = BestCell(c);
                        return cell.HasValue && city.TryBuild(c.type, cell.Value, out _);
                    },
                });
            }
            if (NeedBorders() && city.TerritoryRadius < city.maxCityRadius && !city.IsTownHallRuined)
            {
                list.Add(new Option
                {
                    Key = "borders", Kind = "borders", Title = "Расширить границы", Cost = city.BorderExpansionCost,
                    Buy = () => city.TryExpandBorders(out _),
                });
            }
            Power partner = powers.Powers.Where(p => p.CanTrade && !p.IsPlundered(game.Turn)).OrderByDescending(powers.CaravanGold).FirstOrDefault();
            string why = partner != null ? powers.WhyCannotSendCaravan(partner) : "нет";
            if (partner != null && (why == null || why.StartsWith("Нужно ")))
            {
                list.Add(new Option
                {
                    Key = "caravan", Kind = "caravan", Title = "Караван", Cost = powers.caravanProduction,
                    Buy = () => powers.TrySendCaravan(partner, out _),
                });
            }
            return list;
        }

        private List<Option> GoldOptions()
        {
            var list = new List<Option>();
            foreach (Building building in city.Buildings)
            {
                foreach (UpgradeConfig upgrade in building.Config.upgrades)
                {
                    string why = city.WhyCannotUpgrade(building, upgrade);
                    if (why != null && !(why.StartsWith("Нужно ") && why.Contains(GameVisuals.IconGold))) continue;
                    Building b = building;
                    string id = upgrade.id;
                    list.Add(new Option
                    {
                        Key = "up:" + id, Kind = b.Config.unitBranch != UnitClass.None ? "guild" : "cityup", Title = upgrade.title,
                        Cost = city.UpgradeCost(b, upgrade),
                        Buy = () => city.TryUpgrade(b, id, out _),
                    });
                }
            }
            foreach (BuildingType type in new[] { BuildingType.Monastery, BuildingType.MageTower })
            {
                BuildingConfig config = city.Config(type);
                if (config == null || !city.IsUnlocked(config) || city.Buildings.Any(b => b.Type == type) || BestCell(config) != null) continue;
                Vector2Int? cell = SiteCells(config).Where(c => city.WhyCannotBuyCell(c) == null || (city.WhyCannotBuyCell(c) ?? "").StartsWith("Нужно "))
                    .OrderBy(c => (c - city.CityCenter).sqrMagnitude).Select(c => (Vector2Int?)c).FirstOrDefault();
                if (cell == null) continue;
                Vector2Int target = cell.Value;
                list.Add(new Option
                {
                    Key = "cell:" + type, Kind = "cell", Title = "Клетка для " + config.title, Cost = city.CellPrice,
                    Buy = () => city.TryBuyCell(target, out _),
                });
            }
            return list;
        }

        /// <summary>Особое место (гора, источник, камни, лес) открыто наукой, а в границах его нет — нужно расширяться.</summary>
        private bool NeedBorders()
        {
            foreach (BuildingConfig config in city.buildings)
            {
                if (config == null || !config.buildable || config.placeOn == null || config.placeOn.Length == 0 || !city.IsUnlocked(config)) continue;
                if (config.unique && city.Buildings.Any(b => b.Type == config.type)) continue;
                UnitClass boosted = CityManager.BoostedClass(config);
                if (boosted != UnitClass.None && (city.Boosters >= CityManager.NeutralParts || city.Guild(boosted) == null)) continue;
                if (BestCell(config) != null) continue;
                // Особое место рядом с границей можно купить за золото — тогда расширяться не надо
                bool buyable = SiteCells(config).Any(c => city.TouchesTerritory(c) && city.ForeignLand?.Invoke(c) == null);
                if (!buyable || boosted != UnitClass.None) return true;
            }
            return false;
        }

        private IEnumerable<Vector2Int> SiteCells(BuildingConfig config)
        {
            for (int x = 0; x < city.World.Width; x++)
                for (int y = 0; y < city.World.Height; y++)
                {
                    var cell = new Vector2Int(x, y);
                    if (!city.InTerritory(cell) && city.FitsTerrain(config, cell) && Mathf.Max(Mathf.Abs(x - city.CityCenter.x), Mathf.Abs(y - city.CityCenter.y)) <= city.maxCityRadius + 1)
                        yield return cell;
                }
        }

        /// <summary>Клетка в границах под здание: обычные — на траву поближе к ратуше (лес и камни — усилителям).</summary>
        private Vector2Int? BestCell(BuildingConfig config)
        {
            int r = city.maxCityRadius + 2;
            Vector2Int center = city.CityCenter;
            Vector2Int? best = null;
            int bestScore = int.MaxValue;
            for (int y = center.y - r; y <= center.y + r; y++)
                for (int x = center.x - r; x <= center.x + r; x++)
                {
                    var cell = new Vector2Int(x, y);
                    string why = city.WhyCannotBuild(config, cell);
                    if (why != null && !why.StartsWith("Нужно ")) continue;
                    int score = (city.TerrainAt(cell) == TerrainType.Grass ? 0 : 1000) + (cell - center).sqrMagnitude;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = cell;
                    }
                }
            return best;
        }

        private static bool IsWonder(BuildingConfig config) =>
            config.type >= BuildingType.Stonehenge && config.type <= BuildingType.TerracottaArmy;

        // =====================================================================
        //  Учёт
        // =====================================================================

        private void MarkAvailable()
        {
            void Mark(string key)
            {
                if (!available.ContainsKey(key)) available[key] = game.Turn;
            }
            foreach (TechConfig tech in tree.techs.Where(t => t != null && !tree.IsResearched(t.id) && tree.PrerequisitesMet(t))) Mark("tech:" + tech.id);
            foreach (Option option in ProductionOptions()) Mark(option.Key);
            foreach (Option option in GoldOptions()) Mark(option.Key);
        }

        private void LogBuy(string kind, string key, string title, int cost, int picked, int balance)
        {
            available.TryGetValue(key, out int avail);
            buys.AppendLine($"{run},{game.Turn},{kind},{key},\"{title}\",{cost},{avail},{picked},{balance}");
        }

        private void RecordTurn()
        {
            int upgrades = city.Buildings.Sum(b => b.Upgrades.Count);
            PlayerArmy home = powers.HomeArmy;
            turnRows[game.Turn] = $"{run},{game.Turn},{city.Population},{city.Gold},{city.Production},{city.Food},{city.Knowledge},{bench.UnitCount}," +
                                  $"{bench.GarrisonCount},{(home != null ? home.Row.Count : 0)},{city.Buildings.Count(b => b.Type != BuildingType.House)}," +
                                  $"{tree.ResearchedCount},{upgrades},{city.TerritoryRadius},{city.KnowledgePerDay}";
        }

        private void Snapshot()
        {
            lastGold = city.Gold;
            lastProd = city.Production;
            lastFood = city.Food;
            lastKnow = city.Knowledge;
        }

        private bool resetting;

        private string CurrentLabel()
        {
            if (resetting) return "start";
            if (game.Phase == GamePhase.Battle || game.Phase == GamePhase.Loot)
                return game.Turn == phaseTurn ? (game.Phase == GamePhase.Battle ? "battle" : "loot") : "endturn";
            if (rain != null && rain.IsPlaying) return "rain";
            return label;
        }

        private void OnCityChanged()
        {
            int g = city.Gold - lastGold, p = city.Production - lastProd, f = city.Food - lastFood, k = city.Knowledge - lastKnow;
            Snapshot();
            if (g == 0 && p == 0 && f == 0 && k == 0) return;
            var key = (game.Turn, CurrentLabel());
            if (!flows.TryGetValue(key, out int[] row)) flows[key] = row = new int[8];
            void Add(int index, int delta)
            {
                if (delta > 0) row[index] += delta;
                else row[index + 1] -= delta;
            }
            Add(0, g);
            Add(2, p);
            Add(4, f);
            Add(6, k);
        }

        private void FlushRun(string result, float seconds)
        {
            RecordTurn();
            File.AppendAllText(Path.Combine(outDir, "turns.csv"), string.Join("\n", turnRows.OrderBy(p => p.Key).Select(p => p.Value)) + "\n");
            var sb = new StringBuilder();
            foreach (var pair in flows.OrderBy(p => p.Key.Item1))
                sb.AppendLine($"{run},{pair.Key.Item1},{pair.Key.Item2},{string.Join(",", pair.Value)}");
            File.AppendAllText(Path.Combine(outDir, "flows.csv"), sb.ToString());
            File.AppendAllText(Path.Combine(outDir, "buys.csv"), buys.ToString());
            buys.Clear();
            File.AppendAllText(Path.Combine(outDir, "battles.csv"), battles.ToString());
            battles.Clear();
            File.AppendAllText(Path.Combine(outDir, "wants.csv"), wants.ToString());
            wants.Clear();
            File.AppendAllText(Path.Combine(outDir, "available.csv"),
                string.Concat(available.Select(p => $"{run},{p.Key},{p.Value}\n")));
            File.AppendAllText(Path.Combine(outDir, "runs.csv"), $"{run},{game.Turn},{result},{seconds:F0},{variant.tempo},{variant.slow}\n");
        }

        private void WriteCatalog()
        {
            var sb = new StringBuilder("kind,key,title,cost,res,tech,owner\n");
            foreach (TechConfig tech in tree.techs.Where(t => t != null))
                sb.AppendLine($"tech,tech:{tech.id},\"{tech.title}\",{tech.knowledgeCost},know,\"{string.Join("+", tech.requires ?? new string[0])}\",{tech.branch}");
            foreach (BuildingConfig config in city.buildings.Where(c => c != null))
            {
                if (config.buildable)
                    sb.AppendLine($"{(IsWonder(config) ? "wonder" : "build")},build:{config.type},\"{config.title}\",{config.productionCost},prod,\"{string.Join("|", config.requiresAnyTech ?? new string[0])}\",");
                foreach (UpgradeConfig upgrade in config.upgrades)
                    sb.AppendLine($"{(config.unitBranch != UnitClass.None ? "guild" : "cityup")},up:{upgrade.id},\"{upgrade.title}\",{upgrade.goldCost},gold,{upgrade.requiredTech},{config.type}");
            }
            File.WriteAllText(Path.Combine(outDir, "catalog.csv"), sb.ToString());
        }
    }
}
#endif
