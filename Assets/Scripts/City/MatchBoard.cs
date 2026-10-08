using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace Casiwar
{
    /// <summary>
    /// Движок поля «3-в-ряд» с ОГРАНИЧЕННЫМ запасом символов и ОГРАНИЧЕННЫМ числом ходов на день.
    ///
    /// • Поле size×size из префабов TileView внутри gridContainer.
    /// • Ход: клик по фишке → клик по соседней, либо свайп. Фишки меняются местами ВСЕГДА, даже если линия
    ///   не сложилась, — так линию можно подготовить за несколько перестановок. Каждая перестановка тратит
    ///   одно действие (каскады — бесплатно).
    /// • Когда символов мало и на поле есть пустые клетки, фишку можно сдвинуть вбок в пустую клетку
    ///   (тоже одно действие): она падает вниз, а фишки, что стояли над ней, опускаются.
    /// • Каскады: собранные фишки лопаются, оставшиеся падают, сверху досыпаются символы из ЗАПАСА.
    ///   Запас кончился — сверху больше ничего не падает, на поле остаются пустые клетки.
    /// • Ходы на поле не гарантируются (кроме стартового расклада), поле само не обновляется. Одним ходом сложить
    ///   нечего — можно переставлять фишки или перемешать поле кнопкой (перемешивания дня дают постройки веры).
    /// • День на поле окончен (событие Exhausted), когда кончились действия или линию уже не сложить никакими
    ///   перестановками (нет трёх подходящих фишек).
    ///
    /// Наследники решают, какие символы выпадают (CreateRandomTile) и что даёт собранная линия (CollectRun).
    /// </summary>
    public abstract class MatchBoard : MonoBehaviour, ITileInputHandler
    {
        [Header("Поле")]
        [Tooltip("UI-область, внутри которой строится поле (квадратный RectTransform). Заполняется кодом.")]
        public RectTransform gridContainer;
        [Tooltip("Префаб клетки с компонентом TileView")]
        public TileView tilePrefab;

        [Header("UI поля (необязательно)")]
        [Tooltip("«Ходы: 7 · запас: 12» — сколько действий осталось и сколько символов ещё упадёт сверху")]
        public TMP_Text reserveText;
        [Tooltip("Подпись кнопки «Авто»")]
        public TMP_Text autoPlayLabel;
        [Tooltip("Кнопка «Перемешать»: доступна, когда сложить нечего и есть перемешивания")]
        public Button reshuffleButton;
        public TMP_Text reshuffleLabel;

        [Header("Вид и анимация")]
        [Tooltip("Зазор между фишками, px канваса")]
        [Min(0f)] public float tileSpacing = 10f;
        [Tooltip("Скорость падения и обмена фишек, клеток в секунду")]
        [Min(1f)] public float tileSpeed = 10f;
        [Min(0.01f)] public float popDuration = 0.18f;
        [Tooltip("Пауза между ходами в режиме «Авто»")]
        [Min(0f)] public float autoPlayDelay = 0.35f;

        protected TileData[,] grid;
        protected TileView[,] views;
        private int size;
        private float step;
        private Vector2 lastContainerSize;
        private Vector2Int? selected;
        private bool busy;
        private bool inputEnabled;
        private bool autoPlay;
        private bool exhausted;
        private int pendingSize;
        private bool pendingReroll;
        private float nextAutoMoveTime;
        private readonly List<TileView> hintedViews = new List<TileView>();

        public int Size => size;
        /// <summary>Сколько символов ещё может упасть сверху за этот день.</summary>
        public int Reserve { get; private set; }
        /// <summary>Сколько удачных ходов ещё можно сделать за день.</summary>
        public int ActionsLeft { get; private set; }
        /// <summary>Сколько раз ещё можно перемешать поле сегодня.</summary>
        public int ReshufflesLeft { get; private set; }
        public int ReshufflesUsed { get; private set; }
        public bool IsBusy => busy;
        public bool AutoPlay => autoPlay;
        public bool IsPhaseActive => inputEnabled;
        /// <summary>День на поле окончен (кончились ходы, или сложить нечего и перемешать нечем).</summary>
        public bool IsExhausted => exhausted;
        /// <summary>Почему день на поле окончен — для сообщения игроку.</summary>
        public string ExhaustReason { get; private set; } = string.Empty;

        /// <summary>День на поле окончен.</summary>
        public event Action Exhausted;

        /// <summary>Фишка в клетке (x — столбец, y — строка сверху вниз). Для отладки и тестов.</summary>
        public TileData GetTile(int x, int y) => grid != null ? grid[x, y] : null;

        private bool CanAcceptInput => inputEnabled && !busy && !exhausted && grid != null && views != null;

        // ---------- Что решают наследники ----------

        /// <summary>Новый случайный символ (монета, юнит, предмет...).</summary>
        protected abstract TileData CreateRandomTile();

        /// <summary>Награда за собранную линию (вызывается до того, как фишки исчезнут).</summary>
        protected abstract void CollectRun(MatchRun run);

        /// <summary>Класс, которому отдаётся приоритет в спорных линиях и подсказках.</summary>
        protected virtual UnitClass PriorityClass => UnitClass.None;

        /// <summary>Сообщение игроку (наследник пересылает в GameManager).</summary>
        protected virtual void ShowMessage(string text) { }

        /// <summary>Обновить собственный UI наследника.</summary>
        protected virtual void OnUiChanged() { }

        protected virtual void Update()
        {
            // Экран/окно поменяли размер — пересчитываем раскладку фишек
            if (views != null && gridContainer != null && gridContainer.rect.size != lastContainerSize)
                LayoutViews();

            if (autoPlay && CanAcceptInput && Time.time >= nextAutoMoveTime)
                AutoMoveOnce();
        }

        // =====================================================================
        //  Управление прокрутом
        // =====================================================================

        /// <summary>Начать день на поле: поле boardSize×boardSize, reserve символов в запасе,
        /// actions удачных ходов и reshuffles перемешиваний.</summary>
        public void BeginPhase(int boardSize, int reserve, int actions, int reshuffles)
        {
            StopAllCoroutines();
            busy = false;
            autoPlay = false;
            exhausted = false;
            ExhaustReason = string.Empty;
            pendingSize = 0;
            pendingReroll = false;
            selected = null;
            Reserve = Mathf.Max(0, reserve);
            ActionsLeft = Mathf.Max(0, actions);
            ReshufflesLeft = Mathf.Max(0, reshuffles);
            ReshufflesUsed = 0;

            BuildBoard(Mathf.Max(MatchRules.MinMatch, boardSize), keepTiles: false);
            inputEnabled = true;
            RefreshAllViews(dropIn: true);
            StartCoroutine(SettleRoutine());
            UpdateUi();
        }

        /// <summary>Закончить прокрут: ввод блокируется.</summary>
        /// <summary>Правила линий изменились (изучили гибрид) — готовые линии на поле складываются сразу.</summary>
        public void CheckNewMatches()
        {
            if (grid == null || !inputEnabled || busy || !isActiveAndEnabled) return;
            if (MatchRules.FindMatches(grid, PriorityClass).Count > 0) StartCoroutine(RefillRoutine());
        }

        public void EndPhase()
        {
            StopAllCoroutines();
            busy = false;
            inputEnabled = false;
            autoPlay = false;
            SetSelected(null);
            ClearHint();
            UpdateUi();
        }

        /// <summary>Добавить символов в запас (например, построили фермы прямо во время прокрута).</summary>
        public void AddReserve(int amount)
        {
            if (amount <= 0) return;
            Reserve += amount;
            if (inputEnabled && !busy && grid != null)
            {
                exhausted = false;
                if (HasHoles()) StartCoroutine(RefillRoutine());
                else if (!MatchRules.HasAnyMove(grid)) HandleNoMoves();
            }
            UpdateUi();
        }

        /// <summary>Увеличить поле. Во время каскада откладывается до его окончания.</summary>
        public void RequestResize(int newSize)
        {
            if (grid == null || newSize <= size) return;
            if (busy) pendingSize = Mathf.Max(pendingSize, newSize);
            else ResizeNow(newSize);
        }

        /// <summary>
        /// Перекатить фишки на поле: каждая занятая клетка получает новый случайный символ (пустые остаются пустыми,
        /// запас и ходы не тратятся). Например, открылся новый класс — его юниты сразу видны. Во время каскада — после него.
        /// </summary>
        public void RerollTiles()
        {
            if (grid == null || !inputEnabled) return;
            if (busy) pendingReroll = true;
            else RerollNow();
        }

        private void RerollNow()
        {
            pendingReroll = false;
            SetSelected(null);
            ClearHint();
            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++)
                {
                    if (grid[x, y] != null) grid[x, y] = CreateRandomTile();
                }
            }
            StabilizeSilently();
            if (ActionsLeft > 0) exhausted = false; // «сложить нечего» могло кончиться — поле проверится заново
            RefreshAllViews(dropIn: true);
            StartCoroutine(SettleRoutine());
            UpdateUi();
        }

        /// <summary>Режим «Авто»: поле само делает лучшие ходы (с учётом приоритета).</summary>
        public void ToggleAutoPlay()
        {
            if (!inputEnabled) return;
            autoPlay = !autoPlay;
            SetSelected(null);
            nextAutoMoveTime = Time.time + 0.1f;
            UpdateUi();
        }

        /// <summary>
        /// Сделать один лучший ход. Одним ходом сложить нечего — «Авто» тратит перемешивание, если оно есть,
        /// иначе делает ход-подготовку (после него линия складывается следующим ходом). Не видит и такого — останавливается.
        /// </summary>
        public void AutoMoveOnce()
        {
            if (!CanAcceptInput) return;
            if (MatchRules.TryFindBestMove(grid, PriorityClass, out MatchRules.Move move))
            {
                StartCoroutine(SwapRoutine(move.A, move.B));
                return;
            }
            if (Reserve > 0 && HasHoles())
            {
                StartCoroutine(RefillRoutine());
                return;
            }
            if (ReshufflesLeft > 0 && TryReshuffleOnce()) return;
            if (exhausted) return;
            if (ActionsLeft >= 2 && MatchRules.TryFindSetupMove(grid, PriorityClass, out MatchRules.Move setup))
            {
                StartCoroutine(SwapRoutine(setup.A, setup.B));
                return;
            }
            autoPlay = false;
            ShowMessage("«Авто» не видит хода — переставьте фишки сами или идите в бой");
            UpdateUi();
        }

        /// <summary>Кнопка «Перемешать»: только когда одним ходом сложить нечего; тратит одно перемешивание дня.</summary>
        public void ReshuffleOnce() => TryReshuffleOnce();

        private bool TryReshuffleOnce()
        {
            if (!inputEnabled || busy || exhausted || grid == null) return false;
            if (ReshufflesLeft <= 0)
            {
                ShowMessage("Перемешиваний нет — их даёт святилище (вера)");
                return false;
            }
            if (MatchRules.HasAnyMove(grid))
            {
                ShowMessage("Ходы ещё есть — перемешать можно, когда одним ходом сложить нечего");
                return false;
            }
            if (!TryReshuffle())
            {
                // Подходящего расклада не нашлось — перемешивание не тратим; переставлять фишки всё ещё можно
                if (!MatchRules.CanEverMatch(grid)) MarkExhausted(Reserve > 0 ? "Сложить нечего" : "Символы кончились");
                else ShowMessage("Перемешать не вышло — подготовьте линию перестановками");
                return false;
            }
            ReshufflesLeft--;
            ReshufflesUsed++;
            ShowMessage($"Поле перемешано (перемешиваний осталось: {ReshufflesLeft})");
            UpdateUi();
            return true;
        }

        // =====================================================================
        //  Ввод от клеток (TileView)
        // =====================================================================

        public void OnTileClicked(Vector2Int cell)
        {
            if (!CanAcceptInput || autoPlay) return;
            bool empty = grid[cell.x, cell.y] == null;

            if (selected == null)
            {
                if (!empty) SetSelected(cell);
                return;
            }

            Vector2Int previous = selected.Value;
            if (previous == cell)
            {
                SetSelected(null);
            }
            else if (AreNeighbors(previous, cell) && MatchRules.IsMove(grid, previous, cell))
            {
                // Обмен с соседней фишкой или сдвиг в соседнюю пустую клетку
                SetSelected(null);
                StartCoroutine(SwapRoutine(previous, cell));
            }
            else
            {
                SetSelected(empty ? (Vector2Int?)null : cell);
            }
        }

        public void OnTileSwiped(Vector2Int cell, Vector2Int direction)
        {
            if (!CanAcceptInput || autoPlay) return;
            Vector2Int other = cell + direction;
            if (grid[cell.x, cell.y] == null || !MatchRules.IsMove(grid, cell, other)) return;
            SetSelected(null);
            StartCoroutine(SwapRoutine(cell, other));
        }

        // =====================================================================
        //  Ход и каскады
        // =====================================================================

        private IEnumerator SwapRoutine(Vector2Int a, Vector2Int b)
        {
            busy = true;
            ClearHint();
            bool slide = MatchRules.IsSlide(grid, a, b);
            yield return AnimateSwap(a, b);
            if (slide)
            {
                // Сдвиг в пустую клетку: фишка падает вниз, фишки над её старым местом опускаются
                ApplyGravityAndRefill();
                yield return WaitForViews();
            }

            // Перестановка остаётся, даже если линия не сложилась: так линию готовят за несколько ходов
            ActionsLeft = Mathf.Max(0, ActionsLeft - 1);
            UpdateUi();
            List<MatchRun> runs = MatchRules.FindMatches(grid, PriorityClass);
            if (runs.Count > 0) yield return ResolveCascade(runs);
            OnBoardSettled();
        }

        private IEnumerator AnimateSwap(Vector2Int a, Vector2Int b)
        {
            MatchRules.Swap(grid, a, b);
            TileView viewA = views[a.x, a.y];
            TileView viewB = views[b.x, b.y];
            viewA.Bind(grid[a.x, a.y]);
            viewA.AnimateFrom(viewB.Home);
            viewB.Bind(grid[b.x, b.y]);
            viewB.AnimateFrom(viewA.Home);
            yield return WaitForViews();
        }

        private IEnumerator ResolveCascade(List<MatchRun> runs)
        {
            int combo = 0;
            while (runs.Count > 0)
            {
                combo++;
                if (combo > 1) ShowMessage($"Каскад ×{combo}!");

                // 1) Награды — пока фишки ещё на месте
                foreach (MatchRun run in runs) CollectRun(run);

                // 2) Собранные фишки «лопаются»
                var cleared = new HashSet<Vector2Int>();
                foreach (MatchRun run in runs)
                {
                    foreach (Vector2Int cell in run.Cells) cleared.Add(cell);
                }
                foreach (Vector2Int cell in cleared) views[cell.x, cell.y].PlayPop(popDuration);
                yield return new WaitForSeconds(popDuration);

                // 3) Удаляем, роняем оставшиеся вниз и досыпаем из запаса
                foreach (Vector2Int cell in cleared) grid[cell.x, cell.y] = null;
                ApplyGravityAndRefill();
                UpdateUi();
                yield return WaitForViews();

                // 4) Новые совпадения после падения → следующий виток каскада
                runs = MatchRules.FindMatches(grid, PriorityClass);
            }
        }

        /// <summary>Фишки падают вниз; пустые клетки сверху заполняются из запаса (пока он есть).</summary>
        private void ApplyGravityAndRefill()
        {
            var lowestEmpty = new int[size]; // после падения пустые клетки колонки — y ∈ [0, lowestEmpty]
            for (int x = 0; x < size; x++)
            {
                // y растёт вниз, поэтому «дно» колонки — y = size - 1
                int write = size - 1;
                for (int y = size - 1; y >= 0; y--)
                {
                    if (grid[x, y] == null) continue;
                    if (y != write)
                    {
                        grid[x, write] = grid[x, y];
                        grid[x, y] = null;
                        TileView view = views[x, write];
                        view.Bind(grid[x, write]);
                        view.AnimateFrom(views[x, y].Home); // падает со своей старой клетки
                    }
                    write--;
                }
                lowestEmpty[x] = write;
                for (int y = write; y >= 0; y--) views[x, y].Bind(null);
            }

            // Досыпаем «слоями» снизу вверх — если запаса мало, он распределится по колонкам равномерно
            for (int layer = 0; layer < size && Reserve > 0; layer++)
            {
                for (int x = 0; x < size && Reserve > 0; x++)
                {
                    int y = lowestEmpty[x] - layer;
                    if (y < 0) continue;
                    grid[x, y] = CreateRandomTile();
                    Reserve--;
                    TileView view = views[x, y];
                    view.Bind(grid[x, y]);
                    view.AnimateFrom(view.Home + Vector2.up * step * (lowestEmpty[x] + 1)); // падают из-за края поля
                }
            }
        }

        private void OnBoardSettled()
        {
            busy = false;

            if (pendingSize > size)
            {
                int newSize = pendingSize;
                pendingSize = 0;
                ResizeNow(newSize);
            }
            else if (pendingReroll)
            {
                RerollNow();
            }
            else if (Reserve > 0 && HasHoles())
            {
                StartCoroutine(RefillRoutine()); // запас пополнили во время каскада
            }
            else if (ActionsLeft <= 0)
            {
                MarkExhausted("Ходы на сегодня кончились");
            }
            else if (!MatchRules.CanEverMatch(grid))
            {
                MarkExhausted(Reserve > 0 ? "Сложить нечего" : "Символы кончились");
            }
            else if (!MatchRules.HasAnyMove(grid))
            {
                HandleNoMoves();
            }

            nextAutoMoveTime = Time.time + autoPlayDelay;
            UpdateUi();
            if (!busy) RefreshHint();
        }

        /// <summary>
        /// Одним ходом сложить нечего: досыпать запас в пустые клетки; иначе поле само НЕ обновляется —
        /// игрок переставляет фишки (линию можно подготовить за несколько ходов) или тратит перемешивание.
        /// «Авто» решает само на следующем шаге (AutoMoveOnce).
        /// </summary>
        private void HandleNoMoves()
        {
            if (Reserve > 0 && HasHoles())
            {
                StartCoroutine(RefillRoutine());
                return;
            }
            if (!MatchRules.CanEverMatch(grid))
            {
                MarkExhausted(Reserve > 0 ? "Сложить нечего" : "Символы кончились");
                return;
            }
            // Перемешать нечем, а за оставшиеся ходы линию не подготовить — день на поле окончен
            if (ReshufflesLeft == 0 && (ActionsLeft <= 1 || (ActionsLeft == 2 && !MatchRules.TryFindSetupMove(grid, PriorityClass, out _))))
            {
                MarkExhausted("Не сложить за оставшиеся ходы");
                return;
            }
            if (!autoPlay)
            {
                ShowMessage(ReshufflesLeft > 0
                    ? $"Одним ходом не сложить — переставьте фишки или перемешайте поле ({ReshufflesLeft})"
                    : "Одним ходом не сложить — подготовьте линию за несколько перестановок");
            }
            UpdateUi();
        }

        private void MarkExhausted(string reason)
        {
            if (exhausted) return;
            ExhaustReason = reason;
            exhausted = true;
            autoPlay = false;
            SetSelected(null);
            ClearHint();
            UpdateUi();
            Exhausted?.Invoke();
        }

        /// <summary>Перемешать имеющиеся фишки (новые не создаются). true — есть ход и нет готовых линий.</summary>
        private bool TryReshuffle()
        {
            var cells = new List<Vector2Int>();
            var tiles = new List<TileData>();
            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++)
                {
                    if (grid[x, y] == null) continue;
                    cells.Add(new Vector2Int(x, y));
                    tiles.Add(grid[x, y]);
                }
            }
            if (tiles.Count < MatchRules.MinMatch) return false;

            var original = new List<TileData>(tiles);
            for (int attempt = 0; attempt < 80; attempt++)
            {
                for (int i = tiles.Count - 1; i > 0; i--)
                {
                    int j = Random.Range(0, i + 1);
                    (tiles[i], tiles[j]) = (tiles[j], tiles[i]);
                }
                for (int i = 0; i < cells.Count; i++) grid[cells[i].x, cells[i].y] = tiles[i];

                if (MatchRules.FindMatches(grid).Count == 0 && MatchRules.HasAnyMove(grid))
                {
                    SetSelected(null);
                    RefreshAllViews(dropIn: true);
                    StartCoroutine(SettleRoutine());
                    return true;
                }
            }

            for (int i = 0; i < cells.Count; i++) grid[cells[i].x, cells[i].y] = original[i];
            return false;
        }

        private IEnumerator RefillRoutine()
        {
            busy = true;
            ClearHint();
            ApplyGravityAndRefill();
            UpdateUi();
            yield return WaitForViews();

            List<MatchRun> runs = MatchRules.FindMatches(grid, PriorityClass);
            if (runs.Count > 0) yield return ResolveCascade(runs);
            OnBoardSettled();
        }

        private IEnumerator SettleRoutine()
        {
            busy = true;
            yield return WaitForViews();
            OnBoardSettled();
        }

        private IEnumerator WaitForViews()
        {
            float timeout = 3f; // страховка от «вечного» ожидания
            while (timeout > 0f && !AllViewsSettled())
            {
                // Поле скрыто (открыта другая вкладка) — анимацию никто не увидит, фишки сразу встают на места
                if (gridContainer != null && !gridContainer.gameObject.activeInHierarchy) SnapViews();
                timeout -= Time.deltaTime;
                yield return null;
            }
        }

        private void SnapViews()
        {
            if (views == null) return;
            foreach (TileView view in views)
            {
                if (view != null) view.AnimateFrom(view.Home);
            }
        }

        private bool AllViewsSettled()
        {
            if (views == null) return true;
            foreach (TileView view in views)
            {
                if (view != null && !view.IsSettled) return false;
            }
            return true;
        }

        private bool HasHoles()
        {
            foreach (TileData tile in grid)
            {
                if (tile == null) return true;
            }
            return false;
        }

        // =====================================================================
        //  Построение поля
        // =====================================================================

        private void ResizeNow(int newSize)
        {
            exhausted = false;
            BuildBoard(newSize, keepTiles: true);
            RefreshAllViews(dropIn: true);
            StartCoroutine(SettleRoutine());
        }

        private void BuildBoard(int newSize, bool keepTiles)
        {
            ClearHint();
            selected = null;

            TileData[,] old = grid;
            int oldSize = size;
            size = newSize;
            grid = new TileData[size, size];
            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++)
                {
                    // Старые фишки остаются внизу слева, новые клетки заполняются бесплатно (бонус за рост поля)
                    int oldY = y - (size - oldSize);
                    bool inOld = keepTiles && old != null && x < oldSize && oldY >= 0 && oldY < oldSize;
                    grid[x, y] = inOld ? old[x, oldY] : CreateRandomTile();
                }
            }
            if (keepTiles) CompactColumns();
            StabilizeSilently();

            if (views == null || views.GetLength(0) != size) RebuildViews();
            else LayoutViews();
        }

        /// <summary>Гравитация без досыпания: пустые клетки уходят наверх.</summary>
        private void CompactColumns()
        {
            for (int x = 0; x < size; x++) MatchRules.DropColumn(grid, x);
        }

        /// <summary>Убрать готовые линии без наград и гарантировать ход (для стартового поля и роста поля).</summary>
        private void StabilizeSilently()
        {
            for (int guard = 0; guard < 200; guard++)
            {
                List<MatchRun> runs = MatchRules.FindMatches(grid);
                if (runs.Count == 0)
                {
                    if (MatchRules.HasAnyMove(grid)) return;
                    for (int x = 0; x < size; x++)
                    {
                        for (int y = 0; y < size; y++)
                        {
                            if (grid[x, y] != null) grid[x, y] = CreateRandomTile(); // тупик — перекатываем
                        }
                    }
                    continue;
                }
                foreach (MatchRun run in runs)
                {
                    foreach (Vector2Int cell in run.Cells) grid[cell.x, cell.y] = CreateRandomTile();
                }
            }
            Debug.LogWarning($"[{name}] Не удалось полностью стабилизировать поле — проверьте набор символов.");
        }

        private void RebuildViews()
        {
            if (views != null)
            {
                foreach (TileView view in views)
                {
                    if (view != null) Destroy(view.gameObject);
                }
            }
            views = null;

            if (gridContainer == null || tilePrefab == null)
            {
                Debug.LogError($"[{name}] Назначьте Grid Container и Tile Prefab в инспекторе.");
                return;
            }

            // Маска, чтобы падающие сверху фишки не вылезали за поле
            if (gridContainer.GetComponent<RectMask2D>() == null)
                gridContainer.gameObject.AddComponent<RectMask2D>();

            views = new TileView[size, size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    TileView view = Instantiate(tilePrefab, gridContainer);
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
            if (side < 1f) side = 900f; // разметка ещё не посчитана
            step = side / size;
            float tileSize = Mathf.Max(8f, step - tileSpacing);

            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++)
                {
                    // Позиция относительно центра контейнера: (0,0) — левый верхний угол поля
                    var home = new Vector2((x - (size - 1) * 0.5f) * step, ((size - 1) * 0.5f - y) * step);
                    views[x, y].Place(home, tileSize, tileSpeed * step);
                }
            }
        }

        private void RefreshAllViews(bool dropIn)
        {
            if (views == null) return;
            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++)
                {
                    TileView view = views[x, y];
                    view.Bind(grid[x, y]);
                    if (dropIn && grid[x, y] != null) view.AnimateFrom(view.Home + Vector2.up * step * size);
                }
            }
        }

        // =====================================================================
        //  Выделение, подсказка, UI
        // =====================================================================

        private void SetSelected(Vector2Int? cell)
        {
            if (selected.HasValue && views != null && MatchRules.InBounds(grid, selected.Value))
                views[selected.Value.x, selected.Value.y].SetSelected(false);

            selected = cell;
            if (cell.HasValue && views != null) views[cell.Value.x, cell.Value.y].SetSelected(true);
        }

        /// <summary>При выбранном приоритете подсвечивает лучший ход, собирающий этот класс.</summary>
        protected void RefreshHint()
        {
            ClearHint();
            if (!CanAcceptInput || PriorityClass == UnitClass.None) return;

            if (MatchRules.TryFindBestMove(grid, PriorityClass, out MatchRules.Move move) && move.HitsPriority)
            {
                AddHint(views[move.A.x, move.A.y]);
                AddHint(views[move.B.x, move.B.y]);
            }
        }

        private void AddHint(TileView view)
        {
            view.SetHint(true);
            hintedViews.Add(view);
        }

        private void ClearHint()
        {
            foreach (TileView view in hintedViews)
            {
                if (view != null) view.SetHint(false);
            }
            hintedViews.Clear();
        }

        protected void UpdateUi()
        {
            if (reserveText != null) reserveText.text = exhausted ? ExhaustReason : $"Ходы: {ActionsLeft} · запас: {Reserve}";
            if (autoPlayLabel != null) autoPlayLabel.text = autoPlay ? "Авто: ВКЛ" : "Авто: ВЫКЛ";
            if (reshuffleLabel != null) reshuffleLabel.text = $"Перемешать ({ReshufflesLeft})";
            if (reshuffleButton != null)
                reshuffleButton.interactable = inputEnabled && !busy && !exhausted && ReshufflesLeft > 0 && grid != null && !MatchRules.HasAnyMove(grid);
            OnUiChanged();
        }

        private static bool AreNeighbors(Vector2Int a, Vector2Int b)
        {
            return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) == 1;
        }
    }
}
