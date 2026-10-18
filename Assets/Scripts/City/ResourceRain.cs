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
    /// <summary>Символ сбора ресурсов.</summary>
    public enum RainSymbol
    {
        Empty, // не выпадает (запасное значение)
        Gold,
        Production,
        Food,
    }

    /// <summary>
    /// Сбор ресурсов в начале хода — сам, без игрока, быстро:
    /// в слот size×size падают символы 🪙 ⚒ 🍎, считаются кластеры — одинаковые символы, соседние по сторонам, —
    /// и в казну идут кластеры — по одному на жителя: символ = 1 ресурс, плюс бонус к кластеру от зданий (амбар,
    /// мастерская, торговые ряды, колосс). Крутка берёт до трёх кластеров — самый большой у каждого ресурса;
    /// жителей не кратно трём — на последней крутке берутся только самые крупные (5 жителей: 3 кластера, потом 2).
    /// Следующая крутка — все символы новые. Круток за ход — жители / 3 с округлением вверх (+ bonusPicks кластеров).
    /// Чего больше падает — решает фокус города (кнопки «Следующий ход» на вкладке «Поле»).
    /// Показывается поверх экрана; клик (или Enter) — досчитать сразу. Окно создаётся кодом — сцену пересобирать не надо.
    /// GameManager добавляет компонент сам.
    /// </summary>
    public class ResourceRain : MonoBehaviour, ITileInputHandler
    {
        private static readonly Vector2Int[] Sides = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

        [Header("Ссылки (пусто — найдутся сами)")]
        public CityManager city;
        [Tooltip("Префаб клетки (тот же TileView, что у поля)")]
        public TileView tilePrefab;
        [Tooltip("Шрифт подписей (берётся с любого текста сцены)")]
        public TMP_FontAsset font;

        [Header("Сбор")]
        [Min(3)] public int size = 4;
        [Tooltip("Кластеров за ход = жители + это (за крутку — до трёх, по одному на ресурс)")]
        [Min(0)] public int bonusPicks = 0;

        [Header("Скорость")]
        [Tooltip("Весь сбор длится примерно столько секунд (каждый шаг — в пределах minStep…maxStep)")]
        [Min(0.5f)] public float totalDuration = 3.2f;
        [Min(0.05f)] public float minStep = 0.2f;
        [Min(0.05f)] public float maxStep = 0.6f;
        [Tooltip("Пауза в конце, чтобы увидеть итог")]
        [Min(0f)] public float endPause = 0.7f;

        private RectTransform overlay;
        private RectTransform gridRoot;
        private TMP_Text titleText;
        private TMP_Text tallyText;
        private TMP_Text hintText;
        private TileView[,] views;
        private RainSymbol[,] grid;
        private int spinsTotal;
        private int spinsDone;
        private int picksTotal;
        private int picksDone;
        private readonly int[] gained = new int[3];
        private bool skip;
        private Coroutine routine;

        public bool IsPlaying { get; private set; }

        /// <summary>Сбор закончен: строка-итог «+12 🪙 +4 ⚒ +8 🍎».</summary>
        public event Action<string> Finished;

        private void Awake()
        {
            if (city == null) city = FindFirstObjectByType<CityManager>();
        }

        /// <summary>Сколько кластеров уйдёт в казну в этот ход: по одному на жителя.</summary>
        public int PicksThisTurn => (city != null ? city.Population : 0) + bonusPicks;

        /// <summary>Сколько круток в этот ход: до трёх кластеров (по ресурсу) за крутку.</summary>
        public int SpinsThisTurn => Mathf.CeilToInt(Mathf.Max(1, PicksThisTurn) / 3f);

        /// <summary>Начать сбор хода turn (окно поверх всего экрана).</summary>
        public void Play(int turn)
        {
            if (city == null) return;
            if (routine != null) StopCoroutine(routine);
            picksTotal = Mathf.Max(1, PicksThisTurn);
            picksDone = 0;
            spinsTotal = SpinsThisTurn;
            spinsDone = 0;
            Array.Clear(gained, 0, gained.Length);
            skip = false;
            IsPlaying = true;
            grid = RollGrid();
            if (!EnsureOverlay())
            {
                FinishInstantly();
                return;
            }
            overlay.gameObject.SetActive(true);
            overlay.SetAsLastSibling();
            if (titleText != null) titleText.text = $"Сбор ресурсов · ход {turn}";
            UpdateTexts();
            routine = StartCoroutine(PlayRoutine());
        }

        /// <summary>Досчитать сразу (клик по окну, Enter).</summary>
        public void Skip()
        {
            if (IsPlaying) skip = true;
        }

        public void OnTileClicked(Vector2Int cell) => Skip();
        public void OnTileSwiped(Vector2Int cell, Vector2Int direction) { }

        // =====================================================================
        //  Логика
        // =====================================================================

        private RainSymbol[,] RollGrid()
        {
            var result = new RainSymbol[size, size];
            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++) result[x, y] = RandomSymbol();
            }
            return result;
        }

        /// <summary>Случайный ресурс: доли — по фокусу города.</summary>
        private RainSymbol RandomSymbol()
        {
            float roll = Random.value;
            if ((roll -= city.FocusShare(ResourceType.Gold)) < 0f) return RainSymbol.Gold;
            if ((roll -= city.FocusShare(ResourceType.Production)) < 0f) return RainSymbol.Production;
            return RainSymbol.Food;
        }

        /// <summary>Самый большой кластер каждого ресурса (при равных — случайный из них). Ресурса нет на поле — его нет и в ответе.</summary>
        public static Dictionary<RainSymbol, List<Vector2Int>> LargestClusters(RainSymbol[,] grid)
        {
            int width = grid.GetLength(0), height = grid.GetLength(1);
            var seen = new bool[width, height];
            var best = new Dictionary<RainSymbol, List<Vector2Int>>();
            var ties = new Dictionary<RainSymbol, int>();
            var stack = new Stack<Vector2Int>();
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    RainSymbol symbol = grid[x, y];
                    if (seen[x, y] || symbol == RainSymbol.Empty) continue;
                    var cluster = new List<Vector2Int>();
                    seen[x, y] = true;
                    stack.Push(new Vector2Int(x, y));
                    while (stack.Count > 0)
                    {
                        Vector2Int cell = stack.Pop();
                        cluster.Add(cell);
                        foreach (Vector2Int side in Sides)
                        {
                            Vector2Int next = cell + side;
                            if (next.x < 0 || next.y < 0 || next.x >= width || next.y >= height || seen[next.x, next.y] || grid[next.x, next.y] != symbol) continue;
                            seen[next.x, next.y] = true;
                            stack.Push(next);
                        }
                    }
                    if (!best.TryGetValue(symbol, out List<Vector2Int> current) || cluster.Count > current.Count)
                    {
                        best[symbol] = cluster;
                        ties[symbol] = 1;
                    }
                    else if (cluster.Count == current.Count)
                    {
                        ties[symbol]++;
                        if (Random.Range(0, ties[symbol]) == 0) best[symbol] = cluster; // при равенстве — любой из равных
                    }
                }
            }
            return best;
        }

        /// <summary>Кластер в казну: по 1 за символ + бонус зданий к кластеру. Возвращает, сколько пришло.</summary>
        private int Collect(RainSymbol symbol, int count)
        {
            if (symbol == RainSymbol.Empty) return 0;
            ResourceType resource = ResourceOf(symbol);
            int amount = count + city.ClusterBonus(resource);
            switch (resource)
            {
                case ResourceType.Gold: city.AddGold(amount); break;
                case ResourceType.Production: city.AddProduction(amount); break;
                default: city.AddFood(amount); break;
            }
            gained[(int)resource] += amount;
            return amount;
        }

        /// <summary>
        /// Одна крутка уже выпавшего поля: до трёх кластеров (по одному на оставшегося жителя) — самые большие кластеры
        /// разных ресурсов, крупные первыми. Возвращает взятые кластеры и строку «+4 🪙 +3 🍎».
        /// </summary>
        private Dictionary<RainSymbol, List<Vector2Int>> StepLogic(out string amounts)
        {
            int take = Mathf.Clamp(picksTotal - picksDone, 0, 3);
            picksDone += take;
            List<KeyValuePair<RainSymbol, List<Vector2Int>>> chosen = LargestClusters(grid)
                .OrderByDescending(pair => pair.Value.Count)
                .ThenBy(_ => Random.value) // при равных — любой
                .Take(take)
                .ToList();
            var parts = new List<string>();
            foreach (RainSymbol symbol in new[] { RainSymbol.Gold, RainSymbol.Production, RainSymbol.Food })
            {
                KeyValuePair<RainSymbol, List<Vector2Int>> pick = chosen.FirstOrDefault(p => p.Key == symbol);
                if (pick.Value == null) continue;
                parts.Add($"+{Collect(symbol, pick.Value.Count)} {GameVisuals.ResourceIcon(ResourceOf(symbol))}");
            }
            amounts = string.Join("  ", parts);
            return chosen.ToDictionary(p => p.Key, p => p.Value);
        }

        /// <summary>Досчитать оставшиеся крутки без показа (текущее поле уже выпало — его кластер ещё не взят).</summary>
        private void FinishInstantly()
        {
            for (; spinsDone < spinsTotal; spinsDone++)
            {
                StepLogic(out _);
                grid = RollGrid();
            }
            Complete();
        }

        private void Complete()
        {
            IsPlaying = false;
            routine = null;
            if (overlay != null) overlay.gameObject.SetActive(false);
            Finished?.Invoke(Summary);
        }

        /// <summary>«+12 🪙 +4 ⚒ +8 🍎».</summary>
        public string Summary => string.Join("  ", new[] { ResourceType.Gold, ResourceType.Production, ResourceType.Food }
            .Select(r => $"+{gained[(int)r]} {GameVisuals.ResourceIcon(r)}"));

        // =====================================================================
        //  Показ
        // =====================================================================

        /// <summary>Крутка: все символы новые падают сверху; когда легли — самый большой кластер каждого ресурса светится и уходит в казну.</summary>
        private IEnumerator PlayRoutine()
        {
            float step = Mathf.Clamp(totalDuration / spinsTotal, minStep, maxStep);
            float dropHeight = CellStep() * size;

            while (spinsDone < spinsTotal && !skip)
            {
                ShowGrid(grid, dropHeight);
                yield return WaitOrSkip(step * 0.35f);
                if (skip) break;

                // Выпавшее поле взято (на экране оно ещё видно); следующая крутка — все символы новые
                Dictionary<RainSymbol, List<Vector2Int>> clusters = StepLogic(out string amounts);
                spinsDone++;
                grid = RollGrid();
                if (clusters.Count == 0) continue;
                var set = new HashSet<Vector2Int>(clusters.Values.SelectMany(c => c));
                foreach (TileView view in views) view.SetDimmed(!set.Contains(view.Coords));
                foreach (Vector2Int cell in set) views[cell.x, cell.y].SetHint(true);
                UpdateTexts(amounts);
                yield return WaitOrSkip(step * 0.45f);
                foreach (Vector2Int cell in set) views[cell.x, cell.y].PlayPop(step * 0.2f);
                yield return WaitOrSkip(step * 0.2f);
            }

            if (skip)
            {
                if (spinsDone < spinsTotal) FinishInstantly();
                else Complete();
                yield break;
            }
            foreach (TileView view in views) view.SetDimmed(false);
            UpdateTexts();
            yield return WaitOrSkip(endPause);
            Complete();
        }

        private IEnumerator WaitOrSkip(float seconds)
        {
            float end = Time.unscaledTime + seconds;
            while (Time.unscaledTime < end && !skip) yield return null;
        }

        private static ResourceType ResourceOf(RainSymbol symbol)
        {
            switch (symbol)
            {
                case RainSymbol.Production: return ResourceType.Production;
                case RainSymbol.Food: return ResourceType.Food;
                default: return ResourceType.Gold;
            }
        }

        private static SymbolLook LookFor(RainSymbol symbol) => GameVisuals.LookFor(TileData.ForResource(ResourceOf(symbol)));

        /// <summary>Все символы падают сверху, столбцы — с небольшой задержкой.</summary>
        private void ShowGrid(RainSymbol[,] symbols, float dropHeight)
        {
            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++)
                {
                    TileView view = views[x, y];
                    view.Show(LookFor(symbols[x, y]));
                    view.AnimateFrom(view.Home + Vector2.up * (dropHeight + x * dropHeight * 0.08f));
                }
            }
        }

        private void UpdateTexts(string last = null)
        {
            if (tallyText != null)
            {
                tallyText.text = string.Join("    ", new[] { ResourceType.Gold, ResourceType.Production, ResourceType.Food }
                    .Select(r => $"{GameVisuals.ResourceIcon(r)} +{gained[(int)r]}"));
            }
            if (hintText != null)
            {
                string pace = last != null ? $"<color=#ffd166>{last}</color>   " : string.Empty;
                hintText.text = $"{pace}крутка {Mathf.Min(spinsDone, spinsTotal)}/{spinsTotal} · кластеров {picksDone}/{picksTotal} " +
                                $"<size=80%>(по кластеру на {GameVisuals.IconPopulation} жителя: за крутку — самый большой у каждого ресурса, " +
                                "1 символ = 1 ресурс · клик — сразу итог)</size>";
            }
        }

        private float CellStep() => views != null && size > 1 ? Mathf.Abs(views[1, 0].Home.x - views[0, 0].Home.x) : 100f;

        /// <summary>Окно сбора: затемнение на весь экран, карточка со слотом, итог и подсказка. false — не из чего строить.</summary>
        private bool EnsureOverlay()
        {
            if (overlay != null) return true;
            if (tilePrefab == null)
            {
                MatchBoard board = FindFirstObjectByType<CityGridManager>();
                if (board != null) tilePrefab = board.tilePrefab;
            }
            // Главный экранный канвас (со скейлером), а не мировые подписи боя
            Canvas canvas = FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Select(c => c.GetComponent<Canvas>())
                .FirstOrDefault(c => c != null && c.isRootCanvas && c.renderMode != RenderMode.WorldSpace);
            if (tilePrefab == null || canvas == null) return false;
            if (font == null)
            {
                TMP_Text sample = FindFirstObjectByType<TMP_Text>(FindObjectsInactive.Include);
                if (sample != null) font = sample.font;
            }

            overlay = NewRect("ResourceRain", canvas.transform);
            overlay.anchorMin = Vector2.zero;
            overlay.anchorMax = Vector2.one;
            overlay.offsetMin = overlay.offsetMax = Vector2.zero;
            var dim = overlay.gameObject.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.6f);
            var button = overlay.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(Skip);

            RectTransform card = NewRect("Card", overlay);
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(700f, 860f);
            var cardImage = card.gameObject.AddComponent<Image>();
            cardImage.sprite = GameVisuals.RoundedRect;
            cardImage.type = Image.Type.Sliced;
            cardImage.color = new Color(0.12f, 0.13f, 0.18f, 0.98f);
            cardImage.raycastTarget = false;

            titleText = NewText("Title", card, 44f, new Color(1f, 0.82f, 0.3f), new Vector2(0f, -20f), new Vector2(660f, 64f));
            RectTransform frame = NewRect("Frame", card);
            frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(0.5f, 1f);
            frame.anchoredPosition = new Vector2(0f, -94f);
            frame.sizeDelta = new Vector2(600f, 600f);
            var frameImage = frame.gameObject.AddComponent<Image>();
            frameImage.sprite = GameVisuals.RoundedRect;
            frameImage.type = Image.Type.Sliced;
            frameImage.color = new Color(0.06f, 0.07f, 0.1f, 1f);
            frameImage.raycastTarget = false;
            gridRoot = NewRect("Grid", frame);
            gridRoot.anchorMin = Vector2.zero;
            gridRoot.anchorMax = Vector2.one;
            gridRoot.offsetMin = new Vector2(10f, 10f);
            gridRoot.offsetMax = new Vector2(-10f, -10f);
            gridRoot.gameObject.AddComponent<RectMask2D>();

            tallyText = NewText("Tally", card, 44f, Color.white, new Vector2(0f, -706f), new Vector2(660f, 60f));
            hintText = NewText("Hint", card, 24f, new Color(0.78f, 0.8f, 0.86f), new Vector2(0f, -770f), new Vector2(660f, 70f));
            hintText.enableWordWrapping = true;

            views = new TileView[size, size];
            float area = 580f; // 4×4 — фишки крупные
            float cell = area / size;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    TileView view = Instantiate(tilePrefab, gridRoot);
                    view.Init(this, new Vector2Int(x, y));
                    var home = new Vector2((x - (size - 1) * 0.5f) * cell, ((size - 1) * 0.5f - y) * cell);
                    view.Place(home, cell - 8f, cell * 26f);
                    views[x, y] = view;
                }
            }
            overlay.gameObject.SetActive(false);
            return true;
        }

        private TMP_Text NewText(string name, RectTransform parent, float fontSize, Color color, Vector2 position, Vector2 boxSize)
        {
            RectTransform rect = NewRect(name, parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = boxSize;
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) text.font = font;
            text.fontSize = fontSize;
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
