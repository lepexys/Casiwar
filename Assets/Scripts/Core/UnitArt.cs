using System.Collections.Generic;
using UnityEngine;

namespace Casiwar
{
    /// <summary>
    /// Арт юнитов из Assets/Art/Resources (нарезан из UnitAssetsProper.png, UnitsAssetsGemini.png — воины и богатыри —
    /// и UnitsIcons.png):
    ///   Units/&lt;класс&gt;_&lt;1..3&gt; — фигурки для боя и скамейки (смотрят вправо, пивот у ног), вариант по звёздам;
    ///   Units/hero_* — облики богатыря (нижний ряд UnitsAssetsGemini.png), по тому, кого больше в его линии;
    ///   Icons/&lt;класс&gt; — символы на поле города и в боевом слоте;
    ///   Gear/&lt;класс&gt;_&lt;weapon|armor&gt;_&lt;1..3&gt; — снаряжение по уровням (GearAssets.png): трофеи и значки на скамейке;
    ///   Buildings/&lt;BuildingType&gt; — здания на карте мира (HouseAssets.png);
    ///   Symbols/&lt;имя&gt; — ресурсы, символы трофеев и поля боя, значки интерфейса (GeneredIcons.png);
    ///   Map/&lt;имя&gt; — лагерь варваров, чужие города и караван на карте мира (GeneredIcons.png).
    /// Нарезает их Tools/slice_art.py.
    /// Грузится из Resources, поэтому работает без пересборки сцены. Спрайт из UnitData.sprite важнее.
    /// </summary>
    public static class UnitArt
    {
        /// <summary>Высота «эталонной» фигурки в мировых единицах (200 px при 100 px/ед.): по ней масштаб в бою одинаков для всех.</summary>
        public const float FigureReference = 2f;

        // Облики богатыря по прозвищу: витязь — рыцари, стрелец — стрелки, волхв — святые, пахарь — старый воевода
        private static readonly string[] HeroKnights = { "hero_knight", "hero_redknight", "hero_paladin" };
        private static readonly string[] HeroShooters = { "hero_elf", "hero_dwarf", "hero_assassin" };
        private static readonly string[] HeroMystics = { "hero_monk", "hero_priest", "hero_priestess" };
        private static readonly string[] HeroPlowmen = { "hero_veteran" };
        private static readonly string[] HeroAll =
        {
            "hero_knight", "hero_redknight", "hero_monk", "hero_priest", "hero_elf",
            "hero_dwarf", "hero_priestess", "hero_paladin", "hero_assassin", "hero_veteran",
        };

        private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

        /// <summary>
        /// Фигурка юнита: арбалетчик вместо лучника, когда арбалеты куплены; у варваров шаман — некромант;
        /// у богатыря — облик по его линии (hero — его черты; у вождя варваров их нет — обычный золотой богатырь).
        /// </summary>
        public static Sprite Figure(UnitData data, int stars, bool crossbows = false, bool enemy = false, HeroTraits hero = null)
        {
            if (data == null) return null;
            if (data.sprite != null) return data.sprite;
            if (data.unitClass == UnitClass.Hero && hero != null)
            {
                Sprite look = HeroLook(hero);
                if (look != null) return look;
            }
            if (enemy && data.unitClass == UnitClass.Mage)
            {
                Sprite shaman = Load("Units/necromancer");
                if (shaman != null) return shaman;
            }
            string key = data.unitClass == UnitClass.Archer && crossbows ? "crossbow" : Key(data.unitClass);
            return Load($"Units/{key}_{Mathf.Clamp(stars, 1, UnitData.MaxStars)}");
        }

        private static Sprite HeroLook(HeroTraits hero)
        {
            string[] looks = HeroAll; // поровну — любой облик
            if (hero.TryGetLeader(out UnitClass leader))
            {
                switch (leader)
                {
                    case UnitClass.Warrior: looks = HeroKnights; break;
                    case UnitClass.Archer: looks = HeroShooters; break;
                    case UnitClass.Priest: looks = HeroMystics; break;
                    default: looks = HeroPlowmen; break;
                }
            }
            return Load("Units/" + looks[Mathf.Abs(hero.look) % looks.Length]);
        }

        /// <summary>Иконка снаряжения класса (воин, лучник, жрец) нужного уровня; null — такой нет.</summary>
        public static Sprite Gear(UnitClass unitClass, ItemSlot slot, int tier)
        {
            if (!UnitClasses.IsBase(unitClass)) return null;
            string kind = slot == ItemSlot.Weapon ? "weapon" : "armor";
            return Load($"Gear/{Key(unitClass)}_{kind}_{Mathf.Clamp(tier, 1, ItemData.MaxTier)}");
        }

        /// <summary>Рисунок здания на карте (null — нет, клетка рисуется цветной плашкой с подписью).</summary>
        public static Sprite Building(BuildingType type) => Load("Buildings/" + type);

        /// <summary>Иконка класса для символа поля: копьё, меч, лук, книга, кулак, посох, бомба, корона.</summary>
        public static Sprite Icon(UnitClass unitClass) => Load("Icons/" + Key(unitClass));

        /// <summary>Горожанин на поле вербовки: крестьянин с мотыгой.</summary>
        public static Sprite Civilian() => Load("Icons/civilian");

        /// <summary>Караван на карте мира: повозка с быком.</summary>
        public static Sprite Caravan() => Load("Map/caravan");

        /// <summary>Значок: gold, production, food, knowledge, population, star, loot_weapons, loot_armor, attack, march,
        /// banner, multiplier, candle, fire, health, turns, death, walls, spin (null — нет такого).</summary>
        public static Sprite Symbol(string name) => Load("Symbols/" + name);

        /// <summary>Объект карты мира: barbarian_camp, trade_city, hostile_city (null — нет).</summary>
        public static Sprite MapObject(string name) => Load("Map/" + name);

        private static string Key(UnitClass unitClass)
        {
            switch (unitClass)
            {
                case UnitClass.Warrior: return "warrior";
                case UnitClass.Archer: return "archer";
                case UnitClass.Priest: return "priest";
                case UnitClass.Bomber: return "bomber";
                case UnitClass.Monk: return "monk";
                case UnitClass.Mage: return "mage";
                case UnitClass.Hero: return "hero";
                default: return "militia";
            }
        }

        private static Sprite Load(string path)
        {
            if (!cache.TryGetValue(path, out Sprite sprite))
            {
                sprite = Resources.Load<Sprite>(path);
                cache[path] = sprite;
            }
            return sprite;
        }
    }
}
