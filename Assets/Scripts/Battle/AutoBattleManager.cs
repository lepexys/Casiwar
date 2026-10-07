using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

namespace Casiwar
{
    public enum Team
    {
        Player,
        Enemy,
    }

    /// <summary>
    /// Итог боя: победа, судьба каждого юнита игрока (погибшие пропадают навсегда, выжившие получают опыт)
    /// и сила уцелевших варваров (по ней считается урон зданиям при набеге).
    /// </summary>
    public sealed class BattleOutcome
    {
        public bool Victory;
        /// <summary>Сколько юнитов игрока дожило до конца боя.</summary>
        public int Survivors;
        /// <summary>Сумма звёзд выживших юнитов игрока — столько круток в «Трофеях» (★★ и ★★★ → 5).</summary>
        public int SurvivorStars;
        /// <summary>Каждый юнит игрока: жив ли и сколько врагов добил (→ BenchManager.ApplyBattleResults).</summary>
        public readonly List<UnitBattleResult> PlayerUnits = new List<UnitBattleResult>();
        /// <summary>Удар каждого уцелевшего варвара по городу.</summary>
        public readonly List<float> RaidHits = new List<float>();
    }

    /// <summary>
    /// ФАЗА 2 — бой на клетках с варварами. Арена — сетка columns×rows (центр = этот объект), юниты стоят
    /// в клетках, ходят из клетки в клетку и бьют тех, до кого достают. Игрок внизу, варвары вверху.
    ///
    /// Бой идёт шагами. Каждый шаг:
    ///   1) спин боевого слота игрока (BattleSlotMachine) → кластеры решают, кто действует:
    ///      массовые навыки классов → «Чудо» (3+ Свечи) → перемещения (➜) → обычные атаки (⚔).
    ///      Каскад: сработавшее лопается, досыпаются новые символы — следующая волна с множителем ×2, ×3…;
    ///      ×2 рядом с кластером ещё умножают его силу (урон атак, сила навыка);
    ///   2) то же самое делают варвары — их слот скрыт, видно только итог (особых символов у них нет).
    /// Победа — когда у противника не осталось юнитов; лимит шагов — решает доля оставшегося ХП.
    /// Если бой проигран, уцелевшие варвары грабят город (BattleOutcome.RaidHits → CityManager.ApplyRaid).
    /// Погибшие юниты игрока не возвращаются, выжившие растут в уровнях (BattleOutcome.PlayerUnits).
    /// Варвары тоже растут: с каждым днём выше уровень, позже среди них бывают ★★ и ★★★.
    ///
    /// Массовые навыки (для обеих сторон):
    ///   Воины   «Стена щитов» — шагают к линии перед своими (без телепортов), разворачиваются к врагу
    ///                           и несколько шагов чаще блокируют щитами; без щитов это «Строй» — просто плотнее;
    ///   Лучники «Залп»        — все лучники разом стреляют (дальность не важна);
    ///   Жрецы   «Молитва»     — лечат всех своих и благословляют: меньше урона несколько шагов.
    ///   Подрывники «Динамит»  — каждый бросает динамит в самую плотную кучу врагов: взрыв бьёт всех вокруг;
    ///   Монахи  «Огненные клинки» — оружие ближнего боя у всех своих горит: удары сильнее несколько шагов;
    ///   Маги    «Цепная молния» — молния бьёт цель и перескакивает на ближайших врагов, щиты и броня не помогают.
    ///   Богатырь своего символа не имеет: он бьёт вместе с любым сработавшим навыком своей стороны.
    ///
    /// Щиты (у своих — после науки и улучшения казарм, у варваров — с определённого дня) ловят удар
    /// не всегда: иногда целиком («БЛОК!»), иногда частично, иногда никак. Стрелы ловятся чаще,
    /// арбалетные болты — реже. Обычный щит прикрывает только лицо, ростовой — и бок.
    /// </summary>
    public class AutoBattleManager : MonoBehaviour
    {
        private static readonly Vector2Int[] Directions8 =
        {
            new Vector2Int(1, 0), new Vector2Int(1, 1), new Vector2Int(0, 1), new Vector2Int(-1, 1),
            new Vector2Int(-1, 0), new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(1, -1),
        };

        [Header("Ссылки")]
        public BattleSlotMachine slotMachine;
        public CityManager city;

        [Header("UI (необязательно)")]
        [Tooltip("«Шаг 3/30»")]
        public TMP_Text stepText;
        [Tooltip("Крупная надпись «ПОБЕДА!» / «ПОРАЖЕНИЕ»")]
        public TMP_Text resultText;
        [Tooltip("Итог скрытого спина варваров")]
        public TMP_Text enemyTurnText;
        [Tooltip("Подпись кнопки скорости («Скорость ×1»)")]
        public TMP_Text speedButtonLabel;

        [Header("Арена — сетка (центр арены = этот объект)")]
        [Min(3)] public int columns = 7;
        [Min(6)] public int rows = 8;
        [Min(0.1f)] public float cellSize = 0.66f;
        [Tooltip("Юнитов в шеренге при расстановке (шеренг у каждой стороны — 3)")]
        [Min(1)] public int unitsPerRow = 5;
        public Color cellColorA = new Color(0.20f, 0.29f, 0.20f);
        public Color cellColorB = new Color(0.17f, 0.25f, 0.17f);

        [Header("Шаги боя")]
        [Min(1)] public int maxSteps = 30;
        [Tooltip("Множитель скорости боя (кнопка ×1/×2/×3)")]
        [Min(0.1f)] public float battleSpeed = 1f;
        [Min(0f)] public float startDelay = 0.5f;
        [Tooltip("Пауза после действий стороны (чтобы успеть увидеть удары)")]
        [Min(0f)] public float actionPause = 0.35f;
        [Tooltip("Пауза после боя, чтобы игрок увидел результат")]
        [Min(0f)] public float endDelay = 2f;

        [Header("Направления")]
        [Tooltip("Удар по боку получает эту долю бонуса удара в спину")]
        [Range(0f, 1f)] public float sideBonusShare = 0.3f;

