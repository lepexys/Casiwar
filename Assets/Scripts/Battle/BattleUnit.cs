using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace Casiwar
{
    /// <summary>С какой стороны пришёл удар относительно взгляда цели.</summary>
    public enum HitSide
    {
        Front,
        Side,
        Back,
    }

    /// <summary>
    /// Юнит на клеточной арене. Создаётся AutoBattleManager-ом по данным скамейки — префаб не нужен.
    /// Бой идёт в реальном времени (Tick): готов таймер — удар по цели в радиусе (жрец лечит раненого рядом),
    /// а не достаёт — шаг к цели (жрецы — к раненым союзникам). Ряды на поле боя добавляют действия сверху:
    ///   MoveAction   (марш ➜): шаги к цели;
    ///   AttackAction (натиск ⚔): удар сразу, не дожидаясь таймера.
    /// У юнита есть «лицо» (Facing): удар в спину сильнее. Воин со щитом (щиты изучаются) ловит удары
    /// в лицо — иногда целиком, иногда частично, иногда щит не помогает; ростовой щит прикрывает и бок.
    /// Перемещение — только шагами (не дальше MoveSpeed клеток за действие), телепортов нет;
    /// «Рывок» воина — быстрый бросок по свободным клеткам (до нескольких клеток), тоже без телепорта.
    /// Оглушённый (таран щитом, землетрясение) несколько секунд не бьёт, не ходит и не участвует в навыках.
    /// Визуал строится кодом: тело, подставка цвета команды, стрелка взгляда, щит, полоска ХП,
    /// цифра уровня и звёзды-точки под юнитом (★★ и ★★★ ещё и крупнее).
    /// </summary>
    public class BattleUnit : MonoBehaviour
    {
        private const float LungeTime = 0.15f;
        private const float WoundedShare = 0.75f; // жрец лечит тех, у кого меньше 75% ХП
        private static readonly Color HitColor = new Color(1f, 0.35f, 0.35f);
        /// <summary>Варвары рисуются теми же фигурками, но чуть «кровавее».</summary>
        private static readonly Color EnemyTint = new Color(1f, 0.8f, 0.76f);
        /// <summary>Рост фигурки 1★ в долях клетки (★★ и ★★★ — на 8% и 16% выше).</summary>
        private const float FigureHeight = 0.88f;
        private static readonly Color ShieldColor = new Color(0.64f, 0.70f, 0.80f);
        private static readonly Color StunColor = new Color(1f, 0.88f, 0.30f, 0.95f);
        private static readonly Color BlessColor = new Color(1f, 0.92f, 0.55f);
        private static readonly Color HealColor = new Color(0.45f, 1f, 0.55f);
        private static readonly Color BleedColor = new Color(0.95f, 0.25f, 0.25f);

        public UnitData Data { get; private set; }
        /// <summary>Юнит скамейки, из которого создан боец (по нему после боя — опыт или гибель навсегда).</summary>
        public BenchUnit Source { get; private set; }
        /// <summary>Звёзды (три одинаковых → один звездой выше).</summary>
        public int Stars { get; private set; }
        /// <summary>Уровень (растёт с опытом за бои).</summary>
        public int Level { get; private set; }
        /// <summary>Сколько врагов добил в этом бою (за них опыт).</summary>
        public int Kills { get; private set; }
        public Team Team { get; private set; }
        public Vector2Int Cell { get; private set; }
        /// <summary>Куда смотрит юнит: один из 8 единичных векторов (компоненты -1..1).</summary>
        public Vector2Int Facing { get; private set; }
        public float MaxHp { get; private set; }
        public float Hp { get; private set; }
        public float AttackDamage { get; private set; }
        public float HealPower { get; private set; }
        /// <summary>«Живая вода» богатыря: доля ХП, которую он восстанавливает после своего действия.</summary>
        public float RegenShare { get; private set; }
        public int Armor { get; private set; }
        public int AttackRange { get; private set; }
        public int MoveSpeed { get; private set; }
        /// <summary>Щит: 0 — нет, 1 — обычный, 2 — ростовой.</summary>
        public int ShieldLevel { get; private set; }
        /// <summary>+шанс поймать удар щитом целиком (улучшения ветки ближнего боя).</summary>
        public float ShieldBonus { get; private set; }
        /// <summary>Арбалетные болты: цель реже ловит их щитом.</summary>
        public bool PiercesShields { get; private set; }
        public float BackstabMultiplier { get; private set; }
        public bool Flanker { get; private set; }
        /// <summary>Перемещается прыжком через занятые клетки (задел под специализацию убийц).</summary>
        public bool Leaps => Flanker && MoveSpeed > 1;
        public bool IsHealer => HealPower > 0f;
        /// <summary>«Обители»: уже вставал в этом бою.</summary>
        public bool SecondWindUsed { get; set; }
        public bool IsDead => Hp <= 0f;
        public bool IsWounded => !IsDead && Hp < MaxHp * WoundedShare;
        public BattleUnit CurrentTarget { get; private set; }
        /// <summary>Оглушён (таран щитом): несколько секунд не бьёт, не ходит, в навыках не участвует.</summary>
        public bool IsStunned => stunTimer > 0f;
        public bool IsBlessed => battle != null && battle.CurrentStep <= blessUntilStep;
        /// <summary>«Огненные клинки» монахов: удары ближнего боя сильнее.</summary>
        public bool IsOnFire => battle != null && battle.CurrentStep <= fireUntilStep;
        public Vector2 Position => transform.position;

        private AutoBattleManager battle;
        private float stunTimer;
        // «Огненные стрелы»: горит — теряет ХП каждую секунду
        private float burnTimer;
        private float burnDps;
        private BattleUnit burnSource;
        /// <summary>Сколько секунд до следующего своего действия (удара или шага).</summary>
        private float actionTimer;
        /// <summary>Шагов за одно перемещение (марш — больше; 0 — MoveSpeed).</summary>
        private int moveReach;
        private float chargeMultiplier = 1f; // «Рывок»: следующий удар сильнее
        private int chargeStun;              // «Таран щитом»: следующий удар оглушает на столько ходов цели
        private int blessUntilStep = -1;
        private float blessReduction;
        private int fireUntilStep = -1;
        private float fireBonus;
        private float blockTextCooldown;

        private Transform bodyRoot;
        private SpriteRenderer body;
        private SpriteRenderer teamBase;
        private SpriteRenderer facingMark;
        private SpriteRenderer shield;
        private SpriteRenderer blessRing;
        private SpriteRenderer fireRing;
        private SpriteRenderer stunMark;
        private SpriteRenderer hpBack;
        private SpriteRenderer hpFill;
        private readonly List<SpriteRenderer> starDots = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> hpTicks = new List<SpriteRenderer>();
        private TextMeshPro levelLabel;
        private Color bodyColor;
        private Color teamBaseColor;
        private Vector3 bodyScale;
        private float bodyRadius;
        private float barY;
        private Vector2 bodyOffset; // где стоит тело (у фигурки — ноги чуть ниже центра клетки)
        private float feetY;        // подставка, стрелка взгляда и щит — у ног
        private Vector3 moveFrom;
        private Vector3 moveTo;
        private float moveT = 1f;
        private float moveDuration = 0.2f;
        private float flashTimer;
        private float lungeTimer;
        private Vector2 lungeDirection;

        /// <summary>Инициализация (вызывает AutoBattleManager сразу после создания).</summary>
        public void Setup(AutoBattleManager owner, BenchUnit source, Team team, float statMultiplier, float healMultiplier,
            int shieldLevel, float shieldBonus, bool piercesShields, Vector2Int cell, Vector2Int facing)
        {
            battle = owner;
            Data = source.data;
            Source = source;
            Stars = Mathf.Clamp(source.stars, 1, UnitData.MaxStars);
            Level = Mathf.Clamp(source.level, 1, UnitData.MaxLevel);
            Kills = 0;
            Team = team;
            Cell = cell;
            Facing = facing;

            MaxHp = Data.GetMaxHp(Stars, Level) * statMultiplier;
            Hp = MaxHp;
            AttackDamage = Data.GetAttack(Stars, Level) * statMultiplier + source.ItemAttack;
            HealPower = Data.GetHeal(Stars, Level) * healMultiplier;
            Armor = Data.armor + source.ItemArmor;
            AttackRange = Mathf.Max(1, Data.attackRange);
            MoveSpeed = Mathf.Max(1, Data.moveSpeed);
            ShieldLevel = Data.shieldBearer ? Mathf.Clamp(shieldLevel, 0, 2) : 0;
            ShieldBonus = ShieldLevel > 0 ? Mathf.Max(0f, shieldBonus) : 0f;
            RegenShare = 0f;
            if (source.IsHero)
            {
                HeroTraits hero = source.hero;
                MaxHp *= hero.HpMultiplier;
                Hp = MaxHp;
                AttackDamage = Data.GetAttack(Stars, Level) * statMultiplier * hero.AttackMultiplier + source.ItemAttack;
                Armor += hero.BonusArmor;
                AttackRange = hero.Range(AttackRange);
                RegenShare = hero.RegenShare;
            }
            PiercesShields = piercesShields && AttackRange > 1;
            BackstabMultiplier = Mathf.Max(1f, Data.backstabMultiplier);
            Flanker = Data.flanker;

            actionTimer = Random.Range(0.1f, 0.8f); // не все разом
            name = $"{team}_{Data.unitName}_{Stars}*_L{Level}";
            transform.position = battle.CellToWorld(cell);
            moveFrom = moveTo = transform.position;
            BuildVisuals();
            UpdateVisuals(0f);
        }

        // =====================================================================
        //  Действия: сами (Tick) и от рядов поля боя
        // =====================================================================

        /// <summary>
        /// Кадр боя (dt — с учётом скорости боя): оглушение проходит; таймер готов — удар (лечение) по цели в радиусе,
        /// иначе шаг к ней.
        /// </summary>
        public void Tick(float dt)
        {
            if (IsDead) return;
            if (burnTimer > 0f)
            {
                burnTimer -= dt;
                float burn = burnDps * Mathf.Min(dt, burnTimer + dt);
                if (burnSource != null) burnSource.DealDamage(this, burn, ignoreArmor: true);
                else TakeDamage(burn, null, ignoreArmor: true);
                if (IsDead) return;
            }
            if (stunTimer > 0f)
            {
                stunTimer -= dt;
                return;
            }
            actionTimer -= dt;
            if (actionTimer > 0f) return;
            if (HasTargetInRange())
            {
                AttackAction();
                actionTimer = battle.AttackIntervalOf(this);
            }
            else if (WantsToMove())
            {
                MoveAction(1);
                actionTimer = battle.moveInterval / Mathf.Max(1, MoveSpeed);
            }
            else
            {
                actionTimer = 0.25f;
            }
        }

        /// <summary>Нужно ли юниту идти (нет цели в радиусе; жрецу — раненый союзник далеко).</summary>
        public bool WantsToMove()
        {
            if (IsDead || IsStunned) return false;
            if (IsHealer)
            {
                BattleUnit patient = battle.FindMostWounded(this);
                if (patient != null) return !InHealRange(patient);
            }
            BattleUnit target = RefreshTarget();
            if (target == null) return false;
            if (!InRange(target)) return true;
            return Flanker && AutoBattleManager.SideOf(target, Cell) != HitSide.Back && FindBackCellInReach(target).HasValue;
        }

        /// <summary>Может ли юнит что-то сделать по символу ⚔ (ударить или вылечить).</summary>
        public bool HasTargetInRange()
        {
            if (IsDead || IsStunned) return false;
            if (IsHealer)
            {
                BattleUnit patient = battle.FindMostWounded(this);
                if (patient != null && InHealRange(patient)) return true;
            }
            BattleUnit target = RefreshTarget();
            return target != null && InRange(target);
        }

        /// <summary>Шаг к цели (жрец — к раненому союзнику): не дальше cells клеток (0 — MoveSpeed); fast — бегом.</summary>
        public void MoveAction(int cells = 0, bool fast = false)
        {
            if (IsDead || IsStunned) return;
            moveReach = cells > 0 ? cells : MoveSpeed;
            Vector2Int? destination;
            BattleUnit patient = IsHealer ? battle.FindMostWounded(this) : null;
            if (patient != null && !InHealRange(patient))
            {
                destination = PathStep(c => AutoBattleManager.Distance(c, patient.Cell) <= HealRange, patient.Cell);
            }
            else
            {
                BattleUnit target = RefreshTarget();
                if (target == null) return;
                destination = ChooseDestination(target);
            }
            if (!destination.HasValue || destination.Value == Cell) return;

            Vector2Int from = Cell;
            battle.Relocate(this, destination.Value, fast);
            Facing = AutoBattleManager.DirectionBetween(from, Cell);
            if (CurrentTarget != null && !CurrentTarget.IsDead && InRange(CurrentTarget)) FaceTowards(CurrentTarget.Cell);
        }

        /// <summary>
        /// Символ ⚔: жрец лечит раненого рядом, остальные (и жрец без раненых) бьют цель в радиусе.
        /// power — множитель из слота (каскад и ×2 рядом с кластером).
        /// </summary>
        public void AttackAction(float power = 1f)
        {
            if (IsDead || IsStunned) return;
            if (IsHealer)
            {
                BattleUnit patient = battle.FindMostWounded(this);
                if (patient != null && InHealRange(patient))
                {
                    FaceTowards(patient.Cell);
                    BattleVfx.Bolt(Position, patient.Position, new Color(0.55f, 1f, 0.6f, 0.8f), 0.06f, 0.2f);
                    patient.Heal(HealPower * power);
                    float splash = battle.PlayerEffect(this, CityEffect.HealSplash); // «Молитвенник»
                    if (splash > 0f)
                    {
                        foreach (BattleUnit ally in battle.Units.Where(u => u != patient && !u.IsDead && u.Team == Team && AutoBattleManager.Distance(u.Cell, patient.Cell) <= 1).ToList())
                            ally.Heal(HealPower * power * splash);
                    }
                    if (battle.PlayerEffect(this, CityEffect.Cleanse) > 0f) patient.Cleanse(); // «Лечебные травы»
                    return;
                }
            }
            BattleUnit target = RefreshTarget();
            if (target != null && InRange(target)) Attack(target, power);
        }

        /// <summary>Удар по цели (используют и навыки). Учитывает, куда смотрит цель.</summary>
        public void Attack(BattleUnit target, float multiplier)
        {
            if (IsDead || target == null || target.IsDead) return;
            FaceTowards(target.Cell);

            HitSide side = AutoBattleManager.SideOf(target, Cell);
            float damage = AttackDamage * multiplier;
            // «Рывок» воина: накопленный разгон уходит в этот удар (со щитом — ещё и оглушает)
            float charge = chargeMultiplier;
            int stun = chargeStun;
            chargeMultiplier = 1f;
            chargeStun = 0;
            damage *= charge;
            if (charge >= 1.25f)
                BattleVfx.FloatingText(Position + Vector2.up * 0.45f, $"РЫВОК ×{charge:0.#}", new Color(0.7f, 0.9f, 1f), 2.6f);
            if (IsOnFire && AttackRange <= 1) damage *= 1f + fireBonus; // «Огненные клинки»
            if (side == HitSide.Back) damage *= BackstabMultiplier;
            else if (side == HitSide.Side) damage *= 1f + (BackstabMultiplier - 1f) * battle.sideBonusShare;

            Vector2 toTarget = target.Position - Position;
            UnitClass unitClass = Data.unitClass;
            if (unitClass == UnitClass.Mage)
                BattleVfx.Bolt(Position, target.Position, new Color(0.6f, 0.8f, 1f, 0.95f), 0.07f, 0.2f); // заклинание
            else if (unitClass == UnitClass.Bomber)
                BattleVfx.Bolt(Position, target.Position, new Color(0.95f, 0.55f, 0.15f, 0.9f), 0.06f, 0.2f); // динамитная шашка
            else if (AttackRange > 1)
            {
                if (PiercesShields) BattleVfx.Bolt(Position, target.Position, new Color(0.55f, 0.45f, 0.35f, 0.95f), 0.08f, 0.16f); // арбалетный болт
                else BattleVfx.Bolt(Position, target.Position, new Color(0.95f, 0.9f, 0.7f, 0.9f), 0.05f, 0.18f); // стрела
            }
            else
            {
                lungeTimer = LungeTime;
                lungeDirection = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : Vector2.zero;
            }
            if (side == HitSide.Back && BackstabMultiplier >= 1.5f)
                BattleVfx.FloatingText(target.Position + Vector2.up * 0.45f, $"В СПИНУ ×{BackstabMultiplier:0.#}", new Color(1f, 0.85f, 0.3f));

            if (unitClass == UnitClass.Mage)
            {
                DealDamage(target, damage, ignoreArmor: true); // магия: щит и броня не помогают
                return;
            }
            target.TakeDamage(damage, this);
            if (target.IsDead) Killed(target);
            else if (stun > 0) target.Stun(stun);
            // «Вихрь ударов»: второй удар с шансом
            if (!target.IsDead && battle.DoubleStrikeChance(this) > 0f && Random.value < battle.DoubleStrikeChance(this))
            {
                BattleVfx.FloatingText(target.Position + Vector2.up * 0.55f, "ЕЩЁ УДАР!", new Color(1f, 0.6f, 0.25f), 2.4f);
                target.TakeDamage(damage, this);
                if (target.IsDead) Killed(target);
            }
            AfterHit(target, damage);
            if (unitClass == UnitClass.Bomber)
            {
                // Шашка взрывается: задевает врагов рядом с целью
                BattleVfx.Ring(target.Position, battle.cellSize * 0.9f, new Color(1f, 0.55f, 0.15f, 0.6f), 0.3f);
                foreach (BattleUnit other in battle.Units.Where(u => !u.IsDead && u.Team != Team && u != target && AutoBattleManager.Distance(u.Cell, target.Cell) <= 1).ToList())
                    DealDamage(other, damage * battle.bomberSplashShare, ignoreArmor: false);
            }
        }

        /// <summary>Что ещё делает удар нашего бойца (дерево «Войск»): раны, пробивание, молния посохом, удар по кругу, вампиризм.</summary>
        private void AfterHit(BattleUnit target, float damage)
        {
            if (Team != Team.Player) return;
            float bleed = battle.PlayerEffect(this, CityEffect.Bleed); // «Бронзовые мечи»
            if (bleed > 0f && !target.IsDead) target.Ignite(this, AttackDamage * bleed, 3f, "РАНА!", BleedColor);

            float pierce = battle.PlayerEffect(this, CityEffect.Pierce); // «Составные луки»
            if (pierce > 0f && AttackRange > 1)
            {
                BattleUnit behind = battle.UnitAt(target.Cell + AutoBattleManager.DirectionBetween(Cell, target.Cell));
                if (behind != null && !behind.IsDead && behind.Team != Team)
                {
                    BattleVfx.Bolt(target.Position, behind.Position, new Color(0.95f, 0.9f, 0.7f, 0.9f), 0.05f, 0.18f);
                    DealDamage(behind, damage * pierce, ignoreArmor: false);
                }
            }

            float chain = battle.PlayerEffect(this, CityEffect.PriestChain); // «Посохи»
            if (chain > 0f && IsHealer)
            {
                BattleUnit next = battle.Units.Where(u => !u.IsDead && u.Team != Team && u != target && AutoBattleManager.Distance(u.Cell, target.Cell) <= 2)
                    .OrderBy(u => AutoBattleManager.Distance(u.Cell, target.Cell)).FirstOrDefault();
                if (next != null)
                {
                    BattleVfx.Bolt(target.Position, next.Position, new Color(0.85f, 0.75f, 1f, 0.95f), 0.06f, 0.25f);
                    DealDamage(next, damage * chain, ignoreArmor: true);
                }
            }

            float cleave = battle.PlayerEffect(this, CityEffect.HeroCleave); // «Богатырская застава»
            if (cleave > 0f && AttackRange <= 1)
            {
                BattleVfx.Ring(Position, battle.cellSize * 2.4f, new Color(1f, 0.85f, 0.4f, 0.6f), 0.35f);
                foreach (BattleUnit enemy in battle.Units.Where(u => u != target && !u.IsDead && u.Team != Team && AutoBattleManager.Distance(u.Cell, Cell) <= 1).ToList())
                    DealDamage(enemy, damage * cleave, ignoreArmor: false);
            }

            float steal = IsOnFire && AttackRange <= 1 ? battle.Lifesteal : 0f; // «Внутренний огонь»
            if (steal > 0f) Heal(damage * steal);
        }

        /// <summary>Урон от навыка (взрыв, молния): щитом не ловится; ignoreArmor — и броня не помогает. Добивание засчитывается.</summary>
        public void DealDamage(BattleUnit target, float amount, bool ignoreArmor)
        {
            if (target == null || target.IsDead) return;
            target.TakeDamage(amount, null, ignoreArmor);
            if (target.IsDead) Killed(target);
        }

        /// <summary>Добил врага: в счёт опыта и для «Знамён полков» (воин лечится).</summary>
        private void Killed(BattleUnit victim)
        {
            Kills++;
            battle.OnKill(this, victim);
        }

        /// <summary>Поджечь («Огненные стрелы»): dps урона в секунду, seconds секунд (добивание — source).</summary>
        public void Ignite(BattleUnit source, float dps, float seconds, string text = "ГОРИТ!", Color? color = null)
        {
            if (IsDead || dps <= 0f) return;
            if (burnTimer <= 0f) BattleVfx.FloatingText(Position + Vector2.up * 0.6f, text, color ?? new Color(1f, 0.55f, 0.2f), 2.4f);
            battle.Tally?.Burned(this);
            burnSource = source;
            burnDps = Mathf.Max(burnTimer > 0f ? burnDps : 0f, dps);
            burnTimer = Mathf.Max(burnTimer, seconds);
        }

        /// <summary>«Лечебные травы»: снять оглушение, горение и раны.</summary>
        public void Cleanse()
        {
            if (stunTimer <= 0f && burnTimer <= 0f) return;
            stunTimer = 0f;
            burnTimer = 0f;
            BattleVfx.FloatingText(Position + Vector2.up * 0.6f, "ОЧИЩЕН", HealColor, 2.2f);
        }

        /// <summary>«Тугая тетива»: стрелок бьёт дальше (только у тех, кто стреляет).</summary>
        public void AddRange(int cells)
        {
            if (cells > 0 && AttackRange > 1) AttackRange += cells;
        }

        public void TakeDamage(float amount, BattleUnit attacker, bool ignoreArmor = false)
        {
            if (IsDead || amount <= 0f) return;

            // «Школа кулака»: от удара можно увернуться (от навыков — нет)
            float dodge = attacker != null ? battle.PlayerEffect(this, CityEffect.Dodge) : 0f;
            if (dodge > 0f && Random.value < dodge)
            {
                ShowShieldText("УВОРОТ!");
                return;
            }

            // Щит: удар в лицо (ростовым — и в бок) иногда ловится целиком, иногда частично, иногда нет.
            // Урон без атакующего (не удар) щитом не ловится
            float blocked = attacker != null
                ? battle.RollShieldBlock(this, AutoBattleManager.SideOf(this, attacker.Cell), attacker.AttackRange > 1, attacker.PiercesShields)
                : 0f;
            if (blocked >= 1f)
            {
                ShowShieldText("БЛОК!");
                return;
            }
            if (blocked > 0f)
            {
                amount *= 1f - blocked;
                ShowShieldText($"щит −{Mathf.RoundToInt(blocked * 100f)}%");
            }
            float wall = battle.PlayerEffect(this, CityEffect.ShieldWall); // «Фаланга»: рядом свой — держим строй
            if (wall > 0f && battle.HasAdjacentAlly(this, Data.unitClass)) amount *= 1f - Mathf.Clamp01(wall);
            float tough = battle.PlayerEffect(this, CityEffect.Toughness); // «Богатырская застава»
            if (tough > 0f) amount *= 1f - Mathf.Clamp01(tough);
            if (IsBlessed) amount *= 1f - blessReduction;
            if (!ignoreArmor) amount *= 100f / (100f + Mathf.Max(0, Armor)); // броня

            float dealt = Mathf.Min(amount, Hp);
            Hp -= amount;
            battle.Tally?.Hit(this, dealt, Hp <= 0f);
            flashTimer = 0.1f;
            if (Hp <= 0f)
            {
                Hp = 0f;
                Die();
            }
        }

        public void Heal(float amount)
        {
            if (IsDead || amount <= 0f) return;
            float healed = Mathf.Min(amount, MaxHp - Hp);
            if (healed <= 0f) return;
            Hp += healed;
            battle.Tally?.Healed(this, healed);
            if (healed >= 1f) BattleVfx.FloatingText(Position + Vector2.up * 0.45f, $"+{Mathf.RoundToInt(healed)}", HealColor, 2.6f);
        }

        // =====================================================================
        //  Для навыков и арены
        // =====================================================================

        public void FaceTowards(Vector2Int cell)
        {
            Vector2Int direction = AutoBattleManager.DirectionBetween(Cell, cell);
            if (direction != Vector2Int.zero) Facing = direction;
        }

        /// <summary>Смена клетки (только через AutoBattleManager.Relocate — он ведёт учёт занятости).</summary>
        public void SetCell(Vector2Int cell, bool fast)
        {
            Cell = cell;
            moveFrom = transform.position;
            moveTo = battle.CellToWorld(cell);
            moveT = 0f;
            moveDuration = fast ? 0.1f : 0.22f;
        }

        /// <summary>
        /// «Рывок» воина: бросок к ближайшему врагу по свободным клеткам (не дальше range). Чем дальше пролетел,
        /// тем сильнее удар — +bonusPerCell за клетку (× power навыка). Достал цель — бьёт сразу,
        /// не достал — разгон копится до следующего удара. stun > 0 (у воинов щиты) — это таран щитом:
        /// удар оглушает цель на stun её ходов.
        /// </summary>
        public void Charge(float power, int range, float bonusPerCell, int stun)
        {
            if (IsDead || IsStunned) return;
            BattleUnit target = battle.FindNearestEnemy(this);
            if (target == null) return;
            CurrentTarget = target;

            int cells = 0;
            if (!InRange(target))
            {
                List<Vector2Int> path = battle.FindPath(Cell, c => AutoBattleManager.Distance(c, target.Cell) <= AttackRange);
                if (path != null && path.Count > 0)
                {
                    cells = Mathf.Min(Mathf.Max(1, range), path.Count);
                    Vector2Int from = Cell;
                    Vector2 start = Position;
                    battle.Relocate(this, path[cells - 1], fast: true);
                    Facing = AutoBattleManager.DirectionBetween(from, Cell);
                    BattleVfx.Bolt(start, battle.CellToWorld(Cell), new Color(0.8f, 0.9f, 1f, 0.55f), 0.14f, 0.25f); // след рывка
                }
            }
            chargeMultiplier = Mathf.Max(chargeMultiplier, Mathf.Max(1f, power) * (1f + bonusPerCell * cells));
            chargeStun = Mathf.Max(chargeStun, stun);
            if (InRange(target)) Attack(target, 1f);
        }

        /// <summary>Бонус вожака перед боем: +доля ХП (полоска остаётся полной) и +доля атаки.</summary>
        public void ApplyLeadership(float hpBonus, float attackBonus)
        {
            MaxHp *= 1f + Mathf.Max(0f, hpBonus);
            Hp = MaxHp;
            AttackDamage *= 1f + Mathf.Max(0f, attackBonus);
            if (hpBack != null) BuildHpTicks(); // деления — по новому ХП
        }

        /// <summary>
        /// «Воскрешение» жрецов: павший встаёт в свободной клетке cell с долей ХП (занятость клетки ведёт AutoBattleManager).
        /// </summary>
        public void Revive(Vector2Int cell, float hpShare)
        {
            if (!IsDead) return;
            StopAllCoroutines(); // гасим растворение
            gameObject.SetActive(true);
            Hp = Mathf.Clamp(MaxHp * hpShare, 1f, MaxHp);
            stunTimer = 0f;
            burnTimer = 0f;
            actionTimer = 0.5f;
            chargeMultiplier = 1f;
            chargeStun = 0;
            CurrentTarget = null;
            Cell = cell;
            transform.position = moveFrom = moveTo = battle.CellToWorld(cell);
            moveT = 1f;
            body.color = bodyColor;
            teamBase.color = teamBaseColor;
            bodyRoot.localScale = bodyScale;
            hpBack.enabled = true;
            hpFill.enabled = true;
            facingMark.enabled = true;
            if (shield != null) shield.enabled = true;
            foreach (SpriteRenderer dot in starDots) dot.enabled = true;
            foreach (SpriteRenderer tick in hpTicks) tick.enabled = true;
            if (levelLabel != null) levelLabel.enabled = true;
            BattleVfx.Ring(Position, 0.6f, BlessColor, 0.6f);
            BattleVfx.FloatingText(Position + Vector2.up * 0.6f, "ВОСКРЕС!", BlessColor, 3f);
            battle.Tally?.Revived(this);
        }

        /// <summary>Оглушить на turns «ходов» (каждый — AutoBattleManager.stunSecondsPerTurn секунд).</summary>
        public void Stun(int turns)
        {
            if (IsDead || turns <= 0) return;
            stunTimer = Mathf.Max(stunTimer, turns * battle.stunSecondsPerTurn);
            battle.Tally?.Stunned(this);
            BattleVfx.FloatingText(Position + Vector2.up * 0.6f, turns > 1 ? $"ОГЛУШЁН ×{turns}" : "ОГЛУШЁН", StunColor, 2.6f);
        }

        private void ShowShieldText(string text)
        {
            if (blockTextCooldown > 0f) return;
            BattleVfx.FloatingText(Position + Vector2.up * 0.45f, text, ShieldColor, 2.6f);
            blockTextCooldown = 0.5f;
        }

        /// <summary>«Огненные клинки» монахов: удары ближнего боя сильнее на bonus до шага untilStep.</summary>
        public void ApplyFireBlades(int untilStep, float bonus)
        {
            fireUntilStep = Mathf.Max(fireUntilStep, untilStep);
            fireBonus = Mathf.Max(fireBonus, bonus);
            battle.Tally?.Buffed(this);
        }

        /// <summary>Благословение жрецов: входящий урон меньше на reduction до шага untilStep.</summary>
        public void ApplyBlessing(int untilStep, float reduction)
        {
            blessUntilStep = Mathf.Max(blessUntilStep, untilStep);
            blessReduction = Mathf.Clamp01(Mathf.Max(blessReduction, reduction));
            battle.Tally?.Buffed(this);
        }

        // =====================================================================
        //  Цель и путь
        // =====================================================================

        private int HealRange => AttackRange + 1;

        private bool InHealRange(BattleUnit ally) => AutoBattleManager.Distance(Cell, ally.Cell) <= HealRange;

        private BattleUnit RefreshTarget()
        {
            // Цель «липкая»: пока жива и достаётся — не меняем (так её можно обойти и ударить в спину)
            if (CurrentTarget != null && !CurrentTarget.IsDead && InRange(CurrentTarget)) return CurrentTarget;
            if (Flanker && CurrentTarget != null && !CurrentTarget.IsDead) return CurrentTarget;

            CurrentTarget = Flanker ? battle.FindWeakestEnemy(this) : battle.FindNearestEnemy(this);
            return CurrentTarget;
        }

        private bool InRange(BattleUnit target) => AutoBattleManager.Distance(Cell, target.Cell) <= AttackRange;

        private Vector2Int? ChooseDestination(BattleUnit target)
        {
            if (Flanker && InRange(target)) return FindBackCellInReach(target); // рядом, но не за спиной — обходим

            System.Predicate<Vector2Int> goal;
            if (AttackRange > 1)
                goal = c => AutoBattleManager.Distance(c, target.Cell) <= AttackRange;
            else if (Flanker && battle.HasFreeBackCell(target))
                goal = c => AutoBattleManager.IsBackCell(target, c);
            else
                goal = c => AutoBattleManager.Distance(c, target.Cell) == 1;

            if (Leaps) return battle.BestLeapCell(this, goal, target);
            return PathStep(goal, target.Cell);
        }

        private Vector2Int? PathStep(System.Predicate<Vector2Int> goal, Vector2Int towards)
        {
            List<Vector2Int> path = battle.FindPath(Cell, goal);
            int reach = moveReach > 0 ? moveReach : MoveSpeed;
            if (path != null && path.Count > 0) return path[Mathf.Min(reach, path.Count) - 1];
            return battle.GreedyStep(this, towards);
        }

        /// <summary>Свободная клетка за спиной цели, до которой можно дойти/допрыгнуть за одно перемещение.</summary>
        private Vector2Int? FindBackCellInReach(BattleUnit target)
        {
            if (Leaps)
            {
                Vector2Int? best = null;
                foreach (Vector2Int cell in AutoBattleManager.Neighbors(target.Cell))
                {
                    if (!battle.IsFree(cell) || !AutoBattleManager.IsBackCell(target, cell)) continue;
                    if (AutoBattleManager.Distance(Cell, cell) > MoveSpeed) continue;
                    if (!best.HasValue || AutoBattleManager.Distance(Cell, cell) < AutoBattleManager.Distance(Cell, best.Value)) best = cell;
                }
                return best;
            }

            List<Vector2Int> path = battle.FindPath(Cell, c => AutoBattleManager.IsBackCell(target, c));
            return path != null && path.Count > 0 && path.Count <= MoveSpeed ? path[path.Count - 1] : (Vector2Int?)null;
        }

        private void Die()
        {
            CurrentTarget = null;
            battle.OnUnitDied(this);
            hpBack.enabled = false;
            hpFill.enabled = false;
            facingMark.enabled = false;
            blessRing.enabled = false;
            fireRing.enabled = false;
            stunMark.enabled = false;
            if (shield != null) shield.enabled = false;
            foreach (SpriteRenderer dot in starDots) dot.enabled = false;
            foreach (SpriteRenderer tick in hpTicks) tick.enabled = false;
            if (levelLabel != null) levelLabel.enabled = false;
            StartCoroutine(DeathRoutine());
        }

        private IEnumerator DeathRoutine()
        {
            Color start = body.color * 0.6f;
            Color startBase = teamBase.color;
            Vector3 startScale = bodyRoot.localScale;
            const float duration = 0.4f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float k = 1f - t / duration;
                body.color = new Color(start.r, start.g, start.b, k);
                teamBase.color = new Color(startBase.r, startBase.g, startBase.b, startBase.a * k);
                bodyRoot.localScale = startScale * (0.6f + 0.4f * k);
                yield return null;
            }
            gameObject.SetActive(false);
        }

        // =====================================================================
        //  Визуал
        // =====================================================================

        private void BuildVisuals()
        {
            float cell = battle.cellSize;
            Color teamColor = Team == Team.Player ? GameVisuals.PlayerColor : GameVisuals.EnemyColor;
            float diameter = cell * 0.62f * (1f + 0.12f * (Stars - 1)); // ★★ и ★★★ крупнее
            bodyRadius = diameter * 0.5f;

            // Тело: фигурка из UnitArt (смотрит вправо, пивот у ног) или круг цвета класса
            bodyRoot = new GameObject("Body").transform;
            bodyRoot.SetParent(transform, false);
            body = bodyRoot.gameObject.AddComponent<SpriteRenderer>();
            Sprite figure = UnitArt.Figure(Data, Stars, PiercesShields, Team == Team.Enemy, Source.hero);
            float top, centerY;
            if (figure != null)
            {
                body.sprite = figure;
                bodyColor = Team == Team.Enemy ? EnemyTint : Color.white;
                float reference = Data.sprite != null ? Mathf.Max(figure.bounds.size.x, figure.bounds.size.y, 0.001f) : UnitArt.FigureReference;
                float scale = cell * FigureHeight * (1f + 0.08f * (Stars - 1)) / reference;
                bodyRoot.localScale = Vector3.one * scale;
                feetY = -cell * 0.34f;
                bodyOffset = new Vector2(0f, feetY - figure.bounds.min.y * scale);
                top = bodyOffset.y + figure.bounds.max.y * scale;
                centerY = (feetY + top) * 0.5f;
                body.flipX = Team == Team.Enemy; // варвары идут сверху и смотрят влево
            }
            else
            {
                body.sprite = GameVisuals.Circle;
                bodyColor = GameVisuals.ClassColor(Data.unitClass);
                bodyRoot.localScale = Vector3.one * (diameter / Mathf.Max(body.sprite.bounds.size.x, 0.001f));
                feetY = -bodyRadius * 0.55f;
                bodyOffset = Vector2.zero;
                top = bodyRadius;
                centerY = 0f;
            }
            body.color = bodyColor;
            bodyRoot.localPosition = bodyOffset;
            float auraHeight = Mathf.Max(diameter * 1.3f, (top - feetY) * 1.05f);

            // Подставка цвета команды под ногами — свои и враги различимы сразу
            teamBase = CreatePart("TeamBase", GameVisuals.Circle, new Color(teamColor.r, teamColor.g, teamColor.b, 0.55f),
                new Vector2(0f, feetY), new Vector2(diameter * 1.25f, diameter * 0.45f));
            teamBaseColor = teamBase.color;
            bodyScale = bodyRoot.localScale;

            // Ореол благословения (виден, пока действует «Молитва»)
            blessRing = CreatePart("Blessing", GameVisuals.Ring, BlessColor, new Vector2(0f, centerY), new Vector2(diameter * 1.3f, auraHeight));
            blessRing.enabled = false;

            // Огненные клинки монахов (виден, пока горит оружие)
            fireRing = CreatePart("Fire", GameVisuals.Ring, new Color(1f, 0.5f, 0.15f, 0.9f), new Vector2(0f, centerY),
                new Vector2(diameter * 1.12f, auraHeight * 0.9f));
            fireRing.enabled = false;

            // Стрелка «куда смотрю» и щит — по краю подставки (если щиты изучены; ростовой — шире и толще)
            facingMark = CreatePart("Facing", GameVisuals.Triangle, teamColor, Vector2.zero, new Vector2(diameter * 0.26f, diameter * 0.26f));
            if (ShieldLevel > 0)
                shield = CreatePart("Shield", GameVisuals.Square, ShieldColor, Vector2.zero, new Vector2(diameter * 0.75f, diameter * 0.14f));

            // Полоска ХП над головой: слева цифра уровня, справа звёзды-точки
            barY = top + 0.06f;
            float barWidth = cell * 0.8f;
            hpBack = CreatePart("HpBack", GameVisuals.Square, new Color(0f, 0f, 0f, 0.65f), new Vector2(0f, barY), new Vector2(barWidth + 0.04f, 0.09f));
            hpFill = CreatePart("HpFill", GameVisuals.Square,
                Team == Team.Player ? new Color(0.35f, 0.9f, 0.4f) : new Color(0.95f, 0.3f, 0.25f),
                new Vector2(0f, barY), new Vector2(barWidth, 0.06f));
            BuildHpTicks();
            levelLabel = CreateLabel("Level", new Vector2(-barWidth * 0.5f - 0.09f, barY), 2f, new Color(1f, 0.85f, 0.25f), new Vector2(0.3f, 0.25f));
            levelLabel.text = Level.ToString();

            // Оглушение: жёлтое кольцо над полоской ХП
            stunMark = CreatePart("Stun", GameVisuals.Ring, StunColor, new Vector2(0f, barY + 0.13f), new Vector2(diameter * 0.55f, diameter * 0.22f));
            stunMark.enabled = false;
            if (Stars > 1)
            {
                for (int i = 0; i < Stars; i++)
                {
                    float x = barWidth * 0.5f + 0.07f + i * 0.075f;
                    starDots.Add(CreatePart("Star", GameVisuals.Circle, new Color(1f, 0.85f, 0.25f), new Vector2(x, barY), new Vector2(0.065f, 0.065f)));
                }
            }
        }

        /// <summary>Шаг делений полоски ХП: 100, а если делений выходит больше 15 — 250, 500 или 1000.</summary>
        private static float HpTickStep(float maxHp)
        {
            foreach (float step in new[] { 100f, 250f, 500f })
            {
                if (maxHp / step <= 15f) return step;
            }
            return 1000f;
        }

        private TextMeshPro CreateLabel(string partName, Vector2 localPosition, float fontSize, Color color, Vector2 size)
        {
            var go = new GameObject(partName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPosition;
            var text = go.AddComponent<TextMeshPro>();
            text.fontSize = fontSize;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.color = color;
            text.rectTransform.sizeDelta = size;
            return text;
        }


        /// <summary>Деления на полоске ХП: каждое — 100 ХП (у толстых — 250/500/1000), крупные — каждые 1000.</summary>
        private void BuildHpTicks()
        {
            foreach (SpriteRenderer old in hpTicks)
            {
                if (old != null) Destroy(old.gameObject);
            }
            hpTicks.Clear();
            float barWidth = battle.cellSize * 0.8f;
            float tickStep = HpTickStep(MaxHp);
            for (int i = 1; i * tickStep < MaxHp - 1f; i++)
            {
                float hp = i * tickStep;
                bool major = tickStep < 1000f && Mathf.Approximately(hp % 1000f, 0f);
                float x = -barWidth * 0.5f + barWidth * (hp / MaxHp);
                hpTicks.Add(CreatePart("HpTick", GameVisuals.Square, new Color(0f, 0f, 0f, major ? 0.95f : 0.7f),
                    new Vector2(x, barY), new Vector2(major ? 0.02f : 0.012f, major ? 0.09f : 0.06f)));
            }
        }

        private SpriteRenderer CreatePart(string partName, Sprite sprite, Color color, Vector2 localPosition, Vector2 scale)
        {
            var go = new GameObject(partName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = new Vector3(scale.x, scale.y, 1f);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            return renderer;
        }

        private void Update()
        {
            if (battle == null) return;
            float dt = Time.deltaTime * battle.battleSpeed;

            if (moveT < 1f)
            {
                moveT = Mathf.Min(1f, moveT + dt / moveDuration);
                transform.position = Vector3.Lerp(moveFrom, moveTo, Mathf.SmoothStep(0f, 1f, moveT));
            }
            blockTextCooldown -= dt;
            if (!IsDead) UpdateVisuals(dt);
        }

        private void UpdateVisuals(float dt)
        {
            // Выпад в сторону цели при ударе
            if (lungeTimer > 0f) lungeTimer -= dt;
            float lunge = Mathf.Sin(Mathf.Clamp01(lungeTimer / LungeTime) * Mathf.PI);
            bodyRoot.localPosition = bodyOffset + lungeDirection * (0.12f * lunge);

            // Вспышка при уроне
            if (flashTimer > 0f) flashTimer -= dt;
            body.color = flashTimer > 0f ? HitColor : IsStunned ? Color.Lerp(bodyColor, Color.gray, 0.5f) : bodyColor;
            blessRing.enabled = IsBlessed;
            fireRing.enabled = IsOnFire || burnTimer > 0f;
            stunMark.enabled = IsStunned;
            if (IsStunned)
            {
                float wobble = 1f + 0.15f * Mathf.Sin(Time.time * 8f);
                stunMark.transform.localScale = new Vector3(bodyRadius * 1.1f * wobble, bodyRadius * 0.44f, 1f);
            }

            // Фигурка разворачивается влево/вправо, стрелка взгляда и щит ходят по краю подставки вслед за Facing
            if (Facing.x != 0 && body.sprite != GameVisuals.Circle) body.flipX = Facing.x < 0;
            Vector2 facing = ((Vector2)Facing).normalized;
            float angle = Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg;
            var rim = new Vector2(bodyRadius * 1.25f, bodyRadius * 0.5f);
            facingMark.transform.localPosition = new Vector2(facing.x * (rim.x + 0.03f), feetY + facing.y * (rim.y + 0.03f));
            facingMark.transform.localRotation = Quaternion.Euler(0f, 0f, angle - 90f);
            if (shield != null)
            {
                bool tower = ShieldLevel >= 2;
                float width = bodyRadius * (tower ? 1.9f : 1.5f);
                shield.transform.localPosition = new Vector2(facing.x * (rim.x + 0.05f), feetY + facing.y * (rim.y + 0.05f));
                shield.transform.localRotation = Quaternion.Euler(0f, 0f, angle - 90f);
                shield.transform.localScale = new Vector3(width, tower ? 0.12f : 0.08f, 1f);
                shield.color = ShieldColor;
            }

            // Полоска ХП тает справа налево
            float share = Mathf.Clamp01(Hp / MaxHp);
            float barWidth = battle.cellSize * 0.8f;
            Transform bar = hpFill.transform;
            bar.localScale = new Vector3(barWidth * share, bar.localScale.y, 1f);
            bar.localPosition = new Vector3(-barWidth * 0.5f + barWidth * share * 0.5f, barY, 0f);

            // Сортировка «чем ниже на экране — тем ближе к камере»
            int order = Mathf.RoundToInt(-transform.position.y * 100f);
            teamBase.sortingOrder = -10000 + order;
            blessRing.sortingOrder = order - 1;
            fireRing.sortingOrder = order - 1;
            stunMark.sortingOrder = order + 6;
            body.sortingOrder = order;
            facingMark.sortingOrder = order + 1;
            if (shield != null) shield.sortingOrder = order + 2;
            hpBack.sortingOrder = order + 3;
            hpFill.sortingOrder = order + 4;
            foreach (SpriteRenderer dot in starDots) dot.sortingOrder = order + 5;
            foreach (SpriteRenderer tick in hpTicks) tick.sortingOrder = order + 5;
            if (levelLabel != null) levelLabel.sortingOrder = order + 5;
        }
    }
}
