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
    /// <summary>Символ слота трофеев. Оружие и броня одни на всех — чьи они, решает фигура линии.</summary>
    public enum LootSymbol
    {
        Gold,   // монеты
        Weapon, // оружие (меч)
        Armor,  // броня (щит)
    }

    /// <summary>Линия выплат: на каком ряду (0 — верхний) она проходит по каждому барабану и чей по ней трофей.</summary>
    public sealed class LootPayline
    {
        public readonly UnitClass Class;
        public readonly int[] Rows;

        public LootPayline(UnitClass unitClass, params int[] rows)
        {
            Class = unitClass;
            Rows = rows;
        }
    }

    /// <summary>Выигрыш: 3+ одинаковых символа подряд по линии выплат, считая с левого барабана.</summary>
    public sealed class LootLine
    {
        /// <summary>Индекс в LootSlotMachine.Paylines.</summary>
        public int Payline;
        public int Length;
        public LootSymbol Symbol;

        /// <summary>Чей трофей — по фигуре линии: прямые — воинам, углы — лучникам, зигзаги — жрецам.</summary>
        public UnitClass Class => LootSlotMachine.Paylines[Payline].Class;

        /// <summary>Уровень предмета: 3 подряд → I, 4 → II, 5 → III.</summary>
        public int Tier => Mathf.Clamp(Length - 2, 1, ItemData.MaxTier);

        /// <summary>Клетки выигрыша [барабан, ряд].</summary>
        public IEnumerable<Vector2Int> Cells
        {
            get
            {
                int[] rows = LootSlotMachine.Paylines[Payline].Rows;
                for (int x = 0; x < Length; x++) yield return new Vector2Int(x, rows[x]);
            }
        }
    }

    /// <summary>
    /// «Трофеи» после победы — слот 5 барабанов × 3 ряда с линиями выплат.
    /// • Круток столько, сколько звёзд у выживших в бою юнитов (★ — 1, ★★ — 2, ★★★ — 3).
    /// • Символы одни на всех: монеты, оружие (меч) и броня (щит).
    /// • Выигрыш — 3+ одинаковых символа подряд по линии, считая с левого барабана: 3 → уровень I, 4 → II, 5 → III.
    /// • Чей трофей — решает фигура линии: прямые — воинам, углы — лучникам, зигзаги — жрецам
    ///   (легенда линий — над барабанами). Предмет сам надевается на подходящего юнита, иначе — на склад;
    ///   монеты по любой линии → золото (каждая — как монета на поле города).
    /// </summary>
    public class LootSlotMachine : MonoBehaviour
    {
        public const int Reels = 5;
        public const int Rows = 3;

        /// <summary>Линии выплат: по три на класс.</summary>
        public static readonly LootPayline[] Paylines =
        {
            // Воины — прямые (шеренга)
            new LootPayline(UnitClass.Warrior, 0, 0, 0, 0, 0),
            new LootPayline(UnitClass.Warrior, 1, 1, 1, 1, 1),
            new LootPayline(UnitClass.Warrior, 2, 2, 2, 2, 2),
            // Лучники — углы (наконечник стрелы и её полёт)
            new LootPayline(UnitClass.Archer, 0, 1, 2, 1, 0),
            new LootPayline(UnitClass.Archer, 2, 1, 0, 1, 2),
            new LootPayline(UnitClass.Archer, 0, 0, 1, 2, 2),
            // Жрецы — зигзаги
            new LootPayline(UnitClass.Priest, 1, 0, 1, 0, 1),
            new LootPayline(UnitClass.Priest, 1, 2, 1, 2, 1),
            new LootPayline(UnitClass.Priest, 2, 1, 2, 1, 2),
        };

        public static readonly UnitClass[] LootClasses = { UnitClass.Warrior, UnitClass.Archer, UnitClass.Priest };

        public static readonly Color WeaponColor = new Color(0.72f, 0.50f, 0.30f);
        public static readonly Color ArmorColor = new Color(0.42f, 0.50f, 0.62f);

        /// <summary>Фигуры линий класса: «прямые», «углы», «зигзаги».</summary>
        public static string ShapeName(UnitClass unitClass)
        {
            switch (unitClass)
            {
                case UnitClass.Warrior: return "прямые";
                case UnitClass.Archer: return "углы";
                case UnitClass.Priest: return "зигзаги";
                default: return string.Empty;
            }
        }

        /// <summary>Кому трофей: «Воинам», «Лучникам», «Жрецам».</summary>
        public static string Recipients(UnitClass unitClass)
        {
            switch (unitClass)
            {
                case UnitClass.Warrior: return "Воинам";
                case UnitClass.Archer: return "Лучникам";
                case UnitClass.Priest: return "Жрецам";
                default: return string.Empty;
            }
        }

        [Header("Системы")]
        public BenchManager bench;
        public GameManager game;
        public CityManager city;
        [Tooltip("Оружие и броня классов (по одному каждого вида на класс)")]
        public List<ItemData> itemPool = new List<ItemData>();

        [Header("UI")]
        [Tooltip("Область барабанов (5×3). Заполняется клетками из префаба.")]
        public RectTransform gridContainer;
        public TileView cellPrefab;
        [Tooltip("«Круток: 3»")]
        public TMP_Text spinsText;
        [Tooltip("Подпись кнопки «Авто»")]
        public TMP_Text autoLabel;
        [Tooltip("Что выпало за эти трофеи")]
        public TMP_Text logText;
        [Min(1)] public int logLines = 6;

        [Header("Символы (веса)")]
        [Min(0f)] public float goldWeight = 1.2f;
        [Min(0f)] public float weaponWeight = 1f;
        [Min(0f)] public float armorWeight = 1f;

        [Header("Анимация")]
        [Min(0f)] public float tileSpacing = 10f;
        [Tooltip("Скорость падения символов, клеток в секунду")]
        [Min(1f)] public float dropSpeed = 16f;
        [Tooltip("Барабаны останавливаются слева направо с этим отставанием (в клетках)")]
        [Min(0f)] public float reelDelay = 0.9f;
        [Tooltip("Пауза после крутки (подсветка линий)")]
        [Min(0f)] public float showDuration = 0.8f;
        [Tooltip("Толщина нарисованной линии выигрыша")]
        [Min(1f)] public float lineThickness = 12f;

        private TileView[,] views;
        private RectTransform linesRoot;
        private float step;
        private bool autoSpin;
        private readonly List<string> log = new List<string>();

        public int SpinsLeft { get; private set; }
        public bool IsSpinning { get; private set; }
        public bool AutoSpin => autoSpin;
        public LootSymbol[,] LastGrid { get; private set; }
        public List<LootLine> LastLines { get; private set; } = new List<LootLine>();

        /// <summary>Крутки кончились.</summary>
        public event Action Finished;

        private void Awake()
        {
            if (bench == null) bench = FindFirstObjectByType<BenchManager>();
            if (game == null) game = FindFirstObjectByType<GameManager>();
            if (city == null) city = FindFirstObjectByType<CityManager>();
        }

        private void Update()
        {
            if (autoSpin && !IsSpinning && SpinsLeft > 0) Spin();
        }

        // =====================================================================
        //  Управление
        // =====================================================================

        /// <summary>Начать трофеи: spins круток (по звёздам выживших юнитов).</summary>
        public void BeginLoot(int spins)
        {
            StopAllCoroutines();
            IsSpinning = false;
            autoSpin = false;
            SpinsLeft = Mathf.Max(0, spins);
            log.Clear();
            BuildViews();
            ClearPaylines();
            ShowIdle();
            RefreshLog();
            UpdateUi();
        }

        public void EndLoot()
        {
            StopAllCoroutines();
            IsSpinning = false;
            autoSpin = false;
            UpdateUi();
        }

        /// <summary>Кнопка «Крутить».</summary>
        public void Spin()
        {
            if (IsSpinning || SpinsLeft <= 0 || views == null) return;
            StartCoroutine(SpinRoutine());
        }

        /// <summary>Кнопка «Авто»: докрутить всё само.</summary>
        public void ToggleAutoSpin()
        {
            autoSpin = !autoSpin && SpinsLeft > 0;
            UpdateUi();
        }

        // =====================================================================
        //  Крутка
        // =====================================================================

        private IEnumerator SpinRoutine()
        {
            IsSpinning = true;
            SpinsLeft--;
            ClearPaylines();
            UpdateUi();

            LootSymbol[,] grid = Roll(goldWeight, weaponWeight, armorWeight);
            LastGrid = grid;

            // Барабаны: символы падают сверху, левые останавливаются раньше правых
            for (int x = 0; x < Reels; x++)
            {
                for (int y = 0; y < Rows; y++)
                {
                    TileView view = views[x, y];
                    view.Show(LookFor(grid[x, y]));
                    view.SetMoveSpeed(dropSpeed * step);
                    view.AnimateFrom(view.Home + Vector2.up * step * (Rows + x * reelDelay));
                }
            }
            float timeout = 4f;
            while (timeout > 0f && !AllSettled())
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            // Линии: оружие и броня окрашиваются в цвет класса, которому трофей (монеты ничьи — остаются золотыми),
            // сама линия рисуется поверх
            List<LootLine> lines = FindLines(grid);
            LastLines = lines;
            var winning = new HashSet<Vector2Int>();
            var owners = new Dictionary<Vector2Int, UnitClass>();
            foreach (LootLine line in lines)
            {
                foreach (Vector2Int cell in line.Cells)
                {
                    winning.Add(cell);
                    if (line.Symbol != LootSymbol.Gold) owners[cell] = line.Class;
                }
            }
            for (int x = 0; x < Reels; x++)
            {
                for (int y = 0; y < Rows; y++)
                {
                    var cell = new Vector2Int(x, y);
                    bool on = winning.Contains(cell);
                    if (owners.TryGetValue(cell, out UnitClass owner))
                    {
                        SymbolLook look = LookFor(grid[x, y]);
                        look.color = Color.Lerp(look.color, GameVisuals.ClassColor(owner), 0.75f);
                        views[x, y].Show(look);
                    }
                    views[x, y].SetHint(on);
                    views[x, y].SetDimmed(lines.Count > 0 && !on);
                }
            }
            foreach (LootLine line in lines) DrawPayline(line);
            if (lines.Count == 0) AddLog("Пусто");
            foreach (LootLine line in lines) Award(line);

            float shown = 0f;
            while (shown < showDuration)
            {
                shown += Time.deltaTime;
                yield return null;
            }
            IsSpinning = false;
            if (SpinsLeft <= 0)
            {
                autoSpin = false;
                Finished?.Invoke();
            }
            UpdateUi();
        }

        private void Award(LootLine line)
        {
            if (line.Symbol == LootSymbol.Gold)
            {
                int gold = line.Length * (city != null ? city.GoldPerCoin : 1);
                if (game != null) game.AddGold(gold);
                AddLog($"+{gold} {GameVisuals.IconGold}");
                return;
            }
            ItemData item = ItemFor(line.Class, line.Symbol);
            if (item == null) return;
            string result = bench != null ? bench.AddItem(item, line.Tier) : item.Describe(line.Tier);
            AddLog($"{Recipients(line.Class)} ({ShapeName(line.Class)}): {result}");
        }

        /// <summary>Предмет класса для символа (оружие или броня).</summary>
        public ItemData ItemFor(UnitClass unitClass, LootSymbol symbol)
        {
            ItemSlot slot = symbol == LootSymbol.Armor ? ItemSlot.Armor : ItemSlot.Weapon;
            return itemPool.FirstOrDefault(i => i != null && i.unitClass == unitClass && i.slot == slot);
        }

        // =====================================================================
        //  Логика (чистые функции — их проверяют тесты)
        // =====================================================================

        /// <summary>Случайный расклад барабанов [reel, row].</summary>
        public static LootSymbol[,] Roll(float goldWeight, float weaponWeight, float armorWeight)
        {
            var grid = new LootSymbol[Reels, Rows];
            float total = Mathf.Max(0.0001f, goldWeight + weaponWeight + armorWeight);
            for (int x = 0; x < Reels; x++)
            {
                for (int y = 0; y < Rows; y++)
                {
                    float roll = Random.value * total;
                    grid[x, y] = roll < goldWeight ? LootSymbol.Gold : roll < goldWeight + weaponWeight ? LootSymbol.Weapon : LootSymbol.Armor;
                }
            }
            return grid;
        }

        /// <summary>Выигрыши: по каждой линии выплат — 3+ одинаковых символа подряд с левого барабана.</summary>
        public static List<LootLine> FindLines(LootSymbol[,] grid)
        {
            var lines = new List<LootLine>();
            int reels = grid.GetLength(0);
            for (int index = 0; index < Paylines.Length; index++)
            {
                int[] rows = Paylines[index].Rows;
                int count = Mathf.Min(reels, rows.Length);
                if (count == 0) continue;
                LootSymbol symbol = grid[0, rows[0]];
                int length = 1;
                while (length < count && grid[length, rows[length]] == symbol) length++;
                if (length >= 3) lines.Add(new LootLine { Payline = index, Length = length, Symbol = symbol });
            }
            return lines;
        }

        // =====================================================================
        //  Вид
        // =====================================================================

        /// <summary>Вид символа: одинаковый для всех классов (монета, меч, щит).</summary>
        public static SymbolLook LookFor(LootSymbol symbol)
        {
            switch (symbol)
            {
                case LootSymbol.Gold:
                    return GameVisuals.LookFor(TileData.ForResource(ResourceType.Gold));
                case LootSymbol.Weapon:
                    return new SymbolLook { color = WeaponColor, shape = GameVisuals.RoundedRect, sliced = true, icon = GameVisuals.Sword, label = string.Empty };
                default:
                    return new SymbolLook { color = ArmorColor, shape = GameVisuals.RoundedRect, sliced = true, icon = GameVisuals.Shield, label = string.Empty };
            }
        }

        private void BuildViews()
        {
            if (views != null || gridContainer == null || cellPrefab == null) return;
            Rect rect = gridContainer.rect;
            step = Mathf.Min(rect.width / Reels, rect.height / Rows);
            if (step < 1f) step = 170f;
            float tileSize = Mathf.Max(8f, step - tileSpacing);

            views = new TileView[Reels, Rows];
            for (int y = 0; y < Rows; y++)
            {
                for (int x = 0; x < Reels; x++)
                {
                    TileView view = Instantiate(cellPrefab, gridContainer);
                    view.Init(null, new Vector2Int(x, y));
                    var home = new Vector2((x - (Reels - 1) * 0.5f) * step, ((Rows - 1) * 0.5f - y) * step);
                    view.Place(home, tileSize, dropSpeed * step);
                    views[x, y] = view;
                }
            }

            // Слой для нарисованных линий выигрыша — поверх символов
            linesRoot = new GameObject("Paylines", typeof(RectTransform)).GetComponent<RectTransform>();
            linesRoot.SetParent(gridContainer, false);
            linesRoot.anchorMin = Vector2.zero;
            linesRoot.anchorMax = Vector2.one;
            linesRoot.offsetMin = Vector2.zero;
            linesRoot.offsetMax = Vector2.zero;
            linesRoot.SetAsLastSibling();
        }

        /// <summary>Линия выигрыша поверх барабанов — цветом класса, которому трофей (монеты — золотом).</summary>
        private void DrawPayline(LootLine line)
        {
            if (linesRoot == null || views == null) return;
            Color color = line.Symbol == LootSymbol.Gold ? GameVisuals.GoldColor : GameVisuals.ClassColor(line.Class);
            color.a = 0.9f;
            Vector2Int[] cells = line.Cells.ToArray();
            for (int i = 1; i < cells.Length; i++)
            {
                Vector2 from = views[cells[i - 1].x, cells[i - 1].y].Home;
                Vector2 to = views[cells[i].x, cells[i].y].Home;
                Vector2 delta = to - from;

                var segment = new GameObject("Segment", typeof(RectTransform), typeof(Image));
                var rect = (RectTransform)segment.transform;
                rect.SetParent(linesRoot, false);
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(delta.magnitude + lineThickness, lineThickness);
                rect.anchoredPosition = (from + to) * 0.5f;
                rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
                var image = segment.GetComponent<Image>();
                image.color = color;
                image.raycastTarget = false;
            }
        }

        private void ClearPaylines()
        {
            if (linesRoot == null) return;
            for (int i = linesRoot.childCount - 1; i >= 0; i--) Destroy(linesRoot.GetChild(i).gameObject);
        }

        /// <summary>До первой крутки барабаны показывают по символу каждого вида — видно, что где падает.</summary>
        private void ShowIdle()
        {
            if (views == null) return;
            for (int x = 0; x < Reels; x++)
            {
                for (int y = 0; y < Rows; y++)
                {
                    views[x, y].Show(LookFor((LootSymbol)((x + y) % 3)));
                    views[x, y].SetDimmed(true);
                }
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

        private void AddLog(string line)
        {
            log.Add(line);
            while (log.Count > logLines) log.RemoveAt(0);
            RefreshLog();
        }

        private void RefreshLog()
        {
            if (logText != null)
                logText.text = log.Count > 0 ? string.Join("\n", log) : "Жмите «Крутить»: 3+ одинаковых подряд по линии с левого барабана — трофей";
        }

        private void UpdateUi()
        {
            if (spinsText != null) spinsText.text = SpinsLeft > 0 || IsSpinning ? $"Круток: {SpinsLeft}" : "Крутки кончились";
            if (autoLabel != null) autoLabel.text = autoSpin ? "Авто: ВКЛ" : "Авто: ВЫКЛ";
        }
    }
}