        [Header("Массовые навыки")]
        [Tooltip("+сила за каждый символ кластера сверх порога")]
        [Min(0f)] public float powerPerExtraSymbol = 0.25f;
        [Tooltip("«Залп»: урон каждого лучника = атака × множитель × сила")]
        [Min(0f)] public float volleyMultiplier = 1.5f;
        [Tooltip("«Стена щитов»: +шанс полного блока (× сила навыка), щиты прикрывают и бок")]
        [Range(0f, 1f)] public float shieldWallBlockBonus = 0.25f;
        [Tooltip("«Строй» без щитов: входящий урон меньше на эту долю")]
        [Range(0f, 0.9f)] public float formationReduction = 0.15f;
        [Tooltip("Сколько шагов держится строй")]
        [Min(1)] public int shieldWallSteps = 2;
        [Tooltip("«Молитва»: лечение каждого союзника = лечение жреца × множитель × сила")]
        [Min(0f)] public float prayerHealMultiplier = 0.8f;
        [Tooltip("«Молитва»: благословение снижает входящий урон на эту долю")]
        [Range(0f, 0.9f)] public float blessingReduction = 0.25f;
        [Min(1)] public int blessingSteps = 2;
        [Tooltip("«Динамит»: урон взрыва каждому врагу рядом с целью = атака подрывника × множитель × сила")]
        [Min(0f)] public float dynamiteMultiplier = 1.4f;
        [Tooltip("Обычный бросок подрывника задевает врагов рядом с целью этой долей урона")]
        [Range(0f, 1f)] public float bomberSplashShare = 0.5f;
        [Tooltip("«Огненные клинки»: +доля к урону ближнего боя у своих (× сила навыка)")]
        [Min(0f)] public float fireBladesBonus = 0.5f;
        [Min(1)] public int fireBladesSteps = 3;
        [Tooltip("«Цепная молния»: урон по первой цели = атака мага × множитель × сила; дальше слабее")]
        [Min(0f)] public float lightningMultiplier = 1.3f;
        [Min(0)] public int lightningJumps = 3;
        [Range(0f, 1f)] public float lightningFalloff = 0.7f;

        [Header("«Чудо» (3+ Свечи в боевом слоте)")]
        [Tooltip("Лечит каждого своего на эту долю его ХП (× сила «Чуда» × множитель каскада)")]
        [Range(0f, 1f)] public float miracleHealShare = 0.12f;
        [Tooltip("+к лечению за каждую Свечу сверх трёх")]
        [Min(0f)] public float miracleExtraPerRelic = 0.25f;

        [Header("Щиты (блок срабатывает не всегда)")]
        [Tooltip("Обычный щит: шанс поймать удар в лицо целиком")]
        [Range(0f, 1f)] public float shieldFullBlock = 0.2f;
        [Tooltip("Обычный щит: шанс поймать удар частично")]
        [Range(0f, 1f)] public float shieldPartialBlock = 0.35f;
        [Tooltip("Ростовой щит: шанс полного блока")]
        [Range(0f, 1f)] public float towerShieldFullBlock = 0.35f;
        [Tooltip("Ростовой щит: шанс частичного блока")]
        [Range(0f, 1f)] public float towerShieldPartialBlock = 0.4f;
        [Tooltip("Частичный блок гасит случайную долю урона в этих пределах")]
        [Range(0f, 1f)] public float partialBlockMin = 0.3f;
        [Range(0f, 1f)] public float partialBlockMax = 0.7f;
        [Tooltip("Удар в бок ростовой щит (и стена щитов) ловит с такой долей шансов")]
        [Range(0f, 1f)] public float sideCover = 0.5f;
        [Tooltip("Стрелу щит ловит целиком чаще: шанс полного блока × множитель")]
        [Min(1f)] public float arrowFullBlockMultiplier = 1.5f;
        [Tooltip("Арбалетный болт пробивает щит: все шансы блока × множитель")]
        [Range(0f, 1f)] public float crossbowBlockMultiplier = 0.5f;
        [Tooltip("С какого дня варвары-воины приходят со щитами и с ростовыми щитами")]
        [Min(1)] public int enemyShieldDay = 4;
        [Min(1)] public int enemyTowerShieldDay = 9;

        [Header("Варвары (каждый день — новая волна)")]
        public List<UnitData> enemyPool = new List<UnitData>();
        [Min(1)] public int baseEnemyCount = 3;
        [Tooltip("+варваров за каждый день")]
        [Min(0f)] public float enemiesPerDay = 1f;
        [Min(1)] public int maxEnemies = 12;
        [Tooltip("+к статам варваров за день (0.06 = +6%) — сверху их уровня")]
        [Min(0f)] public float enemyStatsGrowthPerDay = 0.06f;
        [Tooltip("Уровень варваров растёт на 1 каждые N дней")]
        [Min(1)] public int enemyLevelEveryDays = 2;
        [Tooltip("С этого дня среди варваров бывают ★★")]
        [FormerlySerializedAs("enemyCorpsDay")]
        [Min(1)] public int enemyTwoStarDay = 6;
        [Tooltip("С этого дня среди варваров бывают ★★★")]
        [FormerlySerializedAs("enemyArmyDay")]
        [Min(1)] public int enemyThreeStarDay = 10;
        [Tooltip("Скрытый слот варваров растёт на 1 клетку каждые N дней")]
        [Min(1)] public int enemySlotGrowthEveryDays = 3;

        [Header("Набег (если бой проигран)")]
        [Tooltip("Удар уцелевшего варвара по зданию = атака × множитель + оставшееся ХП × доля")]
        [Min(0f)] public float raidAttackMultiplier = 1.5f;
        [Range(0f, 1f)] public float raidHpShare = 0.15f;

        private readonly List<BattleUnit> units = new List<BattleUnit>();
        private BattleUnit[,] occupancy;
        private Transform unitsRoot;
        private Transform arenaRoot;
        private Action<BattleOutcome> onFinished;
        private Coroutine loopRoutine;
        private float playerStartHp;
        private float enemyStartHp;
        private int bannerFrame = -1;
        private readonly List<Vector2> bannersThisFrame = new List<Vector2>();

        public bool IsRunning { get; private set; }
        public int CurrentStep { get; private set; }
        public int Day { get; private set; } = 1;
        public IReadOnlyList<BattleUnit> Units => units;
        public int Columns => columns;
        public int Rows => rows;

        private float EnemyStatMultiplier => 1f + (Day - 1) * enemyStatsGrowthPerDay;
        private int EnemyClassLevel => Day >= 5 ? 1 : 0;

        public static Vector2Int Forward(Team team) => team == Team.Player ? Vector2Int.up : Vector2Int.down;
        public static Team Opposite(Team team) => team == Team.Player ? Team.Enemy : Team.Player;

        // =====================================================================
        //  Запуск и завершение боя
        // =====================================================================

