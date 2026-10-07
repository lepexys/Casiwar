using UnityEngine;

namespace Casiwar
{
    /// <summary>
    /// Класс юнита.
    /// None — нейтрал (ополченец): в 3-в-ряд работает как джокер и складывается с ЛЮБЫМ классом.
    /// Базовые классы (воин, лучник, жрец) падают на поле, только если в городе есть их здание:
    /// казармы (воины — «Военное дело»), стрельбище (лучники — «Стрельба из лука»), святилище (жрецы — «Мистицизм»).
    /// Гибриды на поле не падают — они получаются из линии с символами двух классов, когда изучена их наука:
    /// воин + лучник → подрывник («Порох»), воин + жрец → монах («Боевые искусства»), лучник + жрец → маг («Чародейство»).
    /// Богатырь — из линии 5+ любых юнитов («Былины»); он всегда один.
    /// </summary>
    public enum UnitClass
    {
        None = 0,
        Warrior = 1,
        Archer = 2,
        Priest = 3, // жрец: лечит и молится (раньше назывался монахом)
        Bomber = 4, // подрывник: воин + лучник — бросает динамит
        Monk = 5,   // монах: воин + жрец — бьётся врукопашную, зажигает оружие своих
        Mage = 6,   // маг: лучник + жрец — боевые заклинания издалека
        Hero = 7,   // богатырь: 5+ любых символов в линию, один на армию
    }

    /// <summary>Как классы связаны: гибриды, их «родители», чьё снаряжение подходит.</summary>
    public static class UnitClasses
    {
        public static readonly UnitClass[] Base = { UnitClass.Warrior, UnitClass.Archer, UnitClass.Priest };
        public static readonly UnitClass[] Hybrids = { UnitClass.Bomber, UnitClass.Monk, UnitClass.Mage };

        public static bool IsBase(UnitClass unitClass) => unitClass == UnitClass.Warrior || unitClass == UnitClass.Archer || unitClass == UnitClass.Priest;
        public static bool IsHybrid(UnitClass unitClass) => unitClass == UnitClass.Bomber || unitClass == UnitClass.Monk || unitClass == UnitClass.Mage;

        /// <summary>Гибрид двух базовых классов (None — такого нет).</summary>
        public static UnitClass Combine(UnitClass a, UnitClass b)
        {
            if (a == b || !IsBase(a) || !IsBase(b)) return UnitClass.None;
            bool warrior = a == UnitClass.Warrior || b == UnitClass.Warrior;
            bool archer = a == UnitClass.Archer || b == UnitClass.Archer;
            if (warrior && archer) return UnitClass.Bomber;
            return warrior ? UnitClass.Monk : UnitClass.Mage;
        }

        /// <summary>Из каких базовых классов состоит класс (чьё снаряжение он носит).</summary>
        public static UnitClass[] Parents(UnitClass unitClass)
        {
            switch (unitClass)
            {
                case UnitClass.Warrior: return new[] { UnitClass.Warrior };
                case UnitClass.Archer: return new[] { UnitClass.Archer };
                case UnitClass.Priest: return new[] { UnitClass.Priest };
                case UnitClass.Bomber: return new[] { UnitClass.Warrior, UnitClass.Archer };
                case UnitClass.Monk: return new[] { UnitClass.Warrior, UnitClass.Priest };
                case UnitClass.Mage: return new[] { UnitClass.Archer, UnitClass.Priest };
                case UnitClass.Hero: return Base;
                default: return new UnitClass[0];
            }
        }

        /// <summary>Подходит ли юниту класса unitClass снаряжение класса itemClass.</summary>
        public static bool CanWear(UnitClass unitClass, UnitClass itemClass) =>
            itemClass != UnitClass.None && System.Array.IndexOf(Parents(unitClass), itemClass) >= 0;
    }

    /// <summary>
    /// Данные одного типа юнита. Создаются в окне Project: ПКМ → Create → Casiwar → Unit Data.
    /// Статы задаются для юнита 1★ 1-го уровня. Три одинаковых юнита одной звёздности сливаются в одного
    /// звездой выше (★★ — ×1.8, ★★★ — ×3.2), а уровни (опыт за бои) добавляют +10% за уровень.
    ///
    /// Бой идёт по клеткам: за ход юнит либо делает шаг (moveSpeed клеток), либо бьёт / лечит.
    /// У каждого юнита есть «лицо» — направление взгляда:
    ///   shieldBearer       — носит щит, когда щиты изучены: удар в лицо щит иногда блокирует целиком,
    ///                        иногда частично, а иногда пропускает (шансы — в AutoBattleManager);
    ///   backstabMultiplier — множитель урона при ударе в спину;
    ///   flanker            — юнит старается зайти цели за спину и прыгает (задел под будущих убийц).
    /// </summary>
    [CreateAssetMenu(fileName = "Unit_New", menuName = "Casiwar/Unit Data", order = 0)]
    public class UnitData : ScriptableObject
    {
        [Header("Описание")]
        public string unitName = "Ополченец";
        public UnitClass unitClass = UnitClass.None;
        [Tooltip("Иконка на сетке/скамейке и спрайт в бою (смотрит вправо). " +
                 "Можно оставить пустым — будет цветная заглушка.")]
        public Sprite sprite;

