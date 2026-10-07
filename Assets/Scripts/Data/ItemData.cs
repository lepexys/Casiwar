using UnityEngine;

namespace Casiwar
{
    public enum ItemSlot
    {
        Armor,
        Weapon,
    }

    /// <summary>
    /// Снаряжение из «Трофеев» — крутки-награды после победы.
    /// Надевается только на юнитов своего класса: латы и меч — воинам, кожа и лук — лучникам,
    /// ряса и посох — жрецам. Нейтралы снаряжение не носят.
    /// Уровень предмета зависит от длины собранной линии: 3 → I, 4 → II, 5+ → III.
    /// Создание: ПКМ в Project → Create → Casiwar → Item Data.
    /// </summary>
    [CreateAssetMenu(fileName = "Item_New", menuName = "Casiwar/Item Data", order = 1)]
    public class ItemData : ScriptableObject
    {
        public const int MaxTier = 3;

        [Header("Описание")]
        public string itemName = "Предмет";
        public ItemSlot slot = ItemSlot.Armor;
        [Tooltip("Кто может надеть")]
        public UnitClass unitClass = UnitClass.Warrior;
        [Tooltip("Иконка на фишке трофеев. Можно оставить пустым — будет фигура с подписью.")]
        public Sprite icon;
        [Tooltip("Подпись на фишке, пока нет иконки")]
        public string shortLabel = "БРОНЯ";

        [Header("Бонус уровня I (II = ×1.6, III = ×2.4)")]
        [Tooltip("Броня: входящий урон × 100 / (100 + броня)")]
        [Min(0)] public int armor;
        [Tooltip("Прибавка к атаке")]
        [Min(0)] public int attack;
        [Tooltip("Цена продажи предмета I уровня (II — ×1.6, III — ×2.4). Ненужное снаряжение продаётся само")]
        [Min(0)] public int sellPrice = 2;

        public static float TierMultiplier(int tier)
        {
            if (tier <= 1) return 1f;
            return tier == 2 ? 1.6f : 2.4f;
        }

        public static string TierName(int tier)
        {
            if (tier <= 1) return "I";
            return tier == 2 ? "II" : "III";
        }

        public int GetArmor(int tier) => Mathf.RoundToInt(armor * TierMultiplier(tier));
        /// <summary>Сколько золота дают за предмет, который никому не нужен (продаётся сам).</summary>
        public int GetSellPrice(int tier) => Mathf.Max(1, Mathf.RoundToInt(sellPrice * TierMultiplier(tier)));
        public int GetAttack(int tier) => Mathf.RoundToInt(attack * TierMultiplier(tier));

        /// <summary>Сила предмета для сравнения «что лучше надеть».</summary>
        public int Score(int tier) => slot == ItemSlot.Armor ? GetArmor(tier) : GetAttack(tier);

        public string Describe(int tier)
        {
            string bonus = slot == ItemSlot.Armor ? $"+{GetArmor(tier)} брони" : $"+{GetAttack(tier)} атаки";
            return $"{itemName} {TierName(tier)} ({bonus})";
        }
    }
}