        /// <summary>Начать бой. army — юниты со скамейки (порядок = строй), finished — итог после боя.</summary>
        public void StartBattle(IReadOnlyList<BenchUnit> army, int day, Action<BattleOutcome> finished)
        {
            StopAllCoroutines();
            if (slotMachine != null) slotMachine.StopSpin();
            ClearUnits();
            EnsureArena();

            Day = Mathf.Max(1, day);
            onFinished = finished;
            CurrentStep = 0;
            occupancy = new BattleUnit[columns, rows];

            SpawnArmy(army, Team.Player);
            SpawnArmy(GenerateEnemies(Day), Team.Enemy);
            playerStartHp = TotalHp(Team.Player);
            enemyStartHp = TotalHp(Team.Enemy);

            if (slotMachine != null) slotMachine.Prepare(AliveClasses(Team.Player));
            if (arenaRoot != null) arenaRoot.gameObject.SetActive(true);
            if (resultText != null) resultText.text = string.Empty;
            if (enemyTurnText != null) enemyTurnText.text = string.Empty;
            UpdateSpeedLabel();

            IsRunning = true;
            UpdateStepText();
            loopRoutine = StartCoroutine(BattleLoop());
        }

        /// <summary>Кнопка скорости: ×1 → ×2 → ×3 → ×1.</summary>
        public void ToggleSpeed()
        {
            battleSpeed = battleSpeed < 1.5f ? 2f : (battleSpeed < 2.5f ? 3f : 1f);
            UpdateSpeedLabel();
        }

        private IEnumerator BattleLoop()
        {
            yield return Wait(startDelay);
            CheckForWinner();

            while (IsRunning && CurrentStep < maxSteps)
            {
                CurrentStep++;
                UpdateStepText();

                // 1) Ход игрока: видимый спин — каждая волна каскада срабатывает прямо во время анимации
                if (slotMachine != null)
                {
                    yield return slotMachine.StartSpin(this);
                }
                else
                {
                    SlotSpinResult spin = BattleSlotMachine.RollCascade(new SlotSetup { Threshold = c => SkillThreshold(Team.Player, c) });
                    foreach (SlotWave wave in spin.Waves) ResolveWave(Team.Player, wave);
                }
                yield return Wait(actionPause);
                CheckForWinner();
                if (!IsRunning) break;

                // 2) Ход варваров: такой же слот с каскадами, только скрытый
                SlotSpinResult enemySpin = slotMachine != null
                    ? slotMachine.SimulateSpin(EnemySlotSize(), AliveClasses(Team.Enemy), c => EnemyClassLevel, c => SkillThreshold(Team.Enemy, c))
                    : BattleSlotMachine.RollCascade(new SlotSetup { Threshold = c => SkillThreshold(Team.Enemy, c) });
                var enemyParts = new List<string>();
                foreach (SlotWave wave in enemySpin.Waves)
                {
                    if (CountAlive(Team.Player) == 0 || CountAlive(Team.Enemy) == 0) break;
                    enemyParts.Add(ResolveWave(Team.Enemy, wave));
                }
                if (enemyTurnText != null) enemyTurnText.text = $"Варвары: {string.Join("  |  ", enemyParts)}";
                yield return Wait(actionPause);
                CheckForWinner();
            }

            loopRoutine = null;
            if (IsRunning) FinishBattle(PlayerWinsOnTimeout(), timeout: true);
        }

        /// <summary>
        /// Применить волну спина за сторону: навыки (набравшие порог) → «Чудо» → ➜ перемещения → ⚔ атаки.
        /// Сила — множитель каскада × множители рядом с кластером. Возвращает короткий итог для UI.
        /// </summary>
        public string ResolveWave(Team team, SlotWave wave)
        {
            var parts = new List<string>();

            // 1) Массовые навыки классов
            foreach (KeyValuePair<UnitClass, SlotCluster> skill in wave.Skills)
            {
                UnitClass unitClass = skill.Key;
                if (CountAlive(team, unitClass) == 0) continue;
                int need = SkillThreshold(team, unitClass);
                float power = (1f + Mathf.Max(0, skill.Value.Size - need) * powerPerExtraSymbol) * SkillPower(team, unitClass) * wave.PowerOf(skill.Value);
                TriggerClassSkill(team, unitClass, power);
                parts.Add($"{SkillTitle(team, unitClass)}{PowerTag(wave.PowerOf(skill.Value))}!");
            }

            // 2) «Чудо»: 3+ Свечи в любом месте поля
            if (wave.Miracle && CountAlive(team) > 0)
            {
                Miracle(team, wave.Relics.Count, wave.Index);
                parts.Add($"Чудо{PowerTag(wave.Index)}!");
            }

            // 3) Перемещения: столько случайных юнитов, сколько символов в самом большом кластере ➜
            int moved = 0;
            foreach (BattleUnit unit in PickRandom(team, wave.Move?.Size ?? 0, u => u.WantsToMove()))
            {
                unit.MoveAction();
                moved++;
            }

            // 4) Атаки: столько случайных юнитов, сколько символов в самом большом кластере ⚔; урон × сила кластера
            int attacked = 0;
            int attackPower = wave.PowerOf(wave.Attack);
            foreach (BattleUnit unit in PickRandom(team, wave.Attack?.Size ?? 0, u => u.HasTargetInRange()))
            {
                unit.AttackAction(attackPower);
                attacked++;
            }

            parts.Insert(0, $"{GameVisuals.IconAttack} {attacked}{PowerTag(attackPower)} · {GameVisuals.IconMove} {moved}");
            string summary = string.Join(" · ", parts);
            return wave.Index > 1 ? $"Каскад ×{wave.Index}: {summary}" : summary;
        }

        private static string PowerTag(int power) => power > 1 ? $" ×{power}" : string.Empty;

        /// <summary>«Чудо»: все свои лечатся и получают благословение. Свечи сверх трёх и множитель каскада усиливают.</summary>
        private void Miracle(Team team, int relics, int waveIndex)
        {
            List<BattleUnit> allies = Alive(team);
            if (allies.Count == 0) return;
            float power = (1f + Mathf.Max(0, relics - 3) * miracleExtraPerRelic) * waveIndex
                          * (team == Team.Player && city != null ? city.MiracleMultiplier : 1f);
            foreach (BattleUnit ally in allies)
            {
                ally.Heal(ally.MaxHp * miracleHealShare * power);
                ally.ApplyBlessing(CurrentStep + blessingSteps - 1, blessingReduction);
                BattleVfx.Ring(ally.Position, 0.45f, new Color(1f, 0.95f, 0.7f, 0.7f), 0.5f);
            }
            SkillBanner(allies, "ЧУДО!", new Color(1f, 0.95f, 0.7f));
        }

        /// <summary>Сколько символов нужно в кластере класса: не меньше, чем его юнитов в бою (и не меньше минимума).</summary>
        public int SkillThreshold(Team team, UnitClass unitClass)
        {
            int minimum = slotMachine != null ? slotMachine.minSkillCluster : 3;
            return Mathf.Max(minimum, CountAlive(team, unitClass));
        }