        [Header("Боевые параметры (1★, 1-й уровень)")]
        [Min(1)] public int maxHp = 180;
        [Min(0)] public int attack = 14;
        [Tooltip("Дальность атаки в клетках: 1 — ближний бой, 3 — лучники.")]
        [Min(1)] public int attackRange = 1;
        [Tooltip("Сколько клеток юнит проходит за ход.")]
        [Min(1)] public int moveSpeed = 1;
        [Tooltip("Природная броня: урон × 100 / (100 + броня). Снаряжение добавляется сверху.")]
        [Min(0)] public int armor;
        [Tooltip("Лечение за действие (жрецы): вместо удара лечит раненого союзника рядом.")]
        [Min(0)] public int heal;

        [Header("Направление: лицо, бок, спина")]
        [Tooltip("Носит щит, когда щиты изучены (воины). Своим щиты дают казармы, варвары берут их с определённого дня.")]
        public bool shieldBearer;
        [Tooltip("Множитель урона при ударе в спину. По боку — треть этого бонуса.")]
        [Min(1f)] public float backstabMultiplier = 1.25f;
        [Tooltip("Старается зайти цели за спину и прыгает через головы (задел под специализацию убийц).")]
        public bool flanker;

        [Header("Экономика")]
        [Tooltip("Цена продажи юнита 1★. ★★ стоит ×3, ★★★ — ×9; каждые 2 уровня — +1.")]
        [Min(0)] public int sellPrice = 1;
        [Tooltip("Вес внутри своего класса (если юнитов одного класса несколько).")]
        [Min(0f)] public float spawnWeight = 1f;

        public bool IsNeutral => unitClass == UnitClass.None;
        public bool IsHealer => heal > 0;

        /// <summary>
        /// Юнит с настройками по умолчанию — для гибридов и богатыря, если в сцене нет их ассетов
        /// (сцена собрана старой версией). Сборщик сцены создаёт ассеты с теми же числами.
        /// </summary>
        public static UnitData CreateDefault(UnitClass unitClass)
        {
            var unit = CreateInstance<UnitData>();
            unit.unitClass = unitClass;
            unit.name = "Unit_" + unitClass;
            Defaults(unitClass, out unit.unitName, out unit.maxHp, out unit.attack, out unit.attackRange, out unit.armor, out unit.heal,
                out unit.shieldBearer, out unit.sellPrice);
            return unit;
        }

        /// <summary>Числа гибридов и богатыря по умолчанию (их же берёт сборщик сцены).</summary>
        public static void Defaults(UnitClass unitClass, out string name, out int hp, out int attack, out int range, out int armor, out int heal,
            out bool shieldBearer, out int sell)
        {
            heal = 0;
            shieldBearer = false;
            switch (unitClass)
            {
                case UnitClass.Bomber:
                    name = "Подрывник"; hp = 160; attack = 24; range = 2; armor = 5; sell = 3;
                    break;
                case UnitClass.Monk:
                    name = "Монах"; hp = 280; attack = 26; range = 1; armor = 12; sell = 3;
                    break;
                case UnitClass.Mage:
                    name = "Маг"; hp = 120; attack = 24; range = 3; armor = 0; sell = 3;
                    break;
                case UnitClass.Hero:
                    name = "Богатырь"; hp = 700; attack = 45; range = 1; armor = 20; sell = 10; shieldBearer = true;
                    break;
                default:
                    name = "Ополченец"; hp = 160; attack = 20; range = 1; armor = 0; sell = 1;
                    break;
            }
        }

        /// <summary>Звёзды как в автобатлере: три одинаковых юнита одной звёздности → один звездой выше.</summary>
        public const int MaxStars = 3;
        /// <summary>Уровни как в «Цивилизации»: опыт за бои, каждый уровень — сильнее.</summary>
        public const int MaxLevel = 10;

        /// <summary>Статы за звёзды: ★ ×1, ★★ ×1.8, ★★★ ×3.2.</summary>
        public static float StarMultiplier(int stars)
        {
            if (stars <= 1) return 1f;
            return stars == 2 ? 1.8f : 3.2f;
        }

        /// <summary>Сколько юнитов 1★ «внутри»: 1, 3, 9.</summary>
        public static int CopiesForStars(int stars)
        {
            int copies = 1;
            for (int i = 1; i < Mathf.Clamp(stars, 1, MaxStars); i++) copies *= 3;
            return copies;
        }

        /// <summary>Статы за уровень: +10% за каждый уровень после первого (ур. 10 — ×1.9).</summary>
        public static float LevelMultiplier(int level) => 1f + 0.1f * (Mathf.Clamp(level, 1, MaxLevel) - 1);

        /// <summary>Опыта до следующего уровня: с 1-го на 2-й — 4, дальше на 1 больше за уровень.</summary>
        public static int XpToNext(int level) => Mathf.Clamp(level, 1, MaxLevel) + 3;

        public int GetMaxHp(int stars, int level) => Mathf.RoundToInt(maxHp * StarMultiplier(stars) * LevelMultiplier(level));
        public int GetAttack(int stars, int level) => Mathf.RoundToInt(attack * StarMultiplier(stars) * LevelMultiplier(level));
        public int GetHeal(int stars, int level) => Mathf.RoundToInt(heal * StarMultiplier(stars) * LevelMultiplier(level));
        public int GetSellPrice(int stars, int level) => sellPrice * CopiesForStars(stars) + (Mathf.Clamp(level, 1, MaxLevel) - 1) / 2;
    }
}
