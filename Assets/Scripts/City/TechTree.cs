using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Casiwar
{
    /// <summary>Колонка дерева науки.</summary>
    public enum TechBranch
    {
        Economy,   // Хозяйство
        Knowledge, // Знания
        Infantry,  // Пехота (воины)
        Ranged,    // Стрелки (лучники)
        Faith,     // Вера
    }

    /// <summary>Идентификаторы технологий (на них ссылаются здания и улучшения в CityManager).</summary>
    public static class TechIds
    {
        public const string Agriculture = "agriculture";
        public const string Pottery = "pottery";
        public const string Trade = "trade";
        public const string Crafts = "crafts";
        public const string Masonry = "masonry";
        public const string Writing = "writing";
        public const string Mathematics = "mathematics";
        public const string Philosophy = "philosophy";
        public const string Warfare = "warfare";
        public const string Shields = "shields";
        public const string TowerShields = "tower_shields";
        public const string Bronze = "bronze";
        public const string Archery = "archery";
        public const string CompositeBow = "composite_bow";
        public const string Crossbows = "crossbows";
        public const string Mysticism = "mysticism";
        public const string Healing = "healing";
        public const string Staffs = "staffs";
        public const string Monasticism = "monasticism";
        public const string Gunpowder = "gunpowder";      // подрывники: воин + лучник
        public const string MartialArts = "martial_arts"; // монахи: воин + жрец
        public const string Sorcery = "sorcery";          // маги: лучник + жрец
        public const string Epics = "epics";              // богатырь: 5+ любых юнитов
    }

    [Serializable]
    public class TechConfig
    {
        public string id = "tech";
        public string title = "Технология";
        [TextArea] public string description = "";
        public TechBranch branch;
        [Tooltip("Цена в очках знаний 📖")]
        [Min(0)] public int knowledgeCost = 5;
        [Tooltip("Какие технологии нужны раньше (id)")]
        public string[] requires = new string[0];

        public TechConfig() { }

        public TechConfig(string id, string title, TechBranch branch, int knowledgeCost, string description, params string[] requires)
        {
            this.id = id;
            this.title = title;
            this.branch = branch;
            this.knowledgeCost = knowledgeCost;
            this.description = description;
            this.requires = requires;
        }
    }

    /// <summary>
    /// Единое дерево науки (как в «Цивилизации»): Хозяйство, Знания, Пехота, Стрелки, Вера. Технологии изучаются за очки знаний 📖 —
    /// они приходят каждое утро от жителей и ратуши, а школа с библиотекой их умножают (CityManager).
    /// Технология открывает здание или улучшение: здание строится за производство ⚒,
    /// улучшение покупается за золото 🪙 — всё это на карте города.
    /// Порядок в списке = порядок карточек в колонке: технология, которой нужна предыдущая
    /// карточка той же колонки, рисуется под ней со стрелкой (цепочка).
    /// </summary>
    public class TechTree : MonoBehaviour
    {
        public List<TechConfig> techs = DefaultTechs();

        private readonly HashSet<string> researched = new HashSet<string>();

        /// <summary>Что-то изучено.</summary>
        public event Action Changed;

        public static List<TechConfig> DefaultTechs()
        {
            const string food = GameVisuals.IconFood;
            const string production = GameVisuals.IconProduction;
            const string gold = GameVisuals.IconGold;
            const string knowledge = GameVisuals.IconKnowledge;
            return new List<TechConfig>
            {
                // Хозяйство: еда и рост → золото; производство → защита
                new TechConfig(TechIds.Agriculture, "Земледелие", TechBranch.Economy, 5,
                    $"Амбар: +1 {food} с каждого символа еды"),
                new TechConfig(TechIds.Pottery, "Гончарное дело", TechBranch.Economy, 9,
                    "Кладовые в амбаре: рост города на 25% дешевле", TechIds.Agriculture),
                new TechConfig(TechIds.Trade, "Торговля", TechBranch.Economy, 12,
                    $"Торговые ряды на базаре: +1 {gold} с каждой монеты", TechIds.Pottery),
                new TechConfig(TechIds.Crafts, "Ремёсла", TechBranch.Economy, 5,
                    $"Мастерская: +1 {production} с каждого символа производства"),
                new TechConfig(TechIds.Masonry, "Каменная кладка", TechBranch.Economy, 8,
                    "Частокол у ратуши: набеги наносят на 40% меньше урона", TechIds.Crafts),
                // Знания: ускоряют саму науку
                new TechConfig(TechIds.Writing, "Письменность", TechBranch.Knowledge, 5,
                    $"Школа: +50% очков знаний {knowledge}"),
                new TechConfig(TechIds.Mathematics, "Математика", TechBranch.Knowledge, 9,
                    "Учёт в ратуше: +6 символов на поле каждый день", TechIds.Writing),
                new TechConfig(TechIds.Philosophy, "Философия", TechBranch.Knowledge, 15,
                    $"Библиотека в школе: ещё +50% {knowledge}", TechIds.Mathematics, TechIds.Mysticism),
                new TechConfig(TechIds.Sorcery, "Чародейство", TechBranch.Knowledge, 16,
                    "Лучник и жрец в одной линии на поле → маг: боевые заклинания издалека (цепная молния)",
                    TechIds.Philosophy, TechIds.Staffs, TechIds.Archery),
                new TechConfig(TechIds.Epics, "Былины", TechBranch.Knowledge, 26,
                    "Линия из 5+ любых юнитов → богатырь (6 — ★★, 7 — ★★★). Богатырь всегда один",
                    TechIds.Sorcery, TechIds.Gunpowder, TechIds.MartialArts),
                // Пехота: воины начинают без щитов — щиты изучаются и покупаются в ветке ближнего боя («Войска»)
                new TechConfig(TechIds.Warfare, "Военное дело", TechBranch.Infantry, 5,
                    "Казармы: на поле появляются воины (пока без щитов)"),
                new TechConfig(TechIds.Shields, "Щиты", TechBranch.Infantry, 7,
                    "Ключ ближнего боя: щиты — удар в лицо иногда ловится целиком или частично", TechIds.Warfare),
                new TechConfig(TechIds.TowerShields, "Ростовые щиты", TechBranch.Infantry, 12,
                    "Ключ II яруса ближнего боя: блок чаще, щит прикрывает и бок", TechIds.Shields),
                new TechConfig(TechIds.Bronze, "Бронза", TechBranch.Infantry, 9,
                    $"Бронзовые мечи для воинов и инструменты в мастерской: +1 {production}", TechIds.Crafts),
                // Стрелки: качаются отдельно от пехоты, луки со временем становятся арбалетами
                new TechConfig(TechIds.Archery, "Стрельба из лука", TechBranch.Ranged, 5,
                    "Стрельбище: на поле появляются лучники"),
                new TechConfig(TechIds.CompositeBow, "Составной лук", TechBranch.Ranged, 9,
                    "Составные луки в ветке дальнего боя: лучники +25%", TechIds.Archery),
                new TechConfig(TechIds.Crossbows, "Арбалеты", TechBranch.Ranged, 14,
                    "Ключ дальнего боя: луки → арбалеты, болты пробивают щиты", TechIds.CompositeBow),
                new TechConfig(TechIds.Gunpowder, "Порох", TechBranch.Ranged, 16,
                    "Воин и лучник в одной линии на поле → подрывник: динамит бьёт по площади",
                    TechIds.Crossbows, TechIds.Warfare),
                // Вера: жрецы лечат и благословляют; посохи открывают II ярус магии
                new TechConfig(TechIds.Mysticism, "Мистицизм", TechBranch.Faith, 5,
                    "Святилище: жрецы на поле и 1 перемешивание поля в день"),
                new TechConfig(TechIds.Healing, "Целительство", TechBranch.Faith, 8,
                    "Лечебные травы в ветке магии: жрецы лечат на 50% сильнее", TechIds.Mysticism),
                new TechConfig(TechIds.Staffs, "Посохи", TechBranch.Faith, 10,
                    "Ключ магии: жрецы с посохами сильнее, открывается II ярус", TechIds.Healing),
                new TechConfig(TechIds.Monasticism, "Монашество", TechBranch.Faith, 15,
                    "Обители в ветке магии: жрецы +25% ХП и атаки", TechIds.Staffs, TechIds.Philosophy),
                new TechConfig(TechIds.MartialArts, "Боевые искусства", TechBranch.Faith, 16,
                    "Воин и жрец в одной линии на поле → монах: бьётся врукопашную и зажигает оружие своих",
                    TechIds.Monasticism, TechIds.Warfare),
            };
        }

        private void Awake() => EnsureDefaults();

        /// <summary>
        /// Добавить технологии, которых нет в сохранённом в сцене списке (сцена собрана старой версией),
        /// на свои места — сразу после соседа по колонке из списка по умолчанию.
        /// </summary>
        public void EnsureDefaults()
        {
            List<TechConfig> defaults = DefaultTechs();
            for (int i = 0; i < defaults.Count; i++)
            {
                TechConfig tech = defaults[i];
                if (techs.Any(t => t != null && t.id == tech.id)) continue;
                int index = techs.Count;
                for (int j = i - 1; j >= 0; j--)
                {
                    int found = techs.FindIndex(t => t != null && t.id == defaults[j].id);
                    if (found >= 0)
                    {
                        index = found + 1;
                        break;
                    }
                }
                techs.Insert(index, tech);
            }
        }

        public TechConfig Find(string id) => techs.FirstOrDefault(t => t != null && t.id == id);

        public string Title(string id) => Find(id)?.title ?? id;

        /// <summary>Пустой id — «ничего не нужно».</summary>
        public bool IsResearched(string id) => string.IsNullOrEmpty(id) || researched.Contains(id);

        public int ResearchedCount => researched.Count;

        public bool PrerequisitesMet(TechConfig tech)
        {
            return tech.requires == null || tech.requires.All(IsResearched);
        }

        /// <summary>Чего не хватает для изучения (null — можно изучать, очки знаний проверяет GameManager).</summary>
        public string MissingPrerequisites(TechConfig tech)
        {
            if (tech.requires == null) return null;
            string[] missing = tech.requires.Where(id => !IsResearched(id)).Select(Title).ToArray();
            return missing.Length == 0 ? null : string.Join(", ", missing);
        }

        public void MarkResearched(string id)
        {
            if (string.IsNullOrEmpty(id) || !researched.Add(id)) return;
            Changed?.Invoke();
        }

        public void ResetTree()
        {
            researched.Clear();
            Changed?.Invoke();
        }
    }
}