        /// <summary>Название навыка класса для стороны: воины без щитов делают «Строй», со щитами — «Стену щитов».</summary>
        public string SkillTitle(Team team, UnitClass unitClass)
        {
            if (unitClass == UnitClass.Warrior && ShieldLevelOf(team) == 0) return "Строй";
            return GameVisuals.SkillName(unitClass);
        }

        /// <summary>Щиты воинов стороны: свои — из города, варвары берут щиты с определённого дня.</summary>
        public int ShieldLevelOf(Team team)
        {
            if (team == Team.Player) return city != null ? city.ShieldLevel : 0;
            return Day >= enemyTowerShieldDay ? 2 : Day >= enemyShieldDay ? 1 : 0;
        }

        /// <summary>
        /// Шансы щита для удара: полный блок и частичный. Обычный щит ловит удары в лицо, ростовой
        /// и стена щитов — ещё и в бок (хуже); в спину щит не помогает. Стрелы ловятся чаще, болты — реже.
        /// </summary>
        public void ShieldChances(int shieldLevel, HitSide side, bool ranged, bool pierce, float wallBonus, out float full, out float partial,
            float blockBonus = 0f)
        {
            full = 0f;
            partial = 0f;
            if (shieldLevel <= 0 || side == HitSide.Back) return;
            bool tower = shieldLevel >= 2;
            float cover = side == HitSide.Front ? 1f : (tower || wallBonus > 0f ? sideCover : 0f);
            if (cover <= 0f) return;

            full = tower ? towerShieldFullBlock : shieldFullBlock;
            partial = tower ? towerShieldPartialBlock : shieldPartialBlock;
            if (ranged) full *= arrowFullBlockMultiplier;
            full += wallBonus + blockBonus;
            if (pierce)
            {
                full *= crossbowBlockMultiplier;
                partial *= crossbowBlockMultiplier;
            }
            full = Mathf.Clamp01(full * cover);
            partial = Mathf.Clamp(partial * cover, 0f, 1f - full);
        }

        /// <summary>Бросок щита: 1 — удар пойман целиком, доля (0..1) — частично, 0 — щит не помог.</summary>
        public float RollShieldBlock(BattleUnit defender, HitSide side, bool ranged, bool pierce)
        {
            ShieldChances(defender.ShieldLevel, side, ranged, pierce, defender.ShieldWallBonus, out float full, out float partial, defender.ShieldBonus);
            float roll = Random.value;
            if (roll < full) return 1f;
            if (roll < full + partial) return Random.Range(partialBlockMin, Mathf.Max(partialBlockMin, partialBlockMax));
            return 0f;
        }

        private float SkillPower(Team team, UnitClass unitClass)
        {
            if (team == Team.Player) return city != null ? city.SkillPower(unitClass) : 1f;
            return 1f + 0.25f * EnemyClassLevel;
        }

        private int EnemySlotSize()
        {
            int baseSize = slotMachine != null ? slotMachine.baseSize : 4;
            int maxSize = slotMachine != null ? slotMachine.maxSize : 7;
            return Mathf.Clamp(baseSize + (Day - 1) / enemySlotGrowthEveryDays, baseSize, maxSize);
        }

        private List<BattleUnit> PickRandom(Team team, int count, Predicate<BattleUnit> eligible)
        {
            var pool = units.Where(u => !u.IsDead && u.Team == team && eligible(u)).ToList();
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
            if (pool.Count > count) pool.RemoveRange(count, pool.Count - count);
            return pool;
        }

        private void CheckForWinner()
        {
            if (!IsRunning) return;
            bool playerAlive = CountAlive(Team.Player) > 0;
            bool enemyAlive = CountAlive(Team.Enemy) > 0;
            if (!playerAlive || !enemyAlive) FinishBattle(playerAlive, timeout: false);
        }

        private bool PlayerWinsOnTimeout()
        {
            float playerShare = playerStartHp > 0f ? TotalHp(Team.Player) / playerStartHp : 0f;
            float enemyShare = enemyStartHp > 0f ? TotalHp(Team.Enemy) / enemyStartHp : 0f;
            return playerShare > enemyShare;
        }

        private void FinishBattle(bool victory, bool timeout)
        {
            if (!IsRunning) return;
            IsRunning = false;
            if (resultText != null) resultText.text = victory ? "ПОБЕДА!" : "ПОРАЖЕНИЕ";
            if (timeout && stepText != null) stepText.text = "Время вышло — решает оставшееся ХП";

            // Уцелевшие варвары идут грабить город: сила удара — от их атаки и оставшегося здоровья
            var outcome = new BattleOutcome
            {
                Victory = victory,
                Survivors = CountAlive(Team.Player),
                SurvivorStars = units.Where(u => !u.IsDead && u.Team == Team.Player).Sum(u => u.Stars),
            };
            foreach (BattleUnit unit in units)
            {
                if (unit.Team == Team.Player && unit.Source != null)
                    outcome.PlayerUnits.Add(new UnitBattleResult { Unit = unit.Source, Alive = !unit.IsDead, Kills = unit.Kills });
            }
            if (!victory)
            {
                foreach (BattleUnit enemy in Alive(Team.Enemy))
                    outcome.RaidHits.Add(enemy.AttackDamage * raidAttackMultiplier + enemy.Hp * raidHpShare);
            }
            StartCoroutine(EndRoutine(outcome));
        }

        private IEnumerator EndRoutine(BattleOutcome outcome)
        {
            yield return Wait(endDelay);

            if (loopRoutine != null)
            {
                StopCoroutine(loopRoutine);
                loopRoutine = null;
            }
            if (slotMachine != null) slotMachine.ResetGrid();
            ClearUnits();
            if (arenaRoot != null) arenaRoot.gameObject.SetActive(false);
            if (resultText != null) resultText.text = string.Empty;
            if (enemyTurnText != null) enemyTurnText.text = string.Empty;

            Action<BattleOutcome> callback = onFinished;
            onFinished = null;
            callback?.Invoke(outcome);
        }

        private IEnumerator Wait(float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime * battleSpeed;
                yield return null;
            }
        }

        // =====================================================================
        //  Массовые навыки
        // =====================================================================

        public void TriggerClassSkill(Team team, UnitClass unitClass, float power)
        {
            switch (unitClass)
            {
                case UnitClass.Archer: Volley(team, power); break;
                case UnitClass.Priest: Prayer(team, power); break;
                case UnitClass.Warrior: ShieldWall(team, power); break;
                case UnitClass.Bomber: Dynamite(team, power); break;
                case UnitClass.Monk: FireBlades(team, power); break;
                case UnitClass.Mage: ChainLightning(team, power); break;
            }
            HeroesJoin(team);
        }

