using System.Collections.Generic;
using UnityEngine;

namespace Casiwar
{
    /// <summary>
    /// Черты богатыря — из каких символов сложена его линия на поле. Каждый символ даёт свою черту:
    ///   • воин — +12% ХП и +3 брони;
    ///   • лучник — +12% атаки, а с трёх лучников богатырь бьёт из лука на 3 клетки;
    ///   • жрец — «живая вода»: +3% ХП после каждого действия богатыря в бою;
    ///   • ополченец — «народная сила»: +6% ХП и атаки.
    /// Прозвище — по тем, кого в линии больше: витязь, стрелец, волхв или пахарь.
    /// Богатырь в слоте вожака ведёт отряд: весь отряд получает +8% ХП и атаки за каждую его ★,
    /// а воины в его линии — ещё +2% ХП, лучники — +2% атаки, ополченцы — +1% того и другого.
    /// </summary>
    [System.Serializable]
    public class HeroTraits
    {
        public const float HpPerWarrior = 0.12f;
        public const int ArmorPerWarrior = 3;
        public const float AttackPerArcher = 0.12f;
        public const int BowArchers = 3;
        public const int BowRange = 3;
        public const float RegenPerPriest = 0.03f;
        public const float MilitiaShare = 0.06f;
        public const float LeadPerStar = 0.08f;
        public const float LeadPerWarrior = 0.02f;
        public const float LeadPerArcher = 0.02f;
        public const float LeadPerMilitia = 0.01f;

        public int warriors;
        public int archers;
        public int priests;
        public int militia;
        /// <summary>Какой из обликов своей группы носит богатырь (выбирается при появлении, см. UnitArt).</summary>
        public int look;

        public static HeroTraits From(IEnumerable<TileData> tiles)
        {
            var traits = new HeroTraits();
            foreach (TileData tile in tiles)
            {
                if (tile == null || !tile.IsUnit) continue;
                switch (tile.Class)
                {
                    case UnitClass.Warrior: traits.warriors++; break;
                    case UnitClass.Archer: traits.archers++; break;
                    case UnitClass.Priest: traits.priests++; break;
                    case UnitClass.None: traits.militia++; break;
                }
            }
            traits.look = Random.Range(0, 1000);
            return traits;
        }

        public float HpMultiplier => 1f + warriors * HpPerWarrior + militia * MilitiaShare;
        public float AttackMultiplier => 1f + archers * AttackPerArcher + militia * MilitiaShare;
        public int BonusArmor => warriors * ArmorPerWarrior;
        public bool HasBow => archers >= BowArchers;
        public int Range(int baseRange) => HasBow ? Mathf.Max(baseRange, BowRange) : baseRange;
        /// <summary>Доля ХП, которую богатырь восстанавливает после каждого своего действия.</summary>
        public float RegenShare => priests * RegenPerPriest;

        /// <summary>Вожак (traits — его черты, у вождя варваров их нет): +доля ХП всему отряду.</summary>
        public static float LeadHp(HeroTraits traits, int stars) =>
            LeadPerStar * stars + (traits != null ? traits.warriors * LeadPerWarrior + traits.militia * LeadPerMilitia : 0f);

        /// <summary>Вожак: +доля атаки всему отряду.</summary>
        public static float LeadAttack(HeroTraits traits, int stars) =>
            LeadPerStar * stars + (traits != null ? traits.archers * LeadPerArcher + traits.militia * LeadPerMilitia : 0f);

        /// <summary>Кого в линии больше всех (ополченцы — None); false — поровну или линия пустая.</summary>
        public bool TryGetLeader(out UnitClass leader)
        {
            leader = UnitClass.None;
            int best = Mathf.Max(Mathf.Max(warriors, archers), Mathf.Max(priests, militia));
            if (best == 0) return false;
            int leaders = (warriors == best ? 1 : 0) + (archers == best ? 1 : 0) + (priests == best ? 1 : 0) + (militia == best ? 1 : 0);
            if (leaders > 1) return false;
            if (warriors == best) leader = UnitClass.Warrior;
            else if (archers == best) leader = UnitClass.Archer;
            else if (priests == best) leader = UnitClass.Priest;
            return true;
        }

        /// <summary>«витязь» / «стрелец» / «волхв» / «пахарь»; поровну — без прозвища.</summary>
        public string Nickname
        {
            get
            {
                if (!TryGetLeader(out UnitClass leader)) return string.Empty;
                switch (leader)
                {
                    case UnitClass.Warrior: return "витязь";
                    case UnitClass.Archer: return "стрелец";
                    case UnitClass.Priest: return "волхв";
                    default: return "пахарь";
                }
            }
        }

        /// <summary>«Линия: воины 3, жрецы 2 → +36% ХП, +9 брони, живая вода 6%».</summary>
        public string Summary
        {
            get
            {
                var parts = new List<string>();
                if (warriors > 0) parts.Add($"воины {warriors}");
                if (archers > 0) parts.Add($"лучники {archers}");
                if (priests > 0) parts.Add($"жрецы {priests}");
                if (militia > 0) parts.Add($"ополченцы {militia}");

                var effects = new List<string>();
                if (HpMultiplier > 1f) effects.Add($"+{Mathf.RoundToInt((HpMultiplier - 1f) * 100f)}% ХП");
                if (BonusArmor > 0) effects.Add($"+{BonusArmor} брони");
                if (AttackMultiplier > 1f) effects.Add($"+{Mathf.RoundToInt((AttackMultiplier - 1f) * 100f)}% атаки");
                if (HasBow) effects.Add($"лук на {BowRange} клетки");
                if (RegenShare > 0f) effects.Add($"живая вода {Mathf.RoundToInt(RegenShare * 100f)}%");
                return $"Линия: {string.Join(", ", parts)} → {string.Join(", ", effects)}";
            }
        }
    }
}
