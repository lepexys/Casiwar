using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
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
        public int HeroArmor => IsHero ? hero.BonusArmor : 0;
        public int MaxHp => Mathf.RoundToInt(data.GetMaxHp(stars, level) * (IsHero ? hero.HpMultiplier : 1f));
        public int Attack => Mathf.RoundToInt(data.GetAttack(stars, level) * (IsHero ? hero.AttackMultiplier : 1f)) + ItemAttack;
        public int Heal => data.GetHeal(stars, level);
        public int SellPrice => data.GetSellPrice(stars, level);
        public int XpToNext => UnitData.XpToNext(level);
        public bool IsMaxLevel => level >= UnitData.MaxLevel;

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
    /// Нижняя панель — армия (скамейка).
    /// • Хранит юнитов, собранных на поле города (capacity слотов). Линия 4 даёт юнита 2-го уровня, 5+ — 3-го.
    /// • Как в автобатлере: три одинаковых юнита одной звёздности сами сливаются в одного звездой выше
    ///   (★ → ★★ → ★★★, каскадом). Уровень слитого — средний по трём (с опытом). Если армия полна,
    ///   а двое таких же уже есть, третий сливается с ними сразу.
    /// • Уровни как в «Цивилизации»: выжившие в бою получают опыт (за бой, убийства и победу) и растут до 10-го уровня.
    ///   Погибшие в бою пропадают навсегда (их снаряжение уходит на склад).
    /// • Клик по юниту — выбрать; клик по другому слоту — поменять местами / переставить.
    /// • «Продать» — убрать выбранного юнита за золото (его вещи уходят на склад).
    /// • Снаряжение: у каждого классового юнита слот брони и слот оружия. Трофей сам надевается
    ///   на подходящего юнита (пустой слот → замена более слабой вещи). Вещь, которую никто не носит
    ///   (лишняя, заменённая, от погибших и проданных), сразу продаётся за золото. Нейтралы снаряжение не носят;
    ///   гибриды носят снаряжение обоих своих классов, богатырь — любое.
    /// • Богатырь всегда один: новый призывается, только когда старого нет (продан или погиб).
    /// Порядок слотов = строй в бою: первые слоты стоят в передней шеренге.
    /// </summary>
    public class BenchManager : MonoBehaviour
    {
        [Header("Ссылки")]
        [Tooltip("Контейнер слотов (например, с HorizontalLayoutGroup). Заполняется кодом.")]
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

        [Header("Настройки")]
        [Min(1)] public int capacity = 8;

        [Header("Опыт за бой (выжившим)")]
        [Min(0)] public int xpPerBattle = 2;
        [Min(0)] public int xpPerKill = 1;
        [Min(0)] public int xpForVictory = 1;

        private BenchUnit[] slots;
        private BenchSlotView[] views;
        private readonly List<EquippedItem> stash = new List<EquippedItem>();
        private int selectedIndex = -1;
        private bool interactable = true;
        private bool initialized;

        public int Capacity => capacity;

        public IReadOnlyList<BenchUnit> Slots
        {
            get
            {
                EnsureInitialized();
                return slots;
            }
        }

        /// <summary>Снаряжение, которое сейчас некому надеть (временно: лишнее сразу продаётся).</summary>
        public IReadOnlyList<EquippedItem> Stash => stash;

        /// <summary>Сколько золота принесло проданное ненужное снаряжение (за забег).</summary>
        public int SoldItemsGold { get; private set; }

        /// <summary>В армии есть богатырь (он может быть только один).</summary>
        public bool HasHero
        {
            get
            {
                EnsureInitialized();
                return slots.Any(unit => unit != null && unit.data != null && unit.data.unitClass == UnitClass.Hero);
            }
        }

        public int UnitCount
        {
            get
            {
                EnsureInitialized();
                return slots.Count(unit => unit != null);
            }
        }

        private void Awake()
        {
            if (game == null) game = FindFirstObjectByType<GameManager>();
            EnsureInitialized();
        }

        private void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;
            slots = new BenchUnit[Mathf.Max(1, capacity)];
            if (mergeButton != null) mergeButton.gameObject.SetActive(false);
            BuildViews();
            Refresh();
        }

        // =====================================================================
        //  Юниты: добавление, слияние, итоги боя
        // =====================================================================

        /// <summary>
        /// Новобранец с поля (1★, уровень level). Третий одинаковый сразу сливается с двумя другими.
        /// Возвращает юнита, в которого он превратился (новый или слитый); null — армия полна.
        /// </summary>
        public BenchUnit AddRecruit(UnitData data, int level = 1, int stars = 1, HeroTraits hero = null)
        {
            EnsureInitialized();
            if (data == null) return null;

            var recruit = new BenchUnit(data, stars, level) { hero = hero };
            int slot = FirstFreeSlot();
            if (slot >= 0)
            {
                slots[slot] = recruit;
            }
            else
            {
                // Армия полна, но двое таких же уже есть — третий сливается с ними сразу, без места
                List<int> copies = FindCopies(data, recruit.stars);
                if (copies.Count < 2) return null;
                recruit = MergeThree(copies[0], copies[1], recruit);
            }

            BenchUnit result = MergeAll(recruit);
            AutoEquipAll(); // снятое при слиянии — другим подходящим юнитам
            int sold = SellLeftovers();
            if (sold > 0 && game != null) game.ShowMessage($"Лишнее снаряжение продано: +{sold} {GameVisuals.IconGold}");
            Refresh();
            return result;
        }

        /// <summary>Добавить юнита (false — армия полна).</summary>
        public bool TryAddUnit(UnitData data, int level = 1) => AddRecruit(data, level) != null;

        /// <summary>Слить все тройки одинаковых (каскадом). Возвращает, во что превратился tracked.</summary>
        private BenchUnit MergeAll(BenchUnit tracked)
        {
            bool merged = true;
            while (merged)
            {
                merged = false;
                for (int i = 0; i < slots.Length && !merged; i++)
                {
                    BenchUnit unit = slots[i];
                    if (unit == null || unit.stars >= UnitData.MaxStars) continue;
                    List<int> copies = FindCopies(unit.data, unit.stars);
                    if (copies.Count < 3) continue;

                    bool hasTracked = copies.Take(3).Any(index => slots[index] == tracked);
                    BenchUnit result = MergeThree(copies[0], copies[1], slots[copies[2]]);
                    slots[copies[2]] = null;
                    if (selectedIndex == copies[2]) selectedIndex = -1;
                    if (hasTracked) tracked = result;
                    merged = true;
                }
            }
            return tracked;
        }

        /// <summary>
        /// Слить юнитов из слотов first и second с третьим: звезда +1, уровень — средний по трём (с опытом),
        /// снаряжение — лучшее из трёх (остальное на склад). Результат встаёт в слот first, слот second освобождается.
        /// </summary>
        private BenchUnit MergeThree(int first, int second, BenchUnit third)
        {
            BenchUnit a = slots[first];
            BenchUnit b = slots[second];
            var merged = new BenchUnit(a.data, a.stars + 1);
            merged.SetLevelValue((a.LevelValue + b.LevelValue + third.LevelValue) / 3f);
            CollectItems(a, stash);
            CollectItems(b, stash);
            CollectItems(third, stash);
            slots[first] = merged;
            slots[second] = null;
            if (selectedIndex == second) selectedIndex = -1;
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
                int index = System.Array.IndexOf(slots, result.Unit);
                if (index < 0) continue;
                if (!result.Alive)
                {
                    dead.Add(result.Unit.Title);
                    CollectItems(result.Unit, stash);
                    slots[index] = null;
                    continue;
                }
                int gained = result.Unit.AddXp(xpPerBattle + bonusXp + result.Kills * xpPerKill + (victory ? xpForVictory : 0));
                if (gained > 0) promoted.Add($"{result.Unit.data.unitName} → ур. {result.Unit.level}");
            }
            selectedIndex = -1;
            AutoEquipAll(); // снаряжение погибших — живым, кому подходит
            int sold = SellLeftovers();
            Refresh();

            var report = new List<string>();
            if (dead.Count > 0) report.Add("Погибли навсегда: " + string.Join(", ", dead));
            if (sold > 0) report.Add($"их снаряжение продано: +{sold} {GameVisuals.IconGold}");
            if (promoted.Count > 0) report.Add("Новые уровни: " + string.Join(", ", promoted));
            return report;
        }

        /// <summary>Слоты юнитов этого типа и звёздности (по порядку строя).</summary>
        private List<int> FindCopies(UnitData data, int stars)
        {
            var result = new List<int>();
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null && slots[i].data == data && slots[i].stars == stars) result.Add(i);
            }
            return result;
        }

        // =====================================================================
        //  Снаряжение
        // =====================================================================

        /// <summary>
        /// Надеть трофей на подходящего юнита: сначала в пустой слот, иначе вместо самой слабой вещи
        /// (старая — на склад), иначе — на склад. Возвращает строку для лога.
        /// </summary>
        public string AddItem(ItemData item, int tier)
        {
            EnsureInitialized();
            if (item == null) return string.Empty;
            var incoming = new EquippedItem(item, tier);

            // 1) Пустой слот у подходящего юнита (по порядку скамейки)
            foreach (BenchUnit unit in slots)
            {
                if (!Fits(unit, item) || EquippedItem.IsValid(unit.GetItem(item.slot))) continue;
                unit.SetItem(item.slot, incoming);
                Refresh();
                return $"{incoming.Name} → {unit.Title}";
            }

            // 2) Замена самой слабой вещи, если новая лучше (старая продаётся)
            BenchUnit weakest = null;
            foreach (BenchUnit unit in slots)
            {
                if (!Fits(unit, item)) continue;
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
        private static bool Fits(BenchUnit unit, ItemData item)
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
            foreach (BenchUnit unit in slots)
            {
                if (unit == null || unit.data == null || unit.data.unitClass == UnitClass.None) continue;
                EquipBestFromStash(unit, ItemSlot.Armor);
                EquipBestFromStash(unit, ItemSlot.Weapon);
            }
        }

        private void EquipBestFromStash(BenchUnit unit, ItemSlot slot)
        {
            EquippedItem best = null;
            foreach (EquippedItem item in stash)
            {
                if (!EquippedItem.IsValid(item) || item.data.slot != slot || !Fits(unit, item.data)) continue;
                if (best == null || item.Score > best.Score) best = item;
            }
            EquippedItem current = unit.GetItem(slot);
            if (best == null || (EquippedItem.IsValid(current) && current.Score >= best.Score)) return;

            stash.Remove(best);
            if (EquippedItem.IsValid(current)) stash.Add(current);
            unit.SetItem(slot, best);
        }

        // =====================================================================
        //  Действия игрока
        // =====================================================================

        /// <summary>Клик по слоту: выбрать юнита / снять выбор / поменять местами с выбранным.</summary>
        public void OnSlotClicked(int index)
        {
            EnsureInitialized();
            if (!interactable || index < 0 || index >= slots.Length) return;

            if (selectedIndex < 0)
            {
                if (slots[index] != null) selectedIndex = index;
            }
            else if (selectedIndex == index)
            {
                selectedIndex = -1;
            }
            else
            {
                // Поменять местами (или переставить в пустой слот) — так настраивается строй перед боем
                (slots[selectedIndex], slots[index]) = (slots[index], slots[selectedIndex]);
                selectedIndex = -1;
            }
            Refresh();
        }

        /// <summary>Продать (выкинуть) выбранного юнита за золото.</summary>
        public void SellSelected()
        {
            EnsureInitialized();
            if (!interactable) return;
            if (selectedIndex < 0 || slots[selectedIndex] == null)
            {
                if (game != null) game.ShowMessage("Сначала выберите юнита на скамейке");
                return;
            }

            BenchUnit unit = slots[selectedIndex];
            int price = unit.SellPrice;
            CollectItems(unit, stash);
            slots[selectedIndex] = null;
            selectedIndex = -1;
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
            for (int i = 0; i < slots.Length; i++) slots[i] = null;
            stash.Clear();
            SoldItemsGold = 0;
            selectedIndex = -1;
            Refresh();
        }

        public void SetInteractable(bool value)
        {
            EnsureInitialized();
            interactable = value;
            if (!value) selectedIndex = -1;
            Refresh();
        }

        /// <summary>Армия для боя: сами юниты по порядку слотов (бой их не меняет; итоги — ApplyBattleResults).</summary>
        public List<BenchUnit> GetArmy()
        {
            EnsureInitialized();
            return slots.Where(unit => unit != null && unit.data != null).ToList();
        }

        // =====================================================================
        //  Внутреннее
        // =====================================================================

        private int FirstFreeSlot()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null) return i;
            }
            return -1;
        }

        private void BuildViews()
        {
            views = new BenchSlotView[slots.Length];
            if (slotContainer == null || slotPrefab == null)
            {
                if (Application.isPlaying) Debug.LogError("[Bench] Назначьте Slot Container и Slot Prefab в инспекторе.");
                return;
            }

            // Контейнер заполняется кодом: убираем всё, что лежало в нём в сцене
            for (int i = slotContainer.childCount - 1; i >= 0; i--)
                Destroy(slotContainer.GetChild(i).gameObject);

            for (int i = 0; i < slots.Length; i++)
            {
                BenchSlotView view = Instantiate(slotPrefab, slotContainer);
                view.Init(this, i);
                views[i] = view;
            }
        }

        private void Refresh()
        {
            if (views != null)
            {
                for (int i = 0; i < views.Length; i++)
                {
                    if (views[i] != null) views[i].Bind(slots[i], i == selectedIndex);
                }
            }

            BenchUnit selectedUnit = selectedIndex >= 0 ? slots[selectedIndex] : null;
            if (infoText != null)
            {
                infoText.text = selectedUnit != null
                    ? Describe(selectedUnit)
                    : $"Армия: {UnitCount}/{slots.Length}. Три одинаковых юнита одной звёздности сами сливаются в одного ★ выше, " +
                      "уровень — средний. Ненужное снаряжение продаётся само. Погибшие в бою не возвращаются.";
            }
            if (sellButton != null) sellButton.interactable = interactable && selectedUnit != null;
        }

        private string Describe(BenchUnit unit)
        {
            UnitData data = unit.data;
            int armor = data.armor + unit.ItemArmor + unit.HeroArmor;
            CityManager city = game != null ? game.city : null;
            string kit = string.Empty; // щит воина и лук/арбалет стрелка — от науки и гильдий
            if (city != null && data.shieldBearer)
                kit = city.ShieldLevel >= 2 ? ", ростовой щит" : city.ShieldLevel == 1 ? ", щит" : ", без щита";
            else if (city != null && data.unitClass == UnitClass.Archer)
                kit = city.HasCrossbows ? ", арбалет" : ", лук";
            string gear = data.unitClass == UnitClass.None
                ? "Нейтралы не носят снаряжение"
                : $"Броня: {(EquippedItem.IsValid(unit.armor) ? unit.armor.Name : "нет")} · Оружие: {(EquippedItem.IsValid(unit.weapon) ? unit.weapon.Name : "нет")}";
            string heal = data.IsHealer ? $" · лечение {unit.Heal}" : string.Empty;
            string xp = unit.IsMaxLevel ? "макс. уровень" : $"опыт {unit.xp}/{unit.XpToNext}";
            string traits = unit.IsHero ? $"\n{unit.hero.Summary}" : string.Empty;
            return $"{unit.Name} {GameVisuals.Stars(unit.stars)} ({GameVisuals.ClassName(data.unitClass)}{kit}) · ур. {unit.level}, {xp}\n" +
                   $"ХП {unit.MaxHp} · АТК {unit.Attack}{heal} · броня {armor}{traits}\n" +
                   $"{gear}\nПродажа: {unit.SellPrice} {GameVisuals.IconGold}. Нажмите другой слот — переставить.";
        }
    }
}