        /// <summary>Богатырь бьёт вместе с любым навыком своей стороны (до цели не достаёт — шагает к ней).</summary>
        private void HeroesJoin(Team team)
        {
            foreach (BattleUnit hero in Alive(team, UnitClass.Hero))
            {
                if (hero.HasTargetInRange()) hero.AttackAction();
                else hero.MoveAction();
            }
        }

        /// <summary>«Динамит»: каждый подрывник бросает динамит туда, где больше всего врагов рядом друг с другом.</summary>
        private void Dynamite(Team team, float power)
        {
            List<BattleUnit> bombers = Alive(team, UnitClass.Bomber);
            foreach (BattleUnit bomber in bombers)
            {
                List<BattleUnit> enemies = Alive(Opposite(team));
                if (enemies.Count == 0) break;
                BattleUnit target = enemies
                    .OrderByDescending(e => enemies.Count(o => Distance(o.Cell, e.Cell) <= 1))
                    .ThenBy(e => Distance(e.Cell, bomber.Cell))
                    .First();
                BattleVfx.Bolt(bomber.Position, target.Position, new Color(0.95f, 0.55f, 0.15f, 0.9f), 0.07f, 0.25f);
                BattleVfx.Ring(target.Position, cellSize * 1.4f, new Color(1f, 0.55f, 0.15f, 0.75f), 0.45f);
                float damage = bomber.AttackDamage * dynamiteMultiplier * power;
                foreach (BattleUnit victim in enemies.Where(e => Distance(e.Cell, target.Cell) <= 1).ToList())
                    bomber.DealDamage(victim, damage, ignoreArmor: false);
            }
            if (bombers.Count > 0) SkillBanner(bombers, "ДИНАМИТ!", new Color(1f, 0.6f, 0.2f));
        }

        /// <summary>«Огненные клинки»: оружие ближнего боя у всех своих горит — удары сильнее несколько шагов.</summary>
        private void FireBlades(Team team, float power)
        {
            List<BattleUnit> monks = Alive(team, UnitClass.Monk);
            if (monks.Count == 0) return;
            int until = CurrentStep + fireBladesSteps - 1;
            foreach (BattleUnit ally in Alive(team).Where(u => u.AttackRange <= 1 && !u.IsHealer))
            {
                ally.ApplyFireBlades(until, fireBladesBonus * power);
                BattleVfx.Ring(ally.Position, 0.4f, new Color(1f, 0.5f, 0.15f, 0.6f), 0.45f);
            }
            SkillBanner(monks, "ОГНЕННЫЕ КЛИНКИ!", new Color(1f, 0.55f, 0.2f));
        }

        /// <summary>«Цепная молния»: бьёт цель и перескакивает на ближайших врагов; щиты и броня не помогают.</summary>
        private void ChainLightning(Team team, float power)
        {
            List<BattleUnit> mages = Alive(team, UnitClass.Mage);
            foreach (BattleUnit mage in mages)
            {
                BattleUnit target = mage.CurrentTarget != null && !mage.CurrentTarget.IsDead ? mage.CurrentTarget : FindNearestEnemy(mage);
                if (target == null) continue;
                var hit = new HashSet<BattleUnit>();
                Vector2 from = mage.Position;
                float damage = mage.AttackDamage * lightningMultiplier * power;
                for (int jump = 0; jump <= lightningJumps && target != null; jump++)
                {
                    BattleVfx.Bolt(from, target.Position, new Color(0.6f, 0.85f, 1f, 0.95f), 0.08f, 0.3f);
                    hit.Add(target);
                    from = target.Position;
                    Vector2Int cell = target.Cell;
                    mage.DealDamage(target, damage, ignoreArmor: true);
                    damage *= lightningFalloff;
                    target = Alive(Opposite(team)).Where(e => !hit.Contains(e) && Distance(e.Cell, cell) <= 2)
                        .OrderBy(e => Distance(e.Cell, cell)).FirstOrDefault();
                }
            }
            if (mages.Count > 0) SkillBanner(mages, "МОЛНИЯ!", new Color(0.6f, 0.85f, 1f));
        }

        /// <summary>«Залп»: все лучники стороны разом стреляют по своим целям (дальность не важна).</summary>
        private void Volley(Team team, float power)
        {
            List<BattleUnit> archers = Alive(team, UnitClass.Archer);
            foreach (BattleUnit archer in archers)
            {
                BattleUnit target = archer.CurrentTarget != null && !archer.CurrentTarget.IsDead ? archer.CurrentTarget : FindNearestEnemy(archer);
                if (target == null) continue;
                BattleVfx.Bolt(archer.Position, target.Position, new Color(1f, 0.92f, 0.6f, 0.95f), 0.1f, 0.35f);
                archer.Attack(target, volleyMultiplier * power);
            }
            if (archers.Count > 0) SkillBanner(archers, "ЗАЛП!", new Color(1f, 0.92f, 0.6f));
        }

        /// <summary>«Молитва»: жрецы лечат всех своих и благословляют их — меньше урона несколько шагов.</summary>
        private void Prayer(Team team, float power)
        {
            List<BattleUnit> monks = Alive(team, UnitClass.Priest);
            if (monks.Count == 0) return;
            float heal = monks.Max(m => m.HealPower) * prayerHealMultiplier * power;
            float reduction = Mathf.Clamp(blessingReduction * power, 0f, 0.6f);

            foreach (BattleUnit ally in Alive(team))
            {
                ally.Heal(heal);
                ally.ApplyBlessing(CurrentStep + blessingSteps - 1, reduction);
                BattleVfx.Ring(ally.Position, 0.4f, new Color(1f, 0.92f, 0.55f, 0.6f), 0.45f);
            }
            SkillBanner(monks, "МОЛИТВА!", new Color(1f, 0.92f, 0.55f));
        }

