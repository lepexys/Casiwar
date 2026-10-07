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
    /// Сам юнит ничего не делает: действия выдаёт боевой слот —
    ///   MoveAction   (символы ➜): шаг к цели (жрецы — к раненым союзникам);
    ///   AttackAction (символы ⚔): удар по цели в радиусе; жрец вместо удара лечит раненого рядом.
    /// У юнита есть «лицо» (Facing): удар в спину сильнее. Воин со щитом (щиты изучаются) ловит удары
    /// в лицо — иногда целиком, иногда частично, иногда щит не помогает; ростовой щит прикрывает и бок.
    /// Перемещение — только шагами (не дальше MoveSpeed клеток за действие), телепортов нет.
    /// Визуал строится кодом: тело, подставка цвета команды, стрелка взгляда, щит, полоска ХП,
    /// цифра уровня и звёзды-точки под юнитом (★★ и ★★★ ещё и крупнее).
    /// </summary>
    public class BattleUnit : MonoBehaviour
    {
        private const float LungeTime = 0.15f;
        private const float WoundedShare = 0.75f; // жрец лечит тех, у кого меньше 75% ХП
        private static readonly Color HitColor = new Color(1f, 0.35f, 0.35f);
        private static readonly Color ShieldColor = new Color(0.64f, 0.70f, 0.80f);
        private static readonly Color ShieldWallColor = new Color(0.45f, 0.80f, 1f);
        private static readonly Color BlessColor = new Color(1f, 0.92f, 0.55f);
        private static readonly Color HealColor = new Color(0.45f, 1f, 0.55f);

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
        public bool IsDead => Hp <= 0f;
        public bool IsWounded => !IsDead && Hp < MaxHp * WoundedShare;
        public BattleUnit CurrentTarget { get; private set; }
        public bool IsShieldWall => battle != null && battle.CurrentStep <= shieldWallUntilStep;
        /// <summary>Прибавка к шансу полного блока, пока держится стена щитов.</summary>
        public float ShieldWallBonus => IsShieldWall ? shieldWallBonus : 0f;
        /// <summary>Место в строю (навык воинов): к нему юнит идёт шагами.</summary>
        public Vector2Int? FormationCell { get; private set; }
        public bool IsBlessed => battle != null && battle.CurrentStep <= blessUntilStep;
        /// <summary>«Огненные клинки» монахов: удары ближнего боя сильнее.</summary>
        public bool IsOnFire => battle != null && battle.CurrentStep <= fireUntilStep;
        public Vector2 Position => transform.position;

        private AutoBattleManager battle;
        private int shieldWallUntilStep = -1;
        private float shieldWallBonus;
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
        private SpriteRenderer hpBack;
        private SpriteRenderer hpFill;
        private readonly List<SpriteRenderer> starDots = new List<SpriteRenderer>();
        private TextMeshPro levelLabel;
        private Color bodyColor;
        private float bodyRadius;
        private float barY;
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
            PiercesShields = piercesShields && AttackRange > 1;
            BackstabMultiplier = Mathf.Max(1f, Data.backstabMultiplier);
            Flanker = Data.flanker;

            name = $"{team}_{Data.unitName}_{Stars}*_L{Level}";
            transform.position = battle.CellToWorld(cell);
            moveFrom = moveTo = transform.position;
            BuildVisuals();
            UpdateVisuals(0f);
        }

        // =====================================================================
        //  Действия от боевого слота
        // =====================================================================

        /// <summary>Нужно ли юниту идти (нет цели в радиусе; жрецу — раненый союзник далеко).</summary>
        public bool WantsToMove()
        {
            if (IsDead) return false;
            if (IsShieldWall) return FormationCell.HasValue && FormationCell.Value != Cell; // строй: идём к своему месту и стоим
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
            if (IsDead) return false;
            if (IsHealer)
            {
                BattleUnit patient = battle.FindMostWounded(this);
                if (patient != null && InHealRange(patient)) return true;
            }
            BattleUnit target = RefreshTarget();
            return target != null && InRange(target);
        }

        /// <summary>Символ ➜: шаг к цели (жрец — к раненому союзнику).</summary>
        public void MoveAction()
        {
            if (IsDead) return;
            if (IsShieldWall && FormationCell.HasValue)
            {
                StepTowardsFormation();
                return;
            }
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
            battle.Relocate(this, destination.Value);
            Facing = AutoBattleManager.DirectionBetween(from, Cell);
            if (CurrentTarget != null && !CurrentTarget.IsDead && InRange(CurrentTarget)) FaceTowards(CurrentTarget.Cell);
        }

        /// <summary>
        /// Символ ⚔: жрец лечит раненого рядом, остальные (и жрец без раненых) бьют цель в радиусе.
        /// power — множитель из слота (каскад и ×2 рядом с кластером).
        /// </summary>
        public void AttackAction(float power = 1f)
        {
            if (IsDead) return;
            if (IsHealer)
            {
                BattleUnit patient = battle.FindMostWounded(this);
                if (patient != null && InHealRange(patient))
                {
                    FaceTowards(patient.Cell);
                    BattleVfx.Bolt(Position, patient.Position, new Color(0.55f, 1f, 0.6f, 0.8f), 0.06f, 0.2f);
                    patient.Heal(HealPower * power);
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
            if (target.IsDead) Kills++;
            if (unitClass == UnitClass.Bomber)
            {
                // Шашка взрывается: задевает врагов рядом с целью
                BattleVfx.Ring(target.Position, battle.cellSize * 0.9f, new Color(1f, 0.55f, 0.15f, 0.6f), 0.3f);
                foreach (BattleUnit other in battle.Units.Where(u => !u.IsDead && u.Team != Team && u != target && AutoBattleManager.Distance(u.Cell, target.Cell) <= 1).ToList())
                    DealDamage(other, damage * battle.bomberSplashShare, ignoreArmor: false);
            }
        }

        /// <summary>Урон от навыка (взрыв, молния): щитом не ловится; ignoreArmor — и броня не помогает. Добивание засчитывается.</summary>
        public void DealDamage(BattleUnit target, float amount, bool ignoreArmor)
        {
            if (target == null || target.IsDead) return;
            target.TakeDamage(amount, null, ignoreArmor);
            if (target.IsDead) Kills++;
        }

        public void TakeDamage(float amount, BattleUnit attacker, bool ignoreArmor = false)
        {
            if (IsDead || amount <= 0f) return;

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
            if (IsShieldWall && ShieldLevel == 0) amount *= 1f - battle.formationReduction; // плотный строй без щитов
            if (IsBlessed) amount *= 1f - blessReduction;
            if (!ignoreArmor) amount *= 100f / (100f + Mathf.Max(0, Armor)); // броня

            Hp -= amount;
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

        /// <summary>Навык воинов: держать строй до шага untilStep на месте slot; щиты блокируют чаще на bonus.</summary>
        public void ApplyShieldWall(int untilStep, Vector2Int? slot, float bonus)
        {
            shieldWallUntilStep = Mathf.Max(shieldWallUntilStep, untilStep);
            shieldWallBonus = Mathf.Clamp01(bonus);
            FormationCell = slot;
        }

        /// <summary>Шаг к своему месту в строю (не дальше MoveSpeed клеток — без телепорта) и разворот щитом к врагу.</summary>
        public void StepTowardsFormation()
        {
            if (IsDead) return;
            if (FormationCell.HasValue && FormationCell.Value != Cell)
            {
                Vector2Int slot = FormationCell.Value;
                Vector2Int? step = PathStep(c => c == slot, slot);
                if (step.HasValue && step.Value != Cell) battle.Relocate(this, step.Value);
            }
            Facing = AutoBattleManager.Forward(Team);
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
        }

        /// <summary>Благословение жрецов: входящий урон меньше на reduction до шага untilStep.</summary>
        public void ApplyBlessing(int untilStep, float reduction)
        {
            blessUntilStep = Mathf.Max(blessUntilStep, untilStep);
            blessReduction = Mathf.Clamp01(Mathf.Max(blessReduction, reduction));
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
            if (path != null && path.Count > 0) return path[Mathf.Min(MoveSpeed, path.Count) - 1];
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
            if (shield != null) shield.enabled = false;
            foreach (SpriteRenderer dot in starDots) dot.enabled = false;
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

            // Подставка цвета команды — свои и враги различимы даже без арта
            teamBase = CreatePart("TeamBase", GameVisuals.Circle, new Color(teamColor.r, teamColor.g, teamColor.b, 0.55f),
                new Vector2(0f, -bodyRadius * 0.55f), new Vector2(diameter * 1.25f, diameter * 0.5f));

            // Ореол благословения (виден, пока действует «Молитва»)
            blessRing = CreatePart("Blessing", GameVisuals.Ring, BlessColor, Vector2.zero, new Vector2(diameter * 1.3f, diameter * 1.3f));
            blessRing.enabled = false;

            // Огненные клинки монахов (виден, пока горит оружие)
            fireRing = CreatePart("Fire", GameVisuals.Ring, new Color(1f, 0.5f, 0.15f, 0.9f), Vector2.zero, new Vector2(diameter * 1.12f, diameter * 1.12f));
            fireRing.enabled = false;

            // Тело: спрайт из UnitData или круг цвета класса
            bodyRoot = new GameObject("Body").transform;
            bodyRoot.SetParent(transform, false);
            body = bodyRoot.gameObject.AddComponent<SpriteRenderer>();
            bool hasSprite = Data.sprite != null;
            body.sprite = hasSprite ? Data.sprite : GameVisuals.Circle;
            bodyColor = hasSprite ? Color.white : GameVisuals.ClassColor(Data.unitClass);
            body.color = bodyColor;
            Vector3 spriteSize = body.sprite.bounds.size;
            bodyRoot.localScale = Vector3.one * (diameter / Mathf.Max(spriteSize.x, spriteSize.y, 0.001f));

            // Стрелка «куда смотрю» и щит (если щиты изучены; ростовой — шире и толще)
            facingMark = CreatePart("Facing", GameVisuals.Triangle, teamColor, Vector2.zero, new Vector2(diameter * 0.32f, diameter * 0.32f));
            if (ShieldLevel > 0)
                shield = CreatePart("Shield", GameVisuals.Square, ShieldColor, Vector2.zero, new Vector2(diameter * 0.75f, diameter * 0.14f));

            // Полоска ХП и цифра уровня над юнитом, звёзды-точки — под ним
            barY = bodyRadius + 0.1f;
            float barWidth = cell * 0.8f;
            hpBack = CreatePart("HpBack", GameVisuals.Square, new Color(0f, 0f, 0f, 0.65f), new Vector2(0f, barY), new Vector2(barWidth + 0.04f, 0.09f));
            hpFill = CreatePart("HpFill", GameVisuals.Square,
                Team == Team.Player ? new Color(0.35f, 0.9f, 0.4f) : new Color(0.95f, 0.3f, 0.25f),
                new Vector2(0f, barY), new Vector2(barWidth, 0.06f));
            levelLabel = CreateLabel("Level", new Vector2(-barWidth * 0.5f - 0.09f, barY), 2f, new Color(1f, 0.85f, 0.25f), new Vector2(0.3f, 0.25f));
            levelLabel.text = Level.ToString();
            if (Stars > 1)
            {
                for (int i = 0; i < Stars; i++)
                {
                    float x = (i - (Stars - 1) * 0.5f) * 0.1f;
                    starDots.Add(CreatePart("Star", GameVisuals.Circle, new Color(1f, 0.85f, 0.25f), new Vector2(x, -bodyRadius - 0.015f), new Vector2(0.07f, 0.07f)));
                }
            }
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
            bodyRoot.localPosition = lungeDirection * (0.12f * lunge);

            // Вспышка при уроне
            if (flashTimer > 0f) flashTimer -= dt;
            body.color = flashTimer > 0f ? HitColor : bodyColor;
            blessRing.enabled = IsBlessed;
            fireRing.enabled = IsOnFire;

            // Стрелка взгляда и щит поворачиваются вслед за Facing
            Vector2 facing = ((Vector2)Facing).normalized;
            float angle = Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg;
            facingMark.transform.localPosition = facing * (bodyRadius + 0.05f);
            facingMark.transform.localRotation = Quaternion.Euler(0f, 0f, angle - 90f);
            if (shield != null)
            {
                bool wall = IsShieldWall;
                bool tower = ShieldLevel >= 2;
                float width = bodyRadius * (tower ? 1.9f : 1.5f) * (wall ? 1.3f : 1f);
                shield.transform.localPosition = facing * (bodyRadius + (wall ? 0.1f : 0.06f));
                shield.transform.localRotation = Quaternion.Euler(0f, 0f, angle - 90f);
                shield.transform.localScale = new Vector3(width, (tower ? 0.12f : 0.08f) * (wall ? 1.25f : 1f), 1f);
                shield.color = wall ? ShieldWallColor : ShieldColor;
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
            body.sortingOrder = order;
            facingMark.sortingOrder = order + 1;
            if (shield != null) shield.sortingOrder = order + 2;
            hpBack.sortingOrder = order + 3;
            hpFill.sortingOrder = order + 4;
            foreach (SpriteRenderer dot in starDots) dot.sortingOrder = order + 5;
            if (levelLabel != null) levelLabel.sortingOrder = order + 5;
        }
    }
}
