using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Casiwar
{
    /// <summary>Тип фишки на поле «3-в-ряд».</summary>
    public enum TileKind
    {
        Unit,     // юнит (поле города)
        Resource, // ресурс: золото, производство, еда
        Item,     // снаряжение (поле трофеев)
    }

    /// <summary>Ресурсы, которые падают на поле.</summary>
    public enum ResourceType
    {
        Gold,       // золото — наука в дереве технологий
        Production, // ⚒ — постройка и улучшение зданий
        Food,       // еда — прокорм жителей и рост города
    }

    /// <summary>Содержимое одной клетки поля (неизменяемое).</summary>
    public sealed class TileData
    {
        public readonly TileKind Kind;
        public readonly UnitData Unit;
        public readonly ItemData Item;
        public readonly ResourceType Resource;

        private TileData(TileKind kind, UnitData unit, ItemData item, ResourceType resource)
        {
            Kind = kind;
            Unit = unit;
            Item = item;
            Resource = resource;
        }

        public static TileData ForUnit(UnitData unit) => new TileData(TileKind.Unit, unit, null, default);
        public static TileData ForResource(ResourceType resource) => new TileData(TileKind.Resource, null, null, resource);
        public static TileData ForItem(ItemData item) => new TileData(TileKind.Item, null, item, default);

        public bool IsUnit => Kind == TileKind.Unit;
        public UnitClass Class => Unit != null ? Unit.unitClass : UnitClass.None;
        public bool IsNeutral => IsUnit && Class == UnitClass.None;

        public override string ToString()
        {
            switch (Kind)
            {
                case TileKind.Unit: return Unit != null ? Unit.unitName : "?";
                case TileKind.Item: return Item != null ? Item.itemName : "?";
                default: return Resource.ToString();
            }
        }
    }

    /// <summary>
    /// Какие смешанные линии юнитов сейчас разрешены — зависит от изученной науки (ставит поле города).
    /// </summary>
    public sealed class LineRules
    {
        /// <summary>Гибриды, которые уже складываются из двух классов (воин + лучник → подрывник и т.д.).</summary>
        public readonly HashSet<UnitClass> Hybrids = new HashSet<UnitClass>();
        /// <summary>С какой длины годится линия из ЛЮБЫХ юнитов (богатырь); 0 — нельзя.</summary>
        public int AnyMixLength;

        public bool Allows(UnitClass a, UnitClass b) => Hybrids.Contains(UnitClasses.Combine(a, b));
    }

    /// <summary>Найденная линия 3+ фишек в ряд.</summary>
    public sealed class MatchRun
    {
        public readonly List<Vector2Int> Cells = new List<Vector2Int>();
        public TileKind Kind;
        /// <summary>
        /// Для линии юнитов — что из неё выходит: класс, гибрид двух классов (Bomber/Monk/Mage)
        /// или Hero — смесь, которая годится только как линия 5+ для богатыря. None — одни нейтралы.
        /// </summary>
        public UnitClass UnitClass;
        /// <summary>Для линии предметов — какой предмет.</summary>
        public ItemData Item;
        /// <summary>Для линии ресурсов — какой ресурс.</summary>
        public ResourceType Resource;

        public int Length => Cells.Count;
        public bool IsUnitRun => Kind == TileKind.Unit;

        /// <summary>«Ключ» линии: один класс юнитов, один ресурс или один и тот же предмет.</summary>
        public bool SameKey(MatchRun other)
        {
            if (other == null || Kind != other.Kind) return false;
            switch (Kind)
            {
                case TileKind.Unit: return UnitClass == other.UnitClass;
                case TileKind.Item: return Item == other.Item;
                default: return Resource == other.Resource;
            }
        }

        public bool Overlaps(MatchRun other)
        {
            foreach (Vector2Int cell in Cells)
            {
                if (other.Cells.Contains(cell)) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Правила совпадений (чистый C#, без MonoBehaviour — легко тестировать).
    ///
    ///  1. Фишки разных типов (юнит / ресурс / предмет) не складываются.
    ///  2. Ресурсы — только одинаковые (еда с едой), предметы — тоже (тяжёлая броня ×3).
    ///  3. Нейтралы (UnitClass.None) — джокеры: подходят к любой линии юнитов.
    ///  4. В линии юнитов — один класс (Лучник + Нейтрал + Лучник), а когда изучен гибрид (Rules.Hybrids) —
    ///     два его класса: Воин + Лучник → подрывник, Воин + Жрец → монах, Лучник + Жрец → маг.
    ///  5. Изучены «Былины» (Rules.AnyMixLength = 5): линия из 5+ ЛЮБЫХ юнитов подряд — тоже линия (богатырь).
    ///  6. Если линии претендуют на одни клетки, побеждает более длинная (при равной — классовая).
    ///  7. Пустые клетки (запас символов кончился) рвут линию.
    ///
    /// Координаты: x — столбец (слева направо), y — строка (сверху вниз), grid[x, y]; null — пустая клетка.
    /// </summary>
    public static class MatchRules
    {
        public const int MinMatch = 3;

        /// <summary>Что сейчас разрешено смешивать (поле города обновляет при изучении науки).</summary>
        public static LineRules Rules = new LineRules();

        /// <summary>Возможный ход (обмен двух соседних фишек) и его оценка.</summary>
        public struct Move
        {
            public Vector2Int A;
            public Vector2Int B;
            public int Score;
            /// <summary>Ход собирает линию класса из Приоритета.</summary>
            public bool HitsPriority;
        }

        /// <summary>«Ключ» растущей линии: что в ней уже есть.</summary>
        private struct LineKey
        {
            public TileKind Kind;
            public UnitClass Class;  // None, пока в линии только нейтралы
            public UnitClass Class2; // второй класс линии-гибрида (None — его нет)
            public ItemData Item;
            public ResourceType Resource;

            public LineKey(TileData first)
            {
                Kind = first.Kind;
                Class = first.IsUnit ? first.Class : UnitClass.None;
                Class2 = UnitClass.None;
                Item = first.Item;
                Resource = first.Resource;
            }

            /// <summary>Что выходит из линии: класс или гибрид двух классов.</summary>
            public UnitClass ResultClass => Class2 == UnitClass.None ? Class : UnitClasses.Combine(Class, Class2);

            /// <summary>Главное правило: можно ли продолжить линию этой фишкой.</summary>
            public bool TryAdd(TileData tile)
            {
                if (tile == null || tile.Kind != Kind) return false;          // пусто или разные типы
                if (Kind == TileKind.Resource) return tile.Resource == Resource; // ресурсы — только одинаковые
                if (Kind == TileKind.Item) return tile.Item == Item;          // предметы — только одинаковые

                UnitClass cls = tile.Class;
                if (cls == UnitClass.None) return true;                  // нейтрал подходит любой линии юнитов
                if (Class == UnitClass.None)                             // первый классовый юнит задаёт класс линии
                {
                    Class = cls;
                    return true;
                }
                if (cls == Class || cls == Class2) return true;
                if (Class2 == UnitClass.None && Rules.Allows(Class, cls)) // изученный гибрид: второй класс
                {
                    Class2 = cls;
                    return true;
                }
                return false;                                            // другой класс — линия рвётся
            }
        }

        /// <summary>Можно ли сложить эти фишки в одну линию (порядок не важен).</summary>
        public static bool CanFormLine(IList<TileData> tiles)
        {
            if (tiles == null || tiles.Count == 0 || tiles[0] == null) return false;
            if (Rules.AnyMixLength > 0 && tiles.Count >= Rules.AnyMixLength && tiles.All(t => t != null && t.IsUnit)) return true;
            var key = new LineKey(tiles[0]);
            for (int i = 1; i < tiles.Count; i++)
            {
                if (!key.TryAdd(tiles[i])) return false;
            }
            return true;
        }

        /// <summary>
        /// Найти все линии 3+ на поле. Пересекающиеся линии одного «ключа» (L/T-фигуры) засчитываются обе,
        /// конфликт линий разных ключей решается приоритетом и длиной.
        /// </summary>
        public static List<MatchRun> FindMatches(TileData[,] grid, UnitClass priority = UnitClass.None)
        {
            int width = grid.GetLength(0);
            int height = grid.GetLength(1);
            var candidates = new List<MatchRun>();
            var line = new List<Vector2Int>(Mathf.Max(width, height));

            for (int y = 0; y < height; y++)
            {
                line.Clear();
                for (int x = 0; x < width; x++) line.Add(new Vector2Int(x, y));
                ScanLine(grid, line, candidates);
            }
            for (int x = 0; x < width; x++)
            {
                line.Clear();
                for (int y = 0; y < height; y++) line.Add(new Vector2Int(x, y));
                ScanLine(grid, line, candidates);
            }
            if (candidates.Count <= 1) return candidates;

            // Сначала приоритетный класс, затем длинные линии (OrderBy — стабильная сортировка)
            List<MatchRun> sorted = candidates.OrderByDescending(run => Score(run, priority)).ToList();
            var accepted = new List<MatchRun>(sorted.Count);
            foreach (MatchRun run in sorted)
            {
                bool conflict = false;
                foreach (MatchRun other in accepted)
                {
                    if (!other.SameKey(run) && other.Overlaps(run))
                    {
                        conflict = true;
                        break;
                    }
                }
                if (!conflict) accepted.Add(run);
            }
            return accepted;
        }

        /// <summary>Лучший ход на поле (для подсказки и авто-сбора). false — ходов нет.</summary>
        public static bool TryFindBestMove(TileData[,] grid, UnitClass priority, out Move best)
        {
            best = default;
            bool found = false;
            int width = grid.GetLength(0);
            int height = grid.GetLength(1);
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    var a = new Vector2Int(x, y);
                    EvaluateMove(grid, priority, a, new Vector2Int(x + 1, y), ref best, ref found);
                    EvaluateMove(grid, priority, a, new Vector2Int(x, y + 1), ref best, ref found);
                }
            }
            return found;
        }

        public static bool HasAnyMove(TileData[,] grid) => TryFindBestMove(grid, UnitClass.None, out _);

        /// <summary>
        /// Ход-подготовка (для «Авто»): перестановка, которая сама ничего не складывает,
        /// но после неё появляется складывающий ход. Выбирается по оценке этого будущего хода.
        /// </summary>
        public static bool TryFindSetupMove(TileData[,] grid, UnitClass priority, out Move best)
        {
            best = default;
            bool found = false;
            int width = grid.GetLength(0);
            int height = grid.GetLength(1);
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    var a = new Vector2Int(x, y);
                    foreach (Vector2Int b in new[] { new Vector2Int(x + 1, y), new Vector2Int(x, y + 1) })
                    {
                        if (!InBounds(grid, b) || Get(grid, a) == null || Get(grid, b) == null || SameSymbol(Get(grid, a), Get(grid, b))) continue;
                        Swap(grid, a, b);
                        Move next = default;
                        bool setup = FindMatches(grid).Count == 0 && TryFindBestMove(grid, priority, out next);
                        Swap(grid, a, b);
                        if (setup && (!found || next.Score > best.Score))
                        {
                            best = new Move { A = a, B = b, Score = next.Score, HitsPriority = next.HitsPriority };
                            found = true;
                        }
                    }
                }
            }
            return found;
        }

        /// <summary>
        /// Можно ли вообще сложить линию перестановками: найдутся 3 совместимые фишки
        /// (одинаковые ресурсы/предметы или юниты одного класса вместе с нейтралами) и три занятые клетки подряд.
        /// </summary>
        public static bool CanEverMatch(TileData[,] grid)
        {
            int width = grid.GetLength(0);
            int height = grid.GetLength(1);
            int neutrals = 0;
            var classes = new Dictionary<UnitClass, int>();
            var resources = new Dictionary<ResourceType, int>();
            var items = new Dictionary<ItemData, int>();
            bool roomForLine = false;
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    TileData tile = grid[x, y];
                    if (tile == null) continue;
                    if ((x + 2 < width && grid[x + 1, y] != null && grid[x + 2, y] != null) ||
                        (y + 2 < height && grid[x, y + 1] != null && grid[x, y + 2] != null))
                        roomForLine = true;

                    if (tile.Kind == TileKind.Resource) resources[tile.Resource] = resources.TryGetValue(tile.Resource, out int r) ? r + 1 : 1;
                    else if (tile.Kind == TileKind.Item && tile.Item != null) items[tile.Item] = items.TryGetValue(tile.Item, out int i) ? i + 1 : 1;
                    else if (tile.IsNeutral) neutrals++;
                    else if (tile.IsUnit) classes[tile.Class] = classes.TryGetValue(tile.Class, out int c) ? c + 1 : 1;
                }
            }
            if (!roomForLine) return false;
            if (neutrals >= MinMatch || resources.Values.Any(n => n >= MinMatch) || items.Values.Any(n => n >= MinMatch)
                || classes.Values.Any(n => n + neutrals >= MinMatch))
                return true;
            foreach (UnitClass hybrid in Rules.Hybrids)
            {
                UnitClass[] parents = UnitClasses.Parents(hybrid);
                int first = classes.TryGetValue(parents[0], out int a) ? a : 0;
                int second = parents.Length > 1 && classes.TryGetValue(parents[1], out int b) ? b : 0;
                if (first + second + neutrals >= MinMatch) return true;
            }
            return Rules.AnyMixLength > 0 && neutrals + classes.Values.Sum() >= Rules.AnyMixLength;
        }

        /// <summary>Одинаковые фишки: менять их местами бессмысленно.</summary>
        private static bool SameSymbol(TileData a, TileData b)
        {
            return a.Kind == b.Kind && a.Unit == b.Unit && a.Item == b.Item && (a.Kind != TileKind.Resource || a.Resource == b.Resource);
        }

        public static bool InBounds(TileData[,] grid, Vector2Int cell)
        {
            return cell.x >= 0 && cell.y >= 0 && cell.x < grid.GetLength(0) && cell.y < grid.GetLength(1);
        }

        public static void Swap(TileData[,] grid, Vector2Int a, Vector2Int b)
        {
            TileData tmp = grid[a.x, a.y];
            grid[a.x, a.y] = grid[b.x, b.y];
            grid[b.x, b.y] = tmp;
        }

        // ---------- Внутреннее ----------

        /// <summary>Ищет максимальные валидные отрезки длиной 3+ в одной строке/столбце.</summary>
        private static void ScanLine(TileData[,] grid, List<Vector2Int> line, List<MatchRun> output)
        {
            int count = line.Count;
            int previousEnd = -1;
            for (int start = 0; start < count; start++)
            {
                TileData first = Get(grid, line[start]);
                if (first == null) continue;

                var key = new LineKey(first);
                int end = start;
                for (int j = start + 1; j < count; j++)
                {
                    if (!key.TryAdd(Get(grid, line[j]))) break;
                    end = j;
                }
                UnitClass unitClass = key.ResultClass;

                // «Былины»: 5+ любых юнитов подряд — линия для богатыря, даже если классы не складываются
                if (Rules.AnyMixLength > 0 && first.IsUnit)
                {
                    int mixEnd = start;
                    for (int j = start + 1; j < count; j++)
                    {
                        TileData tile = Get(grid, line[j]);
                        if (tile == null || !tile.IsUnit) break;
                        mixEnd = j;
                    }
                    if (mixEnd > end && mixEnd - start + 1 >= Rules.AnyMixLength)
                    {
                        end = mixEnd;
                        unitClass = UnitClass.Hero;
                    }
                }

                // Отрезок, целиком лежащий внутри предыдущего, — не новая линия
                if (end > previousEnd && end - start + 1 >= MinMatch)
                {
                    var run = new MatchRun { Kind = key.Kind, UnitClass = unitClass, Item = key.Item, Resource = key.Resource };
                    for (int k = start; k <= end; k++) run.Cells.Add(line[k]);
                    output.Add(run);
                }
                if (end > previousEnd) previousEnd = end;
            }
        }

        private static int Score(MatchRun run, UnitClass priority)
        {
            int score = run.Length * 10;
            if (run.IsUnitRun)
            {
                if (priority != UnitClass.None && run.UnitClass == priority) score += 1000;
                if (run.UnitClass != UnitClass.None) score += 1; // при равной длине классовая линия важнее нейтральной
            }
            return score;
        }

        private static void EvaluateMove(TileData[,] grid, UnitClass priority, Vector2Int a, Vector2Int b, ref Move best, ref bool found)
        {
            if (!InBounds(grid, b) || Get(grid, a) == null || Get(grid, b) == null) return;

            Swap(grid, a, b);
            List<MatchRun> runs = FindMatches(grid, priority);
            Swap(grid, a, b); // откат пробного хода
            if (runs.Count == 0) return;

            int score = 0;
            bool hitsPriority = false;
            foreach (MatchRun run in runs)
            {
                score += run.Length * 10;
                if (run.IsUnitRun && priority != UnitClass.None && run.UnitClass == priority)
                {
                    score += 100;
                    hitsPriority = true;
                }
            }

            if (!found || score > best.Score)
            {
                best = new Move { A = a, B = b, Score = score, HitsPriority = hitsPriority };
                found = true;
            }
        }

        private static TileData Get(TileData[,] grid, Vector2Int cell) => grid[cell.x, cell.y];
    }
}