        /// <summary>
        /// «Стена щитов» / «Строй»: каждый воин получает место в линии перед своими и делает к нему
        /// обычный шаг (не дальше своего хода — без телепортов), разворачиваясь лицом к врагу.
        /// Пока держится строй, символы ➜ ведут воинов к их местам, а щиты блокируют чаще.
        /// </summary>
        private void ShieldWall(Team team, float power)
        {
            List<BattleUnit> warriors = Alive(team, UnitClass.Warrior);
            if (warriors.Count == 0) return;
            // Прикрываем тех, кто стоит в строю (лучники, жрецы, нейтралы); ныряющих в тыл (flanker) не ждём
            List<BattleUnit> others = Alive(team).Where(u => u.Data.unitClass != UnitClass.Warrior && !u.Flanker).ToList();
            int forward = Forward(team).y;

            // Линия перед самым передним из прикрываемых (в сторону врага)
            List<BattleUnit> anchor = others.Count > 0 ? others : warriors;
            int frontRow = forward > 0 ? anchor.Max(u => u.Cell.y) + (others.Count > 0 ? 1 : 0)
                                       : anchor.Min(u => u.Cell.y) - (others.Count > 0 ? 1 : 0);
            frontRow = Mathf.Clamp(frontRow, 0, rows - 1);
            float centerX = (float)anchor.Average(u => u.Cell.x);

            // Места в строю: клетки линии (и соседних рядов), свободные или занятые самими воинами
            var slots = new List<Vector2Int>();
            foreach (int row in new[] { frontRow, frontRow - forward, frontRow + forward })
            {
                if (row < 0 || row >= rows) continue;
                slots.AddRange(Enumerable.Range(0, columns)
                    .Select(x => new Vector2Int(x, row))
                    .Where(c => IsFree(c) || warriors.Contains(UnitAt(c)))
                    .OrderBy(c => Mathf.Abs(c.x - centerX)));
            }

            // Ближние к линии выбирают места первыми; каждый — ближайшее к себе
            var taken = new HashSet<Vector2Int>();
            int until = CurrentStep + shieldWallSteps - 1;
            float bonus = ShieldLevelOf(team) > 0 ? shieldWallBlockBonus * power : 0f;
            foreach (BattleUnit warrior in warriors.OrderBy(w => slots.Count > 0 ? slots.Min(s => Distance(s, w.Cell)) : 0))
            {
                Vector2Int? slot = null;
                foreach (Vector2Int candidate in slots.Where(c => !taken.Contains(c))
                             .OrderBy(c => Distance(c, warrior.Cell)).ThenBy(c => Mathf.Abs(c.x - centerX)))
                {
                    slot = candidate;
                    break;
                }
                if (slot.HasValue) taken.Add(slot.Value);
                warrior.ApplyShieldWall(until, slot, bonus);
                warrior.StepTowardsFormation();
            }
            SkillBanner(warriors, SkillTitle(team, UnitClass.Warrior).ToUpperInvariant() + "!", new Color(0.55f, 0.85f, 1f));
        }

        // =====================================================================
        //  Спавн армий
        // =====================================================================

        private void SpawnArmy(IReadOnlyList<BenchUnit> army, Team team)
        {
            if (army == null) return;
            List<BenchUnit> valid = army.Where(u => u != null && u.data != null).ToList();
            int perRow = Mathf.Clamp(unitsPerRow, 1, columns);

            for (int i = 0; i < valid.Count; i++)
            {
                int rowIndex = i / perRow; // 0 — передняя шеренга
                if (rowIndex >= 3)
                {
                    Debug.LogWarning($"[Battle] Не хватило клеток для {valid[i].data.unitName}");
                    continue;
                }
                int inRow = Mathf.Min(perRow, valid.Count - rowIndex * perRow);
                int x = (columns - inRow) / 2 + i % perRow;
                int y = team == Team.Player ? 2 - rowIndex : rows - 3 + rowIndex;
                var cell = new Vector2Int(x, y);

                var go = new GameObject();
                go.transform.SetParent(unitsRoot, false);
                var unit = go.AddComponent<BattleUnit>();
                bool player = team == Team.Player;
                float multiplier = player ? (city != null ? city.ClassStatMultiplier(valid[i].data.unitClass) : 1f) : EnemyStatMultiplier;
                float healMultiplier = player ? (city != null ? city.PriestHealMultiplier : 1f) : EnemyStatMultiplier;
                bool crossbows = player && city != null && city.HasCrossbows;
                float shieldBonus = player && city != null ? city.ShieldBlockBonus : 0f;
                unit.Setup(this, valid[i], team, multiplier, healMultiplier, ShieldLevelOf(team), shieldBonus, crossbows, cell, Forward(team));
                occupancy[x, y] = unit;
                units.Add(unit);
            }
        }

        /// <summary>Волна варваров дня: с каждым днём их больше, уровень выше, позже — ★★ и ★★★.</summary>
        private List<BenchUnit> GenerateEnemies(int day)
        {
            var enemies = new List<BenchUnit>();
            List<UnitData> pool = enemyPool.FindAll(unit => unit != null);
            if (pool.Count == 0)
            {
                Debug.LogWarning("[Battle] Enemy Pool пуст — варваров не будет.");
                return enemies;
            }

            int count = Mathf.Clamp(baseEnemyCount + Mathf.FloorToInt((day - 1) * enemiesPerDay), 1, Mathf.Min(maxEnemies, unitsPerRow * 3));
            for (int i = 0; i < count; i++)
            {
                UnitData data = pool[Random.Range(0, pool.Count)];
                int level = 1 + (day - 1) / enemyLevelEveryDays + (day > 1 && Random.value < 0.25f ? 1 : 0);
                int stars = 1;
                if (day >= enemyTwoStarDay && Random.value < 0.1f * (day - enemyTwoStarDay + 1)) stars = 2;
                if (day >= enemyThreeStarDay && Random.value < 0.06f * (day - enemyThreeStarDay + 1)) stars = 3;
                enemies.Add(new BenchUnit(data, stars, level));
            }

            // Варвары тоже стоят строем: рубаки впереди, лучники сзади
            enemies.Sort((a, b) => FormationRank(a.data).CompareTo(FormationRank(b.data)));
            return enemies;
        }

        private static int FormationRank(UnitData data)
        {
            switch (data.unitClass)
            {
                case UnitClass.Warrior: return 0;
                case UnitClass.None: return 1;
                case UnitClass.Priest: return 2;
                default: return 3;
            }
        }

        private void EnsureArena()
        {
            if (unitsRoot == null)
            {
                unitsRoot = new GameObject("Units").transform;
                unitsRoot.SetParent(transform, false);
            }
            if (arenaRoot != null && arenaRoot.childCount == columns * rows) return;
            if (arenaRoot != null) Destroy(arenaRoot.gameObject);

            arenaRoot = new GameObject("ArenaCells").transform;
            arenaRoot.SetParent(transform, false);
            for (int x = 0; x < columns; x++)
            {
                for (int y = 0; y < rows; y++)
                {
                    var go = new GameObject($"Cell_{x}_{y}");
                    go.transform.SetParent(arenaRoot, false);
                    go.transform.position = CellToWorld(new Vector2Int(x, y));
                    go.transform.localScale = new Vector3(cellSize * 0.96f, cellSize * 0.96f, 1f);
                    var renderer = go.AddComponent<SpriteRenderer>();
                    renderer.sprite = GameVisuals.Square;
                    Color color = (x + y) % 2 == 0 ? cellColorA : cellColorB;
                    if (y <= 2) color = Color.Lerp(color, GameVisuals.PlayerColor, 0.08f);         // зона игрока
                    else if (y >= rows - 3) color = Color.Lerp(color, GameVisuals.EnemyColor, 0.08f); // зона врага
                    renderer.color = color;
                    renderer.sortingOrder = -30000;
                }
            }
        }

