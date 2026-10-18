using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace Casiwar.EditorTools
{
    /// <summary>
    /// Меню «Casiwar → Собрать демо-сцену». Создаёт:
    ///   • ассеты юнитов и варваров (Assets/Data/Units) и снаряжения (Assets/Data/Items);
    ///   • шрифт с кириллицей (Assets/Fonts/Casiwar SDF) и иконки ★ ⚒ 🪙 ⚔ ➜ 🍎 👤 📖 🚩 🕯 для TextMeshPro;
    ///   • префабы клетки поля, слота скамейки, клетки карты города, карточки науки и кнопки действия;
    ///   • сцену Assets/Scenes/CasiwarDemo.unity со всем UI и связями между скриптами.
    /// Повторный запуск пересоздаёт сцену и префабы; существующие ассеты юнитов/предметов/шрифта не трогает.
    /// </summary>
    public static class CasiwarSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/CasiwarDemo.unity";
        /// <summary>
        /// Версия сцены: поднимайте при каждом изменении того, что строит сборщик (UI, префабы, связи).
        /// Сцена старой версии → редактор сам предложит её пересобрать (CasiwarSceneUpdateCheck).
        /// </summary>
        public const int SceneVersion = 26;
        public const string FontAssetPath = FontsFolder + "/Casiwar SDF.asset";
        public const string IconsSpriteAssetPath = FontsFolder + "/Casiwar Icons.asset";
        private const string UnitsFolder = "Assets/Data/Units";
        private const string ItemsFolder = "Assets/Data/Items";
        private const string PrefabsFolder = "Assets/Prefabs";
        private const string FontsFolder = "Assets/Fonts";
        private const string IconsTexturePath = FontsFolder + "/Casiwar Icons.png";
        private const string SourceFontPath = "Assets/TextMesh Pro/Fonts/LiberationSans.ttf";
        private const float HudHeight = 100f;
        // Горизонтальная раскладка (1920×1080): ширина левой колонки, правой колонки и высота вкладок под панелью вкладок
        private const float LandLeft = 830f;
        private const float LandRight = 1060f;
        private const float LandTabHeight = 868f;
        private const float RosterHeight = 190f; // панель наших юнитов внизу экрана боя
        private const int IconSize = 128;
        private const string IconsVersion = "casiwar-icons-v8";
        /// <summary>Иконок в ряду атласа: атлас — сетка (полоса в одну строку шире 2048 px, и Unity её ужимает).</summary>
        private const int IconColumns = 8;

        /// <summary>Символы, которые сразу запекаются в атлас шрифта (остальные добавятся динамически).</summary>
        private const string PrebakedCharacters =
            "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюя" +
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789" +
            " .,:;!?-+=/*()[]%$#№'\"«»—–·×…<>_●○→";

        private static readonly Color ScreenBg = new Color(0.10f, 0.11f, 0.15f);
        private static readonly Color PanelBg = new Color(0.14f, 0.16f, 0.21f);
        private static readonly Color CardBg = new Color(0.17f, 0.19f, 0.25f);
        private static readonly Color HudBg = new Color(0.06f, 0.07f, 0.10f);
        private static readonly Color GridBg = new Color(0.07f, 0.08f, 0.11f);
        private static readonly Color ButtonColor = new Color(0.25f, 0.28f, 0.36f);
        private static readonly Color AutoColor = new Color(0.27f, 0.50f, 0.36f);
        private static readonly Color BuildColor = new Color(0.30f, 0.46f, 0.70f);
        private static readonly Color FightColor = new Color(0.85f, 0.30f, 0.25f);
        private static readonly Color SellColor = new Color(0.62f, 0.30f, 0.30f);
        private static readonly Color GoldText = new Color(1f, 0.82f, 0.30f);
        private static readonly Color MutedText = new Color(0.75f, 0.78f, 0.85f);

        private static TMP_FontAsset uiFont;
        private static AdaptiveLayout adaptive;
        private static List<UnitData> specialUnits = new List<UnitData>();
        private static List<UnitData> enemySpecials = new List<UnitData>();
        private static Sprite uiSprite;
        private static Sprite UiSprite =>
            uiSprite != null ? uiSprite : (uiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"));

        [MenuItem("Casiwar/Собрать демо-сцену", false, 0)]
        public static void BuildFromMenu()
        {
            if (!TmpReady())
            {
                EditorApplication.ExecuteMenuItem("Window/TextMeshPro/Import TMP Essential Resources");
                EditorUtility.DisplayDialog("Casiwar",
                    "Сначала нужны TMP Essential Resources — импорт запущен.\n" +
                    "Когда он закончится, снова выберите Casiwar → Собрать демо-сцену.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("Casiwar", $"Сцена {ScenePath} уже есть. Пересоздать её и префабы?", "Пересоздать", "Отмена"))
                return;

            Build();
            EditorUtility.DisplayDialog("Casiwar",
                "Готово! Открыта сцена CasiwarDemo — жмите Play.\n" +
                "Совет: в окне Game выберите портретное разрешение 1080×1920.", "OK");
        }

        /// <summary>Пересборка устаревшей сцены (по предложению CasiwarSceneUpdateCheck). false — не получилось или отменили.</summary>
        public static bool RebuildOutdated()
        {
            if (!TmpReady())
            {
                BuildFromMenu(); // подскажет про TMP Essential Resources
                return false;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            Build();
            return true;
        }

        /// <summary>Запуск без UI: Unity.exe -batchmode -quit -executeMethod Casiwar.EditorTools.CasiwarSceneBuilder.BuildFromCommandLine</summary>
        public static void BuildFromCommandLine()
        {
            if (!TmpReady()) throw new System.InvalidOperationException("TMP Essential Resources не импортированы.");
            Build();
        }

        public static void Build()
        {
            EnsureFolder(UnitsFolder);
            EnsureFolder(ItemsFolder);
            EnsureFolder(PrefabsFolder);
            EnsureFolder("Assets/Scenes");

            // 0) Шрифт с кириллицей и иконки для TextMeshPro
            uiFont = EnsureCyrillicFont();
            EnsureIconSprites();

            // 1) Наши юниты: на старте все без класса; воины и лучники — из казарм, жрецы — из святилища
            UnitData militia = CreateOrLoadUnit("Unit_Militia", "Ополченец", UnitClass.None, hp: 160, attack: 20, range: 1, speed: 1,
                armor: 0, heal: 0, shieldBearer: false, sell: 1);
            UnitData warrior = CreateOrLoadUnit("Unit_Warrior", "Воин", UnitClass.Warrior, hp: 260, attack: 22, range: 1, speed: 1,
                armor: 10, heal: 0, shieldBearer: true, sell: 2);
            UnitData archer = CreateOrLoadUnit("Unit_Archer", "Лучник", UnitClass.Archer, hp: 120, attack: 26, range: 3, speed: 1,
                armor: 0, heal: 0, shieldBearer: false, sell: 2);
            UnitData monk = CreateOrLoadUnit("Unit_Monk", "Жрец", UnitClass.Priest, hp: 170, attack: 14, range: 1, speed: 1,
                armor: 5, heal: 30, shieldBearer: false, sell: 2);
            var units = new List<UnitData> { militia, warrior, archer, monk };

            // 1б) Гибриды и богатырь: на поле не падают — получаются из смешанных линий (числа — UnitData.Defaults)
            specialUnits = new List<UnitData>();
            foreach ((UnitClass unitClass, string file) in new[]
                     {
                         (UnitClass.Bomber, "Unit_Bomber"), (UnitClass.Monk, "Unit_BattleMonk"),
                         (UnitClass.Mage, "Unit_Mage"), (UnitClass.Hero, "Unit_Hero"),
                     })
            {
                UnitData.Defaults(unitClass, out string name, out int hp, out int attack, out int range, out int armor, out int heal,
                    out bool shieldBearer, out int sell);
                specialUnits.Add(CreateOrLoadUnit(file, name, unitClass, hp, attack, range, 1, armor, heal, shieldBearer, sell));
            }

            // 2) Варвары: каждый день новая волна
            var barbarians = new List<UnitData>
            {
                CreateOrLoadUnit("Enemy_Barbarian", "Варвар", UnitClass.Warrior, hp: 200, attack: 20, range: 1, speed: 1,
                    armor: 0, heal: 0, shieldBearer: true, sell: 0),
                CreateOrLoadUnit("Enemy_BarbarianArcher", "Варвар-лучник", UnitClass.Archer, hp: 110, attack: 20, range: 3, speed: 1,
                    armor: 0, heal: 0, shieldBearer: false, sell: 0),
                CreateOrLoadUnit("Enemy_Savage", "Дикарь", UnitClass.None, hp: 150, attack: 18, range: 1, speed: 1,
                    armor: 0, heal: 0, shieldBearer: false, sell: 0),
            };

            // 2б) Особые варвары: подрывники и шаманы-маги с 10-го дня, вождь-богатырь с 15-го (числа — UnitData.Defaults)
            enemySpecials = new List<UnitData>();
            foreach ((UnitClass unitClass, string file) in new[]
                     {
                         (UnitClass.Bomber, "Enemy_Bomber"), (UnitClass.Mage, "Enemy_Shaman"), (UnitClass.Hero, "Enemy_Chieftain"),
                     })
            {
                UnitData.Defaults(unitClass, out _, out int hp, out int attack, out int range, out int armor, out int heal,
                    out bool shieldBearer, out _);
                enemySpecials.Add(CreateOrLoadUnit(file, AutoBattleManager.EnemySpecialName(unitClass), unitClass, hp, attack, range, 1,
                    armor, heal, shieldBearer, 0));
            }

            // 3) Снаряжение: тяжёлое — воинам, кожаное — лучникам, ряса и посох — жрецам
            var items = new List<ItemData>
            {
                CreateOrLoadItem("Item_HeavyArmor", "Тяжёлая броня", ItemSlot.Armor, UnitClass.Warrior, "ЛАТЫ", armor: 30, attack: 0),
                CreateOrLoadItem("Item_Sword", "Меч", ItemSlot.Weapon, UnitClass.Warrior, "МЕЧ", armor: 0, attack: 6),
                CreateOrLoadItem("Item_LeatherArmor", "Кожаная броня", ItemSlot.Armor, UnitClass.Archer, "КОЖА", armor: 16, attack: 0),
                CreateOrLoadItem("Item_Bow", "Лук", ItemSlot.Weapon, UnitClass.Archer, "ЛУК", armor: 0, attack: 6),
                CreateOrLoadItem("Item_Robe", "Ряса", ItemSlot.Armor, UnitClass.Priest, "РЯСА", armor: 12, attack: 0),
                CreateOrLoadItem("Item_Staff", "Посох", ItemSlot.Weapon, UnitClass.Priest, "ПОСОХ", armor: 0, attack: 5),
            };

            // 4) Новая сцена (временные объекты префабов создаются уже в ней)
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 5) Префабы UI
            var prefabs = new UiPrefabs
            {
                Tile = BuildTilePrefab(),
                BenchSlot = BuildBenchSlotPrefab(),
                CityCell = BuildCityCellPrefab(),
                TechCard = BuildTechCardPrefab(),
                UpgradeCard = BuildUpgradeCardPrefab(),
                ActionButton = BuildActionButtonPrefab(),
            };

            // 6) Камера, свет, системы, Canvas и все связи
            Camera camera = CreateCameraAndLight();
            BuildSceneObjects(units, barbarians, items, militia, prefabs, camera);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Casiwar] Демо-сцена собрана: {ScenePath}");
        }

        private sealed class UiPrefabs
        {
            public TileView Tile;
            public BenchSlotView BenchSlot;
            public CityCellView CityCell;
            public TechCardView TechCard;
            public UpgradeCardView UpgradeCard;
            public Button ActionButton;
        }

        // =====================================================================
        //  Сцена
        // =====================================================================

        private static Camera CreateCameraAndLight()
        {
            var cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
            cameraGo.transform.position = new Vector3(0f, 0f, -10f);
            var camera = cameraGo.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.09f, 0.12f, 0.10f);
            cameraGo.AddComponent<AudioListener>();
            cameraGo.AddComponent<UniversalAdditionalCameraData>();

            // URP 2D: без глобального 2D-света спрайты с Sprite-Lit материалом будут тёмными
            var lightGo = new GameObject("Global Light 2D");
            var light = lightGo.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Global;
            light.color = Color.white;
            light.intensity = 1f;
            return camera;
        }

        private static void BuildSceneObjects(List<UnitData> units, List<UnitData> barbarians, List<ItemData> items,
            UnitData starter, UiPrefabs prefabs, Camera camera)
        {
            // ---------- Системы (логика) ----------
            GameManager game = new GameObject("GameManager").AddComponent<GameManager>();
            TechTree techTree = new GameObject("TechTree").AddComponent<TechTree>();
            CityManager city = new GameObject("City").AddComponent<CityManager>();
            CityGridManager cityGrid = new GameObject("CityGrid").AddComponent<CityGridManager>();
            LootSlotMachine lootSlot = new GameObject("LootSlot").AddComponent<LootSlotMachine>();
            BenchManager bench = new GameObject("Bench").AddComponent<BenchManager>();
            var battleGo = new GameObject("Battle");
            battleGo.transform.position = new Vector3(0f, -2.1f, 0f); // центр клеточной арены
            AutoBattleManager battle = battleGo.AddComponent<AutoBattleManager>();
            BattleBoard battleBoard = new GameObject("BattleBoard").AddComponent<BattleBoard>();
            RecruitSlot recruit = game.gameObject.AddComponent<RecruitSlot>();
            WorldPowers powers = new GameObject("World").AddComponent<WorldPowers>();

            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            // ---------- Canvas ----------
            var canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.layer = LayerMask.NameToLayer("UI");
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var root = (RectTransform)canvasGo.transform;
            // Вертикальная раскладка строится ниже; горизонтальную (ПК) — в конце, через Landscape(...)
            adaptive = canvasGo.AddComponent<AdaptiveLayout>();
            adaptive.scaler = scaler;
            adaptive.entries = new List<AdaptiveLayout.Entry>();

            // ===== День: вкладки «Город», «Наука», «Поле», «Войска» (день начинается с «Города») =====
            RectTransform prepScreen = NewRect("PrepScreen", root);
            Stretch(prepScreen, 0f, 0f, HudHeight, 0f);
            AddImage(prepScreen, ScreenBg, sliced: false, raycast: true);
            RectTransform prep = Column("Content", prepScreen, 1080f);

            RectTransform tabBar = NewRect("TabBar", prep);
            TopStretch(tabBar, 8f, 96f, 20f);
            AddRow(tabBar, 10f);
            Button cityTabButton = NewButton("Btn_TabCity", tabBar, "Город", ButtonColor, 32f, out _);
            Flex(cityTabButton.gameObject, 1f);
            Button techTabButton = NewButton("Btn_TabTech", tabBar, "Наука", ButtonColor, 32f, out _);
            Flex(techTabButton.gameObject, 1f);
            Button boardTabButton = NewButton("Btn_TabBoard", tabBar, "Поле", ButtonColor, 32f, out _);
            Flex(boardTabButton.gameObject, 1f);
            Button unitsTabButton = NewButton("Btn_TabUnits", tabBar, "Войска", ButtonColor, 32f, out _);
            Flex(unitsTabButton.gameObject, 1f);
            // «Конец хода», а когда враг в наших границах — «В БОЙ!» (подпись меняет GameManager)
            Button fightButton = NewButton("Btn_Fight", tabBar, "Конец хода", FightColor, 34f, out _);
            Flex(fightButton.gameObject, 1.15f);

            // --- Вкладка «Поле»: вербовка (окно слота RecruitSlot строит сам в GridContainer) и фокус сбора ресурсов ---
            RectTransform boardTab = NewRect("BoardTab", prep);
            TopStretch(boardTab, 112f, 1196f, 0f);

            // Приоритетов больше нет: смешанные линии сами дают гибридов — поле занимает всё место
            RectTransform gridArea = NewRect("GridContainer", boardTab);
            Anchored(gridArea, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(996f, 996f));
            AddImage(gridArea, GridBg, sliced: true, raycast: false);
            gridArea.gameObject.AddComponent<RectMask2D>();

            // Фокус сбора: каких ресурсов больше упадёт в сбор ресурсов со следующего хода
            RectTransform focusBar = NewRect("FocusBar", boardTab);
            TopStretch(focusBar, 1012f, 80f, 20f);
            AddRow(focusBar, 10f);
            TextMeshProUGUI focusTitle = NewText("Label", focusBar, "Сбор след. хода:", 30f, TextAlignmentOptions.MidlineLeft, MutedText);
            AutoSize(focusTitle, 16f, 30f);
            Flex(focusTitle.gameObject, 1.3f);
            var focusLabels = new TMP_Text[3];
            for (int i = 0; i < focusLabels.Length; i++)
            {
                var resource = (ResourceType)i;
                Color color = Color.Lerp(GameVisuals.ResourceColor(resource), ButtonColor, 0.55f);
                Button button = NewButton($"Btn_Focus_{resource}", focusBar, GameVisuals.ResourceIcon(resource), color, 34f, out TextMeshProUGUI label);
                Flex(button.gameObject, 1f);
                UnityEventTools.AddIntPersistentListener(button.onClick, cityGrid.CycleFocus, i);
                focusLabels[i] = label;
            }

            RectTransform boardControls = NewRect("ControlsBar", boardTab);
            TopStretch(boardControls, 1100f, 92f, 20f);
            AddRow(boardControls, 14f);
            TextMeshProUGUI reserveText = NewText("ReserveText", boardControls, "Спины вербовки", 34f, TextAlignmentOptions.Center, Color.white, bold: true);
            AutoSize(reserveText, 16f, 34f);
            Flex(reserveText.gameObject, 1.3f);
            // «Перемешать» больше нет: поле перемешивается само, когда одним ходом сложить нечего
            Button autoButton = NewButton("Btn_Auto", boardControls, "Авто: ВЫКЛ", AutoColor, 30f, out TextMeshProUGUI autoLabel);
            Flex(autoButton.gameObject, 0.9f);

            // --- Вкладка «Город»: карта мира 50×50 с городом (слои карты CityMapView создаёт сам; скамейка здесь скрыта) ---
            RectTransform cityTab = NewRect("CityTab", prep);
            TopStretch(cityTab, 112f, 1300f, 0f);
            TextMeshProUGUI citySummary = NewText("SummaryText", cityTab, string.Empty, 26f, TextAlignmentOptions.Center, MutedText);
            TopStretch(citySummary.rectTransform, 0f, 70f, 20f);
            AutoSize(citySummary, 14f, 26f);
            RectTransform map = NewRect("Map", cityTab);
            Anchored(map, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -78f), new Vector2(1040f, 720f));
            AddImage(map, GridBg, sliced: true, raycast: true);
            map.gameObject.AddComponent<RectMask2D>();
            RectTransform panel = NewRect("BuildingPanel", cityTab);
            Anchored(panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -808f), new Vector2(1040f, 380f));
            AddImage(panel, CardBg, sliced: true, raycast: true);
            TextMeshProUGUI panelTitle = NewText("Title", panel, "Карта города", 34f, TextAlignmentOptions.MidlineLeft, Color.white, bold: true);
            Anchored(panelTitle.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -12f), new Vector2(560f, 50f));
            AutoSize(panelTitle, 18f, 34f);
            TextMeshProUGUI panelInfo = NewText("Info", panel, string.Empty, 24f, TextAlignmentOptions.TopLeft, MutedText);
            Anchored(panelInfo.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -66f), new Vector2(560f, 300f));
            AutoSize(panelInfo, 14f, 24f);
            // Действия здания (постройка, улучшения, ремонт, караваны) — прокручиваемый список: чудес света много
            RectTransform actions = NewRect("Actions", panel);
            Anchored(actions, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -12f), new Vector2(458f, 356f));
            RectTransform actionsList = ScrollList(actions, 6f);
            CityMapView mapView = cityTab.gameObject.AddComponent<CityMapView>();
            cityTab.gameObject.SetActive(false);

            // --- Вкладка «Наука»: дерево технологий за очки знаний (как дерево прокачки в tower defense) ---
            RectTransform techTab = NewRect("TechTab", prep);
            TopStretch(techTab, 112f, 1300f, 0f);
            TextMeshProUGUI techSummary = NewText("SummaryText", techTab, string.Empty, 24f, TextAlignmentOptions.Center, MutedText);
            TopStretch(techSummary.rectTransform, 0f, 70f, 20f);
            AutoSize(techSummary, 14f, 24f);
            // Само дерево (узлы, линии, подробности) TechTreeView строит кодом под сводкой
            TechTreeView techView = techTab.gameObject.AddComponent<TechTreeView>();
            techTab.gameObject.SetActive(false);

            // --- Вкладка «Войска»: общее дерево прокачки гильдий, гибридов и богатыря ---
            RectTransform unitsTab = NewRect("UnitsTab", prep);
            TopStretch(unitsTab, 112f, 1300f, 0f);
            TextMeshProUGUI unitsSummary = NewText("SummaryText", unitsTab, string.Empty, 22f, TextAlignmentOptions.Center, MutedText);
            TopStretch(unitsSummary.rectTransform, 0f, 64f, 20f);
            AutoSize(unitsSummary, 13f, 22f);
            // Дерево войск (гильдии, гибриды, богатырь; узлы и линии) UnitTreeView строит кодом под сводкой
            UnitTreeView unitView = unitsTab.gameObject.AddComponent<UnitTreeView>();
            unitsTab.gameObject.SetActive(false);

            // ===== Бой =====
            RectTransform battleScreen = NewRect("BattleScreen", root);
            Stretch(battleScreen, 0f, 0f, HudHeight, 0f);
            RectTransform arena = Column("Content", battleScreen, 1080f);

            RectTransform slotPanel = NewRect("SlotPanel", arena);
            Anchored(slotPanel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(620f, 620f));
            AddImage(slotPanel, new Color(0.06f, 0.06f, 0.09f, 0.95f), sliced: true, raycast: false);
            RectTransform slotGrid = NewRect("SlotGrid", slotPanel);
            Stretch(slotGrid, 12f, 12f, 12f, 12f);
            slotGrid.gameObject.AddComponent<RectMask2D>();

            TextMeshProUGUI stepText = NewText("StepText", arena, string.Empty, 30f, TextAlignmentOptions.Center, MutedText, bold: true);
            Anchored(stepText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -12f), new Vector2(200f, 84f));
            AutoSize(stepText, 16f, 30f);
            // Легенда: что делает каждый символ — пишет AutoBattleManager под армию и открытые символы
            TextMeshProUGUI legend = NewText("Legend", arena, string.Empty, 22f, TextAlignmentOptions.TopLeft, MutedText);
            Anchored(legend.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -110f), new Vector2(196f, 520f));
            AutoSize(legend, 10f, 22f);
            // Бой в реальном времени: поле три в ряд (BattleBoard) в SlotGrid, арена, кнопка скорости
            Button speedButton = NewButton("Btn_Speed", arena, $"{GameVisuals.IconSpeed}×1", ButtonColor, 40f, out TextMeshProUGUI speedLabel);
            Anchored((RectTransform)speedButton.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-12f, -12f), new Vector2(196f, 84f));

            TextMeshProUGUI slotResult = NewText("SlotResultText", arena, string.Empty, 34f, TextAlignmentOptions.Center, GoldText, bold: true);
            Anchored(slotResult.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -640f), new Vector2(1040f, 56f));
            AutoSize(slotResult, 16f, 34f);
            TextMeshProUGUI enemyTurn = NewText("EnemyTurnText", arena, string.Empty, 28f, TextAlignmentOptions.Center, new Color(1f, 0.6f, 0.55f));
            Anchored(enemyTurn.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -696f), new Vector2(1040f, 44f));
            AutoSize(enemyTurn, 16f, 28f);
            TextMeshProUGUI battleResult = NewText("BattleResultText", arena, string.Empty, 120f, TextAlignmentOptions.Center, Color.white, bold: true);
            Anchored(battleResult.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -1265f), new Vector2(1040f, 220f));
            // Где арена на экране: в горизонтальной раскладке AdaptiveLayout вписывает в эту рамку камеру
            RectTransform arenaFrame = NewRect("ArenaFrame", arena);
            Stretch(arenaFrame, 0f, 0f, 860f, RosterHeight + 24f);

            // Наши юниты внизу экрана: атака и точное ХП, колонка — шеренга (на арене — только полоски с делениями)
            RectTransform roster = NewRect("Roster", arena);
            BottomStretch(roster, 12f, RosterHeight, 20f);
            AddImage(roster, new Color(0.06f, 0.06f, 0.09f, 0.85f), sliced: true, raycast: false);
            AddRow(roster, 14f);
            roster.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(14, 14, 8, 8);
            var rosterLabels = new List<TMP_Text>();
            for (int c = 0; c < 3; c++)
            {
                RectTransform rank = NewRect($"Rank_{c}", roster);
                Flex(rank.gameObject, 1f);
                var rankLayout = rank.gameObject.AddComponent<VerticalLayoutGroup>();
                rankLayout.childControlWidth = true;
                rankLayout.childControlHeight = true;
                rankLayout.childForceExpandWidth = true;
                rankLayout.childForceExpandHeight = true;
                for (int r = 0; r < 5; r++)
                {
                    TextMeshProUGUI label = NewText($"Unit_{c * 5 + r}", rank, string.Empty, 22f, TextAlignmentOptions.Left, Color.white);
                    label.enableWordWrapping = false;
                    AutoSize(label, 10f, 22f);
                    rosterLabels.Add(label);
                }
            }
            battleScreen.gameObject.SetActive(false);

            // ===== Трофеи: слот 5×3 с линиями выплат, круток — по звёздам убитых врагов =====
            RectTransform lootScreen = NewRect("LootScreen", root);
            Stretch(lootScreen, 0f, 0f, HudHeight, 0f);
            AddImage(lootScreen, ScreenBg, sliced: false, raycast: true);
            RectTransform loot = Column("Content", lootScreen, 1080f);
            TextMeshProUGUI lootTitle = NewText("Title", loot, "Трофеи за победу!", 52f, TextAlignmentOptions.Center, GoldText, bold: true);
            TopStretch(lootTitle.rectTransform, 10f, 64f, 20f);
            TextMeshProUGUI lootHint = NewText("Hint", loot,
                $"{GameVisuals.IconStar} убитого врага = {GameVisuals.IconSpin}1 · 3+ подряд по линии слева: 3 → I, 4 → II, 5 → III · фигура линии — чей трофей:",
                26f, TextAlignmentOptions.Center, MutedText);
            TopStretch(lootHint.rectTransform, 76f, 84f, 30f);
            AutoSize(lootHint, 16f, 26f);
            RectTransform paylineLegend = BuildPaylineLegend(loot, 166f);
            RectTransform reels = NewRect("Reels", loot);
            Anchored(reels, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -314f), new Vector2(880f, 528f));
            AddImage(reels, GridBg, sliced: true, raycast: false);
            reels.gameObject.AddComponent<RectMask2D>();
            RectTransform lootControls = NewRect("ControlsBar", loot);
            TopStretch(lootControls, 860f, 96f, 20f);
            AddRow(lootControls, 12f);
            TextMeshProUGUI lootSpins = NewText("SpinsText", lootControls, $"{GameVisuals.IconSpin} 0", 32f, TextAlignmentOptions.Center, Color.white, bold: true);
            AutoSize(lootSpins, 16f, 32f);
            Flex(lootSpins.gameObject, 1.1f);
            Button lootSpinButton = NewButton("Btn_LootSpin", lootControls, GameVisuals.IconSpin, FightColor, 44f, out _);
            Flex(lootSpinButton.gameObject, 1f);
            Button lootAutoButton = NewButton("Btn_LootAuto", lootControls, GameVisuals.IconPause, AutoColor, 44f, out TextMeshProUGUI lootAutoLabel);
            Flex(lootAutoButton.gameObject, 0.9f);
            Button lootNextButton = NewButton("Btn_LootNext", lootControls, "Дальше", BuildColor, 34f, out _);
            Flex(lootNextButton.gameObject, 0.9f);
            TextMeshProUGUI lootLog = NewText("LootLog", loot, string.Empty, 28f, TextAlignmentOptions.Top, Color.white);
            TopStretch(lootLog.rectTransform, 970f, 260f, 30f);
            AutoSize(lootLog, 16f, 28f);
            lootScreen.gameObject.SetActive(false);

            // ===== Армия: отряд (вожак + бойцы) и гарнизон — видна на поле, на трофеях и на карте у ратуши/отряда =====
            // Обе линейки BenchManager раскладывает сам по ширине панели
            RectTransform benchPanel = NewRect("BenchPanel", root);
            BottomStretch(benchPanel, 0f, 400f, 0f);
            AddImage(benchPanel, PanelBg, sliced: false, raycast: true);
            RectTransform benchColumn = Column("Content", benchPanel, 1080f);
            TextMeshProUGUI benchTitle = NewText("Title", benchColumn, "Отряд ходит по карте, гарнизон защищает город",
                26f, TextAlignmentOptions.Center, MutedText);
            TopStretch(benchTitle.rectTransform, 10f, 44f, 20f);
            AutoSize(benchTitle, 14f, 26f);
            RectTransform benchSlots = NewRect("BenchSlots", benchColumn);
            TopStretch(benchSlots, 60f, 160f, 20f);
            AddRow(benchSlots, 8f, controlChildSize: false);
            TextMeshProUGUI benchInfo = NewText("BenchInfoText", benchColumn, string.Empty, 26f, TextAlignmentOptions.TopLeft, Color.white);
            Anchored(benchInfo.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -232f), new Vector2(740f, 156f));
            AutoSize(benchInfo, 14f, 26f);
            Button sellButton = NewButton("Btn_Sell", benchColumn, "Продать", SellColor, 36f, out _);
            Anchored((RectTransform)sellButton.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -244f), new Vector2(256f, 120f));

            // ===== HUD (всегда сверху) =====
            RectTransform hud = NewRect("HUD", root);
            TopStretch(hud, 0f, HudHeight, 0f);
            AddImage(hud, HudBg, sliced: false, raycast: false);
            RectTransform hudRow = Column("Row", hud, 1060f);
            AddRow(hudRow, 10f);
            TextMeshProUGUI dayText = HudText("DayText", hudRow, Color.white, 1.35f);
            TextMeshProUGUI goldText = HudText("GoldText", hudRow, GoldText, 0.9f);
            TextMeshProUGUI productionText = HudText("ProductionText", hudRow, new Color(0.85f, 0.78f, 0.65f), 0.9f);
            TextMeshProUGUI foodText = HudText("FoodText", hudRow, new Color(0.65f, 0.92f, 0.55f), 1.2f);
            TextMeshProUGUI populationText = HudText("PopulationText", hudRow, new Color(0.75f, 0.85f, 1f), 0.8f);
            TextMeshProUGUI knowledgeText = HudText("KnowledgeText", hudRow, new Color(0.62f, 0.80f, 1f), 0.9f);

            // ===== Сообщения (полоса между полем и скамейкой) =====
            RectTransform messagePanel = NewRect("MessagePanel", root);
            Anchored(messagePanel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -(HudHeight + 1312f)), new Vector2(1040f, 104f));
            AddImage(messagePanel, new Color(0f, 0f, 0f, 0.78f), sliced: true, raycast: false);
            TextMeshProUGUI messageText = NewText("MessageText", messagePanel, string.Empty, 30f, TextAlignmentOptions.Center, Color.white, bold: true);
            Stretch(messageText.rectTransform, 16f, 16f, 6f, 6f);
            AutoSize(messageText, 16f, 30f);
            messagePanel.gameObject.SetActive(false);

            // ===== Конец забега =====
            RectTransform gameOver = NewRect("GameOverScreen", root);
            Stretch(gameOver, 0f, 0f, 0f, 0f);
            AddImage(gameOver, new Color(0f, 0f, 0f, 0.88f), sliced: false, raycast: true);
            TextMeshProUGUI gameOverText = NewText("GameOverText", gameOver, string.Empty, 80f, TextAlignmentOptions.Center, Color.white, bold: true);
            Anchored(gameOverText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), new Vector2(1000f, 500f));
            Button restartButton = NewButton("Btn_Restart", gameOver, "Новый забег", AutoColor, 48f, out _);
            Anchored((RectTransform)restartButton.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -200f), new Vector2(520f, 150f));
            gameOver.gameObject.SetActive(false);

            // ===== Горизонтальная раскладка (ПК, 1920×1080): слева — поле/барабаны/слот, справа — панели и армия =====
            // Слева колонка шириной LandLeft (от x=20), справа — LandRight (армия прижата к правому нижнему углу).
            Landscape(hudRow, r => r.sizeDelta = new Vector2(1500f, 0f));
            Landscape(prep, r => Stretch(r, 0f, 0f, 0f, 0f));

            Landscape(boardTab, r => TopStretch(r, 112f, LandTabHeight, 0f));
            Landscape(gridArea, r => TopLeft(r, 20f, 8f, LandLeft, LandLeft));
            Landscape(focusBar, r => TopRight(r, 20f, 8f, LandRight - 40f, 80f));
            Landscape(boardControls, r => TopRight(r, 20f, 100f, LandRight - 40f, 92f));

            Landscape(cityTab, r => TopStretch(r, 112f, LandTabHeight, 0f));
            Landscape(map, r => Stretch(r, 20f, 760f, 78f, 0f));
            Landscape(panel, r => TopRight(r, 20f, 78f, 720f, LandTabHeight - 78f));
            Landscape(panelTitle.rectTransform, r => TopLeft(r, 20f, 12f, 680f, 50f));
            Landscape(panelInfo.rectTransform, r => TopLeft(r, 20f, 66f, 680f, 300f));
            Landscape(actions, r => Anchored(r, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -380f), new Vector2(680f, 400f)));

            // Деревья науки и войск высокие — колонки шире, а вкладка целиком чуть мельче
            Landscape(techTab, r => Anchored(r, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -112f), new Vector2(1700f, 1100f)), 0.78f);
            Landscape(unitsTab, r => Anchored(r, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -112f), new Vector2(1800f, 1180f)), 0.735f);

            // Бой: слева слот и подписи, посередине легенда, справа — арена (камера вписывает её в ArenaFrame)
            Landscape(arena, r => Stretch(r, 0f, 0f, 0f, 0f));
            Landscape(stepText.rectTransform, r => TopLeft(r, 20f, 12f, 200f, 84f));
            Landscape((RectTransform)speedButton.transform, r => TopLeft(r, 524f, 12f, 196f, 84f));
            Landscape(slotPanel, r => TopLeft(r, 60f, 108f, 620f, 620f));
            Landscape(slotResult.rectTransform, r => TopLeft(r, 20f, 760f, 700f, 48f));
            Landscape(enemyTurn.rectTransform, r => TopLeft(r, 20f, 820f, 700f, 44f));
            Landscape(legend.rectTransform, r => TopLeft(r, 740f, 108f, 225f, 860f));
            Landscape(arenaFrame, r => Stretch(r, 980f, 20f, 12f, RosterHeight + 24f));
            Landscape(battleResult.rectTransform, r => Stretch(r, 980f, 20f, 12f, RosterHeight + 24f));
            Landscape(roster, r =>
            {
                r.anchorMin = new Vector2(0f, 0f);
                r.anchorMax = new Vector2(1f, 0f);
                r.pivot = new Vector2(0.5f, 0f);
                r.offsetMin = new Vector2(980f, 12f);
                r.offsetMax = new Vector2(-20f, 12f + RosterHeight);
            });

            // Трофеи: слева барабаны и журнал, справа подсказка, легенда линий и армия
            Landscape(loot, r => Stretch(r, 0f, 0f, 0f, 0f));
            Landscape(lootTitle.rectTransform, r => TopLeft(r, 20f, 10f, LandLeft, 64f));
            Landscape(reels, r => TopLeft(r, 20f, 84f, LandLeft, LandLeft * 0.6f));
            Landscape(lootControls, r => TopLeft(r, 20f, 594f, LandLeft, 96f));
            Landscape(lootLog.rectTransform, r => TopLeft(r, 20f, 700f, LandLeft, 270f));
            Landscape(lootHint.rectTransform, r => TopRight(r, 20f, 10f, LandRight - 40f, 100f));
            Landscape(paylineLegend, r => TopRight(r, 10f, 120f, LandRight - 20f, 136f));

            Landscape(benchPanel, r => Anchored(r, new Vector2(1f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(LandRight, 400f)));
            Landscape(benchColumn, r => r.sizeDelta = new Vector2(LandRight, 0f));
            // Сообщения — туда, где они ничего не закрывают: над армией, под ареной боя или поверх строки сводки под вкладками
            System.Action<RectTransform> aboveBench = r =>
                Anchored(r, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-20f, 410f), new Vector2(LandRight - 40f, 104f));
            Landscape(messagePanel, aboveBench);
            adaptive.messagePanel = messagePanel;
            adaptive.messageDocks = new List<AdaptiveLayout.Dock>
            {
                new AdaptiveLayout.Dock { whenActive = benchPanel.gameObject, pose = PoseOf(messagePanel, aboveBench) },
                new AdaptiveLayout.Dock
                {
                    whenActive = battleScreen.gameObject,
                    pose = PoseOf(messagePanel, r => Anchored(r, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-20f, 4f), new Vector2(900f, 100f))),
                },
                new AdaptiveLayout.Dock
                {
                    whenActive = prepScreen.gameObject,
                    pose = PoseOf(messagePanel, r => Anchored(r, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -(HudHeight + 108f)), new Vector2(1400f, 64f))),
                },
            };

            adaptive.worldCamera = camera;
            adaptive.battle = battle;
            adaptive.arenaFrame = arenaFrame;

            // ---------- Связи между скриптами ----------
            game.cityGrid = cityGrid;
            game.lootSlot = lootSlot;
            game.bench = bench;
            game.battle = battle;
            game.city = city;
            game.techTree = techTree;
            game.powers = powers;
            game.prepScreen = prepScreen.gameObject;
            game.boardTab = boardTab.gameObject;
            game.cityTab = cityTab.gameObject;
            game.techTab = techTab.gameObject;
            game.unitsTab = unitsTab.gameObject;
            game.battleScreen = battleScreen.gameObject;
            game.lootScreen = lootScreen.gameObject;
            game.gameOverScreen = gameOver.gameObject;
            game.benchPanel = benchPanel.gameObject;
            game.sceneVersion = SceneVersion;
            game.boardTabButton = boardTabButton.GetComponent<Image>();
            game.cityTabButton = cityTabButton.GetComponent<Image>();
            game.techTabButton = techTabButton.GetComponent<Image>();
            game.unitsTabButton = unitsTabButton.GetComponent<Image>();
            game.dayText = dayText;
            game.goldText = goldText;
            game.productionText = productionText;
            game.foodText = foodText;
            game.populationText = populationText;
            game.knowledgeText = knowledgeText;
            game.messageText = messageText;
            game.messagePanel = messagePanel.gameObject;
            game.gameOverText = gameOverText;
            game.startingUnits = new List<UnitData> { starter, starter };

            city.techTree = techTree;
            powers.city = city;
            powers.battle = battle;

            cityGrid.gridContainer = gridArea;
            cityGrid.tilePrefab = prefabs.Tile;
            cityGrid.reserveText = reserveText;
            cityGrid.autoPlayLabel = autoLabel;
            cityGrid.bench = bench;
            cityGrid.game = game;
            cityGrid.city = city;
            cityGrid.focusLabels = focusLabels;
            cityGrid.unitPool = new List<UnitData>(units);
            cityGrid.specialUnits = new List<UnitData>(specialUnits);

            lootSlot.gridContainer = reels;
            lootSlot.cellPrefab = prefabs.Tile;
            lootSlot.spinsText = lootSpins;
            lootSlot.autoLabel = lootAutoLabel;
            lootSlot.bench = bench;
            lootSlot.game = game;
            lootSlot.city = city;
            lootSlot.itemPool = new List<ItemData>(items);
            lootSlot.logText = lootLog;

            bench.slotContainer = benchSlots;
            bench.slotPrefab = prefabs.BenchSlot;
            bench.game = game;
            bench.infoText = benchInfo;
            bench.sellButton = sellButton;

            battle.board = battleBoard;
            battle.city = city;
            battle.stepText = stepText;
            battle.resultText = battleResult;
            battle.enemyTurnText = enemyTurn;
            battle.speedButtonLabel = speedLabel;
            battle.rosterLabels = rosterLabels.ToArray();
            battle.legendText = legend;
            battle.enemyPool = new List<UnitData>(barbarians);
            battle.enemySpecials = new List<UnitData>(enemySpecials);

            battleBoard.gridContainer = slotGrid;
            battleBoard.cellPrefab = prefabs.Tile;
            battleBoard.city = city;
            battleBoard.resultText = slotResult;

            game.recruit = recruit;
            recruit.city = city;
            recruit.board = cityGrid;
            recruit.bench = bench;
            recruit.game = game;

            mapView.city = city;
            mapView.game = game;
            mapView.powers = powers;
            mapView.mapContainer = map;
            mapView.cellPrefab = prefabs.CityCell;
            mapView.summaryText = citySummary;
            mapView.panelTitle = panelTitle;
            mapView.panelInfo = panelInfo;
            mapView.actionsContainer = actionsList;
            mapView.actionButtonPrefab = prefabs.ActionButton;

            techView.techTree = techTree;
            techView.game = game;
            techView.city = city;
            techView.summaryText = techSummary;

            unitView.city = city;
            unitView.techTree = techTree;
            unitView.game = game;
            unitView.summaryText = unitsSummary;

            // ---------- Кнопки ----------
            UnityEventTools.AddPersistentListener(boardTabButton.onClick, game.ShowBoardTab);
            UnityEventTools.AddPersistentListener(cityTabButton.onClick, game.ShowCityTab);
            UnityEventTools.AddPersistentListener(techTabButton.onClick, game.ShowTechTab);
            UnityEventTools.AddPersistentListener(unitsTabButton.onClick, game.ShowUnitsTab);
            UnityEventTools.AddPersistentListener(fightButton.onClick, game.EndTurn);
            UnityEventTools.AddPersistentListener(autoButton.onClick, recruit.ToggleAuto);
            UnityEventTools.AddPersistentListener(lootSpinButton.onClick, lootSlot.Spin);
            UnityEventTools.AddPersistentListener(lootAutoButton.onClick, lootSlot.ToggleAutoSpin);
            UnityEventTools.AddPersistentListener(lootNextButton.onClick, game.FinishLoot);
            UnityEventTools.AddPersistentListener(sellButton.onClick, bench.SellSelected);
            UnityEventTools.AddPersistentListener(speedButton.onClick, battle.ToggleSpeed);
            UnityEventTools.AddPersistentListener(restartButton.onClick, game.StartRun);
        }

        // =====================================================================
        //  Префабы
        // =====================================================================

        private static TileView BuildTilePrefab()
        {
            var root = new GameObject("Tile", typeof(RectTransform)) { layer = LayerMask.NameToLayer("UI") };
            var rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(120f, 120f);
            Image background = root.AddComponent<Image>(); // форму и цвет задаёт TileView во время игры
            TileView view = root.AddComponent<TileView>();

            RectTransform iconRect = NewRect("Icon", rect);
            iconRect.anchorMin = new Vector2(0.12f, 0.12f);
            iconRect.anchorMax = new Vector2(0.88f, 0.88f);
            iconRect.offsetMin = Vector2.zero;
            iconRect.offsetMax = Vector2.zero;
            Image icon = iconRect.gameObject.AddComponent<Image>();
            icon.raycastTarget = false;
            icon.preserveAspect = true;
            icon.enabled = false;

            TextMeshProUGUI label = NewText("Label", rect, string.Empty, 64f, TextAlignmentOptions.Center, Color.white, bold: true);
            Stretch(label.rectTransform, 8f, 8f, 8f, 8f);
            AutoSize(label, 10f, 80f);

            view.background = background;
            view.icon = icon;
            view.label = label;
            return SavePrefab(root, PrefabsFolder + "/Tile.prefab").GetComponent<TileView>();
        }

        private static BenchSlotView BuildBenchSlotPrefab()
        {
            var root = new GameObject("BenchSlot", typeof(RectTransform)) { layer = LayerMask.NameToLayer("UI") };
            var rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(120f, 160f);
            Image background = AddImage(rect, new Color(0.19f, 0.21f, 0.27f), sliced: true, raycast: true);
            BenchSlotView view = root.AddComponent<BenchSlotView>();

            RectTransform unitRect = NewRect("Unit", rect);
            Anchored(unitRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 12f), new Vector2(88f, 88f));
            Image unitImage = unitRect.gameObject.AddComponent<Image>();
            unitImage.raycastTarget = false;
            unitImage.preserveAspect = true;
            unitImage.enabled = false;

            TextMeshProUGUI label = NewText("Label", rect, string.Empty, 44f, TextAlignmentOptions.Center, Color.white, bold: true);
            Anchored(label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 12f), new Vector2(88f, 88f));

            TextMeshProUGUI level = NewText("Level", rect, string.Empty, 26f, TextAlignmentOptions.Center, GoldText, bold: true);
            BottomStretch(level.rectTransform, 4f, 36f, 4f);
            AutoSize(level, 14f, 26f);

            // Звёзды «★★» — плашкой у нижнего края значка юнита
            RectTransform starsRect = NewRect("StarsBadge", rect);
            Anchored(starsRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -24f), new Vector2(88f, 32f));
            Image starsBackground = AddImage(starsRect, new Color(0.05f, 0.06f, 0.09f, 0.85f), sliced: true, raycast: false);
            starsBackground.enabled = false;
            TextMeshProUGUI starsLabel = NewText("StarsText", starsRect, string.Empty, 28f, TextAlignmentOptions.Center, GoldText, bold: true);
            Stretch(starsLabel.rectTransform, 2f, 2f, 0f, 0f);
            AutoSize(starsLabel, 12f, 28f);

            Image armorBadge = Badge("ArmorBadge", rect, new Vector2(0f, 1f), new Vector2(6f, -6f));
            Image weaponBadge = Badge("WeaponBadge", rect, new Vector2(1f, 1f), new Vector2(-6f, -6f));

            view.background = background;
            view.unitImage = unitImage;
            view.label = label;
            view.levelText = level;
            view.starsText = starsLabel;
            view.starsBackground = starsBackground;
            view.armorBadge = armorBadge;
            view.weaponBadge = weaponBadge;
            return SavePrefab(root, PrefabsFolder + "/BenchSlot.prefab").GetComponent<BenchSlotView>();
        }

        /// <summary>Легенда линий трофеев: три группы мини-сеток 5×3 — прямые воинам, углы лучникам, зигзаги жрецам.</summary>
        private static RectTransform BuildPaylineLegend(RectTransform parent, float top)
        {
            const float cell = 16f;
            const float gap = 3f;
            const float groupWidth = 340f;
            RectTransform legend = NewRect("PaylineLegend", parent);
            TopStretch(legend, top, 136f, 20f);
            UnitClass[] classes = LootSlotMachine.LootClasses;
            for (int g = 0; g < classes.Length; g++)
            {
                UnitClass unitClass = classes[g];
                Color classColor = GameVisuals.ClassColor(unitClass);
                RectTransform group = NewRect($"Group_{unitClass}", legend);
                Anchored(group, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2((g - (classes.Length - 1) * 0.5f) * (groupWidth + 6f), 0f),
                    new Vector2(groupWidth, 136f));
                AddImage(group, CardBg, sliced: true, raycast: false);
                TextMeshProUGUI caption = NewText("Caption", group, $"{GameVisuals.ClassIcon(unitClass)} {LootSlotMachine.ShapeName(unitClass)}",
                    26f, TextAlignmentOptions.Center, classColor, bold: true);
                TopStretch(caption.rectTransform, 6f, 34f, 8f);
                AutoSize(caption, 14f, 26f);

                var lines = LootSlotMachine.Paylines.Where(p => p.Class == unitClass).ToList();
                float miniWidth = LootSlotMachine.Reels * cell + (LootSlotMachine.Reels - 1) * gap;
                float miniHeight = LootSlotMachine.Rows * cell + (LootSlotMachine.Rows - 1) * gap;
                for (int i = 0; i < lines.Count; i++)
                {
                    RectTransform mini = NewRect($"Line_{i + 1}", group);
                    Anchored(mini, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2((i - (lines.Count - 1) * 0.5f) * (miniWidth + 18f), -54f),
                        new Vector2(miniWidth, miniHeight));
                    for (int x = 0; x < LootSlotMachine.Reels; x++)
                    {
                        for (int y = 0; y < LootSlotMachine.Rows; y++)
                        {
                            RectTransform dot = NewRect($"C{x}{y}", mini);
                            Anchored(dot, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x * (cell + gap), -y * (cell + gap)), new Vector2(cell, cell));
                            bool onLine = lines[i].Rows[x] == y;
                            AddImage(dot, onLine ? classColor : new Color(1f, 1f, 1f, 0.10f), sliced: true, raycast: false);
                        }
                    }
                }
            }
            return legend;
        }

        private static Image Badge(string name, RectTransform parent, Vector2 corner, Vector2 offset)
        {
            RectTransform badgeRect = NewRect(name, parent);
            Anchored(badgeRect, corner, corner, offset, new Vector2(32f, 32f));
            Image badge = badgeRect.gameObject.AddComponent<Image>();
            badge.raycastTarget = false;
            badge.preserveAspect = true;
            badge.enabled = false; // спрайт и цвет (уровень предмета) задаёт BenchSlotView
            return badge;
        }

        private static CityCellView BuildCityCellPrefab()
        {
            var root = new GameObject("CityCell", typeof(RectTransform)) { layer = LayerMask.NameToLayer("UI") };
            var rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(170f, 170f);
            Image background = AddImage(rect, new Color(0.15f, 0.17f, 0.22f), sliced: true, raycast: true);
            CityCellView view = root.AddComponent<CityCellView>();

            TextMeshProUGUI title = NewText("Title", rect, "+", 30f, TextAlignmentOptions.Center, Color.white, bold: true);
            Stretch(title.rectTransform, 8f, 8f, 20f, 44f);
            AutoSize(title, 14f, 30f);
            TextMeshProUGUI detail = NewText("Detail", rect, string.Empty, 22f, TextAlignmentOptions.Center, Color.white, bold: true);
            BottomStretch(detail.rectTransform, 20f, 30f, 6f);
            AutoSize(detail, 12f, 22f);

            RectTransform hpRect = NewRect("HpBar", rect);
            BottomStretch(hpRect, 8f, 10f, 14f);
            Image hp = hpRect.gameObject.AddComponent<Image>();
            hp.sprite = UiSprite; // у Filled без спрайта заливка не работает
            hp.type = Image.Type.Filled;
            hp.fillMethod = Image.FillMethod.Horizontal;
            hp.fillOrigin = (int)Image.OriginHorizontal.Left;
            hp.raycastTarget = false;
            hp.enabled = false;

            view.background = background;
            view.title = title;
            view.detail = detail;
            view.hpBar = hp;
            return SavePrefab(root, PrefabsFolder + "/CityCell.prefab").GetComponent<CityCellView>();
        }

        private static UpgradeCardView BuildUpgradeCardPrefab()
        {
            var root = new GameObject("UpgradeCard", typeof(RectTransform)) { layer = LayerMask.NameToLayer("UI") };
            var rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(330f, 128f);
            Image background = AddImage(rect, CardBg, sliced: true, raycast: true);
            UpgradeCardView view = root.AddComponent<UpgradeCardView>();

            TextMeshProUGUI title = NewText("Title", rect, "Улучшение", 22f, TextAlignmentOptions.Center, Color.white, bold: true);
            TopStretch(title.rectTransform, 4f, 28f, 8f);
            AutoSize(title, 12f, 22f);
            TextMeshProUGUI description = NewText("Description", rect, string.Empty, 15f, TextAlignmentOptions.Center, MutedText);
            TopStretch(description.rectTransform, 31f, 58f, 8f); // описания особых символов длинные — места на 3–4 строки
            AutoSize(description, 10f, 15f);
            Button button = NewButton("Button", rect, "Купить", BuildColor, 17f, out TextMeshProUGUI buttonLabel);
            BottomStretch((RectTransform)button.transform, 5f, 32f, 8f);

            view.background = background;
            view.title = title;
            view.description = description;
            view.button = button;
            view.buttonLabel = buttonLabel;
            return SavePrefab(root, PrefabsFolder + "/UpgradeCard.prefab").GetComponent<UpgradeCardView>();
        }

        private static TechCardView BuildTechCardPrefab()
        {
            var root = new GameObject("TechCard", typeof(RectTransform)) { layer = LayerMask.NameToLayer("UI") };
            var rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(200f, 176f);
            Image background = AddImage(rect, CardBg, sliced: true, raycast: true);
            TechCardView view = root.AddComponent<TechCardView>();

            TextMeshProUGUI title = NewText("Title", rect, "Технология", 22f, TextAlignmentOptions.Center, Color.white, bold: true);
            TopStretch(title.rectTransform, 6f, 34f, 6f);
            AutoSize(title, 12f, 22f);
            TextMeshProUGUI description = NewText("Description", rect, string.Empty, 16f, TextAlignmentOptions.Center, MutedText);
            TopStretch(description.rectTransform, 40f, 84f, 6f);
            AutoSize(description, 10f, 16f);
            Button button = NewButton("Button", rect, "Изучить", BuildColor, 18f, out TextMeshProUGUI buttonLabel);
            BottomStretch((RectTransform)button.transform, 6f, 42f, 6f);

            view.background = background;
            view.title = title;
            view.description = description;
            view.button = button;
            view.buttonLabel = buttonLabel;
            return SavePrefab(root, PrefabsFolder + "/TechCard.prefab").GetComponent<TechCardView>();
        }

        private static Button BuildActionButtonPrefab()
        {
            var root = new GameObject("ActionButton", typeof(RectTransform)) { layer = LayerMask.NameToLayer("UI") };
            var rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(440f, 54f);
            Image image = AddImage(rect, BuildColor, sliced: true, raycast: true);
            var button = root.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.disabledColor = new Color(0.5f, 0.5f, 0.55f, 0.6f);
            button.colors = colors;
            TextMeshProUGUI label = NewText("Label", rect, "Действие", 24f, TextAlignmentOptions.Center, Color.white, bold: true);
            Stretch(label.rectTransform, 12f, 12f, 4f, 4f);
            AutoSize(label, 12f, 24f);
            return SavePrefab(root, PrefabsFolder + "/ActionButton.prefab").GetComponent<Button>();
        }

        private static GameObject SavePrefab(GameObject root, string path)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // =====================================================================
        //  Шрифт и иконки
        // =====================================================================

        /// <summary>
        /// Динамический шрифт из LiberationSans с атласом 1024 и Multi Atlas: стандартный fallback TMP
        /// (512×512) вмещает лишь ~40 символов, и часть кириллицы превращается в квадраты.
        /// Шрифт становится шрифтом TMP по умолчанию и глобальным fallback.
        /// </summary>
        private static TMP_FontAsset EnsureCyrillicFont()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (font == null)
            {
                var source = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
                if (source == null)
                {
                    Debug.LogWarning($"[Casiwar] Не найден {SourceFontPath} — используется шрифт TMP по умолчанию.");
                    return TMP_Settings.defaultFontAsset;
                }

                EnsureFolder(FontsFolder);
                font = TMP_FontAsset.CreateFontAsset(source, 56, 6, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
                font.name = "Casiwar SDF";
                AssetDatabase.CreateAsset(font, FontAssetPath);
                font.atlasTextures[0].name = "Casiwar SDF Atlas";
                AssetDatabase.AddObjectToAsset(font.atlasTextures[0], font);
                font.material.name = "Casiwar SDF Material";
                AssetDatabase.AddObjectToAsset(font.material, font);
            }

            // Запекаем нужные символы (уже имеющиеся пропускаются)
            if (!font.HasCharacters(PrebakedCharacters))
            {
                font.TryAddCharacters(PrebakedCharacters, out string missing);
                if (!string.IsNullOrEmpty(missing)) Debug.Log($"[Casiwar] В шрифте нет символов: {missing}");
                EditorUtility.SetDirty(font);
                AssetDatabase.SaveAssets();
            }

            // Шрифт по умолчанию для новых текстов + общий fallback для старых
            TMP_Settings settings = Resources.Load<TMP_Settings>("TMP Settings");
            if (settings != null)
            {
                var serialized = new SerializedObject(settings);
                serialized.FindProperty("m_defaultFontAsset").objectReferenceValue = font;
                SerializedProperty fallbacks = serialized.FindProperty("m_fallbackFontAssets");
                bool listed = false;
                for (int i = 0; i < fallbacks.arraySize; i++)
                {
                    if (fallbacks.GetArrayElementAtIndex(i).objectReferenceValue == font) listed = true;
                }
                if (!listed)
                {
                    fallbacks.InsertArrayElementAtIndex(fallbacks.arraySize);
                    fallbacks.GetArrayElementAtIndex(fallbacks.arraySize - 1).objectReferenceValue = font;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(settings);
            }
            return font;
        }

        private struct IconSpec
        {
            public string Name;
            public uint Unicode;
            /// <summary>Рисунок из многоугольников…</summary>
            public System.Func<List<Layer>> Draw;
            /// <summary>…или готовая картинка (иконки классов с фишек поля) — на кружке цвета класса, как фишка.</summary>
            public string Image;
            public UnitClass Class;
        }

        private const string ClassIconsFolder = "Assets/Art/Resources/Icons/";

        private static readonly IconSpec[] Icons =
        {
            new IconSpec { Name = "star", Unicode = 0x2605, Draw = StarIcon },              // ★ улучшения зданий
            new IconSpec { Name = "production", Unicode = 0x2692, Draw = ProductionIcon },  // ⚒ очки производства
            new IconSpec { Name = "gold", Unicode = 0x1FA99, Draw = GoldIcon },             // 🪙 золото
            new IconSpec { Name = "attack", Unicode = 0x2694, Draw = AttackIcon },          // ⚔ атака
            new IconSpec { Name = "move", Unicode = 0x279C, Draw = MoveIcon },              // ➜ перемещение
            new IconSpec { Name = "food", Unicode = 0x1F34E, Draw = FoodIcon },             // 🍎 еда
            new IconSpec { Name = "population", Unicode = 0x1F464, Draw = PopulationIcon }, // 👤 жители
            new IconSpec { Name = "knowledge", Unicode = 0x1F4D6, Draw = BookIcon },        // 📖 очки знаний
            new IconSpec { Name = "banner", Unicode = 0x1F6A9, Draw = FlagIcon },           // 🚩 Знамя — джокер боевого слота
            new IconSpec { Name = "candle", Unicode = 0x1F56F, Draw = CandleIcon },         // 🕯 Свеча — 3+ в слоте: «Чудо»
            // Свои значки (GameVisuals.Icon…): область частного использования — в шрифте их нет
            new IconSpec { Name = "morale", Unicode = 0xE010, Draw = FlameIcon },
            new IconSpec { Name = "hp", Unicode = 0xE011, Draw = HeartIcon },
            new IconSpec { Name = "pause", Unicode = 0xE012, Draw = PauseIcon },
            new IconSpec { Name = "play", Unicode = 0xE013, Draw = PlayIcon },
            new IconSpec { Name = "speed", Unicode = 0xE014, Draw = SpeedIcon },
            new IconSpec { Name = "turns", Unicode = 0xE015, Draw = HourglassIcon },
            new IconSpec { Name = "dead", Unicode = 0xE016, Draw = SkullIcon },
            new IconSpec { Name = "hold", Unicode = 0xE017, Draw = LockIcon },
            new IconSpec { Name = "spin", Unicode = 0xE018, Draw = SpinIcon },
            new IconSpec { Name = "walls", Unicode = 0xE019, Draw = TowerIcon },
            // Классы — картинки с фишек поля (GameVisuals.ClassIcon)
            new IconSpec { Name = "class_warrior", Unicode = 0xE001, Image = ClassIconsFolder + "warrior.png", Class = UnitClass.Warrior },
            new IconSpec { Name = "class_archer", Unicode = 0xE002, Image = ClassIconsFolder + "archer.png", Class = UnitClass.Archer },
            new IconSpec { Name = "class_priest", Unicode = 0xE003, Image = ClassIconsFolder + "priest.png", Class = UnitClass.Priest },
            new IconSpec { Name = "class_bomber", Unicode = 0xE004, Image = ClassIconsFolder + "bomber.png", Class = UnitClass.Bomber },
            new IconSpec { Name = "class_monk", Unicode = 0xE005, Image = ClassIconsFolder + "monk.png", Class = UnitClass.Monk },
            new IconSpec { Name = "class_mage", Unicode = 0xE006, Image = ClassIconsFolder + "mage.png", Class = UnitClass.Mage },
            new IconSpec { Name = "class_hero", Unicode = 0xE007, Image = ClassIconsFolder + "hero.png", Class = UnitClass.Hero },
            new IconSpec { Name = "class_militia", Unicode = 0xE008, Image = ClassIconsFolder + "militia.png", Class = UnitClass.None },
        };

        /// <summary>
        /// В шрифте нет символов ★ ⚒ 🪙 ⚔ ➜ 🍎 👤, поэтому рисуем их в атлас и создаём TMP Sprite Asset,
        /// подключённый к спрайтам TMP по умолчанию: эти символы в любом тексте рисуются картинками.
        /// </summary>
        private static void EnsureIconSprites()
        {
            var icons = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(IconsSpriteAssetPath);
            var existingImporter = AssetImporter.GetAtPath(IconsTexturePath) as TextureImporter;
            bool upToDate = icons != null && existingImporter != null && existingImporter.userData == IconsVersion &&
                            Icons.All(spec => icons.spriteCharacterTable.Any(c => c.unicode == spec.Unicode));
            if (!upToDate)
            {
                EnsureFolder(FontsFolder);

                // 1) Атлас иконок (PNG): иконки IconSize×IconSize в ряд
                Texture2D generated = DrawIconAtlas();
                File.WriteAllBytes(IconsTexturePath, generated.EncodeToPNG());
                Object.DestroyImmediate(generated);
                AssetDatabase.ImportAsset(IconsTexturePath);
                var importer = (TextureImporter)AssetImporter.GetAtPath(IconsTexturePath);
                importer.textureType = TextureImporterType.Sprite; // для спрайтов размер не округляется до степени двойки
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 4096; // иначе большой атлас ужмётся, и прямоугольники значков съедут
                importer.userData = IconsVersion; // при изменении рисунков поднимите версию — атлас перерисуется
                importer.SaveAndReimport();
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(IconsTexturePath);

                // 2) TMP Sprite Asset: символ → прямоугольник на атласе (отдельные Sprite-объекты TMP не нужны)
                if (icons == null)
                {
                    icons = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
                    AssetDatabase.CreateAsset(icons, IconsSpriteAssetPath);
                }
                icons.hashCode = TMP_TextUtilities.GetSimpleHashCode(icons.name);
                icons.spriteSheet = texture;
                icons.spriteGlyphTable.Clear();
                icons.spriteCharacterTable.Clear();
                for (int i = 0; i < Icons.Length; i++)
                {
                    // Метрики как у эмодзи из TMP Essentials (128×128) — иконка стоит на строке вровень с текстом
                    var glyph = new TMP_SpriteGlyph((uint)i, new GlyphMetrics(IconSize, IconSize, 0f, 112f, IconSize),
                        new GlyphRect(i % IconColumns * IconSize, i / IconColumns * IconSize, IconSize, IconSize), 1f, 0);
                    icons.spriteGlyphTable.Add(glyph);
                    icons.spriteCharacterTable.Add(new TMP_SpriteCharacter(Icons[i].Unicode, glyph) { name = Icons[i].Name });
                }

                if (icons.material == null)
                {
                    var material = new Material(Shader.Find("TextMeshPro/Sprite")) { name = "Casiwar Icons Material", hideFlags = HideFlags.HideInHierarchy };
                    icons.material = material;
                    AssetDatabase.AddObjectToAsset(material, icons);
                }
                icons.material.SetTexture(ShaderUtilities.ID_MainTex, texture);

                // version — internal; без него TMP при загрузке «обновит» ассет и сотрёт таблицы
                var serialized = new SerializedObject(icons);
                serialized.FindProperty("m_Version").stringValue = "1.1.0";
                serialized.ApplyModifiedPropertiesWithoutUndo();
                icons.UpdateLookupTables();
                EditorUtility.SetDirty(icons);
                AssetDatabase.SaveAssets();
            }

            TMP_SpriteAsset defaultSprites = TMP_Settings.defaultSpriteAsset;
            if (defaultSprites != null && defaultSprites != icons)
            {
                if (defaultSprites.fallbackSpriteAssets == null) defaultSprites.fallbackSpriteAssets = new List<TMP_SpriteAsset>();
                defaultSprites.fallbackSpriteAssets.RemoveAll(asset => asset == null);
                if (!defaultSprites.fallbackSpriteAssets.Contains(icons)) defaultSprites.fallbackSpriteAssets.Add(icons);
                EditorUtility.SetDirty(defaultSprites);
            }
            AssetDatabase.SaveAssets();
        }

        // ---------- Рисование иконок (многоугольники со сглаживанием) ----------

        private struct Layer
        {
            public Vector2[] Polygon;
            public Color Color;
        }

        private static Texture2D DrawIconAtlas()
        {
            int rows = (Icons.Length + IconColumns - 1) / IconColumns;
            var texture = new Texture2D(IconSize * IconColumns, IconSize * rows, TextureFormat.RGBA32, false);
            var pixels = new Color32[texture.width * texture.height];
            for (int i = 0; i < Icons.Length; i++)
            {
                // Клетка i: столбец i % IconColumns, строка i / IconColumns (снизу вверх — как GlyphRect у TMP)
                int offset = i / IconColumns * IconSize * texture.width + i % IconColumns * IconSize;
                if (Icons[i].Image != null) BlitImage(Icons[i].Image, pixels, texture.width, offset, GameVisuals.IconBackground(Icons[i].Class));
                else Rasterize(Icons[i].Draw(), pixels, texture.width, offset);
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// Картинку с диска — в клетку атласа, с сохранением пропорций, поверх кружка цвета background
        /// (в тексте тонкий меч или книга без подложки теряются). Нет файла — клетка пустая.
        /// </summary>
        private static void BlitImage(string path, Color32[] pixels, int textureWidth, int offsetX, Color background)
        {
            if (!File.Exists(path)) return;
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!source.LoadImage(File.ReadAllBytes(path)))
            {
                Object.DestroyImmediate(source);
                return;
            }
            float fit = IconSize * 0.80f / Mathf.Max(source.width, source.height);
            float width = source.width * fit, height = source.height * fit;
            float left = (IconSize - width) * 0.5f, bottom = (IconSize - height) * 0.5f;
            for (int y = 0; y < IconSize; y++)
            {
                for (int x = 0; x < IconSize; x++)
                {
                    // Подложка: кружок цвета класса с тёмной обводкой
                    float distance = new Vector2(x + 0.5f - IconSize * 0.5f, y + 0.5f - IconSize * 0.5f).magnitude;
                    float outer = Mathf.Clamp01(IconSize * 0.49f - distance);
                    float inner = Mathf.Clamp01(IconSize * 0.45f - distance);
                    Color baseColor = Color.Lerp(new Color(Outline.r, Outline.g, Outline.b, outer), new Color(background.r, background.g, background.b, 1f), inner);
                    baseColor.a = outer;
                    float u = (x + 0.5f - left) / width, v = (y + 0.5f - bottom) / height;
                    Color art = u < 0f || v < 0f || u > 1f || v > 1f ? new Color(0f, 0f, 0f, 0f) : source.GetPixelBilinear(u, v);
                    float alpha = art.a + baseColor.a * (1f - art.a);
                    Color rgb = (art * art.a + baseColor * baseColor.a * (1f - art.a)) / Mathf.Max(alpha, 0.0001f);
                    pixels[y * textureWidth + offsetX + x] = new Color(rgb.r, rgb.g, rgb.b, alpha);
                }
            }
            Object.DestroyImmediate(source);
        }

        private static void Rasterize(List<Layer> layers, Color32[] pixels, int textureWidth, int offsetX)
        {
            const int samples = 4;
            for (int y = 0; y < IconSize; y++)
            {
                for (int x = 0; x < IconSize; x++)
                {
                    Color color = new Color(0f, 0f, 0f, 0f);
                    foreach (Layer layer in layers)
                    {
                        int inside = 0;
                        for (int sy = 0; sy < samples; sy++)
                        {
                            for (int sx = 0; sx < samples; sx++)
                            {
                                var point = new Vector2((x + (sx + 0.5f) / samples) / IconSize, (y + (sy + 0.5f) / samples) / IconSize);
                                if (GameVisuals.InsidePolygon(layer.Polygon, point)) inside++;
                            }
                        }
                        float coverage = inside / (float)(samples * samples);
                        if (coverage <= 0f) continue;
                        // Наложение слоя «поверх» с учётом покрытия пикселя
                        float alpha = coverage * layer.Color.a;
                        float outAlpha = alpha + color.a * (1f - alpha);
                        Color rgb = (layer.Color * alpha + color * color.a * (1f - alpha)) / Mathf.Max(outAlpha, 0.0001f);
                        color = new Color(rgb.r, rgb.g, rgb.b, outAlpha);
                    }
                    pixels[y * textureWidth + offsetX + x] = color;
                }
            }
        }

        private static readonly Color Outline = new Color(0.12f, 0.10f, 0.08f);

        private static List<Layer> StarIcon()
        {
            var center = new Vector2(0.5f, 0.47f);
            return new List<Layer>
            {
                new Layer { Polygon = Star(center, 0.49f), Color = new Color(0.55f, 0.33f, 0.05f) },
                new Layer { Polygon = Star(center, 0.42f), Color = new Color(1f, 0.84f, 0.25f) },
            };
        }

        private static List<Layer> ProductionIcon()
        {
            // Молот: рукоять и боёк, повёрнутые на 30°
            Vector2[] handle = Rotate(Box(0.45f, 0.12f, 0.55f, 0.66f), 30f);
            Vector2[] head = Rotate(Box(0.24f, 0.62f, 0.76f, 0.84f), 30f);
            return new List<Layer>
            {
                new Layer { Polygon = Offset(handle, 0.04f), Color = Outline },
                new Layer { Polygon = Offset(head, 0.04f), Color = Outline },
                new Layer { Polygon = handle, Color = new Color(0.62f, 0.40f, 0.20f) },
                new Layer { Polygon = head, Color = new Color(0.80f, 0.83f, 0.90f) },
            };
        }

        private static List<Layer> GoldIcon()
        {
            var center = new Vector2(0.5f, 0.5f);
            return new List<Layer>
            {
                new Layer { Polygon = Circle(center, 0.48f), Color = new Color(0.55f, 0.36f, 0.05f) },
                new Layer { Polygon = Circle(center, 0.42f), Color = new Color(1f, 0.80f, 0.22f) },
                new Layer { Polygon = Circle(center, 0.29f), Color = new Color(0.86f, 0.62f, 0.12f) },
                new Layer { Polygon = Circle(center, 0.24f), Color = new Color(1f, 0.86f, 0.35f) },
                new Layer { Polygon = Circle(new Vector2(0.38f, 0.64f), 0.07f), Color = new Color(1f, 0.97f, 0.8f, 0.9f) },
            };
        }

        private static List<Layer> AttackIcon()
        {
            // Два скрещённых меча (сначала обводки обоих, потом заливки — чтобы мечи читались целиком)
            var outlines = new List<Layer>();
            var fills = new List<Layer>();
            foreach (float angle in new[] { 42f, -42f })
            {
                Vector2[] blade = Rotate(new[] { V(0.455f, 0.28f), V(0.545f, 0.28f), V(0.545f, 0.86f), V(0.50f, 0.95f), V(0.455f, 0.86f) }, angle);
                Vector2[] guard = Rotate(Box(0.31f, 0.21f, 0.69f, 0.29f), angle);
                Vector2[] grip = Rotate(Box(0.465f, 0.05f, 0.535f, 0.22f), angle);
                outlines.Add(new Layer { Polygon = Offset(blade, 0.035f), Color = Outline });
                outlines.Add(new Layer { Polygon = Offset(guard, 0.035f), Color = Outline });
                outlines.Add(new Layer { Polygon = Offset(grip, 0.035f), Color = Outline });
                fills.Add(new Layer { Polygon = blade, Color = new Color(0.92f, 0.94f, 0.98f) });
                fills.Add(new Layer { Polygon = guard, Color = new Color(0.85f, 0.66f, 0.25f) });
                fills.Add(new Layer { Polygon = grip, Color = new Color(0.45f, 0.28f, 0.15f) });
            }
            outlines.AddRange(fills);
            return outlines;
        }

        private static List<Layer> MoveIcon()
        {
            // Стрелка из двух выпуклых частей: древко и наконечник
            Vector2[] shaft = Box(0.10f, 0.40f, 0.58f, 0.60f);
            Vector2[] head = { V(0.52f, 0.16f), V(0.92f, 0.50f), V(0.52f, 0.84f) };
            return new List<Layer>
            {
                new Layer { Polygon = Offset(shaft, 0.05f), Color = Outline },
                new Layer { Polygon = Offset(head, 0.05f), Color = Outline },
                new Layer { Polygon = shaft, Color = Color.white },
                new Layer { Polygon = head, Color = Color.white },
            };
        }

        private static List<Layer> FoodIcon()
        {
            // Яблоко: тело, черенок, листик и блик
            var body = new Vector2(0.5f, 0.42f);
            return new List<Layer>
            {
                new Layer { Polygon = Circle(body, 0.40f), Color = new Color(0.42f, 0.07f, 0.05f) },
                new Layer { Polygon = Circle(body, 0.34f), Color = new Color(0.90f, 0.23f, 0.18f) },
                new Layer { Polygon = Box(0.47f, 0.70f, 0.53f, 0.93f), Color = new Color(0.38f, 0.24f, 0.12f) },
                new Layer { Polygon = Ellipse(new Vector2(0.66f, 0.84f), 0.15f, 0.06f, 25f), Color = new Color(0.36f, 0.74f, 0.28f) },
                new Layer { Polygon = Circle(new Vector2(0.38f, 0.54f), 0.07f), Color = new Color(1f, 0.92f, 0.9f, 0.85f) },
            };
        }

        private static List<Layer> PopulationIcon()
        {
            // Житель: голова и плечи
            Vector2[] shoulders = HalfEllipse(new Vector2(0.5f, 0.06f), 0.36f, 0.42f);
            var face = new Color(0.82f, 0.88f, 0.96f);
            return new List<Layer>
            {
                new Layer { Polygon = Circle(new Vector2(0.5f, 0.70f), 0.20f), Color = Outline },
                new Layer { Polygon = Offset(shoulders, 0.04f), Color = Outline },
                new Layer { Polygon = Circle(new Vector2(0.5f, 0.70f), 0.16f), Color = face },
                new Layer { Polygon = shoulders, Color = face },
            };
        }

        private static List<Layer> FlagIcon()
        {
            // Знамя: древко и треугольное полотнище
            Vector2[] pole = Box(0.17f, 0.05f, 0.25f, 0.90f);
            Vector2[] cloth = { V(0.25f, 0.90f), V(0.90f, 0.72f), V(0.25f, 0.52f) };
            return new List<Layer>
            {
                new Layer { Polygon = Offset(pole, 0.035f), Color = Outline },
                new Layer { Polygon = Offset(cloth, 0.045f), Color = Outline },
                new Layer { Polygon = pole, Color = new Color(0.62f, 0.42f, 0.22f) },
                new Layer { Polygon = cloth, Color = new Color(0.90f, 0.20f, 0.18f) },
                new Layer { Polygon = new[] { V(0.30f, 0.84f), V(0.62f, 0.76f), V(0.30f, 0.70f) }, Color = new Color(1f, 0.45f, 0.38f) },
                new Layer { Polygon = Circle(new Vector2(0.21f, 0.92f), 0.06f), Color = new Color(1f, 0.80f, 0.25f) },
            };
        }

        private static List<Layer> CandleIcon()
        {
            // Свеча: воск, фитиль и пламя
            Vector2[] wax = Box(0.36f, 0.06f, 0.64f, 0.60f);
            return new List<Layer>
            {
                new Layer { Polygon = Offset(wax, 0.035f), Color = Outline },
                new Layer { Polygon = wax, Color = new Color(0.98f, 0.95f, 0.86f) },
                new Layer { Polygon = Box(0.36f, 0.06f, 0.44f, 0.60f), Color = new Color(0.86f, 0.82f, 0.72f) },
                new Layer { Polygon = Box(0.485f, 0.60f, 0.515f, 0.69f), Color = Outline },
                new Layer { Polygon = Ellipse(new Vector2(0.5f, 0.80f), 0.10f, 0.16f, 0f), Color = new Color(0.98f, 0.55f, 0.12f) },
                new Layer { Polygon = Ellipse(new Vector2(0.5f, 0.78f), 0.055f, 0.09f, 0f), Color = new Color(1f, 0.92f, 0.45f) },
            };
        }

        private static List<Layer> FlameIcon()
        {
            // Огонь: капля (круг снизу и острый верх), внутри — светлое ядро
            Vector2[] outerTop = { V(0.22f, 0.40f), V(0.50f, 0.97f), V(0.78f, 0.40f) };
            Vector2[] innerTop = { V(0.36f, 0.32f), V(0.52f, 0.70f), V(0.66f, 0.32f) };
            return new List<Layer>
            {
                new Layer { Polygon = Offset(outerTop, 0.04f), Color = Outline },
                new Layer { Polygon = Circle(new Vector2(0.5f, 0.33f), 0.31f), Color = Outline },
                new Layer { Polygon = outerTop, Color = new Color(0.95f, 0.36f, 0.10f) },
                new Layer { Polygon = Circle(new Vector2(0.5f, 0.33f), 0.27f), Color = new Color(0.95f, 0.36f, 0.10f) },
                new Layer { Polygon = innerTop, Color = new Color(1f, 0.78f, 0.25f) },
                new Layer { Polygon = Circle(new Vector2(0.51f, 0.28f), 0.16f), Color = new Color(1f, 0.78f, 0.25f) },
                new Layer { Polygon = Circle(new Vector2(0.51f, 0.24f), 0.08f), Color = new Color(1f, 0.97f, 0.75f) },
            };
        }

        private static List<Layer> HeartIcon()
        {
            var red = new Color(0.90f, 0.20f, 0.24f);
            Vector2[] tip = { V(0.10f, 0.60f), V(0.90f, 0.60f), V(0.50f, 0.08f) };
            return new List<Layer>
            {
                new Layer { Polygon = Circle(new Vector2(0.31f, 0.64f), 0.24f), Color = Outline },
                new Layer { Polygon = Circle(new Vector2(0.69f, 0.64f), 0.24f), Color = Outline },
                new Layer { Polygon = Offset(tip, 0.045f), Color = Outline },
                new Layer { Polygon = Circle(new Vector2(0.31f, 0.64f), 0.20f), Color = red },
                new Layer { Polygon = Circle(new Vector2(0.69f, 0.64f), 0.20f), Color = red },
                new Layer { Polygon = tip, Color = red },
                new Layer { Polygon = Circle(new Vector2(0.28f, 0.70f), 0.06f), Color = new Color(1f, 0.85f, 0.85f, 0.9f) },
            };
        }

        private static List<Layer> PauseIcon()
        {
            Vector2[] left = Box(0.24f, 0.14f, 0.42f, 0.86f);
            Vector2[] right = Box(0.58f, 0.14f, 0.76f, 0.86f);
            return new List<Layer>
            {
                new Layer { Polygon = Offset(left, 0.045f), Color = Outline },
                new Layer { Polygon = Offset(right, 0.045f), Color = Outline },
                new Layer { Polygon = left, Color = Color.white },
                new Layer { Polygon = right, Color = Color.white },
            };
        }

        private static List<Layer> PlayIcon()
        {
            Vector2[] triangle = { V(0.24f, 0.10f), V(0.86f, 0.50f), V(0.24f, 0.90f) };
            return new List<Layer>
            {
                new Layer { Polygon = Offset(triangle, 0.05f), Color = Outline },
                new Layer { Polygon = triangle, Color = Color.white },
            };
        }

        private static List<Layer> SpeedIcon()
        {
            Vector2[] first = { V(0.06f, 0.16f), V(0.50f, 0.50f), V(0.06f, 0.84f) };
            Vector2[] second = { V(0.48f, 0.16f), V(0.94f, 0.50f), V(0.48f, 0.84f) };
            return new List<Layer>
            {
                new Layer { Polygon = Offset(first, 0.045f), Color = Outline },
                new Layer { Polygon = Offset(second, 0.045f), Color = Outline },
                new Layer { Polygon = first, Color = Color.white },
                new Layer { Polygon = second, Color = Color.white },
            };
        }

        private static List<Layer> HourglassIcon()
        {
            // Песочные часы: рамка сверху и снизу, две колбы, песок внизу
            var wood = new Color(0.62f, 0.42f, 0.22f);
            var glass = new Color(0.82f, 0.90f, 0.98f);
            var sand = new Color(0.98f, 0.78f, 0.30f);
            Vector2[] top = { V(0.24f, 0.86f), V(0.76f, 0.86f), V(0.52f, 0.50f), V(0.48f, 0.50f) };
            Vector2[] bottom = { V(0.48f, 0.50f), V(0.52f, 0.50f), V(0.76f, 0.14f), V(0.24f, 0.14f) };
            return new List<Layer>
            {
                new Layer { Polygon = Offset(top, 0.04f), Color = Outline },
                new Layer { Polygon = Offset(bottom, 0.04f), Color = Outline },
                new Layer { Polygon = top, Color = glass },
                new Layer { Polygon = bottom, Color = glass },
                new Layer { Polygon = new[] { V(0.37f, 0.66f), V(0.63f, 0.66f), V(0.52f, 0.50f), V(0.48f, 0.50f) }, Color = sand },
                new Layer { Polygon = new[] { V(0.33f, 0.28f), V(0.67f, 0.28f), V(0.72f, 0.16f), V(0.28f, 0.16f) }, Color = sand },
                new Layer { Polygon = Offset(Box(0.16f, 0.84f, 0.84f, 0.94f), 0.03f), Color = Outline },
                new Layer { Polygon = Offset(Box(0.16f, 0.06f, 0.84f, 0.16f), 0.03f), Color = Outline },
                new Layer { Polygon = Box(0.16f, 0.84f, 0.84f, 0.94f), Color = wood },
                new Layer { Polygon = Box(0.16f, 0.06f, 0.84f, 0.16f), Color = wood },
            };
        }

        private static List<Layer> SkullIcon()
        {
            var bone = new Color(0.94f, 0.92f, 0.86f);
            return new List<Layer>
            {
                new Layer { Polygon = Circle(new Vector2(0.5f, 0.58f), 0.38f), Color = Outline },
                new Layer { Polygon = Offset(Box(0.31f, 0.10f, 0.69f, 0.36f), 0.04f), Color = Outline },
                new Layer { Polygon = Circle(new Vector2(0.5f, 0.58f), 0.34f), Color = bone },
                new Layer { Polygon = Box(0.31f, 0.10f, 0.69f, 0.36f), Color = bone },
                new Layer { Polygon = Circle(new Vector2(0.36f, 0.56f), 0.10f), Color = Outline },
                new Layer { Polygon = Circle(new Vector2(0.64f, 0.56f), 0.10f), Color = Outline },
                new Layer { Polygon = new[] { V(0.5f, 0.44f), V(0.56f, 0.34f), V(0.44f, 0.34f) }, Color = Outline },
                new Layer { Polygon = Box(0.40f, 0.10f, 0.43f, 0.22f), Color = Outline },
                new Layer { Polygon = Box(0.485f, 0.10f, 0.515f, 0.22f), Color = Outline },
                new Layer { Polygon = Box(0.57f, 0.10f, 0.60f, 0.22f), Color = Outline },
            };
        }

        private static List<Layer> LockIcon()
        {
            var gold = new Color(1f, 0.78f, 0.25f);
            var steel = new Color(0.80f, 0.83f, 0.90f);
            Vector2[] body = Box(0.18f, 0.08f, 0.82f, 0.56f);
            return new List<Layer>
            {
                new Layer { Polygon = Offset(Box(0.26f, 0.50f, 0.38f, 0.78f), 0.04f), Color = Outline },
                new Layer { Polygon = Offset(Box(0.62f, 0.50f, 0.74f, 0.78f), 0.04f), Color = Outline },
                new Layer { Polygon = Offset(Box(0.26f, 0.76f, 0.74f, 0.88f), 0.04f), Color = Outline },
                new Layer { Polygon = Box(0.27f, 0.50f, 0.37f, 0.86f), Color = steel },
                new Layer { Polygon = Box(0.63f, 0.50f, 0.73f, 0.86f), Color = steel },
                new Layer { Polygon = Box(0.27f, 0.77f, 0.73f, 0.87f), Color = steel },
                new Layer { Polygon = Offset(body, 0.045f), Color = Outline },
                new Layer { Polygon = body, Color = gold },
                new Layer { Polygon = Circle(new Vector2(0.5f, 0.36f), 0.07f), Color = Outline },
                new Layer { Polygon = Box(0.475f, 0.18f, 0.525f, 0.34f), Color = Outline },
            };
        }

        private static List<Layer> SpinIcon()
        {
            // Круговая стрелка: дуга из отрезков и наконечник
            var layers = new List<Layer>();
            var fills = new List<Layer>();
            var center = new Vector2(0.5f, 0.5f);
            const int segments = 12;
            for (int i = 0; i < segments; i++)
            {
                float a0 = Mathf.Deg2Rad * (60f + i * 22f), a1 = Mathf.Deg2Rad * (60f + (i + 1) * 22f + 2f);
                Vector2 Point(float angle, float radius) => center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                Vector2[] piece = { Point(a0, 0.40f), Point(a1, 0.40f), Point(a1, 0.26f), Point(a0, 0.26f) };
                layers.Add(new Layer { Polygon = Offset(piece, 0.035f), Color = Outline });
                fills.Add(new Layer { Polygon = piece, Color = Color.white });
            }
            Vector2[] head = { V(0.52f, 0.97f), V(0.80f, 0.79f), V(0.52f, 0.60f) };
            layers.Add(new Layer { Polygon = Offset(head, 0.04f), Color = Outline });
            layers.AddRange(fills);
            layers.Add(new Layer { Polygon = head, Color = Color.white });
            return layers;
        }

        private static List<Layer> TowerIcon()
        {
            var stone = new Color(0.78f, 0.74f, 0.66f);
            var layers = new List<Layer>
            {
                new Layer { Polygon = Offset(Box(0.22f, 0.06f, 0.78f, 0.74f), 0.04f), Color = Outline },
            };
            for (int i = 0; i < 3; i++)
            {
                float x = 0.22f + i * 0.22f;
                layers.Add(new Layer { Polygon = Offset(Box(x, 0.72f, x + 0.12f, 0.90f), 0.035f), Color = Outline });
            }
            layers.Add(new Layer { Polygon = Box(0.22f, 0.06f, 0.78f, 0.74f), Color = stone });
            for (int i = 0; i < 3; i++)
            {
                float x = 0.22f + i * 0.22f;
                layers.Add(new Layer { Polygon = Box(x, 0.72f, x + 0.12f, 0.90f), Color = stone });
            }
            layers.Add(new Layer { Polygon = HalfEllipse(new Vector2(0.5f, 0.06f), 0.12f, 0.28f), Color = Outline });
            layers.Add(new Layer { Polygon = Box(0.44f, 0.46f, 0.56f, 0.60f), Color = Outline });
            return layers;
        }

        private static List<Layer> BookIcon()
        {
            // Раскрытая книга: обложка, две страницы, корешок и строчки
            Vector2[] left = { V(0.08f, 0.27f), V(0.50f, 0.19f), V(0.50f, 0.81f), V(0.08f, 0.87f) };
            Vector2[] right = { V(0.50f, 0.19f), V(0.92f, 0.27f), V(0.92f, 0.87f), V(0.50f, 0.81f) };
            var cover = new Color(0.22f, 0.46f, 0.82f);
            var page = new Color(0.98f, 0.95f, 0.86f);
            var ink = new Color(0.60f, 0.64f, 0.74f);
            var layers = new List<Layer>
            {
                new Layer { Polygon = Offset(left, 0.065f), Color = Outline },
                new Layer { Polygon = Offset(right, 0.065f), Color = Outline },
                new Layer { Polygon = Offset(left, 0.035f), Color = cover },
                new Layer { Polygon = Offset(right, 0.035f), Color = cover },
                new Layer { Polygon = left, Color = page },
                new Layer { Polygon = right, Color = page },
                new Layer { Polygon = Box(0.488f, 0.19f, 0.512f, 0.81f), Color = Outline },
            };
            for (int line = 0; line < 3; line++)
            {
                float y = 0.66f - line * 0.13f;
                layers.Add(new Layer { Polygon = Box(0.15f, y, 0.43f, y + 0.04f), Color = ink });
                layers.Add(new Layer { Polygon = Box(0.57f, y, 0.85f, y + 0.04f), Color = ink });
            }
            return layers;
        }

        private static Vector2[] Ellipse(Vector2 center, float radiusX, float radiusY, float degrees, int segments = 32)
        {
            float rotation = degrees * Mathf.Deg2Rad;
            var points = new Vector2[segments];
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                var p = new Vector2(Mathf.Cos(angle) * radiusX, Mathf.Sin(angle) * radiusY);
                points[i] = center + new Vector2(p.x * Mathf.Cos(rotation) - p.y * Mathf.Sin(rotation), p.x * Mathf.Sin(rotation) + p.y * Mathf.Cos(rotation));
            }
            return points;
        }

        /// <summary>Верхняя половина эллипса (купол) с центром основания в center.</summary>
        private static Vector2[] HalfEllipse(Vector2 center, float radiusX, float radiusY, int segments = 24)
        {
            var points = new Vector2[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * Mathf.PI / segments;
                points[i] = center + new Vector2(Mathf.Cos(angle) * radiusX, Mathf.Sin(angle) * radiusY);
            }
            return points;
        }

        private static Vector2 V(float x, float y) => new Vector2(x, y);

        private static Vector2[] Box(float x0, float y0, float x1, float y1) => new[] { V(x0, y0), V(x1, y0), V(x1, y1), V(x0, y1) };

        private static Vector2[] Circle(Vector2 center, float radius, int segments = 40)
        {
            var points = new Vector2[segments];
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                points[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            }
            return points;
        }

        private static Vector2[] Star(Vector2 center, float outerRadius)
        {
            var points = new Vector2[10];
            for (int i = 0; i < points.Length; i++)
            {
                float angle = Mathf.PI * 0.5f + i * Mathf.PI / 5f; // начинаем с верхнего луча
                float radius = i % 2 == 0 ? outerRadius : outerRadius * 0.45f;
                points[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            }
            return points;
        }

        private static Vector2[] Rotate(Vector2[] polygon, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            var center = new Vector2(0.5f, 0.5f);
            return polygon.Select(p =>
            {
                Vector2 d = p - center;
                return center + new Vector2(d.x * cos - d.y * sin, d.x * sin + d.y * cos);
            }).ToArray();
        }

        /// <summary>Сдвинуть рёбра выпуклого многоугольника наружу на margin (ровная обводка, острые углы).</summary>
        private static Vector2[] Offset(Vector2[] polygon, float margin)
        {
            int count = polygon.Length;
            float area = 0f;
            for (int i = 0; i < count; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % count];
                area += a.x * b.y - b.x * a.y;
            }
            float side = area > 0f ? 1f : -1f; // против часовой стрелки — нормаль наружу (dy, -dx)

            var result = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                Vector2 previous = polygon[(i + count - 1) % count];
                Vector2 current = polygon[i];
                Vector2 next = polygon[(i + 1) % count];
                Vector2 d0 = (current - previous).normalized;
                Vector2 d1 = (next - current).normalized;
                Vector2 n0 = new Vector2(d0.y, -d0.x) * side;
                Vector2 n1 = new Vector2(d1.y, -d1.x) * side;
                float cosine = Mathf.Max(0.2f, 1f + Vector2.Dot(n0, n1)); // ограничение «шипов» на острых углах
                result[i] = current + (n0 + n1) * (margin / cosine);
            }
            return result;
        }

        // =====================================================================
        //  Ассеты и настройки
        // =====================================================================

        private static UnitData CreateOrLoadUnit(string fileName, string displayName, UnitClass unitClass, int hp, int attack,
            int range, int speed, int armor, int heal, bool shieldBearer, int sell)
        {
            string path = $"{UnitsFolder}/{fileName}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<UnitData>(path);
            if (existing != null)
            {
                if (existing.unitName != displayName || existing.unitClass != unitClass)
                {
                    existing.unitName = displayName; // переименования (монах → жрец) доходят и до старых ассетов
                    existing.unitClass = unitClass;
                    EditorUtility.SetDirty(existing);
                }
                return existing;
            }

            var unit = ScriptableObject.CreateInstance<UnitData>();
            unit.unitName = displayName;
            unit.unitClass = unitClass;
            unit.maxHp = hp;
            unit.attack = attack;
            unit.attackRange = range;
            unit.moveSpeed = speed;
            unit.armor = armor;
            unit.heal = heal;
            unit.shieldBearer = shieldBearer;
            unit.backstabMultiplier = 1.25f;
            unit.flanker = false;
            unit.sellPrice = sell;
            unit.spawnWeight = 1f;
            AssetDatabase.CreateAsset(unit, path);
            return unit;
        }

        private static ItemData CreateOrLoadItem(string fileName, string displayName, ItemSlot slot, UnitClass unitClass,
            string label, int armor, int attack)
        {
            string path = $"{ItemsFolder}/{fileName}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<ItemData>(path);
            if (existing != null) return existing;

            var item = ScriptableObject.CreateInstance<ItemData>();
            item.itemName = displayName;
            item.slot = slot;
            item.unitClass = unitClass;
            item.shortLabel = label;
            item.armor = armor;
            item.attack = attack;
            AssetDatabase.CreateAsset(item, path);
            return item;
        }

        private static void AddSceneToBuildSettings(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == path);
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static bool TmpReady() => Resources.Load<TMP_Settings>("TMP Settings") != null;

        // =====================================================================
        //  UI-хелперы
        // =====================================================================

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = LayerMask.NameToLayer("UI") };
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        /// <summary>Колонка фиксированной ширины по центру, растянутая по высоте (контент не «разъезжается» на широких экранах).</summary>
        private static RectTransform Column(string name, Transform parent, float width)
        {
            RectTransform rect = NewRect(name, parent);
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, 0f);
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        private static void Stretch(RectTransform rect, float left, float right, float top, float bottom)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static void TopStretch(RectTransform rect, float top, float height, float side)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -top);
            rect.sizeDelta = new Vector2(-2f * side, height);
        }

        private static void BottomStretch(RectTransform rect, float bottom, float height, float side)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, bottom);
            rect.sizeDelta = new Vector2(-2f * side, height);
        }

        /// <summary>
        /// Записать горизонтальную позу элемента (раскладка для ПК): arrange ставит её обычными хелперами,
        /// после чего элемент возвращается в вертикальную позу. Переключает позы AdaptiveLayout.
        /// </summary>
        private static void Landscape(RectTransform rect, System.Action<RectTransform> arrange, float scale = 1f, float width = -1f)
        {
            AdaptiveLayout.Pose portrait = AdaptiveLayout.Pose.Of(rect);
            AdaptiveLayout.Pose landscape = PoseOf(rect, arrange, scale, width);
            adaptive.entries.Add(new AdaptiveLayout.Entry { rect = rect, portrait = portrait, landscape = landscape });
        }

        /// <summary>Поза, которую arrange придал бы элементу; сам элемент остаётся как был.</summary>
        private static AdaptiveLayout.Pose PoseOf(RectTransform rect, System.Action<RectTransform> arrange, float scale = 1f, float width = -1f)
        {
            AdaptiveLayout.Pose original = AdaptiveLayout.Pose.Of(rect);
            arrange(rect);
            if (width > 0f) rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
            rect.localScale = new Vector3(scale, scale, 1f);
            AdaptiveLayout.Pose pose = AdaptiveLayout.Pose.Of(rect);
            original.ApplyTo(rect);
            return pose;
        }

        private static void TopLeft(RectTransform rect, float left, float top, float width, float height) =>
            Anchored(rect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(left, -top), new Vector2(width, height));

        private static void TopRight(RectTransform rect, float right, float top, float width, float height) =>
            Anchored(rect, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-right, -top), new Vector2(width, height));

        private static void Anchored(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static Image AddImage(RectTransform rect, Color color, bool sliced, bool raycast)
        {
            var image = rect.gameObject.AddComponent<Image>();
            if (sliced)
            {
                image.sprite = UiSprite;
                image.type = Image.Type.Sliced;
            }
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        /// <summary>
        /// Сделать из root прокручиваемый список (колесо мыши, свайп, полоса справа — видна, когда не влезает).
        /// Возвращает Content: VerticalLayoutGroup, высота — по содержимому.
        /// </summary>
        private static RectTransform ScrollList(RectTransform root, float spacing)
        {
            var scroll = root.gameObject.AddComponent<ScrollRect>();
            RectTransform viewport = NewRect("Viewport", root);
            Stretch(viewport, 0f, 18f, 0f, 0f);
            viewport.gameObject.AddComponent<RectMask2D>();
            AddImage(viewport, new Color(0f, 0f, 0f, 0f), sliced: false, raycast: true); // колесо мыши ловится и между строками
            Scrollbar scrollbar = BuildScrollbar(root);
            RectTransform content = NewRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 60f;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return content;
        }

        /// <summary>Вертикальная полоса прокрутки у правого края parent (ручка тянется мышью).</summary>
        private static Scrollbar BuildScrollbar(RectTransform parent)
        {
            RectTransform track = NewRect("Scrollbar", parent);
            track.anchorMin = new Vector2(1f, 0f);
            track.anchorMax = new Vector2(1f, 1f);
            track.pivot = new Vector2(1f, 0.5f);
            track.anchoredPosition = Vector2.zero;
            track.sizeDelta = new Vector2(10f, 0f);
            AddImage(track, new Color(0.12f, 0.13f, 0.17f, 0.9f), sliced: true, raycast: true);
            RectTransform handle = NewRect("Handle", track);
            handle.offsetMin = Vector2.zero;
            handle.offsetMax = Vector2.zero;
            Image handleImage = AddImage(handle, new Color(0.58f, 0.63f, 0.75f), sliced: true, raycast: true);
            var scrollbar = track.gameObject.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handleImage;
            return scrollbar;
        }

        private static void AddRow(RectTransform rect, float spacing, bool controlChildSize = true)
        {
            var row = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = spacing;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = controlChildSize;
            row.childControlHeight = controlChildSize;
            row.childForceExpandWidth = false; // ширину делят LayoutElement.flexibleWidth
            row.childForceExpandHeight = controlChildSize;
        }

        private static void Flex(GameObject go, float flexibleWidth)
        {
            var element = go.GetComponent<LayoutElement>();
            if (element == null) element = go.AddComponent<LayoutElement>();
            element.preferredWidth = 1f;
            element.flexibleWidth = flexibleWidth;
        }

        private static TextMeshProUGUI NewText(string name, Transform parent, string text, float size,
            TextAlignmentOptions alignment, Color color, bool bold = false)
        {
            RectTransform rect = NewRect(name, parent);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            TMP_FontAsset font = uiFont != null ? uiFont : TMP_Settings.defaultFontAsset;
            if (font != null) label.font = font;
            label.text = text;
            label.fontSize = size;
            label.alignment = alignment;
            label.color = color;
            label.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            label.enableWordWrapping = true;
            label.raycastTarget = false;
            return label;
        }

        private static void AutoSize(TMP_Text text, float min, float max)
        {
            text.enableAutoSizing = true;
            text.fontSizeMin = min;
            text.fontSizeMax = max;
        }

        private static TextMeshProUGUI HudText(string name, Transform row, Color color, float flex)
        {
            TextMeshProUGUI text = NewText(name, row, string.Empty, 34f, TextAlignmentOptions.Center, color, bold: true);
            AutoSize(text, 18f, 34f);
            Flex(text.gameObject, flex);
            return text;
        }

        private static Button NewButton(string name, Transform parent, string caption, Color color, float fontSize, out TextMeshProUGUI label)
        {
            RectTransform rect = NewRect(name, parent);
            Image image = AddImage(rect, color, sliced: true, raycast: true);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            label = NewText("Label", rect, caption, fontSize, TextAlignmentOptions.Center, Color.white, bold: true);
            Stretch(label.rectTransform, 10f, 10f, 6f, 6f);
            AutoSize(label, 14f, fontSize);
            return button;
        }
    }
}
