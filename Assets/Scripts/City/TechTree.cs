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
    /// Вид — дерево (TechTreeView): столбец — ветка, строка — глубина, линии — что из чего растёт.
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
                    $"Амбар: +1 {food} к каждому собранному кластеру еды"),
                new TechConfig(TechIds.Pottery, "Гончарное дело", TechBranch.Economy, 9,
                    "Кладовые в амбаре: рост города на 25% дешевле", TechIds.Agriculture),
                new TechConfig(TechIds.Trade, "Торговля", TechBranch.Economy, 12,
                    $"Торговые ряды на базаре: +1 {gold} к каждому кластеру монет. Чудо: Колосс", TechIds.Pottery),
                new TechConfig(TechIds.Crafts, "Ремёсла", TechBranch.Economy, 5,
                    $"Мастерская: +1 {production} к каждому кластеру производства. Чудо: Пирамиды"),
                new TechConfig(TechIds.Masonry, "Каменная кладка", TechBranch.Economy, 8,
                    "Частокол у ратуши: набеги наносят на 40% меньше урона. Чудо: Великая стена", TechIds.Crafts),
                // Знания: ускоряют саму науку
                new TechConfig(TechIds.Writing, "Письменность", TechBranch.Knowledge, 5,
                    $"Школа: +50% очков знаний {knowledge}"),
                new TechConfig(TechIds.Mathematics, "Математика", TechBranch.Knowledge, 9,
                    "Учёт в ратуше: +1 спин вербовки на «Поле» каждый ход", TechIds.Writing),
                new TechConfig(TechIds.Philosophy, "Философия", TechBranch.Knowledge, 15,
                    $"Библиотека в школе: ещё +50% {knowledge}. Чудо: Великая библиотека", TechIds.Mathematics, TechIds.Mysticism),
                new TechConfig(TechIds.Epics, "Былины", TechBranch.Knowledge, 26,
                    "Все три класса в одном спине вербовки — ДЖЕКПОТ: богатырь (больше линий — больше ★), черты — от символов линий. " +
                    "В «Войсках» — ветка богатыря в самом низу дерева",
                    TechIds.Philosophy, TechIds.Sorcery, TechIds.Gunpowder, TechIds.MartialArts),
                // Пехота: воины начинают без щитов — щиты изучаются и покупаются в ветке ближнего боя («Войска»); боевые искусства (монахи) — здесь же
                new TechConfig(TechIds.Warfare, "Военное дело", TechBranch.Infantry, 5,
                    "Казармы: +1 спин вербовки, в нём много воинов (пока без щитов)"),
                new TechConfig(TechIds.Shields, "Щиты", TechBranch.Infantry, 7,
                    "Ключ ветки воинов в «Войсках»: щиты ловят удары в лицо, «Рывок» → «Таран щитом» (оглушает)", TechIds.Warfare),
                new TechConfig(TechIds.TowerShields, "Ростовые щиты", TechBranch.Infantry, 12,
                    "Ростовые щиты в «Войсках»: блок чаще, щит прикрывает и бок, таран оглушает вдвое дольше", TechIds.Shields),
                new TechConfig(TechIds.MartialArts, "Боевые искусства", TechBranch.Infantry, 18,
                    "Линии воинов и жрецов в одном спине вербовки → монах: бьётся врукопашную и зажигает оружие своих. " +
                    "Нужен ещё Монастырь — только на горе (горы редкие, среди камней). Ветка монахов — в «Войсках» под воинами и жрецами",
                    TechIds.TowerShields, TechIds.Staffs),
                new TechConfig(TechIds.Bronze, "Бронза", TechBranch.Infantry, 9,
                    $"Бронзовые мечи в «Войсках» и инструменты в мастерской: +1 {production}. " +
                    "Плавильня (только на камнях): спин вербовки, где полно воинов. Чудо: Ристалище", TechIds.Crafts),
                // Стрелки: качаются отдельно от пехоты, луки со временем становятся арбалетами
                new TechConfig(TechIds.Archery, "Стрельба из лука", TechBranch.Ranged, 5,
                    "Стрельбище: +1 спин вербовки, в нём много лучников"),
                new TechConfig(TechIds.CompositeBow, "Составной лук", TechBranch.Ranged, 9,
                    "Составные луки в «Войсках»: лучники +25%. Лесопилка (только в лесу): спин вербовки, где полно лучников", TechIds.Archery),
                new TechConfig(TechIds.Crossbows, "Арбалеты", TechBranch.Ranged, 14,
                    "Ключ ветки лучников в «Войсках»: луки → арбалеты, болты пробивают щиты", TechIds.CompositeBow),
                new TechConfig(TechIds.Gunpowder, "Порох", TechBranch.Ranged, 16,
                    "Линии воинов и лучников в одном спине вербовки → подрывник: динамит бьёт по площади. Ветка подрывников — в «Войсках» между воинами и лучниками",
                    TechIds.Crossbows, TechIds.Warfare),
                // Вера: жрецы лечат и благословляют; посохи открывают II ярус магии, чародейство (маги) — в конце ветки
                new TechConfig(TechIds.Mysticism, "Мистицизм", TechBranch.Faith, 5,
                    "Святилище: +1 спин вербовки, в нём много жрецов. Чудо: Стоунхендж"),
                new TechConfig(TechIds.Healing, "Целительство", TechBranch.Faith, 8,
                    "Лечебные травы в «Войсках»: жрецы лечат на 50% сильнее. Храм: спин вербовки, где полно жрецов", TechIds.Mysticism),
                new TechConfig(TechIds.Staffs, "Посохи", TechBranch.Faith, 10,
                    "Ключ ветки жрецов в «Войсках»: жрецы с посохами сильнее", TechIds.Healing),
                new TechConfig(TechIds.Monasticism, "Монашество", TechBranch.Faith, 15,
                    "Обители в «Войсках»: жрецы +25% ХП и атаки, «Молитва» на 50% сильнее", TechIds.Staffs, TechIds.Philosophy),
                new TechConfig(TechIds.Sorcery, "Чародейство", TechBranch.Faith, 16,
                    "Линии лучников и жрецов в одном спине вербовки → маг: цепная молния сквозь броню. " +
                    "Нужна ещё Башня магов — только на волшебном источнике (редкий, у рек и озёр). Ветка магов — в «Войсках» между лучниками и жрецами",
                    TechIds.Monasticism, TechIds.Archery),
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