        private void ClearUnits()
        {
            foreach (BattleUnit unit in units)
            {
                if (unit != null) Destroy(unit.gameObject);
            }
            units.Clear();
        }

        // =====================================================================
        //  Сетка: клетки, путь, направления
        // =====================================================================

        public Vector3 CellToWorld(Vector2Int cell)
        {
            return transform.position + new Vector3((cell.x - (columns - 1) * 0.5f) * cellSize, (cell.y - (rows - 1) * 0.5f) * cellSize, 0f);
        }

        public bool InBounds(Vector2Int cell) => cell.x >= 0 && cell.y >= 0 && cell.x < columns && cell.y < rows;
        public bool IsFree(Vector2Int cell) => InBounds(cell) && occupancy != null && occupancy[cell.x, cell.y] == null;
        public BattleUnit UnitAt(Vector2Int cell) => InBounds(cell) && occupancy != null ? occupancy[cell.x, cell.y] : null;

        /// <summary>Переместить юнита в свободную клетку (единственный способ менять занятость).</summary>
        public void Relocate(BattleUnit unit, Vector2Int to, bool fast = false)
        {
            if (!IsFree(to)) return;
            if (occupancy[unit.Cell.x, unit.Cell.y] == unit) occupancy[unit.Cell.x, unit.Cell.y] = null;
            occupancy[to.x, to.y] = unit;
            unit.SetCell(to, fast);
        }

        public void OnUnitDied(BattleUnit unit)
        {
            if (occupancy != null && InBounds(unit.Cell) && occupancy[unit.Cell.x, unit.Cell.y] == unit)
                occupancy[unit.Cell.x, unit.Cell.y] = null;
        }

        /// <summary>Кратчайший путь по свободным клеткам (8 направлений) до любой клетки, где isGoal. null — пути нет.</summary>
        public List<Vector2Int> FindPath(Vector2Int start, Predicate<Vector2Int> isGoal)
        {
            return FindPath(columns, rows, IsFree, start, isGoal);
        }

