using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Casiwar
{
    /// <summary>Предмет снаряжения с уровнем (I–III).</summary>
    [System.Serializable]
    public class EquippedItem
    {
        public ItemData data;
        [Range(1, ItemData.MaxTier)] public int tier = 1;

        public EquippedItem() { }

        public EquippedItem(ItemData data, int tier)
        {
            this.data = data;
            this.tier = Mathf.Clamp(tier, 1, ItemData.MaxTier);
        }

        public int Score => data != null ? data.Score(tier) : 0;
        public string Name => data != null ? $"{data.itemName} {ItemData.TierName(tier)}" : string.Empty;

        public static bool IsValid(EquippedItem item) => item != null && item.data != null;
    }

    /// <summary>
    /// Юнит армии: тип, звёзды (три одинаковых одной звёздности → один звездой выше), уровень и опыт
    /// (растут в боях) и надетое снаряжение. Погиб в бою — его больше нет.
    /// </summary>
    [System.Serializable]
    public class BenchUnit
    {
        public UnitData data;
        [Range(1, UnitData.MaxStars)] public int stars = 1;
        [Range(1, UnitData.MaxLevel)] public int level = 1;
        [Min(0)] public int xp;
        public EquippedItem armor;
        public EquippedItem weapon;
        /// <summary>Черты богатыря (из каких символов сложена его линия); у остальных не используются.</summary>
        public HeroTraits hero;

        public BenchUnit() { }

        public BenchUnit(UnitData data, int stars = 1, int level = 1)
        {
            this.data = data;
            this.stars = Mathf.Clamp(stars, 1, UnitData.MaxStars);
            this.level = Mathf.Clamp(level, 1, UnitData.MaxLevel);
        }

        public EquippedItem GetItem(ItemSlot slot) => slot == ItemSlot.Armor ? armor : weapon;

        public void SetItem(ItemSlot slot, EquippedItem item)
        {
            if (slot == ItemSlot.Armor) armor = item;
            else weapon = item;
        }

        public int ItemArmor => EquippedItem.IsValid(armor) ? armor.data.GetArmor(armor.tier) : 0;
        public int ItemAttack => EquippedItem.IsValid(weapon) ? weapon.data.GetAttack(weapon.tier) : 0;

        public bool IsHero => data != null && data.unitClass == UnitClass.Hero && hero != null;
        /// <summary>Богатырь (или вождь варваров): может вести отряд, не сливается с другими.</summary>
        public bool IsHeroClass => data != null && data.unitClass == UnitClass.Hero;
        public int HeroArmor => IsHero ? hero.BonusArmor : 0;
        public int MaxHp => Mathf.RoundToInt(data.GetMaxHp(stars, level) * (IsHero ? hero.HpMultiplier : 1f));
        public int Attack => Mathf.RoundToInt(data.GetAttack(stars, level) * (IsHero ? hero.AttackMultiplier : 1f)) + ItemAttack;
        public int Heal => data.GetHeal(stars, level);
        public int SellPrice => data.GetSellPrice(stars, level);
        public int XpToNext => UnitData.XpToNext(level);
        public bool IsMaxLevel => level >= UnitData.MaxLevel;

        /// <summary>Вожак отряда: +доля ХП и атаки всем остальным в его отряде.</summary>
        public float LeadHp => IsHeroClass ? HeroTraits.LeadHp(hero, stars) : 0f;
        public float LeadAttack => IsHeroClass ? HeroTraits.LeadAttack(hero, stars) : 0f;
        /// <summary>«+24% ХП, +16% атаки».</summary>
        public string LeadText => $"+{Mathf.RoundToInt(LeadHp * 100f)}% {GameVisuals.IconHp}, +{Mathf.RoundToInt(LeadAttack * 100f)}% {GameVisuals.IconAttack}";

        /// <summary>Уровень с дробной частью — опытом к следующему (3.5 — полпути к 4-му).</summary>
        public float LevelValue => IsMaxLevel ? UnitData.MaxLevel : level + Mathf.Clamp01(xp / (float)XpToNext);

        /// <summary>«Воин ★★ ур. 3».</summary>
        public string Title => $"{Name} {GameVisuals.Stars(stars)} ур. {level}";
        /// <summary>Имя; у богатыря — с прозвищем по чертам («Богатырь-витязь»).</summary>
        public string Name => IsHero && hero.Nickname.Length > 0 ? $"{data.unitName}-{hero.Nickname}" : data.unitName;

        /// <summary>Задать уровень с дробной частью: целое — уровень, дробь — опыт к следующему.</summary>
        public void SetLevelValue(float value)
        {
            value = Mathf.Clamp(value, 1f, UnitData.MaxLevel);
            level = Mathf.Clamp(Mathf.FloorToInt(value + 0.0001f), 1, UnitData.MaxLevel);
            xp = IsMaxLevel ? 0 : Mathf.Clamp(Mathf.RoundToInt((value - level) * XpToNext), 0, XpToNext - 1);
        }

        /// <summary>Получить опыт. Возвращает, сколько уровней набрано.</summary>
        public int AddXp(int amount)
        {
            if (IsMaxLevel) return 0;
            xp += Mathf.Max(0, amount);
            int gained = 0;
            while (!IsMaxLevel && xp >= XpToNext)
            {
                xp -= XpToNext;
                level++;
                gained++;
            }
            if (IsMaxLevel) xp = 0;
            return gained;
        }
    }

    /// <summary>Итог боя для одного юнита игрока.</summary>
    public struct UnitBattleResult
    {
        public BenchUnit Unit;
        public bool Alive;
        public int Kills;
    }


    /// <summary>
    /// Линейка армии, как в «Героях»: слот вожака (только богатырь) и места бойцов. У гарнизона и у каждого отряда —
    /// своя, одинаковая.
    /// </summary>
    public sealed class ArmyRow
    {
        /// <summary>Индекс слота вожака.</summary>
        public const int LeaderIndex = -1;

        public BenchUnit Leader;
        public readonly BenchUnit[] Slots;

        public ArmyRow(int slots) => Slots = new BenchUnit[Mathf.Max(1, slots)];

        /// <summary>Слот по индексу: LeaderIndex — вожак, 0… — бойцы.</summary>
        public BenchUnit this[int index]
        {
            get => index == LeaderIndex ? Leader : index >= 0 && index < Slots.Length ? Slots[index] : null;
            set
            {
                if (index == LeaderIndex) Leader = value;
                else if (index >= 0 && index < Slots.Length) Slots[index] = value;
            }
        }

        /// <summary>Вожак (если есть), потом бойцы по порядку слотов — это и строй в бою.</summary>
        public IEnumerable<BenchUnit> Units
        {
            get
            {
                if (Leader != null) yield return Leader;
                foreach (BenchUnit unit in Slots)
                {
                    if (unit != null) yield return unit;
                }
            }
        }

        public int Count => (Leader != null ? 1 : 0) + Slots.Count(u => u != null);
        public int Capacity => Slots.Length + 1;
        public bool IsEmpty => Count == 0;
        public bool HasFreeSlot => FirstFree().HasValue;

        /// <summary>Первое свободное место бойца (не вожака); null — мест нет.</summary>
        public int? FirstFree()
        {
            int index = Array.IndexOf(Slots, null);
            return index >= 0 ? index : (int?)null;
        }

        /// <summary>Где стоит этот боец (LeaderIndex — вожак); null — его тут нет.</summary>
        public int? IndexOf(BenchUnit unit)
        {
            if (unit == null) return null;
            if (Leader == unit) return LeaderIndex;
            int index = Array.IndexOf(Slots, unit);
            return index >= 0 ? index : (int?)null;
        }

        public void Clear()
        {
            Leader = null;
            Array.Clear(Slots, 0, Slots.Length);
        }

        /// <summary>Поменять местами всё содержимое двух линеек (как обмен героями в «Героях»).</summary>
        public void SwapWith(ArmyRow other)
        {
            (Leader, other.Leader) = (other.Leader, Leader);
            for (int i = 0; i < Mathf.Min(Slots.Length, other.Slots.Length); i++) (Slots[i], other.Slots[i]) = (other.Slots[i], Slots[i]);
        }
    }

    /// <summary>Строка панели армии: сверху — гарнизон (или встреченный отряд), снизу — отряд.</summary>
    public enum PanelRow
    {
        Top,
        Bottom,
    }

    /// <summary>
    /// Армия — как в «Героях 3». Линейки одинаковые: слот вожака (только богатырь) + rowSlots мест.
    /// • Гарнизон стоит в городе и защищает его. Сюда приходят новобранцы с поля армии (линия 4 — 2-й уровень,
    ///   5+ — 3-й); гарнизон полон — новобранец продаётся.
    /// • Отряды (до WorldPowers.maxSquads) — фигурки на карте: ходят, нападают на отряды и лагеря, в границах помогают
    ///   защищать город. Отряд на ратуше меняется бойцами с гарнизоном; ушёл в поле — на ратуше собирают следующий
    ///   (пустой отряд всегда стоит там, пока отрядов меньше предела). Два своих отряда рядом на карте меняются бойцами
    ///   между собой — как встреча героев.
    /// • Панель: сверху — гарнизон (или встреченный отряд), снизу — отряд (выбранный на карте, иначе тот, что на ратуше).
    ///   Клик по бойцу, потом по слоту — переставить (поменять местами); бойца можно и перетащить. Подпись линейки
    ///   («Гарнизон», «Отряд 2») тянут на другую линейку — переходят все бойцы; места нет совсем — линейки меняются местами.
    /// • Вожак (слот с золотой рамкой, только богатырь) — вся его линейка +8% ХП и атаки за каждую его ★ (и бонусы
    ///   от его линии). Богатырей может быть до maxHeroes.
    /// • Как в автобатлере: три одинаковых бойца одной звёздности сами сливаются в одного звездой выше
    ///   (★ → ★★ → ★★★, каскадом; уровень — средний). Отряд на ратуше сливается вместе с гарнизоном, в поле — сам с собой.
    ///   Богатыри не сливаются.
    /// • Выжившие в бою получают опыт и растут до 10-го уровня; погибшие пропадают навсегда.
    /// • «Продать» — выбранного бойца за золото. Снаряжение: трофей сам надевается на подходящего бойца; ненужное сразу продаётся.
    /// Порядок слотов = строй в бою: первые стоят в передней шеренге (вожак — первым).
    /// Обе линейки строятся кодом внутри панели скамейки — сцену пересобирать не надо.
    /// </summary>
    public class BenchManager : MonoBehaviour
    {
        private struct SlotRef
        {
            public ArmyRow Row;
            public int Index;

            public SlotRef(ArmyRow row, int index)
            {
                Row = row;
                Index = index;
            }
        }

        /// <summary>Что показано в панели: сверху — встреченный отряд или гарнизон (null), снизу — отряд, и можно ли меняться.</summary>
        private struct PanelView
        {
            public PlayerArmy Top;
            public PlayerArmy Bottom;
            public bool Together;

            public bool Same(PanelView other) => Top == other.Top && Bottom == other.Bottom && Together == other.Together;
        }

        [Header("Ссылки")]
        [Tooltip("Строка слотов (HorizontalLayoutGroup): в ней — гарнизон, строка отряда создаётся под ней. Заполняется кодом.")]
        public RectTransform slotContainer;
        [Tooltip("Префаб слота с компонентом BenchSlotView")]
        public BenchSlotView slotPrefab;
        public GameManager game;

        [Header("UI (необязательно)")]
        [Tooltip("Описание выбранного юнита / подсказка")]
        public TMP_Text infoText;
        [Tooltip("Кнопка «Продать» — активна, только когда выбран юнит")]
        public Button sellButton;
        [Tooltip("Устарела: слияние теперь автоматическое (кнопка из старых сцен прячется)")]
        public Button mergeButton;

        [Header("Линейки (гарнизон и отряды одинаковые)")]
        [Tooltip("Мест в линейке, кроме слота вожака, — у гарнизона и у каждого отряда (как в «Героях»: 7)")]
        [Min(1)] public int rowSlots = 7;
        [Tooltip("Сколько богатырей может быть сразу")]
        [Min(1)] public int maxHeroes = 3;
        [Tooltip("Слоты в панели не крупнее префаба × это (две линейки должны влезть)")]
        [Range(0.4f, 1f)] public float slotScale = 0.76f;

        [Header("Опыт за бой (выжившим)")]
        [Min(0)] public int xpPerBattle = 2;
        [Min(0)] public int xpPerKill = 1;
        [Min(0)] public int xpForVictory = 1;

        private const float LabelWidth = 104f;
        private const float RowSpacing = 6f;
        private const float RowPadding = 16f;
        private const float TitleHeight = 36f;
        private static readonly Color LabelPlate = new Color(0.30f, 0.34f, 0.46f, 0.9f);
        private static readonly Color LabelPlateDrag = new Color(0.62f, 0.5f, 0.2f, 0.95f);

        private ArmyRow garrison;
        /// <summary>Без карты (WorldPowers нет) — один отряд, всегда «на ратуше».</summary>
        private PlayerArmy fallbackSquad;
        /// <summary>Какой отряд показан снизу (выбран на карте); null — тот, что на ратуше.</summary>
        private PlayerArmy focus;
        private readonly List<EquippedItem> stash = new List<EquippedItem>();
        private ArmyRow selectedRow;
        private int selectedIndex;
        private bool interactable = true;
        private bool initialized;
        private PlayerArmy lastHome;
        private PanelView lastView;

        /// <summary>Слоты строк панели: [строка][индекс + 1], 0 — вожак.</summary>
        private readonly BenchSlotView[][] slotViews = { new BenchSlotView[0], new BenchSlotView[0] };
        private readonly RectTransform[] rowRects = new RectTransform[2];
        private readonly TMP_Text[] rowLabels = new TMP_Text[2];
        private readonly Image[] rowPlates = new Image[2];
        private readonly List<RectTransform> holders = new List<RectTransform>();
        private RectTransform column;
        private TMP_Text titleText;
        private float laidOutWidth = -1f;

        // Перетаскивание: боец (слот) или вся линейка (подпись)
        private bool dragging;
        private bool dragWholeRow;
        private PanelRow dragRow;
        private int dragIndex;
        private RectTransform dragGhost;
        private Image dragGhostFigure;
        private TMP_Text dragGhostText;

        /// <summary>Состав армии поменялся (перестановка, новобранец, слияние, итоги боя).</summary>
        public event Action Changed;

        private WorldPowers Powers => game != null ? game.powers : null;

        public ArmyRow GarrisonRow
        {
            get
            {
                EnsureInitialized();
                return garrison;
            }
        }

        /// <summary>Все наши отряды (и пустой на ратуше, который собирают из гарнизона).</summary>
        public IEnumerable<PlayerArmy> Squads
        {
            get
            {
                WorldPowers powers = Powers;
                if (powers != null) return powers.AllArmies;
                if (fallbackSquad == null) fallbackSquad = new PlayerArmy(rowSlots) { Number = 1 };
                return new[] { fallbackSquad };
            }
        }

        /// <summary>Отряд на ратуше: стоящий там или новый пустой; null — все отряды в поле и новый собрать нельзя.</summary>
        public PlayerArmy HomeSquad => Powers != null ? Powers.HomeArmy : Squads.First();

        public IEnumerable<BenchUnit> GarrisonUnits => GarrisonRow.Units;
        /// <summary>Бойцы всех отрядов.</summary>
        public IEnumerable<BenchUnit> SquadUnits => Squads.SelectMany(s => s.Row.Units);
        /// <summary>Все бойцы: отряды, потом гарнизон.</summary>
        public IEnumerable<BenchUnit> AllUnits => SquadUnits.Concat(GarrisonUnits);
        private IEnumerable<ArmyRow> AllRows => Squads.Select(s => s.Row).Append(GarrisonRow);

        public int GarrisonCount => GarrisonRow.Count;
        public int UnitCount => AllUnits.Count();

        /// <summary>Снаряжение, которое сейчас некому надеть (временно: лишнее сразу продаётся).</summary>
        public IReadOnlyList<EquippedItem> Stash => stash;

        /// <summary>Сколько золота принесло проданное ненужное снаряжение (за забег).</summary>
        public int SoldItemsGold { get; private set; }

        public int HeroCount => AllUnits.Count(u => u.IsHeroClass);
        /// <summary>Богатырей уже столько, сколько можно (новый не придёт).</summary>
        public bool HeroLimitReached => HeroCount >= maxHeroes;
        /// <summary>Старое имя: «нового богатыря не будет».</summary>
        public bool HasHero => HeroLimitReached;

        /// <summary>Отряд, показанный снизу панели (null — все в поле, новый собрать нельзя).</summary>
        public PlayerArmy BottomSquad => View().Bottom;

        private void Awake()
        {
            if (game == null) game = FindFirstObjectByType<GameManager>();
            EnsureInitialized();
        }

        private void LateUpdate()
        {
            if (column != null && !Mathf.Approximately(column.rect.width, laidOutWidth)) ApplyLayout();
        }

        private void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;
            garrison = new ArmyRow(rowSlots);
            if (mergeButton != null) mergeButton.gameObject.SetActive(false);
            BuildViews();
            Refresh();
        }

        // =====================================================================
        //  Что показано в панели
        // =====================================================================

        /// <summary>
        /// Снизу — выбранный на карте отряд, иначе тот, что на ратуше. Сверху — гарнизон, а если нижний отряд в поле
        /// и рядом стоит другой наш отряд — он (встреча). Меняться можно на ратуше и на встрече.
        /// </summary>
        private PanelView View()
        {
            EnsureInitialized();
            WorldPowers powers = Powers;
            PlayerArmy bottom = focus != null && !focus.IsEmpty && Squads.Contains(focus) ? focus : HomeSquad;
            if (bottom == null || powers == null || powers.AtTownHall(bottom)) return new PanelView { Bottom = bottom, Together = true };
            PlayerArmy friend = powers.MeetingWith(bottom);
            return new PanelView { Top = friend, Bottom = bottom, Together = friend != null };
        }

        private ArmyRow RowOf(PanelRow row) => RowOf(row, View());

        private ArmyRow RowOf(PanelRow row, PanelView view) =>
            row == PanelRow.Top ? (view.Top != null ? view.Top.Row : garrison) : view.Bottom?.Row;

        private string RowName(PanelRow row, PanelView view) =>
            row == PanelRow.Top ? (view.Top != null ? view.Top.Title : "Гарнизон") : view.Bottom != null ? view.Bottom.Title : "Отряд";

        /// <summary>Показать снизу этот отряд (выбран на карте); null — тот, что на ратуше.</summary>
        public void Focus(PlayerArmy army)
        {
            EnsureInitialized();
            if (focus == army) return;
            focus = army;
            DropHiddenSelection();
            lastView = View();
            RefreshViews();
        }

        /// <summary>Выбранный боец больше не виден в панели — выбор снимается.</summary>
        private void DropHiddenSelection()
        {
            if (selectedRow == null) return;
            PanelView view = View();
            if (selectedRow != RowOf(PanelRow.Top, view) && selectedRow != RowOf(PanelRow.Bottom, view)) selectedRow = null;
        }

        private string NoSquadText
        {
            get
            {
                int max = Powers != null ? Powers.maxSquads : 1;
                return $"Все {max} {SquadWord(max)} в поле — новый соберётся на ратуше, когда один вернётся";
            }
        }

        private static string SquadWord(int count) => count % 10 == 1 && count % 100 != 11 ? "отряд"
            : count % 10 >= 2 && count % 10 <= 4 && (count % 100 < 12 || count % 100 > 14) ? "отряда" : "отрядов";

        private string LockedText(PanelView view) =>
            $"{view.Bottom?.Title ?? "Отряд"} в поле: меняться бойцами с гарнизоном можно на ратуше, с другим отрядом — встав рядом с ним";

        // =====================================================================
        //  Слоты
        // =====================================================================

        private static BenchUnit Get(SlotRef slot) => slot.Row[slot.Index];

        private static void Set(SlotRef slot, BenchUnit unit) => slot.Row[slot.Index] = unit;

        private SlotRef? Find(BenchUnit unit)
        {
            foreach (ArmyRow row in AllRows)
            {
                int? index = row.IndexOf(unit);
                if (index.HasValue) return new SlotRef(row, index.Value);
            }
            return null;
        }

        /// <summary>Места бойцов линейки (без вожака: богатыри не сливаются).</summary>
        private static IEnumerable<SlotRef> FighterSlots(ArmyRow row) => Enumerable.Range(0, row.Slots.Length).Select(i => new SlotRef(row, i));

        // =====================================================================
        //  Юниты: добавление, слияние, итоги боя
        // =====================================================================

        /// <summary>
        /// Новобранец с поля (уровень level) — в гарнизон. Богатырь встаёт вожаком отряда на ратуше (если там есть бойцы
        /// и слот вожака пуст), иначе вожаком гарнизона, иначе — в гарнизон бойцом. Третий одинаковый сразу сливается
        /// с двумя другими. Возвращает юнита, в которого он превратился (новый или слитый); null — гарнизон полон
        /// (или богатырей уже предел).
        /// </summary>
        public BenchUnit AddRecruit(UnitData data, int level = 1, int stars = 1, HeroTraits hero = null)
        {
            EnsureInitialized();
            if (data == null) return null;

            var recruit = new BenchUnit(data, stars, level) { hero = hero };
            if (recruit.IsHeroClass && HeroLimitReached) return null;
            PlayerArmy home = HomeSquad;
            if (recruit.IsHeroClass && home != null && !home.IsEmpty && home.Row.Leader == null)
            {
                home.Row.Leader = recruit;
            }
            else if (recruit.IsHeroClass && garrison.Leader == null)
            {
                garrison.Leader = recruit;
            }
            else
            {
                int? slot = garrison.FirstFree();
                if (slot.HasValue)
                {
                    garrison.Slots[slot.Value] = recruit;
                }
                else
                {
                    // Гарнизон полон, но двое таких же уже есть — третий сливается с ними сразу, без места
                    if (recruit.IsHeroClass) return null;
                    List<SlotRef> copies = Copies(MergeGroups()[0], data, recruit.stars);
                    if (copies.Count < 2) return null;
                    recruit = MergeThree(copies[0], copies[1], recruit);
                }
            }

            BenchUnit result = MergeAll(recruit);
            AutoEquipAll(); // снятое при слиянии — другим подходящим юнитам
            int sold = SellLeftovers();
            if (sold > 0 && game != null) game.ShowMessage($"Лишнее снаряжение продано: +{sold} {GameVisuals.IconGold}");
            Refresh();
            return result;
        }

        /// <summary>Добавить юнита в гарнизон (false — гарнизон полон).</summary>
        public bool TryAddUnit(UnitData data, int level = 1) => AddRecruit(data, level) != null;

        /// <summary>Сразу в отряд на ратуше (стартовые бойцы); там нет места — в гарнизон.</summary>
        public BenchUnit AddToHomeSquad(UnitData data, int level = 1)
        {
            EnsureInitialized();
            if (data == null) return null;
            PlayerArmy home = HomeSquad;
            int? slot = home != null ? home.Row.FirstFree() : null;
            if (!slot.HasValue) return AddRecruit(data, level);
            var unit = new BenchUnit(data, 1, level);
            home.Row.Slots[slot.Value] = unit;
            BenchUnit result = MergeAll(unit);
            Refresh();
            return result;
        }

        /// <summary>
        /// Группы, внутри которых сливаются одинаковые: отряд на ратуше — вместе с гарнизоном (результат — в отряд),
        /// каждый отряд в поле — сам с собой.
        /// </summary>
        private List<List<SlotRef>> MergeGroups()
        {
            PlayerArmy home = HomeSquad;
            var homeGroup = new List<SlotRef>();
            if (home != null) homeGroup.AddRange(FighterSlots(home.Row));
            homeGroup.AddRange(FighterSlots(garrison));
            var groups = new List<List<SlotRef>> { homeGroup };
            foreach (PlayerArmy squad in Squads)
            {
                if (squad != home && !squad.IsEmpty) groups.Add(FighterSlots(squad.Row).ToList());
            }
            return groups;
        }

        /// <summary>Слоты группы с юнитами этого типа и звёздности (богатыри не сливаются).</summary>
        private static List<SlotRef> Copies(List<SlotRef> group, UnitData data, int stars)
        {
            return group.Where(s =>
            {
                BenchUnit unit = Get(s);
                return unit != null && !unit.IsHeroClass && unit.data == data && unit.stars == stars;
            }).ToList();
        }

        /// <summary>Слить все тройки одинаковых (каскадом). Возвращает, во что превратился tracked.</summary>
        private BenchUnit MergeAll(BenchUnit tracked)
        {
            bool merged = true;
            while (merged)
            {
                merged = false;
                foreach (List<SlotRef> group in MergeGroups())
                {
                    foreach (SlotRef slot in group)
                    {
                        BenchUnit unit = Get(slot);
                        if (unit == null || unit.IsHeroClass || unit.stars >= UnitData.MaxStars) continue;
                        List<SlotRef> copies = Copies(group, unit.data, unit.stars);
                        if (copies.Count < 3) continue;

                        BenchUnit third = Get(copies[2]);
                        bool hasTracked = copies.Take(3).Any(s => Get(s) == tracked);
                        Set(copies[2], null);
                        BenchUnit result = MergeThree(copies[0], copies[1], third);
                        if (hasTracked) tracked = result;
                        merged = true;
                        break;
                    }
                    if (merged) break;
                }
            }
            return tracked;
        }

        /// <summary>
        /// Слить юнитов из слотов first и second с третьим: звезда +1, уровень — средний по трём (с опытом),
        /// снаряжение — лучшее из трёх (остальное на склад). Результат встаёт в слот first, слот second освобождается.
        /// </summary>
        private BenchUnit MergeThree(SlotRef first, SlotRef second, BenchUnit third)
        {
            BenchUnit a = Get(first);
            BenchUnit b = Get(second);
            var merged = new BenchUnit(a.data, a.stars + 1);
            merged.SetLevelValue((a.LevelValue + b.LevelValue + third.LevelValue) / 3f);
            CollectItems(a, stash);
            CollectItems(b, stash);
            CollectItems(third, stash);
            Set(first, merged);
            Set(second, null);
            selectedRow = null;
            if (merged.data.unitClass != UnitClass.None)
            {
                EquipBestFromStash(merged, ItemSlot.Armor);
                EquipBestFromStash(merged, ItemSlot.Weapon);
            }
            if (game != null) game.ShowMessage($"Слияние: 3 × {a.data.unitName} → {merged.Title}");
            return merged;
        }

        /// <summary>Устаревшая кнопка «Объединить» из старых сцен: слияние теперь происходит само.</summary>
        public void MergeSelected()
        {
            EnsureInitialized();
            MergeAll(null);
            AutoEquipAll();
            SellLeftovers();
            Refresh();
        }

        /// <summary>
        /// Мир сдвинулся (отряд пришёл на ратушу или ушёл с неё, отряды встретились): вернувшийся отряд сливается
        /// с гарнизоном, панель показывает новые линейки. Сообщает о переменах, только если кто-то слился.
        /// </summary>
        public void OnWorldChanged()
        {
            EnsureInitialized();
            PlayerArmy home = HomeSquad;
            PanelView view = View();
            if (home == lastHome && view.Same(lastView)) return;
            bool homeChanged = home != lastHome;
            lastHome = home;
            lastView = view;
            DropHiddenSelection();
            int before = UnitCount;
            if (homeChanged) MergeAll(null);
            if (UnitCount != before)
            {
                AutoEquipAll();
                SellLeftovers();
                Refresh();
            }
            else
            {
                RefreshViews();
            }
        }

        /// <summary>
        /// Итоги боя: погибшие пропадают навсегда (снаряжение — на склад), выжившие получают опыт.
        /// Возвращает строки для сообщений.
        /// </summary>
        public List<string> ApplyBattleResults(IEnumerable<UnitBattleResult> results, bool victory)
        {
            EnsureInitialized();
            var dead = new List<string>();
            var promoted = new List<string>();
            int bonusXp = game != null && game.city != null ? game.city.BattleXpBonus : 0; // Ристалище
            foreach (UnitBattleResult result in results)
            {
                SlotRef? slot = Find(result.Unit);
                if (!slot.HasValue) continue;
                if (!result.Alive)
                {
                    dead.Add(result.Unit.Title);
                    CollectItems(result.Unit, stash);
                    Set(slot.Value, null);
                    continue;
                }
                int gained = result.Unit.AddXp(xpPerBattle + bonusXp + result.Kills * xpPerKill + (victory ? xpForVictory : 0));
                if (gained > 0) promoted.Add($"{result.Unit.data.unitName} → ур. {result.Unit.level}");
            }
            selectedRow = null;
            AutoEquipAll(); // снаряжение погибших — живым, кому подходит
            int sold = SellLeftovers();
            Refresh();

            var report = new List<string>();
            if (dead.Count > 0) report.Add("Погибли навсегда: " + string.Join(", ", dead));
            if (sold > 0) report.Add($"их снаряжение продано: +{sold} {GameVisuals.IconGold}");
            if (promoted.Count > 0) report.Add("Новые уровни: " + string.Join(", ", promoted));
            return report;
        }

        // =====================================================================
        //  Снаряжение
        // =====================================================================

        /// <summary>
        /// Надеть трофей на подходящего юнита (сначала отряды, потом гарнизон): сначала в пустой слот, иначе вместо самой
        /// слабой вещи (старая продаётся), иначе — продать. Возвращает строку для лога.
        /// </summary>
        public string AddItem(ItemData item, int tier)
        {
            EnsureInitialized();
            if (item == null) return string.Empty;
            var incoming = new EquippedItem(item, tier);
            List<BenchUnit> units = AllUnits.ToList();

            // 1) Пустой слот у подходящего юнита
            foreach (BenchUnit unit in units)
            {
                if (!Wears(unit, item) || EquippedItem.IsValid(unit.GetItem(item.slot))) continue;
                unit.SetItem(item.slot, incoming);
                Refresh();
                return $"{incoming.Name} → {unit.Title}";
            }

            // 2) Замена самой слабой вещи, если новая лучше (старая продаётся)
            BenchUnit weakest = null;
            foreach (BenchUnit unit in units)
            {
                if (!Wears(unit, item)) continue;
                if (weakest == null || unit.GetItem(item.slot).Score < weakest.GetItem(item.slot).Score) weakest = unit;
            }
            if (weakest != null && weakest.GetItem(item.slot).Score < incoming.Score)
            {
                stash.Add(weakest.GetItem(item.slot));
                weakest.SetItem(item.slot, incoming);
                int soldOld = SellLeftovers();
                Refresh();
                return $"{incoming.Name} → {weakest.Title} (старое продано: +{soldOld} {GameVisuals.IconGold})";
            }

            // 3) Никому не нужно — продаём
            stash.Add(incoming);
            int sold = SellLeftovers();
            Refresh();
            return $"{incoming.Name} — никому не нужно, продано: +{sold} {GameVisuals.IconGold}";
        }

        /// <summary>Продать всё, что лежит без дела. Возвращает выручку.</summary>
        private int SellLeftovers()
        {
            int gold = 0;
            foreach (EquippedItem item in stash)
            {
                if (EquippedItem.IsValid(item)) gold += item.data.GetSellPrice(item.tier);
            }
            stash.Clear();
            if (gold <= 0) return 0;
            SoldItemsGold += gold;
            if (game != null) game.AddGold(gold);
            return gold;
        }

        /// <summary>Подходит ли вещь юниту: свой класс, у гибридов — классы обоих «родителей», богатырю — любая.</summary>
        private static bool Wears(BenchUnit unit, ItemData item)
        {
            return unit != null && unit.data != null && item != null && UnitClasses.CanWear(unit.data.unitClass, item.unitClass);
        }

        private static void CollectItems(BenchUnit unit, List<EquippedItem> into)
        {
            if (unit == null) return;
            if (EquippedItem.IsValid(unit.armor)) into.Add(unit.armor);
            if (EquippedItem.IsValid(unit.weapon)) into.Add(unit.weapon);
            unit.armor = null;
            unit.weapon = null;
        }

        /// <summary>Каждому юниту — лучшие подходящие вещи со склада (если они лучше надетых).</summary>
        private void AutoEquipAll()
        {
            foreach (BenchUnit unit in AllUnits.ToList())
            {
                if (unit.data == null || unit.data.unitClass == UnitClass.None) continue;
                EquipBestFromStash(unit, ItemSlot.Armor);
                EquipBestFromStash(unit, ItemSlot.Weapon);
            }
        }

        private void EquipBestFromStash(BenchUnit unit, ItemSlot slot)
        {
            EquippedItem best = null;
            foreach (EquippedItem item in stash)
            {
                if (!EquippedItem.IsValid(item) || item.data.slot != slot || !Wears(unit, item.data)) continue;
                if (best == null || item.Score > best.Score) best = item;
            }
            EquippedItem current = unit.GetItem(slot);
            if (best == null || (EquippedItem.IsValid(current) && current.Score >= best.Score)) return;

            stash.Remove(best);
            if (EquippedItem.IsValid(current)) stash.Add(current);
            unit.SetItem(slot, best);
        }

        // =====================================================================
        //  Действия игрока: клик, перетаскивание, «Продать»
        // =====================================================================

        /// <summary>Клик по слоту: выбрать юнита / снять выбор / переставить выбранного сюда (поменять местами).</summary>
        public void OnSlotClicked(PanelRow row, int index)
        {
            EnsureInitialized();
            if (!interactable) return;
            ArmyRow target = RowOf(row);
            if (target == null)
            {
                Say(NoSquadText);
                return;
            }

            if (selectedRow == null)
            {
                if (target[index] != null)
                {
                    selectedRow = target;
                    selectedIndex = index;
                }
            }
            else if (selectedRow == target && selectedIndex == index)
            {
                selectedRow = null;
            }
            else
            {
                var from = new SlotRef(selectedRow, selectedIndex);
                selectedRow = null;
                if (TryMove(from, new SlotRef(target, index))) AfterRearrange();
            }
            Refresh();
        }

        /// <summary>Клик по подписи линейки: выбранный боец из другой линейки встаёт на её первое свободное место.</summary>
        public void OnRowLabelClicked(PanelRow row)
        {
            EnsureInitialized();
            if (!interactable || selectedRow == null) return;
            ArmyRow target = RowOf(row);
            if (target == null || target == selectedRow) return;
            var from = new SlotRef(selectedRow, selectedIndex);
            selectedRow = null;
            if (TryMoveInto(from, target)) AfterRearrange();
            Refresh();
        }

        /// <summary>После перестановки: слияния, снаряжение, продажа лишнего.</summary>
        private void AfterRearrange()
        {
            MergeAll(null);
            AutoEquipAll();
            SellLeftovers();
        }

        /// <summary>
        /// Переставить бойца из from в to (там кто-то есть — меняются местами). Между линейками — только на ратуше или
        /// на встрече отрядов. В слот вожака — только богатырь; богатырь, пришедший в линейку без вожака, сам встаёт вожаком.
        /// </summary>
        private bool TryMove(SlotRef from, SlotRef to)
        {
            BenchUnit moving = Get(from);
            if (moving == null) return false;
            PanelView view = View();
            if (from.Row != to.Row && !view.Together)
            {
                Say(LockedText(view));
                return false;
            }
            if (to.Index == ArmyRow.LeaderIndex && !moving.IsHeroClass)
            {
                Say("Слот вожака — только для богатыря");
                return false;
            }
            if (moving.IsHeroClass && from.Row != to.Row && to.Row.Leader == null && Get(to) == null)
                to = new SlotRef(to.Row, ArmyRow.LeaderIndex); // богатырь ведёт линейку, куда пришёл
            if (to.Row == from.Row && to.Index == from.Index) return false;

            BenchUnit target = Get(to);
            if (from.Index == ArmyRow.LeaderIndex && target != null && !target.IsHeroClass)
            {
                Say($"{target.Name} не встанет в слот вожака — переставьте богатыря на пустой слот");
                return false;
            }
            Set(from, target);
            Set(to, moving);
            return true;
        }

        /// <summary>Боец — на свободное место линейки (богатырь — вожаком, если слот пуст).</summary>
        private bool TryMoveInto(SlotRef from, ArmyRow target)
        {
            BenchUnit moving = Get(from);
            if (moving == null) return false;
            int? index = moving.IsHeroClass && target.Leader == null ? ArmyRow.LeaderIndex : target.FirstFree();
            if (!index.HasValue)
            {
                Say("Там нет свободного места — перетащите бойца на занятый слот, чтобы поменять их местами");
                return false;
            }
            return TryMove(from, new SlotRef(target, index.Value));
        }

        /// <summary>
        /// Все бойцы линейки from — в линейку to (как перетаскивание героя в «Героях»): вожак — в слот вожака (занят —
        /// бойцом), остальные — на свободные места; что не влезло — остаётся. Места нет совсем — линейки меняются местами.
        /// </summary>
        public void MoveAll(PanelRow from, PanelRow to)
        {
            EnsureInitialized();
            if (!interactable || from == to) return;
            PanelView view = View();
            ArmyRow source = RowOf(from, view), target = RowOf(to, view);
            if (source == null || target == null)
            {
                Say(NoSquadText);
                return;
            }
            if (!view.Together)
            {
                Say(LockedText(view));
                return;
            }
            if (source.IsEmpty)
            {
                Say($"{RowName(from, view)}: никого нет");
                return;
            }
            selectedRow = null;
            string fromName = RowName(from, view), toName = RowName(to, view);

            bool fits = (source.Leader != null && (target.Leader == null || target.HasFreeSlot)) ||
                        (source.Slots.Any(u => u != null) && target.HasFreeSlot);
            if (!fits)
            {
                source.SwapWith(target);
                Say($"{fromName} и {toName} поменялись бойцами");
            }
            else
            {
                int moved = 0;
                if (source.Leader != null)
                {
                    int? slot = target.Leader == null ? ArmyRow.LeaderIndex : target.FirstFree();
                    if (slot.HasValue)
                    {
                        target[slot.Value] = source.Leader;
                        source.Leader = null;
                        moved++;
                    }
                }
                for (int i = 0; i < source.Slots.Length; i++)
                {
                    int? slot = target.FirstFree();
                    if (source.Slots[i] == null || !slot.HasValue) continue;
                    target.Slots[slot.Value] = source.Slots[i];
                    source.Slots[i] = null;
                    moved++;
                }
                int left = source.Count;
                Say($"{fromName} {GameVisuals.IconMove} {toName}: {moved} {UnitWord(moved)}" + (left > 0 ? $", не влезли {left}" : string.Empty));
            }
            AfterRearrange();
            Refresh();
        }

        private static string UnitWord(int count) => count % 10 == 1 && count % 100 != 11 ? "боец"
            : count % 10 >= 2 && count % 10 <= 4 && (count % 100 < 12 || count % 100 > 14) ? "бойца" : "бойцов";

        // ----- Перетаскивание (BenchSlotView — боец, ArmyRowHandle — вся линейка) -----

        public void BeginUnitDrag(PanelRow row, int index, PointerEventData eventData)
        {
            ArmyRow source = interactable ? RowOf(row) : null;
            BenchUnit unit = source != null ? source[index] : null;
            if (unit == null) return;
            dragging = true;
            dragWholeRow = false;
            dragRow = row;
            dragIndex = index;
            selectedRow = source; // перетаскиваемый подсвечен и описан
            selectedIndex = index;
            RefreshViews();
            ShowGhost(UnitArt.Figure(unit.data, unit.stars, HasCrossbows, hero: unit.hero), $"{GameVisuals.Stars(unit.stars)}", eventData);
        }

        public void BeginRowDrag(PanelRow row, PointerEventData eventData)
        {
            PanelView view = View();
            ArmyRow source = interactable ? RowOf(row, view) : null;
            if (source == null || source.IsEmpty) return;
            dragging = true;
            dragWholeRow = true;
            dragRow = row;
            if (rowPlates[(int)row] != null) rowPlates[(int)row].color = LabelPlateDrag;
            ShowGhost(null, $"{RowName(row, view)} ×{source.Count}", eventData);
        }

        public void OnDragMove(PointerEventData eventData)
        {
            if (dragging) MoveGhost(eventData);
        }

        public void EndDrag(PointerEventData eventData)
        {
            if (!dragging) return;
            dragging = false;
            HideGhost();
            if (rowPlates[(int)dragRow] != null) rowPlates[(int)dragRow].color = LabelPlate;
            if (!TryGetDropTarget(eventData, out PanelRow row, out int? index))
            {
                RefreshViews();
                return;
            }

            if (dragWholeRow)
            {
                if (row != dragRow) MoveAll(dragRow, row);
                return;
            }

            ArmyRow source = RowOf(dragRow), target = RowOf(row);
            selectedRow = null;
            if (source == null || source[dragIndex] == null)
            {
                Refresh();
                return;
            }
            if (target == null)
            {
                Say(NoSquadText);
                Refresh();
                return;
            }
            var from = new SlotRef(source, dragIndex);
            bool moved = index.HasValue ? TryMove(from, new SlotRef(target, index.Value)) : target != source && TryMoveInto(from, target);
            if (moved) AfterRearrange();
            Refresh();
        }

        /// <summary>Куда отпустили: слот (row, index) или строка целиком (index = null).</summary>
        private bool TryGetDropTarget(PointerEventData eventData, out PanelRow row, out int? index)
        {
            row = PanelRow.Top;
            index = null;
            GameObject hit = eventData.pointerCurrentRaycast.gameObject;
            if (hit == null) return false;
            BenchSlotView slot = hit.GetComponentInParent<BenchSlotView>();
            if (slot != null && slot.Owner == this)
            {
                row = slot.Row;
                index = slot.Index;
                return true;
            }
            for (int r = 0; r < rowRects.Length; r++)
            {
                if (rowRects[r] == null || !hit.transform.IsChildOf(rowRects[r])) continue;
                row = (PanelRow)r;
                return true;
            }
            return false;
        }

        private void ShowGhost(Sprite figure, string caption, PointerEventData eventData)
        {
            EnsureGhost();
            if (dragGhost == null) return;
            dragGhost.gameObject.SetActive(true);
            dragGhost.SetAsLastSibling();
            bool hasFigure = figure != null;
            dragGhostFigure.enabled = hasFigure;
            if (hasFigure) dragGhostFigure.sprite = figure;
            dragGhost.sizeDelta = hasFigure ? new Vector2(110f, 130f) : new Vector2(260f, 64f);
            dragGhostText.text = caption;
            dragGhostText.rectTransform.anchorMin = hasFigure ? new Vector2(0f, 0f) : Vector2.zero;
            dragGhostText.rectTransform.anchorMax = hasFigure ? new Vector2(1f, 0.3f) : Vector2.one;
            dragGhost.GetComponent<Image>().enabled = !hasFigure;
            MoveGhost(eventData);
        }

        private void MoveGhost(PointerEventData eventData)
        {
            if (dragGhost == null) return;
            var parent = (RectTransform)dragGhost.parent;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, eventData.position, eventData.pressEventCamera, out Vector2 local))
                dragGhost.localPosition = local;
        }

        private void HideGhost()
        {
            if (dragGhost != null) dragGhost.gameObject.SetActive(false);
        }

        /// <summary>«Призрак» перетаскиваемого: фигурка бойца или табличка линейки поверх всего экрана.</summary>
        private void EnsureGhost()
        {
            if (dragGhost != null) return;
            Canvas canvas = column != null ? column.GetComponentInParent<Canvas>() : null;
            if (canvas == null) return;
            dragGhost = NewRect("DragGhost", canvas.rootCanvas.transform);
            dragGhost.pivot = new Vector2(0.5f, 0.5f);
            var plate = dragGhost.gameObject.AddComponent<Image>();
            plate.sprite = GameVisuals.RoundedRect;
            plate.type = Image.Type.Sliced;
            plate.color = new Color(0.62f, 0.5f, 0.2f, 0.9f);
            plate.raycastTarget = false;
            RectTransform figure = NewRect("Figure", dragGhost);
            figure.anchorMin = Vector2.zero;
            figure.anchorMax = Vector2.one;
            figure.offsetMin = figure.offsetMax = Vector2.zero;
            dragGhostFigure = figure.gameObject.AddComponent<Image>();
            dragGhostFigure.preserveAspect = true;
            dragGhostFigure.raycastTarget = false;
            dragGhostFigure.color = new Color(1f, 1f, 1f, 0.85f);
            RectTransform text = NewRect("Text", dragGhost);
            text.offsetMin = text.offsetMax = Vector2.zero;
            dragGhostText = text.gameObject.AddComponent<TextMeshProUGUI>();
            if (infoText != null) dragGhostText.font = infoText.font;
            dragGhostText.fontSize = 26f;
            dragGhostText.fontStyle = FontStyles.Bold;
            dragGhostText.alignment = TextAlignmentOptions.Center;
            dragGhostText.color = Color.white;
            dragGhostText.outlineWidth = 0.2f;
            dragGhostText.outlineColor = Color.black;
            dragGhostText.raycastTarget = false;
            dragGhost.gameObject.SetActive(false);
        }

        private void Say(string text)
        {
            if (game != null) game.ShowMessage(text);
        }

        /// <summary>Продать (выкинуть) выбранного юнита за золото.</summary>
        public void SellSelected()
        {
            EnsureInitialized();
            if (!interactable) return;
            BenchUnit unit = selectedRow != null ? selectedRow[selectedIndex] : null;
            if (unit == null)
            {
                Say("Сначала выберите бойца");
                return;
            }

            int price = unit.SellPrice;
            CollectItems(unit, stash);
            selectedRow[selectedIndex] = null;
            selectedRow = null;
            AutoEquipAll(); // его вещи — другим подходящим юнитам, остальное продаётся
            int itemsGold = SellLeftovers();
            if (game != null)
            {
                game.AddGold(price);
                game.ShowMessage($"Продан {unit.Title}: +{price} {GameVisuals.IconGold}" +
                                 (itemsGold > 0 ? $" и снаряжение +{itemsGold} {GameVisuals.IconGold}" : string.Empty));
            }
            Refresh();
        }

        public void ClearAll()
        {
            EnsureInitialized();
            garrison.Clear();
            foreach (PlayerArmy squad in Squads) squad.Row.Clear();
            stash.Clear();
            SoldItemsGold = 0;
            selectedRow = null;
            focus = null;
            Refresh();
        }

        public void SetInteractable(bool value)
        {
            EnsureInitialized();
            interactable = value;
            if (!value)
            {
                selectedRow = null;
                dragging = false;
                HideGhost();
            }
            Refresh();
        }

        // =====================================================================
        //  Вид: две линейки в панели скамейки
        // =====================================================================

        /// <summary>
        /// Строит линейки: верхняя (гарнизон) — в slotContainer, нижняя (отряд) — новой строкой под ней. В каждой — подпись
        /// (её тянут, чтобы перенести всех), слот вожака и rowSlots мест. Описание и «Продать» опускаются ниже.
        /// Размеры — по ширине панели (ApplyLayout).
        /// </summary>
        private void BuildViews()
        {
            if (slotContainer == null || slotPrefab == null)
            {
                if (Application.isPlaying) Debug.LogError("[Bench] Назначьте Slot Container и Slot Prefab в инспекторе.");
                return;
            }
            column = slotContainer.parent as RectTransform;
            if (column == null) return;
            // Колонка — во всю ширину панели (на карте в горизонтальной раскладке панель уже)
            column.anchorMin = new Vector2(0f, column.anchorMin.y);
            column.anchorMax = new Vector2(1f, column.anchorMax.y);
            column.sizeDelta = new Vector2(0f, column.sizeDelta.y);
            column.anchoredPosition = new Vector2(0f, column.anchoredPosition.y);

            Transform title = column.Find("Title");
            titleText = title != null ? title.GetComponent<TMP_Text>() : null;

            // Контейнер заполняется кодом: убираем всё, что лежало в нём в сцене
            for (int i = slotContainer.childCount - 1; i >= 0; i--) Destroy(slotContainer.GetChild(i).gameObject);
            rowRects[0] = slotContainer;
            rowRects[1] = NewRect("SquadRow", column);
            for (int r = 0; r < rowRects.Length; r++)
            {
                var row = (PanelRow)r;
                SetupRow(rowRects[r]);
                rowLabels[r] = RowLabel(rowRects[r], row, out rowPlates[r]);
                slotViews[r] = new BenchSlotView[rowSlots + 1];
                slotViews[r][0] = NewSlot(rowRects[r], row, ArmyRow.LeaderIndex);
                for (int i = 0; i < rowSlots; i++) slotViews[r][i + 1] = NewSlot(rowRects[r], row, i);

                BenchSlotView leaderView = slotViews[r][0];
                leaderView.emptyCaption = "вожак";
                leaderView.emptyColor = new Color(0.32f, 0.27f, 0.14f);
                Outline frame = leaderView.gameObject.AddComponent<Outline>();
                frame.effectColor = new Color(1f, 0.8f, 0.3f, 0.9f);
                frame.effectDistance = new Vector2(3f, -3f);
            }
            ApplyLayout();
        }

        private static void SetupRow(RectTransform row)
        {
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            if (layout == null) layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = RowSpacing;
            layout.padding = new RectOffset((int)RowPadding, (int)RowPadding, 0, 0);
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            // Прозрачный фон: бойца или линейку можно бросить и между слотами
            if (row.GetComponent<Graphic>() == null) row.gameObject.AddComponent<Image>().color = Color.clear;
        }

        /// <summary>Подпись линейки на табличке: её тянут на другую линейку — переходят все бойцы.</summary>
        private TMP_Text RowLabel(RectTransform row, PanelRow panelRow, out Image plate)
        {
            RectTransform rect = NewRect("Label", row);
            var element = rect.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = LabelWidth;
            element.minWidth = LabelWidth;
            plate = rect.gameObject.AddComponent<Image>();
            plate.sprite = GameVisuals.RoundedRect;
            plate.type = Image.Type.Sliced;
            plate.color = LabelPlate;
            var handle = rect.gameObject.AddComponent<ArmyRowHandle>();
            handle.owner = this;
            handle.row = panelRow;

            RectTransform textRect = NewRect("Text", rect);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(4f, 4f);
            textRect.offsetMax = new Vector2(-4f, -4f);
            var text = textRect.gameObject.AddComponent<TextMeshProUGUI>();
            if (infoText != null) text.font = infoText.font;
            text.enableAutoSizing = true;
            text.fontSizeMin = 12f;
            text.fontSizeMax = 24f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false; // строки заданы явно; длинное имя — мельче, а не в три строки
            text.color = new Color(0.9f, 0.92f, 0.98f);
            text.raycastTarget = false;
            return text;
        }

        /// <summary>Слот в своей «рамке»: рамка задаёт место в строке, сам слот (префаб 120×160) уменьшается масштабом.</summary>
        private BenchSlotView NewSlot(RectTransform row, PanelRow panelRow, int index)
        {
            RectTransform holder = NewRect(index == ArmyRow.LeaderIndex ? $"{panelRow}_Leader" : $"{panelRow}_{index + 1}", row);
            holder.gameObject.AddComponent<LayoutElement>();
            holders.Add(holder);
            BenchSlotView view = Instantiate(slotPrefab, holder);
            var rect = (RectTransform)view.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(120f, 160f);
            view.Init(this, panelRow, index);
            return view;
        }

        /// <summary>Размеры под ширину панели: слоты как можно крупнее, но обе строки влезают; ниже — описание и «Продать».</summary>
        private void ApplyLayout()
        {
            if (column == null) return;
            laidOutWidth = column.rect.width;
            int perRow = rowSlots + 1;
            float free = laidOutWidth - RowPadding * 2f - LabelWidth - RowSpacing * perRow;
            float scale = Mathf.Clamp(free / perRow / 120f, 0.4f, slotScale);
            float slotWidth = 120f * scale, slotHeight = 160f * scale;
            foreach (RectTransform holder in holders)
            {
                if (holder == null) continue;
                var element = holder.GetComponent<LayoutElement>();
                element.preferredWidth = element.minWidth = slotWidth;
                element.preferredHeight = element.minHeight = slotHeight;
                if (holder.childCount > 0) holder.GetChild(0).localScale = new Vector3(scale, scale, 1f);
            }
            foreach (TMP_Text label in rowLabels)
            {
                var element = label != null ? label.GetComponentInParent<LayoutElement>() : null;
                if (element != null) element.preferredHeight = element.minHeight = slotHeight;
            }

            float top = 4f;
            if (titleText != null) TopRow(titleText.rectTransform, top, TitleHeight - 4f, 16f);
            top += TitleHeight;
            for (int r = 0; r < rowRects.Length; r++)
            {
                if (rowRects[r] == null || !rowRects[r].gameObject.activeSelf) continue;
                TopRow(rowRects[r], top, slotHeight, 0f);
                top += slotHeight + (r == 0 ? 6f : 8f);
            }
            float rest = Mathf.Max(40f, column.rect.height - top - 4f);
            if (infoText != null)
            {
                RectTransform info = infoText.rectTransform;
                info.anchorMin = new Vector2(0f, 1f);
                info.anchorMax = new Vector2(1f, 1f);
                info.pivot = new Vector2(0f, 1f);
                info.offsetMin = new Vector2(20f, -top - rest);
                info.offsetMax = new Vector2(-260f, -top);
                infoText.enableAutoSizing = true;
                infoText.fontSizeMin = 12f;
                infoText.fontSizeMax = 24f;
            }
            if (sellButton != null)
            {
                var sell = (RectTransform)sellButton.transform;
                sell.anchorMin = sell.anchorMax = sell.pivot = new Vector2(1f, 1f);
                sell.anchoredPosition = new Vector2(-20f, -top - 4f);
                sell.sizeDelta = new Vector2(220f, Mathf.Min(84f, rest - 8f));
            }
        }

        private static void TopRow(RectTransform rect, float top, float height, float side)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(side, -top - height);
            rect.offsetMax = new Vector2(-side, -top);
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private void Refresh()
        {
            RefreshViews();
            Changed?.Invoke();
        }

        private void RefreshViews()
        {
            if (garrison == null) return;
            PanelView view = View();
            // Отряд в поле и ни с кем не встретился — гарнизон не показываем: с ним всё равно не поменяться
            bool showTop = !TopHidden(view);
            if (rowRects[0] != null && rowRects[0].gameObject.activeSelf != showTop)
            {
                rowRects[0].gameObject.SetActive(showTop);
                ApplyLayout();
            }
            for (int r = 0; r < rowRects.Length; r++)
            {
                ArmyRow row = RowOf((PanelRow)r, view);
                BenchSlotView[] views = slotViews[r];
                for (int i = 0; i < views.Length; i++)
                {
                    int index = i - 1; // 0 — вожак
                    BenchUnit unit = row?[index];
                    if (views[i] != null) views[i].Bind(unit, row != null && selectedRow == row && selectedIndex == index);
                }
                if (rowLabels[r] == null) continue;
                string name = RowName((PanelRow)r, view);
                string lockIcon = r == (int)PanelRow.Bottom && !view.Together ? $" {GameVisuals.IconHold}" : string.Empty;
                rowLabels[r].text = row != null
                    ? $"{name}{lockIcon}\n<size=80%>{row.Count}/{row.Capacity}</size>"
                    : $"{name}\n<size=80%>—</size>";
            }

            if (titleText != null) titleText.text = TitleLine(view);

            BenchUnit selectedUnit = selectedRow != null ? selectedRow[selectedIndex] : null;
            if (infoText != null)
            {
                infoText.text = selectedUnit != null
                    ? Describe(selectedUnit, selectedRow, selectedIndex)
                    : "Гарнизон защищает город, отряды ходят по карте (в границах — помогают ему). Ушёл отряд — на ратуше " +
                      "собирают следующий. Тяните бойца на слот или подпись линейки на другую — переходят все. " +
                      $"Богатырь-вожак усиливает свою линейку. Три одинаковых сами сливаются в {GameVisuals.IconStar} выше, " +
                      "погибшие не возвращаются.";
            }
            if (sellButton != null) sellButton.interactable = interactable && selectedUnit != null;
        }

        /// <summary>Верхняя линейка (гарнизон) скрыта: снизу отряд в поле, и рядом нет другого нашего отряда.</summary>
        private static bool TopHidden(PanelView view) => view.Bottom != null && !view.Together && view.Top == null;

        private string TitleLine(PanelView view)
        {
            if (view.Bottom == null) return $"{GameVisuals.IconHold} {NoSquadText}";
            if (!view.Together)
                return $"{GameVisuals.IconHold} {view.Bottom.Title} в поле — бойцами меняется на ратуше или встав рядом с другим отрядом";
            if (view.Top != null) return $"Встреча: {view.Top.Title} и {view.Bottom.Title} — меняйтесь бойцами";
            if (view.Bottom.IsEmpty) return $"Новый отряд {view.Bottom.Number} на ратуше: перетащите в него бойцов (или подпись «Гарнизон») — и он выйдет на карту";
            return $"{view.Bottom.Title} на ратуше: меняйтесь бойцами с гарнизоном (тяните бойца или подпись линейки)";
        }

        /// <summary>Лучники с арбалетами (на скамейке рисуются арбалетчиками).</summary>
        public bool HasCrossbows => game != null && game.city != null && game.city.HasCrossbows;

        private string Describe(BenchUnit unit, ArmyRow row, int index)
        {
            UnitData data = unit.data;
            int armor = data.armor + unit.ItemArmor + unit.HeroArmor;
            CityManager city = game != null ? game.city : null;
            string kit = string.Empty; // щит воина и лук/арбалет стрелка — от науки и гильдий
            if (city != null && data.shieldBearer)
                kit = city.ShieldLevel >= 2 ? " · ростовой щит" : city.ShieldLevel == 1 ? " · щит" : string.Empty;
            else if (city != null && data.unitClass == UnitClass.Archer)
                kit = city.HasCrossbows ? " · арбалет" : string.Empty;
            string gear = data.unitClass == UnitClass.None
                ? "Нейтралы не носят снаряжение"
                : $"Броня: {(EquippedItem.IsValid(unit.armor) ? unit.armor.Name : "нет")} · Оружие: {(EquippedItem.IsValid(unit.weapon) ? unit.weapon.Name : "нет")}";
            string heal = data.IsHealer ? $" · лечение {unit.Heal}" : string.Empty;
            string xp = unit.IsMaxLevel ? "макс. уровень" : $"опыт {unit.xp}/{unit.XpToNext}";
            string traits = unit.IsHero ? $"\n{unit.hero.Summary}" : string.Empty;

            PlayerArmy squad = Squads.FirstOrDefault(s => s.Row == row);
            string where;
            if (index == ArmyRow.LeaderIndex)
                where = squad != null ? $"Вожак отряда {squad.Number}: всем в нём {unit.LeadText}" : $"Вожак гарнизона: всем в нём {unit.LeadText}";
            else if (unit.IsHeroClass) where = $"{(squad != null ? $"В отряде {squad.Number}" : "В гарнизоне")}. Вожаком дал бы {unit.LeadText}";
            else where = squad != null ? $"В отряде {squad.Number} — ходит по карте" : "В гарнизоне — защищает город";
            return $"{GameVisuals.ClassIcon(data.unitClass)} {unit.Name} {GameVisuals.Stars(unit.stars)}{kit} · ур. {unit.level}, {xp}\n" +
                   $"{GameVisuals.IconHp}{unit.MaxHp} · {GameVisuals.IconAttack}{unit.Attack}{heal} · броня {armor}{traits}\n" +
                   $"{where} · продажа {unit.SellPrice} {GameVisuals.IconGold}\n{gear}";
        }
    }
}
