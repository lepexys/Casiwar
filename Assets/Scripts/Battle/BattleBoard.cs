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
    /// <summary>Символ поля боя (три в ряд).</summary>
    public enum BattleSymbol
    {
        Blank,      // пусто: складывается, но ничего не делает (когда символов мало — чтобы поле не складывалось само)
        Attack,     // ⚔ натиск: все, кто достаёт, бьют разом
        Tactics,    // ⏳ тактика: арена на несколько секунд медленнее, поле — нет (успеть собрать больше рядов)
        Warrior,    // навык воинов — «Рывок» / «Таран щитом»
        Archer,     // навык лучников — «Залп»
        Priest,     // навык жрецов — «Молитва»
        Banner,     // 🚩 Знамя — джокер: встаёт в любой ряд
        Multiplier, // ×2 (×3) — джокер: ряд с ним действует сильнее
        Relic,      // 🕯 Свеча — 3 в ряд: «Чудо»
        Bomber,     // навык подрывников — «Динамит»
        Monk,       // навык монахов — «Огненные клинки»
        Mage,       // навык магов — «Цепная молния»
    }

    /// <summary>Собранный ряд: чей символ, сколько в ряду, множитель от ×2 в нём и волна каскада (1 — от хода игрока).</summary>
    public sealed class BoardMatch
    {
        public BattleSymbol Symbol;
        public int Length;
        public int Multiplier = 1;
        public int Wave = 1;
        public readonly List<Vector2Int> Cells = new List<Vector2Int>();
    }

    /// <summary>
    /// Поле боя — три в ряд прямо во время боя (бой идёт сам, в реальном времени; AutoBattleManager):
    /// • меняем местами две соседние фишки (клик-клик или свайп) — только если собирается ряд 3+, иначе фишки
    ///   возвращаются. Ряд лопается, сверху падают новые; новые ряды после падения — каскад, сильнее с каждой волной;
    /// • символы: ⚔ натиск и ⏳ тактика есть всегда; символы классов — только тех, кто есть в нашей армии; особые
    ///   (открываются в гильдиях): 🚩 Знамя и ×2 — джокеры (×2 ещё и усиливает ряд), 🕯 Свеча — 3 в ряд «Чудо»;
    /// • что делает ряд — решает AutoBattleManager (событие Matched): 3 — навык, 4 — сильнее, 5+ — суперудар;
    /// • ходов нет — поле перемешивается само. Пока бой не идёт, поле не принимает ходы.
    /// Клетки поля создаются кодом внутри gridContainer.
    /// </summary>
    public class BattleBoard : MonoBehaviour, ITileInputHandler
    {
        [Header("Ссылки")]
        [Tooltip("Квадратная область поля (заполняется кодом)")]
        public RectTransform gridContainer;
        public TileView cellPrefab;
        public CityManager city;
        [Tooltip("Строка под полем: что сделал последний ряд")]
        public TMP_Text resultText;

        [Header("Поле")]
        [Range(4, 9)] public int size = 7;
        [Tooltip("Классов бойцов на поле не больше стольких (самые многочисленные): вместе с ⚔ и ➜ — до 5 видов фишек, иначе рядов почти не собрать")]
        [Min(1)] public int maxClassSymbols = 3;
        [Tooltip("Столько секунд без хода — две фишки, обмен которых собирает ряд, начинают пульсировать")]
        [Min(0.5f)] public float hintDelay = 3f;

        [Header("Символы (веса)")]
        [Min(0f)] public float attackWeight = 1.4f;
        [Tooltip("Часы «Тактики» — реже натиска: замедление надо ловить, а не держать весь бой")]
        [Min(0f)] public float tacticsWeight = 0.7f;
        [Min(0f)] public float classWeight = 1f;
        [Tooltip("Пустышки — только когда разных символов меньше minKinds (иначе поле складывается само)")]
        [Min(0f)] public float blankWeight = 0.9f;
        [Min(2)] public int minKinds = 4;
        [Min(0f)] public float bannerWeightPerLevel = 0.2f;
        [Min(0f)] public float multiplierWeightPerLevel = 0.15f;
        [Min(0f)] public float relicWeightPerLevel = 0.3f;
        [Min(1)] public int maxMultiplier = 8;

        [Header("Анимация")]
        [Min(0f)] public float tileSpacing = 6f;
        [Tooltip("Клеток в секунду при обмене и падении")]
        [Min(1f)] public float tileSpeed = 14f;
        [Min(0.01f)] public float popDuration = 0.16f;

        private BattleSymbol[,] grid;
        private TileView[,] views;
        private readonly List<(BattleSymbol symbol, float weight)> pool = new List<(BattleSymbol symbol, float weight)>();
        private Vector2Int? selected;
        private bool busy;
        private float step;
        private Vector2 lastContainerSize;

        /// <summary>Поле принимает ходы (бой идёт).</summary>
        public bool Interactable { get; private set; }

        /// <summary>Собран ряд (в том числе в каскаде) — до того, как фишки лопнут.</summary>
        public event Action<BoardMatch> Matched;

        private int MultiplierValue => city != null ? city.MultiplierValue : 2;

        private void Update()
        {
            if (views != null && gridContainer != null && gridContainer.rect.size != lastContainerSize && !busy) LayoutViews();
            UpdatePopups(Time.unscaledDeltaTime);
            UpdateHint(Time.deltaTime);
        }

        // =====================================================================
        //  Управление
        // =====================================================================

        /// <summary>Новый бой: символы — по классам армии; поле без готовых рядов и с ходом. Ходы — после Unlock.</summary>
        public void Begin(IEnumerable<UnitClass> classes)
        {
            StopAllCoroutines();
            ClearPopups();
            busy = false;
            SetSelected(null);
            SetClasses(classes);
            EnsureViews();
            grid = new BattleSymbol[size, size];
            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++) grid[x, y] = RandomSymbol();
            }
            Stabilize();
            if (views != null)
            {
                for (int x = 0; x < size; x++)
                {
                    for (int y = 0; y < size; y++)
                    {
                        Bind(x, y);
                        views[x, y].AnimateFrom(views[x, y].Home + Vector2.up * step * (size + x * 0.4f));
                    }
                }
            }
            Interactable = false;
            if (resultText != null) resultText.text = "Армии строятся — сейчас начнётся";
        }

        /// <summary>Бой пошёл: поле принимает ходы.</summary>
        public void Unlock()
        {
            Interactable = true;
            if (resultText != null) resultText.text = "Собирайте три в ряд — бойцы делают то, что собрано";
        }

        /// <summary>Какой обмен соседей собирает ряд (для подсказки). false — ходов нет.</summary>
        public bool TryFindMove(out Vector2Int a, out Vector2Int b)
        {
            a = b = Vector2Int.zero;
            return grid != null && FindMove(grid, MultiplierValue, maxMultiplier, out a, out b);
        }

        /// <summary>Бой кончился: ходы не принимаются.</summary>
        public void End()
        {
            StopAllCoroutines();
            ClearPopups();
            busy = false;
            Interactable = false;
            SetSelected(null);
            if (resultText != null) resultText.text = string.Empty;
        }

        /// <summary>
        /// Какие символы падают: ⚔ и ➜ всегда, символы классов армии, особые — по уровню в городе, пустышки — если
        /// разных символов мало. Уже лежащие фишки не меняются.
        /// </summary>
        public void SetClasses(IEnumerable<UnitClass> classes)
        {
            pool.Clear();
            pool.Add((BattleSymbol.Attack, attackWeight));
            pool.Add((BattleSymbol.Tactics, tacticsWeight));
            List<UnitClass> present = classes.Distinct().ToList();
            foreach (UnitClass unitClass in present.Where(c => SymbolFor(c) != BattleSymbol.Blank).Take(maxClassSymbols))
            {
                BattleSymbol symbol = SymbolFor(unitClass);
                // «Пороховые бочки», «Школа кулака», «Гримуары» — символов этого класса больше
                float boost = city != null ? 1f + city.ClassEffect(CityEffect.BoardSymbols, unitClass) : 1f;
                if (symbol != BattleSymbol.Blank) pool.Add((symbol, classWeight * boost));
            }
            if (city != null && city.RelicLevel > 0) pool.Add((BattleSymbol.Relic, relicWeightPerLevel * city.RelicLevel));
            if (pool.Count < minKinds) pool.Add((BattleSymbol.Blank, blankWeight));
            int banners = city != null ? city.BannerLevel + (present.Contains(UnitClass.Hero) && city.Effect(CityEffect.HeroBanner) > 0f ? 2 : 0) : 0;
            if (banners > 0) pool.Add((BattleSymbol.Banner, bannerWeightPerLevel * banners)); // «Знамя богатыря» — пока он в бою
            if (city != null && city.MultiplierLevel > 0) pool.Add((BattleSymbol.Multiplier, multiplierWeightPerLevel * city.MultiplierLevel));
        }

        /// <summary>
        /// Превратить count случайных обычных фишек в symbol («Руны» магов, Свеча «Мучеников»). Сложился ряд — каскад сам.
        /// </summary>
        public void Inject(BattleSymbol symbol, int count)
        {
            if (grid == null || count <= 0) return;
            var cells = new List<Vector2Int>();
            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++)
                {
                    if (grid[x, y] != symbol && !IsWild(grid[x, y]) && grid[x, y] != BattleSymbol.Relic) cells.Add(new Vector2Int(x, y));
                }
            }
            for (int i = 0; i < count && cells.Count > 0; i++)
            {
                int pick = Random.Range(0, cells.Count);
                Vector2Int cell = cells[pick];
                cells.RemoveAt(pick);
                grid[cell.x, cell.y] = symbol;
                if (views == null) continue;
                Bind(cell.x, cell.y);
                views[cell.x, cell.y].AnimateFrom(views[cell.x, cell.y].Home + Vector2.up * step * 0.4f); // фишка «подпрыгивает»
            }
            if (busy || !Interactable) return; // идёт каскад — новые ряды он и подберёт
            List<BoardMatch> matches = FindMatches(grid, MultiplierValue, maxMultiplier);
            if (matches.Count > 0) StartCoroutine(FreeCascade(matches));
        }

        private IEnumerator FreeCascade(List<BoardMatch> matches)
        {
            busy = true;
            yield return WaitForViews();
            yield return ResolveRoutine(matches);
            if (Interactable && !HasAnyMove(grid, MultiplierValue, maxMultiplier)) Reshuffle();
            busy = false;
        }

        /// <summary>Какие символы сейчас падают (для легенды боя).</summary>
        public IEnumerable<BattleSymbol> Symbols => pool.Select(p => p.symbol);

        private BattleSymbol RandomSymbol()
        {
            float total = pool.Sum(p => p.weight);
            if (pool.Count == 0 || total <= 0f) return BattleSymbol.Attack;
            float roll = Random.value * total;
            foreach ((BattleSymbol symbol, float weight) in pool)
            {
                roll -= weight;
                if (roll < 0f) return symbol;
            }
            return pool[pool.Count - 1].symbol;
        }

        // =====================================================================
        //  Ввод
        // =====================================================================

        public void OnTileClicked(Vector2Int cell)
        {
            if (!Interactable || busy || grid == null) return;
            if (!selected.HasValue)
            {
                SetSelected(cell);
                return;
            }
            Vector2Int previous = selected.Value;
            if (previous == cell)
            {
                SetSelected(null);
                return;
            }
            if (Mathf.Abs(previous.x - cell.x) + Mathf.Abs(previous.y - cell.y) == 1)
            {
                SetSelected(null);
                StartCoroutine(SwapRoutine(previous, cell));
                return;
            }
            SetSelected(cell);
        }

        public void OnTileSwiped(Vector2Int cell, Vector2Int direction)
        {
            if (!Interactable || busy || grid == null) return;
            Vector2Int other = cell + direction;
            if (other.x < 0 || other.y < 0 || other.x >= size || other.y >= size) return;
            SetSelected(null);
            StartCoroutine(SwapRoutine(cell, other));
        }

        private void SetSelected(Vector2Int? cell)
        {
            ClearHints();
            if (selected.HasValue && views != null) views[selected.Value.x, selected.Value.y].SetSelected(false);
            selected = cell;
            if (cell.HasValue && views != null) views[cell.Value.x, cell.Value.y].SetSelected(true);
            if (cell.HasValue && Interactable) HintSwapsFrom(cell.Value);
        }

        // =====================================================================
        //  Подсказки: что с чем можно сложить
        // =====================================================================

        private float idleTime;
        private readonly List<Vector2Int> hintCells = new List<Vector2Int>();

        /// <summary>Нет хода hintDelay секунд — пульсируют две фишки, обмен которых собирает ряд.</summary>
        private void UpdateHint(float dt)
        {
            if (!Interactable || busy || grid == null || views == null)
            {
                if (hintCells.Count > 0) ClearHints();
                idleTime = 0f;
                return;
            }
            if (selected.HasValue || hintCells.Count > 0) return;
            idleTime += dt;
            if (idleTime >= hintDelay && TryFindMove(out Vector2Int a, out Vector2Int b)) SetHints(new[] { a, b });
        }

        /// <summary>Выбрана фишка — пульсируют соседи, с которыми она соберёт ряд (нет таких — подсказка в строке).</summary>
        private void HintSwapsFrom(Vector2Int cell)
        {
            var targets = new List<Vector2Int>();
            foreach (Vector2Int direction in new[] { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down })
            {
                Vector2Int other = cell + direction;
                if (other.x < 0 || other.y < 0 || other.x >= size || other.y >= size) continue;
                if (MakesMatch(cell, other)) targets.Add(other);
            }
            SetHints(targets);
            if (targets.Count == 0 && resultText != null) resultText.text = "С этой фишкой ряда не собрать — выберите другую";
        }

        private bool MakesMatch(Vector2Int a, Vector2Int b)
        {
            Swap(a, b);
            bool found = FindMatches(grid, MultiplierValue, maxMultiplier).Count > 0;
            Swap(a, b);
            return found;
        }

        private void SetHints(IEnumerable<Vector2Int> cells)
        {
            foreach (Vector2Int cell in cells)
            {
                hintCells.Add(cell);
                if (views != null) views[cell.x, cell.y].SetHint(true);
            }
        }

        private void ClearHints()
        {
            if (views != null)
            {
                foreach (Vector2Int cell in hintCells)
                {
                    if (cell.x < size && cell.y < size) views[cell.x, cell.y].SetHint(false);
                }
            }
            hintCells.Clear();
            idleTime = 0f;
        }

        // =====================================================================
        //  Обмен, ряды, каскад
        // =====================================================================

        private IEnumerator SwapRoutine(Vector2Int a, Vector2Int b)
        {
            ClearHints();
            busy = true;
            Swap(a, b);
            yield return AnimateSwap(a, b);
            List<BoardMatch> matches = FindMatches(grid, MultiplierValue, maxMultiplier);
            if (matches.Count == 0)
            {
                // Ряд не собрался — фишки возвращаются
                Swap(a, b);
                yield return AnimateSwap(a, b);
                busy = false;
                yield break;
            }
            yield return ResolveRoutine(matches);
            if (Interactable && !HasAnyMove(grid, MultiplierValue, maxMultiplier)) Reshuffle();
            busy = false;
        }

        private IEnumerator ResolveRoutine(List<BoardMatch> matches)
        {
            int wave = 1;
            while (matches.Count > 0 && Interactable)
            {
                var cleared = new HashSet<Vector2Int>();
                foreach (BoardMatch match in matches)
                {
                    match.Wave = wave;
                    Matched?.Invoke(match);
                    foreach (Vector2Int cell in match.Cells) cleared.Add(cell);
                }
                if (!Interactable) yield break; // бой кончился этим ударом
                if (views != null)
                {
                    foreach (Vector2Int cell in cleared) views[cell.x, cell.y].PlayPop(popDuration);
                }
                yield return new WaitForSeconds(popDuration);
                Collapse(cleared);
                yield return WaitForViews();
                wave++;
                matches = FindMatches(grid, MultiplierValue, maxMultiplier);
            }
        }

        private void Swap(Vector2Int a, Vector2Int b) => (grid[a.x, a.y], grid[b.x, b.y]) = (grid[b.x, b.y], grid[a.x, a.y]);

        private IEnumerator AnimateSwap(Vector2Int a, Vector2Int b)
        {
            if (views == null) yield break;
            Bind(a.x, a.y);
            Bind(b.x, b.y);
            views[a.x, a.y].AnimateFrom(views[b.x, b.y].Home);
            views[b.x, b.y].AnimateFrom(views[a.x, a.y].Home);
            yield return WaitForViews();
        }

        /// <summary>Лопнувшие клетки пустеют, фишки падают вниз (y растёт вниз), сверху падают новые.</summary>
        private void Collapse(HashSet<Vector2Int> cleared)
        {
            for (int x = 0; x < size; x++)
            {
                int write = size - 1;
                for (int y = size - 1; y >= 0; y--)
                {
                    if (cleared.Contains(new Vector2Int(x, y))) continue;
                    if (write != y)
                    {
                        grid[x, write] = grid[x, y];
                        if (views != null)
                        {
                            Bind(x, write);
                            views[x, write].AnimateFrom(views[x, y].Home);
                        }
                    }
                    write--;
                }
                int fresh = write + 1;
                for (int y = write; y >= 0; y--)
                {
                    grid[x, y] = RandomSymbol();
                    if (views == null) continue;
                    Bind(x, y);
                    views[x, y].AnimateFrom(views[x, y].Home + Vector2.up * step * fresh);
                }
            }
        }

        /// <summary>Нет ни одного хода — новое поле (без готовых рядов, с ходом).</summary>
        private void Reshuffle()
        {
            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++) grid[x, y] = RandomSymbol();
            }
            Stabilize();
            if (views != null)
            {
                for (int x = 0; x < size; x++)
                {
                    for (int y = 0; y < size; y++)
                    {
                        Bind(x, y);
                        views[x, y].AnimateFrom(views[x, y].Home + Vector2.up * step * size);
                    }
                }
            }
            if (resultText != null) resultText.text = "Ходов не было — поле перемешалось";
        }

        /// <summary>Убрать готовые ряды и гарантировать ход.</summary>
        private void Stabilize()
        {
            for (int guard = 0; guard < 300; guard++)
            {
                List<BoardMatch> matches = FindMatches(grid, MultiplierValue, maxMultiplier);
                if (matches.Count == 0)
                {
                    if (HasAnyMove(grid, MultiplierValue, maxMultiplier)) return;
                    for (int x = 0; x < size; x++)
                    {
                        for (int y = 0; y < size; y++) grid[x, y] = RandomSymbol();
                    }
                    continue;
                }
                foreach (Vector2Int cell in matches.SelectMany(m => m.Cells)) grid[cell.x, cell.y] = RandomSymbol();
            }
        }

        // =====================================================================
        //  Логика (чистые функции)
        // =====================================================================

        public static bool IsWild(BattleSymbol symbol) => symbol == BattleSymbol.Banner || symbol == BattleSymbol.Multiplier;

        /// <summary>
        /// Все ряды 3+ по строкам и столбцам. Джокеры (Знамя, ×2) встают в любой ряд; ряд из одних джокеров — натиск ⚔.
        /// Джокеры на стыке двух разных рядов идут в оба. ×2 в ряду умножает его силу (не больше maxMultiplier).
        /// </summary>
        public static List<BoardMatch> FindMatches(BattleSymbol[,] grid, int multiplierValue, int maxMultiplier)
        {
            var result = new List<BoardMatch>();
            int width = grid.GetLength(0), height = grid.GetLength(1);
            var line = new List<Vector2Int>(Mathf.Max(width, height));
            for (int y = 0; y < height; y++)
            {
                line.Clear();
                for (int x = 0; x < width; x++) line.Add(new Vector2Int(x, y));
                ScanLine(grid, line, result, multiplierValue, maxMultiplier);
            }
            for (int x = 0; x < width; x++)
            {
                line.Clear();
                for (int y = 0; y < height; y++) line.Add(new Vector2Int(x, y));
                ScanLine(grid, line, result, multiplierValue, maxMultiplier);
            }
            return result;
        }

        private static void ScanLine(BattleSymbol[,] grid, List<Vector2Int> cells, List<BoardMatch> result, int multiplierValue, int maxMultiplier)
        {
            int i = 0;
            while (i < cells.Count)
            {
                BattleSymbol key = BattleSymbol.Attack;
                bool hasKey = false;
                int j = i;
                while (j < cells.Count)
                {
                    BattleSymbol symbol = grid[cells[j].x, cells[j].y];
                    if (!IsWild(symbol))
                    {
                        if (!hasKey)
                        {
                            key = symbol;
                            hasKey = true;
                        }
                        else if (symbol != key) break;
                    }
                    j++;
                }
                if (j - i < 3)
                {
                    i++;
                    continue;
                }
                var match = new BoardMatch { Symbol = key, Length = j - i };
                for (int k = i; k < j; k++)
                {
                    match.Cells.Add(cells[k]);
                    if (grid[cells[k].x, cells[k].y] == BattleSymbol.Multiplier)
                        match.Multiplier = Mathf.Min(maxMultiplier, match.Multiplier * Mathf.Max(1, multiplierValue));
                }
                result.Add(match);
                // Джокеры в хвосте ряда могут начать следующий ряд другого символа
                int next = j;
                while (next - 1 > i && IsWild(grid[cells[next - 1].x, cells[next - 1].y])) next--;
                i = next > i ? next : i + 1;
            }
        }

        /// <summary>Есть ли обмен соседей, который собирает ряд.</summary>
        public static bool HasAnyMove(BattleSymbol[,] grid, int multiplierValue, int maxMultiplier) =>
            FindMove(grid, multiplierValue, maxMultiplier, out _, out _);

        /// <summary>Первый обмен соседей, который собирает ряд.</summary>
        public static bool FindMove(BattleSymbol[,] grid, int multiplierValue, int maxMultiplier, out Vector2Int a, out Vector2Int b)
        {
            a = b = Vector2Int.zero;
            int width = grid.GetLength(0), height = grid.GetLength(1);
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    foreach (Vector2Int direction in new[] { Vector2Int.right, Vector2Int.up })
                    {
                        int nx = x + direction.x, ny = y + direction.y;
                        if (nx >= width || ny >= height) continue;
                        (grid[x, y], grid[nx, ny]) = (grid[nx, ny], grid[x, y]);
                        bool found = FindMatches(grid, multiplierValue, maxMultiplier).Count > 0;
                        (grid[x, y], grid[nx, ny]) = (grid[nx, ny], grid[x, y]);
                        if (!found) continue;
                        a = new Vector2Int(x, y);
                        b = new Vector2Int(nx, ny);
                        return true;
                    }
                }
            }
            return false;
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
        //  Итог хода — слово и вспышка прямо на поле
        // =====================================================================

        /// <summary>Эффект поверх поля: всплывающее слово, вспышка фишки, волна или заливка поля.</summary>
        private sealed class Fx
        {
            public RectTransform Rect;
            public CanvasRenderer Renderer; // вспышка
            public CanvasGroup Group;       // слово (с тенями)
            public float Alpha = 1f;
            public Vector2 Start;
            public float Rise;
            public float FromScale = 1f;
            public float ToScale = 1f;
            public float Age;
            public float Life;
            public bool IsWord;
        }

        [Header("Итог хода на поле")]
        [Tooltip("Сколько секунд висит слово над собранным рядом")]
        [Min(0.3f)] public float wordSeconds = 1f;

        private RectTransform fxLayer;
        private readonly List<Fx> effects = new List<Fx>();

        /// <summary>
        /// Что сделал собранный ряд: одно слово над ним (цвет действия; серое — ничего не вышло) и вспышка: фишки ряда
        /// вспыхивают, от центра расходится волна. Суперудар — крупнее, и поле на миг заливает цветом класса.
        /// </summary>
        public void ShowOutcome(IReadOnlyList<Vector2Int> cells, string word, Color color, bool super, bool wasted)
        {
            if (views == null || cells == null || cells.Count == 0 || FxLayer() == null) return;
            Color tint = wasted ? new Color(0.62f, 0.64f, 0.7f) : color;
            Vector2 center = Vector2.zero;
            foreach (Vector2Int cell in cells)
            {
                center += CellHome(cell);
                if (!wasted) SpawnFlash(CellHome(cell), step * 0.9f, Color.Lerp(tint, Color.white, 0.3f), 0.6f, 1f, 1.15f, 0.3f, ring: false);
            }
            center /= cells.Count;
            if (!wasted) SpawnFlash(center, step, Color.Lerp(tint, Color.white, 0.2f), 0.9f, 0.6f, Mathf.Min(cells.Count + 0.5f, 3.5f), 0.45f, ring: true);
            if (super)
            {
                SpawnFlash(Vector2.zero, step * size, tint, 0.3f, 1f, 1f, 0.55f, ring: false, board: true);
                SpawnFlash(center, step, Color.Lerp(tint, Color.white, 0.4f), 0.9f, 1f, size * 1.4f, 0.6f, ring: true);
            }
            if (!string.IsNullOrEmpty(word)) SpawnWord(center, word, tint, super ? 1.45f : 1f);
        }

        /// <summary>Навык не с поля (враг — красным, наши классы не на поле — их цветом): слово у верхнего края.</summary>
        public void ShowTopAction(string word, Color color, bool enemy)
        {
            if (views == null || FxLayer() == null) return;
            if (enemy) SpawnFlash(Vector2.zero, step * size, color, 0.18f, 1f, 1f, 0.4f, ring: false, board: true);
            SpawnWord(new Vector2(0f, (size - 1) * 0.5f * step), word, color, 0.85f);
        }

        private Vector2 CellHome(Vector2Int cell) =>
            new Vector2((cell.x - (size - 1) * 0.5f) * step, ((size - 1) * 0.5f - cell.y) * step);

        /// <summary>Слой эффектов поверх поля (не внутри сетки: её маска обрезала бы края, а пересборка — стирала).</summary>
        private RectTransform FxLayer()
        {
            if (fxLayer != null) return fxLayer;
            if (gridContainer == null || gridContainer.parent == null) return null;
            fxLayer = NewRect("BoardFx", gridContainer.parent);
            fxLayer.anchorMin = Vector2.zero;
            fxLayer.anchorMax = Vector2.one;
            fxLayer.offsetMin = fxLayer.offsetMax = Vector2.zero;
            fxLayer.gameObject.AddComponent<RectMask2D>(); // волны не вылезают за поле
            return fxLayer;
        }

        private Vector2 ToLayer(Vector2 gridPoint) => fxLayer.InverseTransformPoint(gridContainer.TransformPoint(gridPoint));

        /// <summary>Вспышка: скруглённая фишка, кольцо-волна или заливка всего поля; растёт от fromScale к toScale и гаснет.</summary>
        private void SpawnFlash(Vector2 gridPoint, float side, Color color, float alpha, float fromScale, float toScale, float life, bool ring,
            bool board = false)
        {
            fxLayer.SetAsLastSibling();
            RectTransform rect = NewRect(ring ? "Wave" : "Flash", fxLayer);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(side, side);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = ring ? GameVisuals.Ring : board ? null : GameVisuals.RoundedRect;
            image.type = !ring && !board ? Image.Type.Sliced : Image.Type.Simple;
            image.color = color;
            image.raycastTarget = false;
            Vector2 at = ToLayer(gridPoint);
            rect.anchoredPosition = at;
            rect.localScale = Vector3.one * fromScale;
            effects.Add(new Fx
            {
                Rect = rect, Renderer = image.canvasRenderer, Alpha = alpha, Start = at,
                FromScale = fromScale, ToScale = toScale, Life = life,
            });
        }

        /// <summary>Слово с обводкой: выскакивает, чуть всплывает и гаснет. Слова одного хода (каскад) встают друг над другом.</summary>
        private void SpawnWord(Vector2 gridPoint, string word, Color color, float scale)
        {
            fxLayer.SetAsLastSibling();
            RectTransform rect = NewRect("Word", fxLayer);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            var group = rect.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            float fontSize = Mathf.Clamp(step * 0.42f, 22f, 44f) * scale;
            float edge = Mathf.Max(2f, fontSize * 0.07f);
            var shade = new Color(0.04f, 0.03f, 0.06f, 0.95f);
            foreach (Vector2 offset in new[] { new Vector2(-edge, 0f), new Vector2(edge, 0f), new Vector2(0f, -edge), new Vector2(0f, edge),
                         new Vector2(-edge, -edge), new Vector2(edge, -edge), new Vector2(-edge, edge), new Vector2(edge, edge),
                         new Vector2(0f, -edge * 2f) })
                WordText(rect, word, fontSize, shade, offset);
            TMP_Text text = WordText(rect, word, fontSize, Color.Lerp(color, Color.white, 0.2f), Vector2.zero);
            Vector2 box = text.GetPreferredValues(word) + new Vector2(edge * 2f + 6f, edge * 2f);
            rect.sizeDelta = box;

            Vector2 at = ToLayer(gridPoint);
            foreach (Fx other in effects)
            {
                if (!other.IsWord || other.Rect == null) continue;
                Vector2 o = other.Rect.anchoredPosition;
                Vector2 half = (other.Rect.sizeDelta + box) * 0.5f;
                if (Mathf.Abs(o.x - at.x) >= half.x || Mathf.Abs(o.y - at.y) >= half.y) continue;
                if (other.Age < 0.3f) at.y = o.y + half.y + 2f; // тот же ход — выше
                else other.Life = Mathf.Min(other.Life, other.Age + 0.15f); // прежнее слово уступает
            }
            Vector2 boardCenter = ToLayer(Vector2.zero);
            float halfSide = step * size * 0.5f;
            at.x = Mathf.Clamp(at.x, boardCenter.x - halfSide + box.x * 0.5f, boardCenter.x + halfSide - box.x * 0.5f);
            at.y = Mathf.Clamp(at.y, boardCenter.y - halfSide + box.y * 0.5f, boardCenter.y + halfSide - box.y * 0.5f);
            rect.anchoredPosition = at;
            effects.Add(new Fx
            {
                Rect = rect, Group = group, Start = at, Rise = step * 0.45f,
                Life = wordSeconds * (scale > 1.2f ? 1.4f : 1f), IsWord = true,
            });
        }

        /// <summary>Одна копия слова (тень обводки или само слово), растянутая на всю табличку и сдвинутая на offset.</summary>
        private TMP_Text WordText(RectTransform parent, string word, float fontSize, Color color, Vector2 offset)
        {
            RectTransform rect = NewRect("Text", parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = offset;
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (resultText != null) text.font = resultText.font;
            text.fontSize = fontSize;
            text.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            text.color = color;
            text.text = word;
            return text;
        }

        private void UpdatePopups(float dt)
        {
            for (int i = effects.Count - 1; i >= 0; i--)
            {
                Fx fx = effects[i];
                fx.Age += dt;
                if (fx.Rect == null || fx.Age >= fx.Life)
                {
                    if (fx.Rect != null) Destroy(fx.Rect.gameObject);
                    effects.RemoveAt(i);
                    continue;
                }
                float t = fx.Age / fx.Life;
                float ease = 1f - (1f - t) * (1f - t);
                if (fx.IsWord)
                {
                    float pop = Mathf.Clamp01(fx.Age / 0.12f);
                    fx.Rect.localScale = Vector3.one * Mathf.Lerp(0.5f, 1f, 1f - (1f - pop) * (1f - pop));
                    fx.Rect.anchoredPosition = fx.Start + Vector2.up * (fx.Rise * ease);
                    fx.Group.alpha = Mathf.Clamp01((1f - t) / 0.35f);
                }
                else
                {
                    fx.Rect.localScale = Vector3.one * Mathf.Lerp(fx.FromScale, fx.ToScale, ease);
                    fx.Renderer.SetAlpha(fx.Alpha * (1f - t));
                }
            }
        }

        private void ClearPopups()
        {
            foreach (Fx fx in effects)
            {
                if (fx.Rect != null) Destroy(fx.Rect.gameObject);
            }
            effects.Clear();
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        // =====================================================================
        //  Вид
        // =====================================================================

        private void Bind(int x, int y)
        {
            if (views == null) return;
            views[x, y].Show(GameVisuals.LookForSlot(grid[x, y], MultiplierValue));
        }

        private void EnsureViews()
        {
            if (views != null && views.GetLength(0) == size) return;
            if (gridContainer == null || cellPrefab == null) return;
            if (views != null)
            {
                foreach (TileView view in views)
                {
                    if (view != null) Destroy(view.gameObject);
                }
            }
            for (int i = gridContainer.childCount - 1; i >= 0; i--) Destroy(gridContainer.GetChild(i).gameObject); // клетки прежнего слота
            if (gridContainer.GetComponent<RectMask2D>() == null) gridContainer.gameObject.AddComponent<RectMask2D>();
            views = new TileView[size, size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    TileView view = Instantiate(cellPrefab, gridContainer);
                    view.Init(this, new Vector2Int(x, y));
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
            float tileSize = Mathf.Max(8f, step - tileSpacing);
            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++)
                {
                    views[x, y].Place(CellHome(new Vector2Int(x, y)), tileSize, tileSpeed * step);
                }
            }
        }

        private IEnumerator WaitForViews()
        {
            float timeout = 2f;
            while (timeout > 0f && !AllSettled())
            {
                timeout -= Time.deltaTime;
                yield return null;
            }
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
    }
}
