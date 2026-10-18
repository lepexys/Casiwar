using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace Casiwar
{
    /// <summary>Что получается из линии юнитов: кто, какого уровня и сколько звёзд.</summary>
    public struct RecruitPlan
    {
        public UnitData Unit;
        public int Level;
        public int Stars;
        public bool IsHero;
        /// <summary>Черты богатыря — из символов его линии.</summary>
        public HeroTraits Traits;
    }

    /// <summary>
    /// Кто выходит из линии юнитов (чистая логика — её проверяют тесты):
    /// • линия 5+ любых юнитов, когда изучены «Былины» и богатырей меньше предела → богатырь (5 — ★, 6 — ★★, 7 — ★★★);
    /// • линия с двумя классами → их гибрид (подрывник, монах, маг);
    /// • смешанная линия «для богатыря», когда он уже есть → юнит самого частого класса в линии;
    /// • иначе — самый частый юнит класса линии (одни нейтралы → нейтрал).
    /// Уровень: линия 3 → 1-й, 4 → 2-й, 5+ → 3-й (не выше maxLevel).
    /// </summary>
    public static class Recruitment
    {
        public const int HeroLength = 5;

        public static RecruitPlan Plan(MatchRun run, IList<TileData> tiles, bool heroUnlocked, bool heroExists,
            Func<UnitClass, UnitData> specialUnit, int maxLevel)
        {
            int level = Mathf.Clamp(1 + run.Length - MatchRules.MinMatch, 1, Mathf.Max(1, maxLevel));
            if (heroUnlocked && !heroExists && run.Length >= HeroLength)
            {
                return new RecruitPlan
                {
                    Unit = specialUnit(UnitClass.Hero),
                    Level = 1,
                    Stars = Mathf.Clamp(run.Length - HeroLength + 1, 1, UnitData.MaxStars),
                    IsHero = true,
                    Traits = HeroTraits.From(tiles),
                };
            }

            UnitClass result = run.UnitClass == UnitClass.Hero ? DominantClass(tiles) : run.UnitClass;
            UnitData unit = UnitClasses.IsHybrid(result) ? specialUnit(result) : MostFrequentUnit(tiles, result);
            return new RecruitPlan { Unit = unit, Level = level, Stars = 1 };
        }

        /// <summary>Самый частый базовый класс в линии (при равенстве — первый по линии); одни нейтралы → None.</summary>
        public static UnitClass DominantClass(IList<TileData> tiles)
        {
            var counts = new Dictionary<UnitClass, int>();
            UnitClass best = UnitClass.None;
            int bestCount = 0;
            foreach (TileData tile in tiles)
            {
                if (tile == null || !tile.IsUnit || tile.IsNeutral) continue;
                counts.TryGetValue(tile.Class, out int count);
                counts[tile.Class] = ++count;
                if (count > bestCount)
                {
                    best = tile.Class;
                    bestCount = count;
                }
            }
            return best;
        }

        /// <summary>Самый частый тип юнита класса unitClass (нейтралы-джокеры тип не определяют; None — нейтрал).</summary>
        public static UnitData MostFrequentUnit(IList<TileData> tiles, UnitClass unitClass)
        {
            var counts = new Dictionary<UnitData, int>();
            UnitData best = null;
            int bestCount = 0;
            foreach (TileData tile in tiles)
            {
                if (tile == null || !tile.IsUnit || tile.Unit == null) continue;
                if (tile.Class != unitClass) continue;
                counts.TryGetValue(tile.Unit, out int count);
                counts[tile.Unit] = ++count;
                if (count > bestCount)
                {
                    best = tile.Unit;
                    bestCount = count;
                }
            }
            return best;
        }
    }

    /// <summary>
    /// УСТАРЕЛО как поле: армию теперь вербует слот (RecruitSlot), а этот компонент — его запасник: рамка поля,
    /// префаб клетки, пул юнитов (RandomUnit, SpecialUnit), подписи под полем и кнопки фокуса сбора ресурсов.
    /// Прежнее описание поля (3-в-ряд) — ниже; само поле больше не запускается.
    ///
    /// Поле армии: 3-в-ряд с ограниченным запасом символов и действий на ход. Ресурсы сюда больше не падают —
    /// они приходят сами в начале хода (ResourceRain); здесь только люди:
    /// • горожане (белые) — не складываются ни с кем, только мешают: их можно двигать, чтобы добраться до бойцов;
    /// • ополченцы (Н) — джокеры: подходят к любой линии юнитов, а линия из одних ополченцев даёт ополченца;
    /// • бойцы гильдий: каждая построенная гильдия (казармы, стрельбище, святилище) превращает треть горожан в своих
    ///   бойцов — когда стоят все три, горожан на поле нет. Построили гильдию посреди хода — фишки сразу перекатываются;
    /// • усилители (плавильня, лесопилка, храм) так же забирают по трети ополченцев — стоят все три, ополченцев нет;
    /// • что даёт линия: юнит → в гарнизон (линия 4 — сразу 2-го уровня, 5+ — 3-го; три одинаковых
    ///   там сами сливаются в одного ★ выше);
    /// • гибриды: изучен «Порох» — воин + лучник в одной линии дают подрывника, «Боевые искусства» —
    ///   воин + жрец дают монаха (нужен ещё монастырь на горе), «Чародейство» — лучник + жрец дают мага
    ///   (нужна ещё башня магов на волшебном источнике);
    /// • «Былины»: линия 5+ любых юнитов даёт богатыря (6 — ★★, 7 — ★★★) — вожака отряда. Богатырей не больше
    ///   BenchManager.maxHeroes: чтобы призвать ещё, кого-то надо продать.
    /// Одним ходом сложить нечего — поле перемешивается само. Размер поля и запас символов растут с населением (CityManager).
    /// Кнопки фокуса («Следующий ход: 🪙 ⚒ 🍎») на этой вкладке решают, чего больше упадёт в сбор ресурсов.
    /// </summary>
    public class CityGridManager : MatchBoard
    {
        [Header("Системы")]
        public BenchManager bench;
        public GameManager game;
        public CityManager city;

        [Header("Устарело: приоритеты убраны (полоса из старых сцен прячется)")]
        [HideInInspector] public TMP_Text priorityText;
        [HideInInspector] public Image[] priorityButtonImages = new Image[0];

        [Header("Фокус на завтра (UI, необязательно)")]
        [Tooltip("Подписи кнопок фокуса по индексу ResourceType: 0 — золото, 1 — производство, 2 — еда")]
        public TMP_Text[] focusLabels = new TMP_Text[3];

        [Header("Символы поля")]
        [Tooltip("Юниты, которые могут выпасть: ополченцы (нейтралы) и бойцы гильдий (только если гильдия построена)")]
        public List<UnitData> unitPool = new List<UnitData>();
        [Tooltip("Гибриды и богатырь (на поле не падают — получаются из смешанных линий)")]
        public List<UnitData> specialUnits = new List<UnitData>();
        [Tooltip("Доля горожан среди символов (остальное — ополченцы). Каждая гильдия забирает треть горожан своими бойцами")]
        [Range(0f, 0.9f)] public float civilianShare = 0.5f;

        [Header("Правила")]
        [Tooltip("Линия длиннее трёх даёт юнита выше уровнем: +1 за каждый лишний символ, но не выше этого")]
        [Range(1, UnitData.MaxLevel)] public int maxRecruitLevel = 3;

        private readonly Dictionary<UnitClass, UnitData> fallbackUnits = new Dictionary<UnitClass, UnitData>();
        /// <summary>Какие базовые классы уже падают на поле (null — ещё не знаем: первое изменение города).</summary>
        private HashSet<UnitClass> knownClasses;

        private void Awake()
        {
            if (bench == null) bench = FindFirstObjectByType<BenchManager>();
            if (game == null) game = FindFirstObjectByType<GameManager>();
            if (city == null) city = FindFirstObjectByType<CityManager>();
            if (city != null) city.Changed += OnCityChanged;
            HideLegacyPriorityBar();
            RefreshRules();
        }

        private void OnDestroy()
        {
            if (city != null) city.Changed -= OnCityChanged;
        }

        private void OnCityChanged()
        {
            UpdateFocusUi();
            RefreshRules();
            RerollIfNewClass();
        }

        /// <summary>
        /// Построили (или починили) казармы, стрельбище или святилище — на поле ещё нет юнитов этого класса,
        /// поэтому фишки поля перекатываются сразу (пустые клетки, запас и ходы не трогаются).
        /// </summary>
        private void RerollIfNewClass()
        {
            var unlocked = new HashSet<UnitClass>(UnitClasses.Base.Where(c => city.IsClassUnlocked(c)));
            List<UnitClass> added = knownClasses == null ? new List<UnitClass>() : unlocked.Where(c => !knownClasses.Contains(c)).ToList();
            knownClasses = unlocked;
            if (added.Count == 0 || !IsPhaseActive) return;
            RerollTiles();
            ShowMessage($"Поле обновлено: на нём появились {string.Join(", ", added.Select(ClassNamePlural))}");
        }

        private static string ClassNamePlural(UnitClass unitClass)
        {
            switch (unitClass)
            {
                case UnitClass.Warrior: return "воины";
                case UnitClass.Archer: return "лучники";
                case UnitClass.Priest: return "жрецы";
                default: return GameVisuals.ClassName(unitClass).ToLowerInvariant();
            }
        }

        /// <summary>Правила смешанных линий — по изученной науке (гибриды, «Былины»).</summary>
        public void RefreshRules()
        {
            var rules = new LineRules();
            if (city != null)
            {
                foreach (UnitClass hybrid in UnitClasses.Hybrids)
                {
                    if (city.IsHybridUnlocked(hybrid)) rules.Hybrids.Add(hybrid);
                }
                if (city.HeroUnlocked) rules.AnyMixLength = Recruitment.HeroLength;
            }
            LineRules old = MatchRules.Rules;
            bool changed = old == null || old.AnyMixLength != rules.AnyMixLength || !old.Hybrids.SetEquals(rules.Hybrids);
            MatchRules.Rules = rules;
            if (changed) CheckNewMatches(); // изучили гибрид посреди дня — готовые линии на поле складываются
        }

        /// <summary>Юнит гибрида или богатыря: из сцены, а если его там нет — с настройками по умолчанию.</summary>
        public UnitData SpecialUnit(UnitClass unitClass)
        {
            UnitData unit = specialUnits.FirstOrDefault(u => u != null && u.unitClass == unitClass);
            if (unit != null) return unit;
            if (!fallbackUnits.TryGetValue(unitClass, out unit))
            {
                unit = UnitData.CreateDefault(unitClass);
                fallbackUnits[unitClass] = unit;
            }
            return unit;
        }

        /// <summary>Кнопки «Завтра»: 0 — золото, 1 — производство, 2 — еда. Вес ресурса 1 → 2 → 3 → 1.</summary>
        public void CycleFocus(int resourceIndex)
        {
            if (city == null) return;
            city.CycleFocus((ResourceType)Mathf.Clamp(resourceIndex, 0, 2));
            ShowMessage($"Сбор ресурсов со следующего хода: {FocusText(tomorrow: true)} (сейчас {FocusText(tomorrow: false)})");
        }

        /// <summary>Фокус символами: «🪙🪙 : ⚒ : 🍎».</summary>
        public string FocusText(bool tomorrow)
        {
            if (city == null) return string.Empty;
            return string.Join(" : ", new[] { ResourceType.Gold, ResourceType.Production, ResourceType.Food }
                .Select(r => GameVisuals.FocusIcons(r, tomorrow ? city.FocusTomorrow(r) : city.FocusToday(r))));
        }

        private void UpdateFocusUi()
        {
            if (focusLabels == null || city == null) return;
            for (int i = 0; i < focusLabels.Length; i++)
            {
                if (focusLabels[i] != null) focusLabels[i].text = GameVisuals.FocusIcons((ResourceType)i, city.FocusTomorrow((ResourceType)i));
            }
        }

        /// <summary>Кнопка «Авто» под полем (сцены, где она ещё ведёт сюда): авто-спины вербовки.</summary>
        public override void ToggleAutoPlay()
        {
            RecruitSlot recruit = game != null ? game.recruit : FindFirstObjectByType<RecruitSlot>();
            if (recruit != null) recruit.ToggleAuto();
        }

        /// <summary>Устарело: приоритетов больше нет (кнопки из старых сцен ничего не делают).</summary>
        public void SetPriority(int classIndex) { }

        /// <summary>Полоса приоритетов из сцен старой версии — прячем.</summary>
        private void HideLegacyPriorityBar()
        {
            Image anyButton = priorityButtonImages?.FirstOrDefault(i => i != null);
            Transform bar = anyButton != null ? anyButton.transform.parent : priorityText != null ? priorityText.transform.parent : null;
            if (bar != null && bar != transform && bar.GetComponentInParent<Canvas>() != null) bar.gameObject.SetActive(false);
        }

        /// <summary>
        /// Новый символ: горожанин (его треть забирает каждая построенная гильдия — тогда это её боец)
        /// или ополченец (его треть забирает каждый усилитель — тогда это боец класса усилителя).
        /// </summary>
        protected override TileData CreateRandomTile()
        {
            if (Random.value < civilianShare)
            {
                var guilds = UnitClasses.Base.Where(c => city != null && city.IsClassUnlocked(c)).ToList();
                int third = Random.Range(0, 3);
                UnitData recruit = third < guilds.Count ? PickUnit(guilds[third]) : null;
                return recruit != null ? TileData.ForUnit(recruit) : TileData.ForCivilian();
            }
            UnitData militia = PickNeutral();
            if (militia == null) return TileData.ForCivilian();
            return TileData.ForUnit(ReplaceNeutral(militia));
        }

        /// <summary>Усилители (плавильня, лесопилка, храм): ополченец заменяется их классом — каждый усилитель забирает треть.</summary>
        private UnitData ReplaceNeutral(UnitData neutral)
        {
            if (city == null) return neutral;
            float roll = Random.value;
            foreach (UnitClass unitClass in UnitClasses.Base)
            {
                roll -= city.NeutralReplaceChance(unitClass);
                if (roll < 0f) return PickUnit(unitClass) ?? neutral;
            }
            return neutral;
        }

        private UnitData PickNeutral() => PickWeighted(unitPool.Where(u => u != null && u.IsNeutral));

        /// <summary>
        /// Юнит класса из пула (None — ополченец) для вербовки: случайный по весу появления, а deterministic — всегда
        /// один и тот же (для рисунка символа на барабане). null — таких в пуле нет.
        /// </summary>
        public UnitData RandomUnit(UnitClass unitClass, bool deterministic = false)
        {
            IEnumerable<UnitData> candidates = unitClass == UnitClass.None
                ? unitPool.Where(u => u != null && u.IsNeutral)
                : unitPool.Where(u => u != null && u.unitClass == unitClass);
            return deterministic ? candidates.FirstOrDefault() : PickWeighted(candidates);
        }

        /// <summary>Случайный юнит класса из пула (по весу появления); null — таких нет.</summary>
        private UnitData PickUnit(UnitClass unitClass) =>
            PickWeighted(unitPool.Where(u => u != null && u.unitClass == unitClass && UnitClasses.IsBase(unitClass)));

        private static UnitData PickWeighted(IEnumerable<UnitData> candidates)
        {
            List<UnitData> list = candidates.ToList();
            float total = list.Sum(u => Mathf.Max(0f, u.spawnWeight));
            if (list.Count == 0) return null;
            if (total <= 0f) return list[Random.Range(0, list.Count)];
            float roll = Random.value * total;
            foreach (UnitData unit in list)
            {
                roll -= Mathf.Max(0f, unit.spawnWeight);
                if (roll <= 0f) return unit;
            }
            return list[list.Count - 1];
        }

        protected override void CollectRun(MatchRun run)
        {
            if (!run.IsUnitRun) return;

            var tiles = run.Cells.Select(cell => grid[cell.x, cell.y]).ToList();
            RecruitPlan plan = Recruitment.Plan(run, tiles, city != null && city.HeroUnlocked, bench != null && bench.HasHero,
                SpecialUnit, maxRecruitLevel);
            UnitData recruit = plan.Unit;
            if (recruit == null) return;

            BenchUnit added = bench != null ? bench.AddRecruit(recruit, plan.Level, plan.Stars, plan.Traits) : null;
            if (added != null)
            {
                // Слияние (три одинаковых → ★ выше) скамейка объявляет сама
                if (plan.IsHero) ShowMessage($"{added.Name} {GameVisuals.Stars(plan.Stars)} пришёл в армию! {plan.Traits.Summary}");
                else if (UnitClasses.IsHybrid(recruit.unitClass))
                    ShowMessage($"+ {recruit.unitName}: {string.Join(" + ", UnitClasses.Parents(recruit.unitClass).Select(GameVisuals.ClassName))}" +
                                (plan.Level > 1 ? $", ур. {plan.Level}" : string.Empty));
                else if (added.stars == 1) ShowMessage($"+ {recruit.unitName}" + (plan.Level > 1 ? $" ур. {plan.Level}" : string.Empty));
            }
            else if (game != null)
            {
                int gold = recruit.GetSellPrice(plan.Stars, plan.Level);
                game.AddGold(gold);
                ShowMessage(plan.IsHero
                    ? $"Богатырей уже предел (или гарнизон полон): {recruit.unitName} ушёл, +{gold} {GameVisuals.IconGold}"
                    : $"Гарнизон полон: {recruit.unitName} продан за {gold} {GameVisuals.IconGold}");
            }
        }

        protected override void ShowMessage(string text)
        {
            if (game != null) game.ShowMessage(text);
        }

        protected override void OnUiChanged()
        {
            UpdateFocusUi();
        }
    }
}
