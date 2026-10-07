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
    }

    /// <summary>
    /// Кто выходит из линии юнитов (чистая логика — её проверяют тесты):
    /// • линия 5+ любых юнитов, когда изучены «Былины» и богатыря ещё нет → богатырь (5 — ★, 6 — ★★, 7 — ★★★);
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
    /// ФАЗА 1 — поле города («начальный слот»): 3-в-ряд с ограниченным запасом символов на день.
    /// Здесь только складываем юнитов и ресурсы — стройка на карте города, наука — в дереве науки.
    /// Движок поля — в MatchBoard; здесь то, что относится к городу:
    /// • какие символы падают: ресурсы (золото, производство ⚒, еда) и юниты. Нейтралы — всегда;
    ///   воины, лучники и жрецы — только если в городе стоит их здание (казармы, стрельбище, святилище);
    /// • доля ресурсов постоянна, а делится она по фокусу города (например 2:1:1). Фокус выбирают
    ///   кнопками «Завтра» — он начинает действовать со следующего дня; юниты падают как обычно;
    /// • что даёт линия: юнит → на скамейку (линия 4 — сразу 2-го уровня, 5+ — 3-го; три одинаковых
    ///   там сами сливаются в одного ★ выше), ресурсы → в город;
    /// • гибриды: изучен «Порох» — воин + лучник в одной линии дают подрывника, «Боевые искусства» —
    ///   воин + жрец дают монаха, «Чародейство» — лучник + жрец дают мага;
    /// • «Былины»: линия 5+ любых юнитов даёт богатыря (6 — ★★, 7 — ★★★). Богатырь всегда один:
    ///   чтобы призвать нового, старого надо продать.
    /// Размер поля и запас символов растут с населением (CityManager).
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
        [Tooltip("Юниты, которые могут выпасть (классовые — только если в городе есть их здание)")]
        public List<UnitData> unitPool = new List<UnitData>();
        [Tooltip("Гибриды и богатырь (на поле не падают — получаются из смешанных линий)")]
        public List<UnitData> specialUnits = new List<UnitData>();
        [Tooltip("Доля ресурсов (золото, ⚒, еда) среди новых символов; между собой они делятся по фокусу города")]
        [Range(0f, 1f)] public float resourceChance = 0.48f;
        [Tooltip("Вес нейтралов среди юнитов")]
        [Min(0f)] public float neutralWeight = 1f;
        [Tooltip("Вес каждого открытого класса среди юнитов")]
        [Min(0f)] public float classWeight = 0.8f;

        [Header("Правила")]
        [Tooltip("Линия длиннее трёх даёт юнита выше уровнем: +1 за каждый лишний символ, но не выше этого")]
        [Range(1, UnitData.MaxLevel)] public int maxRecruitLevel = 3;

        private readonly Dictionary<UnitClass, UnitData> fallbackUnits = new Dictionary<UnitClass, UnitData>();

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
            ShowMessage($"Завтра на поле: {FocusText(tomorrow: true)} (сегодня {FocusText(tomorrow: false)})");
        }

        /// <summary>Фокус символами: «🪙🪙 : ⚒ : 🍎».</summary>
        public string FocusText(bool tomorrow)
        {
            if (city == null) return string.Empty;
            return string.Join(" : ", new[] { ResourceType.Gold, ResourceType.Production, ResourceType.Food }
                .Select(r => GameVisuals.FocusIcons(r, tomorrow ? city.FocusTomorrow(r) : city.FocusToday(r))));
        }

        /// <summary>Шанс, что новый символ — этот ресурс (сегодняшний фокус).</summary>
        public float ResourceChance(ResourceType resource)
        {
            return resourceChance * (city != null ? city.FocusShare(resource) : 1f / 3f);
        }

        private void UpdateFocusUi()
        {
            if (focusLabels == null || city == null) return;
            for (int i = 0; i < focusLabels.Length; i++)
            {
                if (focusLabels[i] != null) focusLabels[i].text = GameVisuals.FocusIcons((ResourceType)i, city.FocusTomorrow((ResourceType)i));
            }
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

        protected override TileData CreateRandomTile()
        {
            float roll = Random.value;
            foreach (ResourceType resource in new[] { ResourceType.Gold, ResourceType.Production, ResourceType.Food })
            {
                roll -= ResourceChance(resource);
                if (roll < 0f) return TileData.ForResource(resource);
            }
            UnitData unit = PickUnit();
            return unit != null ? TileData.ForUnit(unit) : TileData.ForResource(ResourceType.Gold);
        }

        private UnitData PickUnit()
        {
            float total = 0f;
            foreach (UnitData unit in unitPool)
            {
                if (unit != null) total += UnitWeight(unit);
            }
            if (total <= 0f) return null;

            float roll = Random.value * total;
            UnitData last = null;
            foreach (UnitData unit in unitPool)
            {
                if (unit == null || UnitWeight(unit) <= 0f) continue;
                last = unit;
                roll -= UnitWeight(unit);
                if (roll <= 0f) return unit;
            }
            return last;
        }

        private float UnitWeight(UnitData unit)
        {
            if (unit.IsNeutral) return neutralWeight * unit.spawnWeight;
            if (!UnitClasses.IsBase(unit.unitClass)) return 0f; // гибриды на поле не падают
            if (city == null || !city.IsClassUnlocked(unit.unitClass)) return 0f; // нет здания — нет класса
            return classWeight * unit.spawnWeight;
        }

        protected override void CollectRun(MatchRun run)
        {
            if (run.Kind == TileKind.Resource)
            {
                CollectResource(run.Resource, run.Length);
                return;
            }
            if (!run.IsUnitRun) return;

            var tiles = run.Cells.Select(cell => grid[cell.x, cell.y]).ToList();
            RecruitPlan plan = Recruitment.Plan(run, tiles, city != null && city.HeroUnlocked, bench != null && bench.HasHero,
                SpecialUnit, maxRecruitLevel);
            UnitData recruit = plan.Unit;
            if (recruit == null) return;

            BenchUnit added = bench != null ? bench.AddRecruit(recruit, plan.Level, plan.Stars) : null;
            if (added != null)
            {
                // Слияние (три одинаковых → ★ выше) скамейка объявляет сама
                if (plan.IsHero) ShowMessage($"Богатырь {GameVisuals.Stars(plan.Stars)} пришёл в армию! (линия из {run.Length})");
                else if (UnitClasses.IsHybrid(recruit.unitClass))
                    ShowMessage($"+ {recruit.unitName}: {string.Join(" + ", UnitClasses.Parents(recruit.unitClass).Select(GameVisuals.ClassName))}" +
                                (plan.Level > 1 ? $", ур. {plan.Level}" : string.Empty));
                else if (added.stars == 1) ShowMessage($"+ {recruit.unitName}" + (plan.Level > 1 ? $" ур. {plan.Level}" : string.Empty));
            }
            else if (game != null)
            {
                int gold = recruit.GetSellPrice(plan.Stars, plan.Level);
                game.AddGold(gold);
                ShowMessage($"Скамейка полна: {recruit.unitName} продан за {gold} {GameVisuals.IconGold}");
            }
        }

        private void CollectResource(ResourceType resource, int symbols)
        {
            switch (resource)
            {
                case ResourceType.Gold:
                    int gold = symbols * (city != null ? city.GoldPerCoin : 1);
                    if (game != null) game.AddGold(gold);
                    ShowMessage($"+{gold} {GameVisuals.IconGold}");
                    break;
                case ResourceType.Production:
                    int production = symbols * (city != null ? city.ProductionPerSymbol : 1);
                    if (city != null) city.AddProduction(production);
                    ShowMessage($"+{production} {GameVisuals.IconProduction}");
                    break;
                case ResourceType.Food:
                    int food = symbols * (city != null ? city.FoodPerSymbol : 1);
                    if (city != null) city.AddFood(food);
                    ShowMessage($"+{food} {GameVisuals.IconFood}");
                    break;
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