        public static List<Vector2Int> FindPath(int width, int height, Func<Vector2Int, bool> isFree, Vector2Int start, Predicate<Vector2Int> isGoal)
        {
            var cameFrom = new Dictionary<Vector2Int, Vector2Int> { [start] = start };
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                Vector2Int current = queue.Dequeue();
                foreach (Vector2Int direction in Directions8)
                {
                    Vector2Int next = current + direction;
                    if (next.x < 0 || next.y < 0 || next.x >= width || next.y >= height) continue;
                    if (cameFrom.ContainsKey(next) || !isFree(next)) continue;
                    cameFrom[next] = current;
                    if (isGoal(next))
                    {
                        var path = new List<Vector2Int>();
                        for (Vector2Int cell = next; cell != start; cell = cameFrom[cell]) path.Add(cell);
                        path.Reverse();
                        return path;
                    }
                    queue.Enqueue(next);
                }
            }
            return null;
        }

        /// <summary>
        /// Прыжок (flanker — задел под будущих убийц): любая свободная клетка в пределах MoveSpeed — даже через занятые.
        /// Выбирается та, откуда ближе всего (по свободным клеткам) до цели прыжка.
        /// </summary>
        public Vector2Int? BestLeapCell(BattleUnit unit, Predicate<Vector2Int> goal, BattleUnit target)
        {
            int[,] distance = DistanceField(goal);
            int currentBest = int.MaxValue;
            foreach (Vector2Int neighbor in Neighbors(unit.Cell))
            {
                if (IsFree(neighbor) && distance[neighbor.x, neighbor.y] < int.MaxValue)
                    currentBest = Mathf.Min(currentBest, distance[neighbor.x, neighbor.y] + 1);
            }

            Vector2Int? best = null;
            int bestDistance = int.MaxValue;
            int bestToTarget = int.MaxValue;
            int reach = unit.MoveSpeed;
            for (int dx = -reach; dx <= reach; dx++)
            {
                for (int dy = -reach; dy <= reach; dy++)
                {
                    var cell = new Vector2Int(unit.Cell.x + dx, unit.Cell.y + dy);
                    if (cell == unit.Cell || !IsFree(cell)) continue;
                    int d = distance[cell.x, cell.y];
                    int toTarget = Distance(cell, target.Cell);
                    if (d < bestDistance || (d == bestDistance && toTarget < bestToTarget))
                    {
                        best = cell;
                        bestDistance = d;
                        bestToTarget = toTarget;
                    }
                }
            }

            if (best.HasValue && bestDistance < int.MaxValue && bestDistance < currentBest) return best;
            return GreedyStep(unit, target.Cell, reach);
        }

        /// <summary>Расстояние (в шагах по свободным клеткам) от каждой клетки до ближайшей цели goal.</summary>
        private int[,] DistanceField(Predicate<Vector2Int> goal)
        {
            var distance = new int[columns, rows];
            var queue = new Queue<Vector2Int>();
            for (int x = 0; x < columns; x++)
            {
                for (int y = 0; y < rows; y++)
                {
                    var cell = new Vector2Int(x, y);
                    if (IsFree(cell) && goal(cell))
                    {
                        distance[x, y] = 0;
                        queue.Enqueue(cell);
                    }
                    else
                    {
                        distance[x, y] = int.MaxValue;
                    }
                }
            }
            while (queue.Count > 0)
            {
                Vector2Int current = queue.Dequeue();
                foreach (Vector2Int next in Neighbors(current))
                {
                    if (!IsFree(next) || distance[next.x, next.y] != int.MaxValue) continue;
                    distance[next.x, next.y] = distance[current.x, current.y] + 1;
                    queue.Enqueue(next);
                }
            }
            return distance;
        }

        /// <summary>Шаг «в лоб»: свободная клетка в пределах reach, которая ближе всего к цели.</summary>
        public Vector2Int? GreedyStep(BattleUnit unit, Vector2Int target, int reach = 1)
        {
            Vector2Int? best = null;
            int bestDistance = Distance(unit.Cell, target);
            for (int dx = -reach; dx <= reach; dx++)
            {
                for (int dy = -reach; dy <= reach; dy++)
                {
                    var cell = new Vector2Int(unit.Cell.x + dx, unit.Cell.y + dy);
                    if (!IsFree(cell)) continue;
                    int d = Distance(cell, target);
                    if (d < bestDistance)
                    {
                        best = cell;
                        bestDistance = d;
                    }
                }
            }
            return best;
        }

        public bool HasFreeBackCell(BattleUnit target)
        {
            foreach (Vector2Int cell in Neighbors(target.Cell))
            {
                if (IsFree(cell) && IsBackCell(target, cell)) return true;
            }
            return false;
        }

        /// <summary>Расстояние в ходах (по 8 направлениям).</summary>
        public static int Distance(Vector2Int a, Vector2Int b) => Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));

        /// <summary>Направление шага из from в to: компоненты -1..1.</summary>
        public static Vector2Int DirectionBetween(Vector2Int from, Vector2Int to)
        {
            return new Vector2Int(Math.Sign(to.x - from.x), Math.Sign(to.y - from.y));
        }

        public static IEnumerable<Vector2Int> Neighbors(Vector2Int cell)
        {
            foreach (Vector2Int direction in Directions8) yield return cell + direction;
        }

        /// <summary>С какой стороны от взгляда defender находится клетка attackerCell.</summary>
        public static HitSide SideOf(BattleUnit defender, Vector2Int attackerCell)
        {
            return SideOf(defender.Cell, defender.Facing, attackerCell);
        }

        public static HitSide SideOf(Vector2Int defenderCell, Vector2Int facing, Vector2Int attackerCell)
        {
            Vector2 toAttacker = attackerCell - defenderCell;
            Vector2 look = facing;
            if (toAttacker == Vector2.zero || look == Vector2.zero) return HitSide.Front;
            float dot = Vector2.Dot(toAttacker.normalized, look.normalized);
            if (dot >= 0.6f) return HitSide.Front; // лицо и 45° от него
            if (dot <= -0.6f) return HitSide.Back; // спина и 45° от неё
            return HitSide.Side;
        }

        /// <summary>Соседняя с целью клетка у неё за спиной (3 клетки сзади).</summary>
        public static bool IsBackCell(BattleUnit target, Vector2Int cell)
        {
            return Distance(cell, target.Cell) == 1 && SideOf(target, cell) == HitSide.Back;
        }

        // =====================================================================
        //  Запросы
        // =====================================================================

        public int CountAlive(Team team) => units.Count(u => !u.IsDead && u.Team == team);
        public int CountAlive(Team team, UnitClass unitClass) => units.Count(u => !u.IsDead && u.Team == team && u.Data.unitClass == unitClass);

        private List<BattleUnit> Alive(Team team) => units.Where(u => !u.IsDead && u.Team == team).ToList();
        private List<BattleUnit> Alive(Team team, UnitClass unitClass) => units.Where(u => !u.IsDead && u.Team == team && u.Data.unitClass == unitClass).ToList();

        private IEnumerable<UnitClass> AliveClasses(Team team) => units.Where(u => !u.IsDead && u.Team == team).Select(u => u.Data.unitClass).Distinct();

        /// <summary>Ближайший враг (при равенстве — самый раненый).</summary>
        public BattleUnit FindNearestEnemy(BattleUnit seeker)
        {
            return units.Where(u => !u.IsDead && u.Team != seeker.Team)
                .OrderBy(u => Distance(u.Cell, seeker.Cell)).ThenBy(u => u.Hp).FirstOrDefault();
        }

        /// <summary>Самый раненый враг (при равенстве — ближайший): добыча тех, кто заходит в спину.</summary>
        public BattleUnit FindWeakestEnemy(BattleUnit seeker)
        {
            return units.Where(u => !u.IsDead && u.Team != seeker.Team)
                .OrderBy(u => u.Hp).ThenBy(u => Distance(u.Cell, seeker.Cell)).FirstOrDefault();
        }

        /// <summary>Самый раненый союзник целителя (доля ХП ниже порога), при равенстве — ближайший.</summary>
        public BattleUnit FindMostWounded(BattleUnit healer)
        {
            return units.Where(u => u != healer && u.Team == healer.Team && u.IsWounded)
                .OrderBy(u => u.Hp / u.MaxHp).ThenBy(u => Distance(u.Cell, healer.Cell)).FirstOrDefault();
        }

        private float TotalHp(Team team) => units.Where(u => !u.IsDead && u.Team == team).Sum(u => u.Hp);

        private static Vector2 Centroid(List<BattleUnit> list)
        {
            Vector2 sum = Vector2.zero;
            foreach (BattleUnit unit in list) sum += unit.Position;
            return list.Count > 0 ? sum / list.Count : Vector2.zero;
        }

        /// <summary>Крупная надпись навыка над его юнитами. Несколько навыков за один ход не налезают друг на друга.</summary>
        private void SkillBanner(List<BattleUnit> casters, string text, Color color)
        {
            if (bannerFrame != Time.frameCount)
            {
                bannerFrame = Time.frameCount;
                bannersThisFrame.Clear();
            }
            Vector2 position = Centroid(casters) + Vector2.up * 0.65f;
            while (bannersThisFrame.Any(b => Mathf.Abs(b.y - position.y) < 0.6f && Mathf.Abs(b.x - position.x) < 2.6f))
                position.y += 0.6f;
            bannersThisFrame.Add(position);
            BattleVfx.FloatingText(position, text, color, 4.2f);
        }

        private void UpdateStepText()
        {
            if (stepText != null) stepText.text = CurrentStep > 0 ? $"Шаг {CurrentStep}/{maxSteps}" : "Армии строятся...";
        }

        private void UpdateSpeedLabel()
        {
            if (speedButtonLabel != null) speedButtonLabel.text = $"Скорость ×{Mathf.RoundToInt(battleSpeed)}";
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(transform.position, new Vector3(columns * cellSize, rows * cellSize, 0f));
            Gizmos.color = new Color(1f, 1f, 0f, 0.25f);
            for (int x = 1; x < columns; x++)
            {
                float wx = transform.position.x + (x - columns * 0.5f) * cellSize;
                Gizmos.DrawLine(new Vector3(wx, transform.position.y - rows * cellSize * 0.5f), new Vector3(wx, transform.position.y + rows * cellSize * 0.5f));
            }
            for (int y = 1; y < rows; y++)
            {
                float wy = transform.position.y + (y - rows * 0.5f) * cellSize;
                Gizmos.DrawLine(new Vector3(transform.position.x - columns * cellSize * 0.5f, wy), new Vector3(transform.position.x + columns * cellSize * 0.5f, wy));
            }
        }
    }
}
