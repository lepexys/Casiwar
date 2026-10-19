using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Casiwar
{
    /// <summary>
    /// Вкладка «Город»: большая карта мира (как в «Цивилизации») с нашим городом и панель выбранной клетки.
    /// • Местность — арт клеток из атласа (TerrainLayer: трава, лес, камни, гора, источник, реки и озёра с берегами);
    ///   поверх одной текстурой — сетка, золотая рамка границ города и туман войны (тусклые клетки, скрытые — тёмные;
    ///   пока все клетки открыты). Нет атласа — местность рисуется процедурно в ту же текстуру.
    /// • Карту двигают, зажав среднюю кнопку мыши (а также левую или пальцем); колесо и кнопки «+»/«−» — масштаб
    ///   (до minZoom — видно полкарты); «К городу» возвращает к ратуше, «Отряд» — по очереди к нашим отрядам.
    /// • Клик по клетке: здание → улучшения за золото 🪙 и «Починить» за ⚒; пустая клетка в границах города →
    ///   что построить за производство ⚒. На воде и за границами города строить нельзя.
    /// • Мир вокруг (WorldPowers): лагеря варваров (шатёр), чужие города (ратуша и рамка их земли: красная — враг,
    ///   бирюзовая — торговый), отряды (фигурка с числом бойцов, значок дела: дозор, набег, погоня; пунктир — куда идут),
    ///   наши караваны (повозка с быком) и наши отряды (фигурка вожака с синим числом). Отряды переходят по карте плавно.
    /// • Наши отряды (как герои в «Героях»): левый клик по отряду — выбран (панель отряда), дальше правый клик по клетке —
    ///   идти туда, по врагу или лагерю — идти и напасть (бой — сразу, ход продолжается), по своему отряду — встреча
    ///   (обмен бойцами). Левый клик в другое место (или Esc) — снять выбор. То же — кнопки «Напасть» в панели врага.
    /// • Выбраны ратуша или отряд — снизу видна панель армии (ArmyContext): гарнизон и отряд на ратуше, а у выбранного
    ///   отряда — он сам (с гарнизоном или со встреченным отрядом). В горизонтальной раскладке панель здания тогда
    ///   ужимается, чтобы освободить ей место (SetBenchReserve).
    /// Окно карты (маска, обработка мыши, слои карты) настраивается кодом — подходит и старая сцена.
    /// Дома ставятся сами, когда город растёт. Изучается всё в дереве науки (TechTreeView).
    /// </summary>
    public class CityMapView : MonoBehaviour
    {
        private static readonly Color BorderColor = new Color(1f, 0.84f, 0.32f);
        private static readonly Color HostileBorderColor = new Color(0.92f, 0.26f, 0.22f);
        private static readonly Color TradeBorderColor = new Color(0.36f, 0.86f, 0.9f);
        private static readonly Color FogColor = new Color(0.45f, 0.47f, 0.52f);
        private static readonly Color HiddenColor = new Color(0.05f, 0.06f, 0.08f);
        private static readonly Color ShoreColor = new Color(0.78f, 0.72f, 0.52f);
        private static readonly Color ArmyColor = new Color(0.2f, 0.45f, 0.9f);
        /// <summary>Высота кнопки с пояснением (название + 2 строки описания); длинное пояснение — кнопка выше.</summary>
        private const float DetailedActionHeight = 96f;
        private const float LongDetailedActionHeight = 124f;
        private const int LongDetailLength = 120;
        /// <summary>Цвет пометок о редких местах (гора, волшебный источник).</summary>
        private const string RareHex = "FFD866";
        /// <summary>Ключ метки нашего отряда среди меток отрядов (у чужих — их id, положительные).</summary>
        private static int ArmyMarkerKey(PlayerArmy army) => -1 - army.Id;

        [Header("Ссылки")]
        public CityManager city;
        public GameManager game;
        [Tooltip("Лагеря, города, отряды и караваны (пусто — найдётся сам)")]
        public WorldPowers powers;
        [Tooltip("Окно карты: в нём двигается карта мира (маска и обработка мыши добавляются сами)")]
        public RectTransform mapContainer;
        [Tooltip("Префаб здания на карте")]
        public CityCellView cellPrefab;
        [Tooltip("Отступ здания от краёв клетки")]
        [Min(0f)] public float cellSpacing = 10f;

        [Header("Карта мира")]
        [Tooltip("Размер клетки на экране")]
        [Min(16f)] public float cellSize = 130f;
        [Tooltip("Пикселей текстуры на клетку (детальность рисунка местности)")]
        [Range(4, 32)] public int texelsPerCell = 24;
        [Tooltip("Масштаб карты: от (отдалить) до (приблизить); 1 — обычный")]
        [Range(0.1f, 1f)] public float minZoom = 0.35f;
        [Range(1f, 3f)] public float maxZoom = 1.3f;
        [Tooltip("Шаг масштаба на щелчок колеса или кнопку")]
        [Range(1.02f, 2f)] public float zoomStep = 1.15f;
        [Tooltip("Скорость, с которой отряды переходят по карте, клеток в секунду")]
        [Min(1f)] public float markerSpeed = 7f;

        [Header("Панель выбранной клетки")]
        public TMP_Text summaryText;
        public TMP_Text panelTitle;
        public TMP_Text panelInfo;
        [Tooltip("Контейнер кнопок действий (VerticalLayoutGroup). Заполняется кодом.")]
        public RectTransform actionsContainer;
        [Tooltip("Префаб кнопки действия: Button с TextMeshPro внутри")]
        public Button actionButtonPrefab;

        private RectTransform content;
        private TerrainLayer terrainArt;
        private RawImage terrainImage;
        private RectTransform buildingsRoot;
        private RectTransform powersRoot;
        private RectTransform squadsRoot;
        private readonly List<GameObject> powerMarkers = new List<GameObject>();
        /// <summary>Фигурки отрядов по ключу (id чужого отряда; наш — ArmyMarkerKey): переходят плавно.</summary>
        private readonly Dictionary<int, RectTransform> squadMarkers = new Dictionary<int, RectTransform>();
        private readonly Dictionary<RectTransform, Vector2> markerTargets = new Dictionary<RectTransform, Vector2>();
        /// <summary>Подписи, которые не мельчают при отдалении карты.</summary>
        private readonly List<Transform> readableLabels = new List<Transform>();
        private int markersVersion = -1;
        private int markersArmy = -1;
        private WorldMap markersWorld;
        private Image selectionFrame;
        private Image sightArea;
        private Texture2D terrainTexture;
        private readonly List<CityCellView> buildingViews = new List<CityCellView>();
        private WorldMap bakedWorld;
        private int bakedVersion = -1;
        private int bakedTerritory = -1;
        private Vector2Int selected = new Vector2Int(-1, -1);
        /// <summary>Выбранный наш отряд (правый клик по клетке — приказ ему); null — выбрана клетка.</summary>
        private PlayerArmy selectedArmy;
        /// <summary>Последний выбранный отряд: кнопки «Напасть», «Отряд сюда» отдают приказ ему.</summary>
        private PlayerArmy lastArmy;
        private float zoom = 1f;
        private bool lastArmyContext;
        private PlayerArmy lastContextArmy;
        private float benchReserve;
        private AdaptiveLayout adaptiveLayout;

        /// <summary>Выбраны ратуша или отряд: под панелью карты видна панель армии (гарнизон и отряд).</summary>
        public bool ArmyContext => isActiveAndEnabled && city != null && city.World != null && (selectedArmy != null || selected == city.CityCenter);

        /// <summary>Выбранный наш отряд (он — снизу панели армии); null — не выбран.</summary>
        public PlayerArmy SelectedArmy => selectedArmy;

        /// <summary>ArmyContext или выбранный отряд поменялись — GameManager показывает или прячет панель армии.</summary>
        public event System.Action ArmyContextChanged;

        private RectTransform PanelRect => panelTitle != null ? panelTitle.transform.parent as RectTransform : null;

        /// <summary>Ширина панели здания (под неё встаёт панель армии в горизонтальной раскладке).</summary>
        public float PanelWidth => PanelRect != null ? PanelRect.rect.width : 0f;

        /// <summary>Отступ панели здания от правого края экрана.</summary>
        public float PanelRightMargin
        {
            get
            {
                RectTransform panel = PanelRect;
                if (panel == null || panel.anchorMax.x < 0.99f || panel.pivot.x < 0.99f) return 20f;
                return Mathf.Max(0f, -panel.anchoredPosition.x);
            }
        }

        /// <summary>
        /// Снизу на столько пикселей встала панель армии: панель здания становится ниже, описание короче, а список
        /// действий — под ним. 0 — панель здания как в раскладке.
        /// </summary>
        public void SetBenchReserve(float reserve)
        {
            benchReserve = reserve;
            RectTransform panel = PanelRect;
            if (panel == null) return;
            if (adaptiveLayout == null) adaptiveLayout = FindFirstObjectByType<AdaptiveLayout>();
            RectTransform info = panelInfo != null ? panelInfo.rectTransform : null;
            ScrollRect scroll = actionsContainer != null ? actionsContainer.GetComponentInParent<ScrollRect>(true) : null;
            RectTransform actions = scroll != null ? (RectTransform)scroll.transform : null;
            foreach (RectTransform rect in new[] { panel, info, actions })
            {
                if (rect != null && adaptiveLayout != null && adaptiveLayout.TryGetPose(rect, out AdaptiveLayout.Pose pose)) pose.ApplyTo(rect);
            }
            if (reserve <= 0f) return;

            const float infoTop = 66f, infoHeight = 118f;
            float height = Mathf.Max(260f, panel.sizeDelta.y - reserve);
            panel.sizeDelta = new Vector2(panel.sizeDelta.x, height);
            if (info != null) info.sizeDelta = new Vector2(info.sizeDelta.x, Mathf.Min(info.sizeDelta.y, infoHeight));
            if (actions != null)
            {
                float top = infoTop + infoHeight + 8f;
                actions.anchorMin = actions.anchorMax = actions.pivot = new Vector2(0.5f, 1f);
                actions.anchoredPosition = new Vector2(0f, -top);
                actions.sizeDelta = new Vector2(actions.sizeDelta.x, Mathf.Max(80f, height - top - 12f));
            }
        }

        /// <summary>Слой карты мира внутри окна (двигается при перетаскивании).</summary>
        public RectTransform Content => content;
        public Vector2Int Selected => selected;
        public float Zoom => zoom;

        private void Awake()
        {
            if (city == null) city = FindFirstObjectByType<CityManager>();
            if (game == null) game = FindFirstObjectByType<GameManager>();
            if (city != null) city.Changed += Refresh;
        }

        private void Start()
        {
            // WorldPowers может добавить GameManager в своём Awake (сцена старой версии) — ищем его здесь
            if (powers == null) powers = FindFirstObjectByType<WorldPowers>();
            if (powers != null) powers.Changed += Refresh;
            Refresh();
        }

        private void OnDestroy()
        {
            if (powers != null) powers.Changed -= Refresh;
            if (city != null) city.Changed -= Refresh;
            if (terrainTexture != null) Destroy(terrainTexture);
        }

        private void OnEnable()
        {
            EnsureMap();
            Refresh();
            SnapMarkers(); // пока вкладка была скрыта, отряды уже дошли
        }

        private void Update()
        {
            // Отряды и армия плавно переходят в свои клетки
            float speed = markerSpeed * cellSize * Time.deltaTime;
            foreach (KeyValuePair<RectTransform, Vector2> pair in markerTargets)
            {
                if (pair.Key != null && pair.Key.anchoredPosition != pair.Value)
                    pair.Key.anchoredPosition = Vector2.MoveTowards(pair.Key.anchoredPosition, pair.Value, speed);
            }
            if (Input.GetKeyDown(KeyCode.Escape) && selectedArmy != null)
            {
                selectedArmy = null;
                Refresh();
            }
        }

        /// <summary>Выбрать клетку (здание, пустую клетку, лагерь, отряд) — без приказа отряду.</summary>
        public void Select(Vector2Int cell)
        {
            selected = cell;
            selectedArmy = null;
            Refresh();
        }

        /// <summary>
        /// Левый клик по клетке — выбрать: свой отряд на ней — его (повторный клик — саму клетку: ратушу, лагерь…),
        /// иначе — клетку. Выбранный отряд при этом снимается с выбора — никуда не идёт.
        /// </summary>
        public void ClickCell(Vector2Int cell)
        {
            if (city == null || !city.InMap(cell)) return;
            PlayerArmy here = powers != null ? powers.ArmyAt(cell) : null;
            if (here != null && here != selectedArmy)
            {
                SelectArmy(here, center: false);
                return;
            }
            Select(cell);
        }

        /// <summary>
        /// Правый клик по клетке — приказ выбранному отряду: идти сюда, по врагу или лагерю — напасть, по своему отряду —
        /// встреча (обмен бойцами). Отряд не выбран — правый клик по своему отряду выбирает его.
        /// </summary>
        public void OrderCell(Vector2Int cell)
        {
            if (city == null || !city.InMap(cell) || powers == null) return;
            if (selectedArmy != null)
            {
                OrderArmy(selectedArmy, cell);
                return;
            }
            PlayerArmy here = powers.ArmyAt(cell);
            if (here != null) SelectArmy(here, center: false);
            else Report(false, "Сначала выберите отряд (левый клик по нему), потом правый клик — куда идти");
        }

        /// <summary>Кнопка «Отряд»: по очереди выбрать и показать наши отряды. Отрядов нет — выбрать ратушу (там их собирают).</summary>
        public void SelectArmy()
        {
            if (powers == null) return;
            List<PlayerArmy> list = powers.Armies.OrderBy(a => a.Number).ToList();
            if (list.Count == 0)
            {
                Select(city.CityCenter);
                CenterOn(city.CityCenter);
                Report(false, "Отрядов нет — соберите отряд из гарнизона (панель армии внизу)");
                return;
            }
            int index = selectedArmy != null ? list.IndexOf(selectedArmy) : -1;
            SelectArmy(list[(index + 1) % list.Count], center: true);
        }

        private void SelectArmy(PlayerArmy army, bool center)
        {
            selectedArmy = army;
            lastArmy = army;
            selected = army.Cell;
            if (center) CenterOn(army.Cell);
            Refresh();
        }

        /// <summary>Приказ отряду на клетку: идти, напасть или встретить свой отряд. Дошёл до врага — бой сразу.</summary>
        private void OrderArmy(PlayerArmy army, Vector2Int cell)
        {
            if (army == null || (game != null && game.Phase != GamePhase.Preparation)) return;
            lastArmy = army;
            string message = powers.OrderArmy(army, cell, out FieldBattle fight);
            if (!string.IsNullOrEmpty(message)) Report(false, message);
            if (fight != null && game != null)
            {
                selectedArmy = null;
                game.AttackFromMap(fight);
            }
            if (selectedArmy != null) selected = selectedArmy.Cell;
            Refresh();
        }

        /// <summary>Кому отдавать приказ с кнопок панелей: выбранный отряд, последний выбранный, иначе ближний к клетке.</summary>
        private PlayerArmy ArmyForOrders(Vector2Int near)
        {
            if (powers == null) return null;
            if (selectedArmy != null && !selectedArmy.IsEmpty) return selectedArmy;
            if (lastArmy != null && !lastArmy.IsEmpty && powers.AllArmies.Contains(lastArmy)) return lastArmy;
            return powers.Armies.OrderBy(a => WorldPowers.Distance(a.Cell, near)).ThenBy(a => a.Number).FirstOrDefault();
        }

        // =====================================================================
        //  Перемещение, масштаб и клики
        // =====================================================================

        /// <summary>Показать клетку в центре окна.</summary>
        public void CenterOn(Vector2Int cell)
        {
            if (content == null) return;
            content.anchoredPosition = new Vector2(-(cell.x + 0.5f) * cellSize, (cell.y + 0.5f) * cellSize) * zoom;
            ClampContent();
        }

        /// <summary>Кнопка «К городу».</summary>
        public void CenterOnCity()
        {
            if (city != null && city.World != null) CenterOn(city.CityCenter);
        }

        /// <summary>Сдвинуть карту на delta (в экранных пикселях).</summary>
        public void Pan(Vector2 screenDelta)
        {
            if (content == null) return;
            Canvas canvas = mapContainer.GetComponentInParent<Canvas>();
            float scale = canvas != null ? canvas.rootCanvas.scaleFactor : 1f;
            content.anchoredPosition += screenDelta / Mathf.Max(0.0001f, scale);
            ClampContent();
        }

        public void OnMapDrag(PointerEventData eventData) => Pan(eventData.delta);

        /// <summary>Колесо мыши: масштаб вокруг точки под курсором.</summary>
        public void OnMapScroll(PointerEventData eventData)
        {
            if (Mathf.Abs(eventData.scrollDelta.y) < 0.01f) return;
            ZoomAt(eventData.scrollDelta.y > 0f ? zoomStep : 1f / zoomStep, eventData.position, eventData.enterEventCamera);
        }

        /// <summary>Кнопки «+» и «−»: масштаб вокруг центра окна.</summary>
        public void ZoomIn() => ZoomAtCenter(zoomStep);
        public void ZoomOut() => ZoomAtCenter(1f / zoomStep);

        private void ZoomAtCenter(float factor)
        {
            if (mapContainer == null) return;
            Canvas canvas = mapContainer.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            ZoomAt(factor, RectTransformUtility.WorldToScreenPoint(camera, mapContainer.TransformPoint(mapContainer.rect.center)), camera);
        }

        /// <summary>Масштаб: точка карты под screenPoint остаётся на месте.</summary>
        private void ZoomAt(float factor, Vector2 screenPoint, Camera camera)
        {
            if (content == null) return;
            float next = Mathf.Clamp(zoom * factor, minZoom, maxZoom);
            if (Mathf.Approximately(next, zoom)) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(content, screenPoint, camera, out Vector2 before);
            zoom = next;
            content.localScale = new Vector3(zoom, zoom, 1f);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(content, screenPoint, camera, out Vector2 after);
            content.anchoredPosition += (after - before) * zoom;
            ClampContent();
            ScaleLabels();
        }

        /// <summary>Подписи на карте при отдалении уменьшаются не так сильно — их можно прочесть.</summary>
        private void ScaleLabels()
        {
            float scale = Mathf.Clamp(1f / zoom, 1f, 2f);
            readableLabels.RemoveAll(t => t == null);
            foreach (Transform label in readableLabels) label.localScale = new Vector3(scale, scale, 1f);
        }

        public void OnMapClick(PointerEventData eventData)
        {
            // Отпустили кнопку после перетаскивания — это не клик. Левая — выбрать, правая — приказ выбранному отряду
            bool left = eventData.button == PointerEventData.InputButton.Left;
            bool right = eventData.button == PointerEventData.InputButton.Right;
            if (eventData.dragging || (!left && !right) || content == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(content, eventData.position, eventData.pressEventCamera, out Vector2 local)) return;
            var cell = new Vector2Int(Mathf.FloorToInt(local.x / cellSize), Mathf.FloorToInt(-local.y / cellSize));
            if (left) ClickCell(cell);
            else OrderCell(cell);
        }

        /// <summary>Карта не уезжает за край окна.</summary>
        private void ClampContent()
        {
            if (content == null || mapContainer == null) return;
            Vector2 view = mapContainer.rect.size;
            Vector2 size = content.sizeDelta * zoom;
            Vector2 position = content.anchoredPosition;
            position.x = size.x > view.x ? Mathf.Clamp(position.x, view.x * 0.5f - size.x, -view.x * 0.5f) : -size.x * 0.5f;
            position.y = size.y > view.y ? Mathf.Clamp(position.y, view.y * 0.5f, size.y - view.y * 0.5f) : size.y * 0.5f;
            content.anchoredPosition = position;
        }

        // =====================================================================
        //  Обновление
        // =====================================================================

        public void Refresh()
        {
            if (!isActiveAndEnabled || city == null || city.World == null || content == null) return;
            WorldMap world = city.World;
            if (selectedArmy != null && (powers == null || selectedArmy.IsEmpty || !powers.AllArmies.Contains(selectedArmy))) selectedArmy = null;

            bool newWorld = bakedWorld != world;
            if (newWorld || bakedVersion != world.Version || bakedTerritory != city.TerritoryVersion)
            {
                content.sizeDelta = new Vector2(world.Width * cellSize, world.Height * cellSize);
                BakeTerrain(world);
                bakedWorld = world;
                bakedVersion = world.Version;
                bakedTerritory = city.TerritoryVersion;
                if (newWorld)
                {
                    selected = new Vector2Int(-1, -1);
                    selectedArmy = null;
                    lastArmy = null;
                    CenterOnCity();
                }
            }

            if (summaryText != null)
            {
                string homeless = city.Homeless > 0 ? $" (без крова {city.Homeless})" : string.Empty; // жители без дома
                int border = city.TerritoryRadius * 2 + 1;
                int picks = game != null && game.rain != null ? game.rain.PicksThisTurn : city.Population;
                string bonuses = string.Join(" ", new[] { ResourceType.Gold, ResourceType.Production, ResourceType.Food }
                    .Where(r => city.ClusterBonus(r) > 0).Select(r => $"{GameVisuals.ResourceIcon(r)}+{city.ClusterBonus(r)}"));
                summaryText.text =
                    $"{GameVisuals.IconPopulation}{city.Population}{homeless} · {GameVisuals.IconFood}{city.Food}/{city.GrowthCost} (−{city.FoodUpkeep}/ход) · " +
                    $"{GameVisuals.IconKnowledge}+{city.KnowledgePerDay}/ход · сбор: {picks} кластеров (по жителю)" +
                    (bonuses.Length > 0 ? $", к кластеру {bonuses}" : string.Empty) + "\n" +
                    $"вербовка: {(game != null && game.recruit != null ? game.recruit.SpinsPerTurn : 1)} спинов за ход · границы {border}×{border}" +
                    (city.Boosters > 0 ? $" · усилителей {city.Boosters} из {CityManager.NeutralParts}" : string.Empty) +
                    ThreatLine();
            }

            RefreshBuildings(world);
            RefreshMarkers(world);
            RefreshSelection();
            RefreshPanel();
            NotifyArmyContext();
        }

        private void OnDisable() => NotifyArmyContext();

        private void NotifyArmyContext()
        {
            bool context = ArmyContext;
            PlayerArmy army = context ? selectedArmy : null;
            if (context == lastArmyContext && army == lastContextArmy) return;
            lastArmyContext = context;
            lastContextArmy = army;
            ArmyContextChanged?.Invoke();
        }

        private void RefreshBuildings(WorldMap world)
        {
            int index = 0;
            foreach (Building building in city.Buildings)
            {
                if (world.VisibilityAt(building.Cell) == CellVisibility.Hidden) continue;
                if (index >= buildingViews.Count)
                {
                    if (cellPrefab == null) break;
                    CityCellView created = Instantiate(cellPrefab, buildingsRoot);
                    var createdRect = (RectTransform)created.transform;
                    createdRect.anchorMin = createdRect.anchorMax = new Vector2(0f, 1f);
                    createdRect.pivot = new Vector2(0.5f, 0.5f);
                    buildingViews.Add(created);
                }
                CityCellView view = buildingViews[index++];
                view.gameObject.SetActive(true);
                view.Init(this, building.Cell);
                var rect = (RectTransform)view.transform;
                float size = Mathf.Max(8f, cellSize - cellSpacing);
                rect.sizeDelta = new Vector2(size, size);
                rect.anchoredPosition = CellCenter(building.Cell);
                string note = building.Type == BuildingType.House && !building.IsRuined
                    ? $"{GameVisuals.IconPopulation}{city.ResidentsIn(building)}"
                    : building.Type == BuildingType.TownHall && game != null && game.bench != null
                        ? $"{GameVisuals.IconWalls}{game.bench.GarrisonCount}" // гарнизон города
                        : null;
                view.Bind(building, building.Cell == selected && selectedArmy == null, note, city.CanUpgradeNow(building));
            }
            for (int i = index; i < buildingViews.Count; i++) buildingViews[i].gameObject.SetActive(false);
        }

        private void RefreshSelection()
        {
            if (selectionFrame != null)
            {
                bool show = selectedArmy == null && city.InMap(selected) && city.BuildingAt(selected) == null;
                selectionFrame.enabled = show;
                if (show)
                {
                    selectionFrame.rectTransform.anchoredPosition = CellCenter(selected);
                    selectionFrame.rectTransform.sizeDelta = new Vector2(cellSize - cellSpacing * 0.5f, cellSize - cellSpacing * 0.5f);
                }
            }

            // Выбран вражеский отряд — видно, куда он смотрит (заметит армию или границы — нападёт)
            if (sightArea == null) return;
            Squad watched = selectedArmy == null && powers != null ? powers.SquadsAt(selected).FirstOrDefault(s => s.IsBarbarian || s.IsHostile) : null;
            sightArea.enabled = watched != null;
            if (watched == null) return;
            float side = (powers.sightRadius * 2 + 1) * cellSize;
            sightArea.rectTransform.anchoredPosition = CellCenter(watched.Cell);
            sightArea.rectTransform.sizeDelta = new Vector2(side, side);
        }

        /// <summary>Центр клетки в координатах слоя карты (левый верхний угол — 0,0; y вниз).</summary>
        private Vector2 CellCenter(Vector2Int cell) => new Vector2((cell.x + 0.5f) * cellSize, -(cell.y + 0.5f) * cellSize);

        private void RefreshPanel()
        {
            ClearActions();
            if (selectedArmy != null && powers != null)
            {
                ShowArmyPanel(selectedArmy);
                return;
            }
            if (!city.InMap(selected))
            {
                SetPanel("Карта мира", "Нажмите на здание, чтобы улучшить или починить его, или на пустую клетку в золотой рамке, чтобы построить.\n" +
                                       "Карту двигают средней кнопкой мыши (или тянут), колесо и «+»/«−» — масштаб.\n" +
                                       "Наши отряды — фигурки с синим числом: левый клик — выбрать, правый клик по клетке — идти, по врагу — напасть, " +
                                       "по своему отряду — встреча (обмен бойцами). Город защищает гарнизон (ратуша — его бойцы, " +
                                       "обмен с отрядом и сбор нового, пока прежние в поле).\n" +
                                       "Шатры — лагеря варваров: их отряды ходят дозором и нападают, когда заметят наш отряд или город. " +
                                       "Чужие города: красная рамка — враг, бирюзовая — торгует (караваны с базара)." + WorldLogText());
                return;
            }

            WorldMap world = city.World;
            if (world.VisibilityAt(selected) == CellVisibility.Hidden)
            {
                SetPanel("Неизвестная земля", "Эту клетку ещё никто не видел.");
                return;
            }

            string terrainName = WorldMap.TerrainName(world.TerrainAt(selected));
            Building building = city.BuildingAt(selected);
            if (building == null && ShowPowerPanel(terrainName)) return;
            if (building == null)
            {
                if (!city.InTerritory(selected))
                {
                    SetPanel(terrainName, "Вне границ города: строить можно только внутри золотой рамки.\n" +
                                          $"Клетку у самой границы можно купить за {GameVisuals.IconGold}, а в ратуше — расширить границы во все стороны " +
                                          $"за {GameVisuals.IconProduction} (дома расширяют их сами, когда им тесно)." + SpecialPlaceNote(selected) + MoveLine());
                    AddBuyCellAction();
                    AddBorderAction();
                    AddMoveArmyAction(selected);
                    return;
                }
                if (world.IsWater(selected))
                {
                    SetPanel(terrainName, "Вода — строить здесь нельзя.");
                    return;
                }
                // Только то, что подходит к местности: первыми — постройки именно этой местности (плавильня на камнях…),
                // потом можно построить, не хватает ⚒, в конце — закрыто наукой
                List<BuildingConfig> options = city.buildings
                    .Where(c => c != null && c.buildable && !(c.unique && city.Buildings.Any(b => b.Type == c.type)) && city.FitsTerrain(c, selected))
                    .OrderBy(c => c.placeOn != null && c.placeOn.Length > 0 ? 0 : 1)
                    .ThenBy(c => city.WhyCannotBuild(c, selected) == null ? 0 : city.IsUnlocked(c) ? 1 : 2)
                    .ToList();
                SetPanel($"{terrainName} · пустая клетка", EmptyCellInfo(options) + SquadNote(selected));
                foreach (BuildingConfig config in options)
                {
                    string reason = city.WhyCannotBuild(config, selected);
                    // Не хватает только ⚒ — цена и так на кнопке; иначе пишем, что мешает (наука, символы поля)
                    string label = reason == null || reason.StartsWith("Нужно ")
                        ? $"{config.title} · {config.productionCost} {GameVisuals.IconProduction}"
                        : $"{config.title} — {reason}";
                    BuildingType type = config.type;
                    Vector2Int cell = selected;
                    // Под названием — что здание даёт (особенно важно для чудес света и усилителей)
                    AddAction(label, reason == null, () => Report(city.TryBuild(type, cell, out string message), message), config.description);
                }
                return;
            }

            string garrisonNote = building.Type == BuildingType.TownHall ? GarrisonNote() : string.Empty;
            SetPanel(building.Config.title, city.DescribeBuilding(building) + garrisonNote + $"\nМестность: {terrainName}" + MoveLine() + SquadNote(selected));
            if (building.Config.unitBranch != UnitClass.None && !building.IsRuined)
            {
                // Гильдия: прокачка отряда — на вкладке «Войска»
                AddAction($"Улучшения отряда → «Войска» ({building.Upgrades.Count}/{building.Config.upgrades.Count})", true,
                    () => { if (game != null) game.ShowUnitsTab(); });
            }
            if (building.Type == BuildingType.TownHall) AddBorderAction();
            if (building.Type == BuildingType.Bazaar) AddCaravanActions();
            if (building.IsDamaged)
            {
                AddAction($"Починить · {city.RepairCost(building)} {GameVisuals.IconProduction}",
                    city.Production >= city.RepairCost(building),
                    () => Report(city.TryRepair(building, out string message), message));
            }
            foreach (UpgradeConfig upgrade in building.Config.upgrades)
            {
                if (building.Config.unitBranch != UnitClass.None) break; // улучшения гильдий — во «Войсках»
                if (building.Upgrades.Contains(upgrade.id)) continue;
                string reason = city.WhyCannotUpgrade(building, upgrade);
                // Не хватает только золота — цена и так на кнопке; иначе пишем, что мешает (наука, руины)
                int cost = city.UpgradeCost(building, upgrade);
                bool onlyGold = reason == null || (city.Gold < cost && reason.StartsWith("Нужно "));
                string label = onlyGold
                    ? $"{upgrade.title} · {cost} {GameVisuals.IconGold}"
                    : $"{upgrade.title} — {reason}";
                string id = upgrade.id;
                AddAction(label, reason == null, () => Report(city.TryUpgrade(building, id, out string message), message), upgrade.description);
            }
            if (building.Type == BuildingType.TownHall) AddHomeActions();
        }

        /// <summary>Гарнизон и отряды — под описанием ратуши.</summary>
        private string GarrisonNote()
        {
            if (game == null || game.bench == null || powers == null) return string.Empty;
            BenchManager bench = game.bench;
            ArmyRow garrison = bench.GarrisonRow;
            string leader = garrison.Leader != null ? $", вожак {garrison.Leader.Name}" : string.Empty;
            var lines = new List<string>
            {
                bench.GarrisonCount > 0
                    ? $"<color=#8FC1FF>{GameVisuals.IconWalls} Гарнизон {garrison.Count}/{garrison.Capacity}</color>{leader} — защищает город " +
                      $"(сила {Mathf.RoundToInt(WorldPowers.RowStrength(garrison))})"
                    : $"<color=#FF7A6E>{GameVisuals.IconWalls} Гарнизон пуст — враг у стен разграбит город, если отрядов нет в границах</color>",
            };
            foreach (PlayerArmy army in powers.Armies.OrderBy(a => a.Number))
            {
                string where = powers.AtTownHall(army) ? "на ратуше — меняется бойцами с гарнизоном"
                    : powers.InCity(army) ? "в границах — поможет у стен" : "в поле";
                lines.Add($"{army.Title} ×{army.Row.Count}: {where}");
            }
            PlayerArmy home = powers.HomeArmy;
            if (home == null) lines.Add($"Все отряды ({powers.maxSquads}) в поле — новый соберётся, когда один вернётся");
            else if (home.IsEmpty) lines.Add($"На ратуше можно собрать {home.Title.ToLowerInvariant()}: перетащите в него бойцов из гарнизона (панель внизу)");
            return "\n" + string.Join("\n", lines);
        }

        /// <summary>Ратуша: «Отряд N на ратушу» для отрядов в поле (ратуша занята — встанет рядом, встреча с тем, кто там).</summary>
        private void AddHomeActions()
        {
            if (powers == null) return;
            foreach (PlayerArmy army in powers.Armies.Where(a => !powers.AtTownHall(a)).OrderBy(a => a.Number))
            {
                PlayerArmy target = army;
                AddAction($"{army.Title} на ратушу", army.MovesLeft > 0 || army.Path.Count == 0, () => OrderArmy(target, city.CityCenter),
                    powers.ArmyAt(city.CityCenter) != null
                        ? "На ратуше уже стоит отряд — этот встанет рядом (встреча: обмен бойцами)"
                        : $"Там отряд меняется бойцами с гарнизоном · {GameVisuals.IconMove}{powers.armyMoves} клетки за ход");
            }
        }

        // =====================================================================
        //  Наша армия
        // =====================================================================

        /// <summary>
        /// Панель отряда: вожак, состав, сила, шаги, приказ; кнопки «Напасть» на врагов рядом, «На ратушу», «Встретить»
        /// другой отряд, «Стоять».
        /// </summary>
        private void ShowArmyPanel(PlayerArmy army)
        {
            BenchManager bench = game != null ? game.bench : null;
            BenchUnit leader = army.Leader;
            int garrison = bench != null ? bench.GarrisonCount : 0;
            PlayerArmy friend = powers.MeetingWith(army);
            var lines = new List<string>
            {
                leader != null
                    ? $"Вожак: {leader.Name} {GameVisuals.Stars(leader.stars)} — отряду {leader.LeadText}"
                    : "Вожака нет: богатырь в слоте вожака (золотая рамка) усилит весь отряд",
                $"{GameVisuals.IconAttack}×{army.Row.Count}: {WorldPowers.Composition(army.Row.Units)}",
                $"Сила {Mathf.RoundToInt(WorldPowers.ArmyStrength(army))} · {GameVisuals.IconMove} шагов {army.MovesLeft}/{powers.armyMoves}" +
                (army.AttackedThisTurn ? " · уже бился в этот ход" : string.Empty),
                powers.AtTownHall(army) ? "На ратуше — меняется бойцами с гарнизоном (панель внизу)."
                : friend != null ? $"Рядом {friend.Title.ToLowerInvariant()} — меняйтесь бойцами (панель внизу)."
                : powers.InCity(army) ? $"В границах — защищает город вместе с гарнизоном ({garrison})."
                : garrison > 0 ? $"В поле: город защищает гарнизон ({garrison})."
                : "<color=#FF7A6E>В поле, а гарнизон пуст — город защищать некому!</color>",
            };
            if (army.TargetSquad != null) lines.Add($"Приказ: напасть — {army.TargetSquad.Title}");
            else if (army.TargetCamp != null) lines.Add($"Приказ: штурм лагеря «{army.TargetCamp.Name}»");
            else if (army.TargetArmy != null) lines.Add($"Приказ: встретить {army.TargetArmy.Title.ToLowerInvariant()}");
            else if (army.Destination.HasValue) lines.Add("Приказ: идти (пунктир — путь)");
            lines.Add("<size=85%>Правый клик по клетке — идти туда; по врагу или лагерю — идти и напасть; по своему отряду — встреча. " +
                      "Левый клик в другое место или Esc — снять выбор.</size>");
            SetPanel(army.Title, string.Join("\n", lines));

            if (!army.AttackedThisTurn)
            {
                foreach (Squad enemy in powers.HostileNear(army))
                {
                    Vector2Int cell = enemy.Cell;
                    AddAction($"Напасть: {enemy.Title} ×{enemy.Units.Count}", true, () => OrderArmy(army, cell), StrengthText(enemy.Units, army));
                }
                Power camp = powers.CampNear(army);
                if (camp != null)
                {
                    Vector2Int cell = camp.Cell;
                    AddAction($"Штурмовать лагерь «{camp.Name}»", true, () => OrderArmy(army, cell),
                        $"Гарнизон лагеря + отряды в нём. Победа — лагерь сожжён, +{powers.CampBounty} {GameVisuals.IconGold}");
                }
            }
            if (!powers.AtTownHall(army))
                AddAction("На ратушу", army.MovesLeft > 0, () => OrderArmy(army, city.CityCenter), "Там отряд меняется бойцами с гарнизоном");
            foreach (PlayerArmy other in powers.Armies.Where(a => a != army && WorldPowers.Distance(a.Cell, army.Cell) > 1).OrderBy(a => a.Number))
            {
                PlayerArmy target = other;
                AddAction($"Встретить: {other.Title}", army.MovesLeft > 0, () => OrderArmy(army, target.Cell),
                    "Встанет рядом — отряды меняются бойцами, как герои в «Героях»");
            }
            if (army.HasOrder) AddAction("Стоять (отменить приказ)", true, () =>
            {
                powers.ClearOrder(army);
                powers.NotifyChanged();
            });
            AddAction("Готово", true, () =>
            {
                selectedArmy = null;
                Refresh();
            });
        }

        /// <summary>«Сила 120 (отряд 1: 95)» — сравнение вражеского отряда с нашим (army — кто пойдёт; null — кому приказать).</summary>
        private string StrengthText(IEnumerable<BenchUnit> units, PlayerArmy army = null)
        {
            List<BenchUnit> list = units.ToList();
            float theirs = WorldPowers.Strength(list);
            if (army == null) army = ArmyForOrders(selected);
            if (army == null) return $"Сила {Mathf.RoundToInt(theirs)} (отрядов нет)";
            float ours = WorldPowers.ArmyStrength(army);
            string color = theirs > ours * 1.15f ? "FF7A6E" : theirs < ours * 0.85f ? "8CE99A" : "FFD866";
            return $"Сила <color=#{color}>{Mathf.RoundToInt(theirs)}</color> ({army.Title.ToLowerInvariant()}: {Mathf.RoundToInt(ours)})";
        }

        /// <summary>«Отряд N сюда» — для клетки за границей.</summary>
        private void AddMoveArmyAction(Vector2Int cell)
        {
            PlayerArmy army = ArmyForOrders(cell);
            if (army == null || cell == army.Cell) return;
            AddAction($"{army.Title} сюда", army.MovesLeft > 0 || army.Path.Count == 0, () => OrderArmy(army, cell),
                $"{GameVisuals.IconMove}{powers.armyMoves} клетки за ход (лес — 1, брод — 2 шага, на горы не пройти); остаток пути — в следующие ходы");
        }

        // =====================================================================
        //  Лагеря, города, отряды и караваны
        // =====================================================================

        /// <summary>Строка сводки: армия, ближайшие угрозы и сколько караванов в пути.</summary>
        private string ThreatLine()
        {
            if (powers == null) return string.Empty;
            var parts = new List<string>();
            int armies = powers.ArmyCount, home = powers.Armies.Count(powers.InCity);
            if (armies > 0)
                parts.Add(home == armies ? $"<color=#8FC1FF>отрядов: {armies}, все в городе</color>"
                    : home > 0 ? $"<color=#FFD866>отрядов: {armies}, в поле {armies - home}</color>"
                    : $"<color=#FFD866>отрядов: {armies}, все в поле</color>");
            int garrison = game != null && game.bench != null ? game.bench.GarrisonCount : 0;
            parts.Add(garrison > 0 ? $"<color=#8FC1FF>{GameVisuals.IconWalls} гарнизон {garrison}</color>" : $"<color=#FF7A6E>{GameVisuals.IconWalls} гарнизон пуст</color>");
            List<Squad> threats = powers.Squads.Where(s => s.Attacking || s.RaidsUs || (s.ChasingArmy && s.Mission == SquadMission.Chase))
                .OrderBy(powers.TurnsToArrive).ToList();
            foreach (Squad squad in threats.Take(3))
            {
                int eta = powers.TurnsToArrive(squad);
                string name = squad.Home?.Name ?? "варвары";
                parts.Add(squad.Attacking
                    ? $"<color=#FF7A6E>{GameVisuals.IconWalls} {name} ×{squad.Units.Count} — бой!</color>"
                    : squad.ChasingArmy
                        ? $"<color=#FF7A6E>{GameVisuals.IconAttack} {name} ×{squad.Units.Count} гонится за отрядом</color>"
                        : $"{GameVisuals.IconAttack} {name} ×{squad.Units.Count} {GameVisuals.IconTurns}{eta}");
            }
            if (threats.Count > 3) parts.Add($"+{threats.Count - 3}");
            int caravans = powers.Squads.Count(s => s.IsCaravan);
            if (caravans > 0) parts.Add($"{GameVisuals.IconGold} караванов: {caravans}");
            if (threats.Count == 0) parts.Add($"{GameVisuals.IconAttack} на нас никто не идёт");
            return "\n" + string.Join("   ", parts);
        }

        /// <summary>Последние события мира — под подсказкой карты.</summary>
        private string WorldLogText()
        {
            if (powers == null || powers.Log.Count == 0) return string.Empty;
            return "\n\n<color=#FFD866>События:</color>\n<size=85%>" + string.Join("\n", powers.Log.Reverse().Take(5)) + "</size>";
        }

        /// <summary>Панель для лагеря, чужого города или отряда на клетке без нашего здания. false — тут никого.</summary>
        private bool ShowPowerPanel(string terrainName)
        {
            if (powers == null) return false;
            Power power = powers.PowerAt(selected);
            List<Squad> here = powers.SquadsAt(selected);
            bool armyHere = powers.ArmyAt(selected) != null;
            if (power == null && here.Count == 0) return false;

            if (power == null)
            {
                Squad squad = here.OrderByDescending(s => s.IsHostile).First();
                SetPanel(squad.Title, DescribeSquad(squad) + (here.Count > 1 ? $"\nЗдесь же ещё отрядов: {here.Count - 1}" : string.Empty) +
                                      $"\nМестность: {terrainName}" + MoveLine());
                if (squad.IsHostile && !armyHere) AddAttackAction(squad.Title, squad.Cell, StrengthText(here.Where(s => s.IsHostile).SelectMany(s => s.Units)));
                return true;
            }

            List<Squad> own = powers.Squads.Where(s => s.Home == power && !s.IsCaravan).ToList();
            var lines = new List<string>();
            if (power.IsCamp)
            {
                lines.Add($"Лагерь варваров. Раз в {powers.campSquadEveryTurns} хода выпускает отряд (не больше {powers.maxSquadsPerCamp} сразу): " +
                          $"отряды ходят дозором вокруг и видят на {powers.sightRadius} клетки — заметят армию или наши границы, нападут. " +
                          "Иногда идут в набег сами.");
                lines.Add(own.Count > 0 ? $"Отрядов на карте: {own.Count} · {string.Join(", ", own.Select(s => powers.MissionText(s)))}" : NextRaidText(power));
                lines.Add($"Сжечь лагерь: наш отряд бьётся с гарнизоном лагеря — победа даёт +{powers.CampBounty} {GameVisuals.IconGold}");
            }
            else if (power.Hostile)
            {
                lines.Add($"<color=#FF7A6E>Враждебный город.</color> Раз в {powers.armyEveryTurns} ходов шлёт армию на нас; " +
                          "его стража бьёт варваров и нападает на наш отряд, если тот подойдёт.");
                lines.AddRange(own.Select(s => $"{s.Title} ×{s.Units.Count}: {powers.MissionText(s)}"));
            }
            else
            {
                int gold = powers.CaravanGold(power);
                int turns = powers.CaravanTurns(power);
                lines.Add("<color=#7FE3E8>Торговый город.</color> С нашего базара сюда ходят караваны: " +
                          $"{powers.caravanProduction} {GameVisuals.IconProduction} сейчас → +{gold} {GameVisuals.IconGold} через {turns} {WorldPowers.TurnWord(turns)}. " +
                          "Чем дальше город, тем больше золота. Его стража ходит вокруг и бьёт варваров.");
                int going = powers.Squads.Count(s => s.IsCaravan && s.Home == power);
                if (going > 0) lines.Add($"Наших караванов в пути: {going}");
            }
            if (power.IsCity && power.IsPlundered(powers.Turn))
                lines.Add($"<color=#FFB86C>Разграблен варварами ещё {power.PlunderedUntil - powers.Turn} {WorldPowers.TurnWord(power.PlunderedUntil - powers.Turn)}: " +
                          "караваны везут вдвое меньше, армий он не собирает.</color>");
            List<Squad> visitors = here.Where(s => s.Home != power).ToList();
            if (visitors.Count > 0) lines.Add(SquadNote(selected).Trim());
            SetPanel(power.IsCamp ? $"Лагерь варваров «{power.Name}»" : power.Title, string.Join("\n", lines));
            if (power.CanTrade) AddCaravanAction(power);
            if (power.IsCamp)
                AddAttackAction($"лагерь «{power.Name}»", power.Cell,
                    StrengthText(here.Where(s => s.IsHostile).SelectMany(s => s.Units)) + " + гарнизон лагеря");
            return true;
        }

        /// <summary>«Напасть»: отряд (выбранный, последний выбранный или ближний) идёт к цели и нападает, когда дойдёт.</summary>
        private void AddAttackAction(string target, Vector2Int cell, string detail)
        {
            PlayerArmy army = ArmyForOrders(cell);
            if (army == null) return;
            int turns = powers.ArmyTurnsTo(army, cell);
            if (turns < 0)
            {
                AddAction($"Напасть: {army.Title.ToLowerInvariant()} — не дойти", false, () => { }, "Путь закрыт горами или озёрами");
                return;
            }
            string when = turns <= 0 ? "сейчас" : $"дойдёт через {turns} {WorldPowers.TurnWord(turns)}";
            AddAction($"Напасть: {army.Title.ToLowerInvariant()} ({when})", !army.AttackedThisTurn || turns > 0, () => OrderArmy(army, cell), detail);
        }

        private string NextRaidText(Power power)
        {
            int wait = Mathf.Max(0, power.NextSquadTurn - powers.Turn);
            return wait > 0 ? $"Новый отряд: {GameVisuals.IconTurns}{wait}" : "Собирают новый отряд";
        }

        private string DescribeSquad(Squad squad)
        {
            if (squad.IsCaravan) return $"Наш караван: {powers.MissionText(squad)}.";
            string speed = $"{GameVisuals.IconMove}{squad.Speed} за ход · видит на {powers.sightRadius}";
            string side = squad.IsHostile ? StrengthText(squad.Units) : $"Сила {Mathf.RoundToInt(WorldPowers.Strength(squad.Units))} · нам не враг";
            return $"{powers.MissionText(squad)}\n{speed}\n{GameVisuals.IconAttack}×{squad.Units.Count}: {WorldPowers.Composition(squad)}\n{side}";
        }

        /// <summary>Что местность выбранной клетки делает с отрядами (лес, брод, гора).</summary>
        private string MoveLine()
        {
            string note = WorldPowers.MoveNote(city.World.TerrainAt(selected));
            return note.Length > 0 ? $"\n<color=#9FB4D8>{note}</color>" : string.Empty;
        }

        /// <summary>Пометка «на этой клетке враг» под описанием здания.</summary>
        private string SquadNote(Vector2Int cell)
        {
            if (powers == null) return string.Empty;
            List<Squad> here = powers.SquadsAt(cell).Where(s => !s.IsCaravan).ToList();
            return here.Count == 0
                ? string.Empty
                : "\n" + string.Join("\n", here.Select(s => $"<color=#{(s.IsHostile ? "FF7A6E" : "7FE3E8")}>{GameVisuals.IconAttack} {s.Title} ×{s.Units.Count}</color>"));
        }

        /// <summary>Караваны с базара: по кнопке на каждый торговый город.</summary>
        private void AddCaravanActions()
        {
            if (powers == null) return;
            List<Power> partners = powers.Powers.Where(p => p.CanTrade).ToList();
            if (partners.Count == 0)
            {
                AddAction("Караван — торговых городов пока нет", false, () => { },
                    "Караваны ходят в чужие торговые города. Города появятся на карте через несколько ходов.");
                return;
            }
            foreach (Power partner in partners) AddCaravanAction(partner);
        }

        private void AddCaravanAction(Power target)
        {
            string reason = powers.WhyCannotSendCaravan(target);
            int gold = powers.CaravanGold(target);
            int turns = powers.CaravanTurns(target);
            string label = reason == null || reason.StartsWith("Нужно ")
                ? $"Караван в {target.Name} · {powers.caravanProduction} {GameVisuals.IconProduction} → +{gold} {GameVisuals.IconGold}"
                : $"Караван в {target.Name} — {reason}";
            AddAction(label, reason == null, () => Report(powers.TrySendCaravan(target, out string message), message),
                $"Караван быстрый ({GameVisuals.IconMove}{powers.caravanSpeed} за ход): золото придёт через {turns} {WorldPowers.TurnWord(turns)}. " +
                "Варвары рядом с путём (жёлтый пунктир) ограбят его — смотрите, кто бродит по дороге");
        }

        /// <summary>
        /// Метки на карте: пунктир путей, лагеря и города (пересоздаются при изменениях мира), отряды, караваны и армия
        /// (остаются и плавно переходят в новые клетки).
        /// </summary>
        private void RefreshMarkers(WorldMap world)
        {
            if (powers == null || powersRoot == null) return;
            int armyKey = 17;
            foreach (PlayerArmy army in powers.Armies) armyKey = armyKey * 31 + army.Id * 100003 + army.Cell.x * 1000 + army.Cell.y;
            armyKey = armyKey * 31 + (selectedArmy != null ? selectedArmy.Id : -1);
            if (markersVersion == powers.Version && markersWorld == world && markersArmy == armyKey) return;
            if (markersWorld != world) ClearSquadMarkers();
            markersVersion = powers.Version;
            markersWorld = world;
            markersArmy = armyKey;
            foreach (GameObject marker in powerMarkers)
            {
                if (marker != null) Destroy(marker);
            }
            powerMarkers.Clear();

            foreach (Squad squad in powers.Squads)
                AddPathDots(squad.Path, powers.TurnEnds(squad.Path, squad.Speed, squad.Speed), PathColor(squad), squad.IsHostile && !squad.RaidsUs ? 0.75f : 1f);
            foreach (PlayerArmy army in powers.Armies)
                AddPathDots(army.Path, powers.TurnEnds(army.Path, powers.armyMoves, powers.armyMoves),
                    new Color(0.45f, 0.7f, 1f, army == selectedArmy ? 0.95f : 0.6f), army == selectedArmy ? 1f : 0.8f);
            foreach (Power power in powers.Powers) AddPowerMarker(power);

            var alive = new HashSet<int>();
            foreach (Squad squad in powers.Squads)
            {
                alive.Add(squad.Id);
                UpdateSquadMarker(squad);
            }
            foreach (PlayerArmy army in powers.Armies)
            {
                alive.Add(ArmyMarkerKey(army));
                UpdateArmyMarker(army);
            }
            foreach (int key in squadMarkers.Keys.Where(k => !alive.Contains(k)).ToList())
            {
                RectTransform root = squadMarkers[key];
                markerTargets.Remove(root);
                if (root != null) Destroy(root.gameObject);
                squadMarkers.Remove(key);
            }
            ScaleLabels();
        }

        private void ClearSquadMarkers()
        {
            foreach (RectTransform root in squadMarkers.Values)
            {
                if (root != null) Destroy(root.gameObject);
            }
            squadMarkers.Clear();
            markerTargets.Clear();
        }

        /// <summary>Отряды сразу в своих клетках (вкладку только открыли).</summary>
        private void SnapMarkers()
        {
            foreach (KeyValuePair<RectTransform, Vector2> pair in markerTargets)
            {
                if (pair.Key != null) pair.Key.anchoredPosition = pair.Value;
            }
        }

        private static Color PathColor(Squad squad)
        {
            if (squad.IsCaravan) return new Color(1f, 0.85f, 0.35f, 0.7f);
            if (!squad.IsHostile) return new Color(0.4f, 0.9f, 0.95f, 0.5f);
            if (squad.RaidsUs || squad.ChasingArmy) return new Color(1f, 0.32f, 0.26f, 0.85f);
            return new Color(1f, 0.55f, 0.45f, 0.45f); // дозор — бледный
        }

        /// <summary>Пунктир пути: крупные точки — где отряд встанет в конце хода (лес и брод сокращают ход).</summary>
        private void AddPathDots(IReadOnlyList<Vector2Int> path, IReadOnlyList<bool> turnEnds, Color color, float scale)
        {
            for (int i = 0; i < path.Count; i++)
            {
                bool turnEnd = i < turnEnds.Count && turnEnds[i]; // здесь отряд встанет в конце хода
                float size = cellSize * (turnEnd ? 0.2f : 0.11f) * scale;
                Image dot = MarkerImage("PathDot", powersRoot, GameVisuals.Circle, color, CellCenter(path[i]), new Vector2(size, size));
                powerMarkers.Add(dot.gameObject);
            }
        }

        private void AddPowerMarker(Power power)
        {
            RectTransform root = NewRect($"Power_{power.Id}", powersRoot);
            root.anchorMin = root.anchorMax = new Vector2(0f, 1f);
            root.anchoredPosition = CellCenter(power.Cell);
            root.sizeDelta = new Vector2(cellSize, cellSize);
            powerMarkers.Add(root.gameObject);

            MarkerImage("Shadow", root, GameVisuals.SoftCircle, new Color(0f, 0f, 0f, 0.5f), new Vector2(3f, -cellSize * 0.28f),
                new Vector2(cellSize * 0.9f, cellSize * 0.34f));
            Color nameColor;
            // Рисунок: лагерь варваров, торговый или враждебный город (нет арта — фигуры и наша ратуша)
            Sprite art = UnitArt.MapObject(power.IsCamp ? "barbarian_camp" : power.Hostile ? "hostile_city" : "trade_city");
            if (art == null && !power.IsCamp) art = UnitArt.Building(BuildingType.TownHall);
            if (art != null)
            {
                Image image = MarkerImage("Art", root, art, power.IsPlundered(powers.Turn) ? new Color(0.6f, 0.55f, 0.5f) : Color.white,
                    Vector2.zero, new Vector2(cellSize * 1.05f, cellSize * 1.05f));
                image.preserveAspect = true;
                image.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(4f, -3f);
                nameColor = power.IsCamp ? new Color(1f, 0.55f, 0.48f) : power.Hostile ? new Color(1f, 0.5f, 0.42f) : new Color(0.5f, 0.92f, 0.95f);
            }
            else if (power.IsCamp)
            {
                // Шатёр: полотно, тёмный вход и красный флаг на шесте
                MarkerImage("Tent", root, GameVisuals.Triangle, new Color(0.62f, 0.44f, 0.26f), new Vector2(0f, -cellSize * 0.04f),
                    new Vector2(cellSize * 0.78f, cellSize * 0.62f));
                MarkerImage("Door", root, GameVisuals.Triangle, new Color(0.18f, 0.11f, 0.07f), new Vector2(0f, -cellSize * 0.2f),
                    new Vector2(cellSize * 0.26f, cellSize * 0.3f));
                MarkerImage("Pole", root, GameVisuals.Square, new Color(0.25f, 0.18f, 0.12f), new Vector2(0f, cellSize * 0.3f),
                    new Vector2(cellSize * 0.035f, cellSize * 0.24f));
                MarkerImage("Flag", root, GameVisuals.Square, new Color(0.86f, 0.18f, 0.14f), new Vector2(cellSize * 0.09f, cellSize * 0.37f),
                    new Vector2(cellSize * 0.16f, cellSize * 0.1f));
                nameColor = new Color(1f, 0.55f, 0.48f);
            }
            else
            {
                MarkerImage("Plate", root, GameVisuals.RoundedRect, new Color(0.78f, 0.62f, 0.32f), Vector2.zero,
                    new Vector2(cellSize * 0.8f, cellSize * 0.8f)).type = Image.Type.Sliced;
                nameColor = power.Hostile ? new Color(1f, 0.5f, 0.42f) : new Color(0.5f, 0.92f, 0.95f);
            }
            TMP_Text label = MarkerLabel(root, power.IsCamp ? $"«{power.Name}»" : power.Name, nameColor, new Vector2(0f, cellSize * 0.62f), 26f);
            readableLabels.Add(label.transform);
        }

        /// <summary>Корень метки отряда: тот же объект между обновлениями (переходит плавно), содержимое — заново.</summary>
        private RectTransform SquadRoot(int key, Vector2 target)
        {
            if (!squadMarkers.TryGetValue(key, out RectTransform root) || root == null)
            {
                root = NewRect(key < 0 ? $"Army_{-1 - key}" : $"Squad_{key}", squadsRoot);
                root.anchorMin = root.anchorMax = new Vector2(0f, 1f);
                root.sizeDelta = new Vector2(cellSize, cellSize);
                root.anchoredPosition = target;
                squadMarkers[key] = root;
            }
            for (int i = root.childCount - 1; i >= 0; i--) Destroy(root.GetChild(i).gameObject);
            markerTargets[root] = target;
            return root;
        }

        private void UpdateSquadMarker(Squad squad)
        {
            // Караван только вышел — стоит у ратуши, а не на ней; у своего лагеря/города отряд стоит чуть в стороне
            bool atHome = squad.IsCaravan ? squad.Cell == city.CityCenter : squad.Home != null && squad.Cell == squad.Home.Cell;
            Vector2 target = CellCenter(squad.Cell) + (atHome ? new Vector2(cellSize * 0.42f, -cellSize * 0.12f) : Vector2.zero);
            RectTransform root = SquadRoot(squad.Id, target);
            float scale = atHome ? 0.7f : 1f;
            MarkerImage("Shadow", root, GameVisuals.SoftCircle, new Color(0f, 0f, 0f, 0.5f), new Vector2(2f, -cellSize * 0.3f * scale),
                new Vector2(cellSize * 0.6f * scale, cellSize * 0.22f * scale));

            if (squad.IsCaravan)
            {
                Sprite cart = UnitArt.Caravan();
                Image image = MarkerImage("Caravan", root, cart != null ? cart : GameVisuals.Circle,
                    cart != null ? Color.white : new Color(0.95f, 0.75f, 0.25f), new Vector2(0f, -cellSize * 0.02f),
                    new Vector2(cellSize * 0.95f, cellSize * 0.62f));
                image.preserveAspect = true;
                image.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(3f, -2f);
                TMP_Text gold = MarkerLabel(root, $"+{squad.Gold}{GameVisuals.IconGold}", new Color(1f, 0.85f, 0.35f), new Vector2(0f, cellSize * 0.42f), 24f);
                readableLabels.Add(gold.transform);
                return;
            }

            BenchUnit leader = squad.Units.OrderByDescending(u => u.data.unitClass == UnitClass.Hero ? 100 : u.stars).FirstOrDefault();
            Sprite figure = leader != null ? UnitArt.Figure(leader.data, leader.stars, enemy: true) : null;
            // Варвары и враги — красноватые, стража торгового города — без оттенка
            Color tint = squad.IsHostile ? new Color(1f, 0.8f, 0.76f) : Color.white;
            Image body = MarkerImage("Figure", root, figure != null ? figure : GameVisuals.Circle,
                figure != null ? tint : GameVisuals.EnemyColor, new Vector2(0f, cellSize * 0.02f * scale),
                new Vector2(cellSize * 0.8f * scale, cellSize * 0.8f * scale));
            body.preserveAspect = true;
            body.rectTransform.localScale = new Vector3(-1f, 1f, 1f); // смотрят влево, как враги в бою
            body.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(3f, -2f);

            // Число бойцов в кружке цвета стороны; у стен — пульсирует
            Color badgeColor = !squad.IsHostile ? new Color(0.12f, 0.55f, 0.6f)
                : squad.Kind == SquadKind.Raiders ? new Color(0.78f, 0.14f, 0.12f) : new Color(0.62f, 0.12f, 0.5f);
            Image badge = MarkerImage("Count", root, GameVisuals.Circle, badgeColor, new Vector2(cellSize * 0.3f * scale, cellSize * 0.3f * scale),
                new Vector2(cellSize * 0.34f * scale, cellSize * 0.34f * scale));
            TMP_Text count = MarkerLabel(badge.rectTransform, squad.Units.Count.ToString(), Color.white, Vector2.zero, 24f * scale);
            count.rectTransform.sizeDelta = badge.rectTransform.sizeDelta;
            if (squad.Attacking) AttentionPulse.Set(badge, true);

            // Что делает: дозор, набег, погоня, с добычей домой
            string state = MissionIcon(squad);
            if (state.Length > 0)
            {
                TMP_Text mission = MarkerLabel(root, state, Color.white, new Vector2(-cellSize * 0.3f * scale, cellSize * 0.32f * scale), 26f * scale);
                mission.rectTransform.sizeDelta = new Vector2(cellSize * 0.5f, cellSize * 0.4f);
            }
        }

        /// <summary>Значок дела отряда над фигуркой.</summary>
        private static string MissionIcon(Squad squad)
        {
            if (squad.Attacking) return $"<color=#FF7A6E>{GameVisuals.IconWalls}</color>";
            switch (squad.Mission)
            {
                case SquadMission.Raid: return squad.RaidTarget == null ? $"<color=#FF7A6E>{GameVisuals.IconAttack}</color>" : GameVisuals.IconAttack;
                case SquadMission.Chase: return squad.ChasingArmy ? "<color=#FF7A6E>!</color>" : "<color=#FFD866>!</color>";
                case SquadMission.Return: return GameVisuals.IconGold;
                default: return string.Empty;
            }
        }

        /// <summary>Наш отряд: фигурка вожака или сильнейшего бойца (смотрит вправо), синее число бойцов, номер, рамка — если выбран.</summary>
        private void UpdateArmyMarker(PlayerArmy army)
        {
            bool crowded = city.BuildingAt(army.Cell) != null || powers.PowerAt(army.Cell) != null || powers.SquadsAt(army.Cell).Count > 0;
            // На клетке со зданием или лагерем армия стоит в левом нижнем углу и помельче — здание видно
            Vector2 target = CellCenter(army.Cell) + (crowded ? new Vector2(-cellSize * 0.36f, -cellSize * 0.26f) : Vector2.zero);
            RectTransform root = SquadRoot(ArmyMarkerKey(army), target);
            root.SetAsLastSibling();
            float scale = crowded ? 0.62f : 1f;

            if (army == selectedArmy)
            {
                Image ring = MarkerImage("Selected", root, GameVisuals.Ring, new Color(0.55f, 0.8f, 1f, 0.95f), new Vector2(0f, -cellSize * 0.26f * scale),
                    new Vector2(cellSize * 0.85f * scale, cellSize * 0.4f * scale));
                AttentionPulse.Set(ring, true);
            }
            MarkerImage("Shadow", root, GameVisuals.SoftCircle, new Color(0f, 0f, 0.1f, 0.55f), new Vector2(2f, -cellSize * 0.3f * scale),
                new Vector2(cellSize * 0.62f * scale, cellSize * 0.22f * scale));
            // Фигурка — вожак, а без него — сильнейший боец отряда
            BenchUnit leader = army.Leader ?? army.Row.Units.OrderByDescending(u => u.data.unitClass == UnitClass.Hero ? 100 : u.stars * 10 + u.level).FirstOrDefault();
            Sprite figure = leader != null ? UnitArt.Figure(leader.data, leader.stars, city.HasCrossbows, false, leader.hero) : null;
            Image body = MarkerImage("Figure", root, figure != null ? figure : GameVisuals.Circle, figure != null ? Color.white : ArmyColor,
                new Vector2(0f, cellSize * 0.04f * scale), new Vector2(cellSize * 0.86f * scale, cellSize * 0.86f * scale));
            body.preserveAspect = true;
            body.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(3f, -2f);

            // Синий флажок с числом бойцов
            Image badge = MarkerImage("Count", root, GameVisuals.Circle, ArmyColor, new Vector2(-cellSize * 0.3f * scale, cellSize * 0.3f * scale),
                new Vector2(cellSize * 0.36f * scale, cellSize * 0.36f * scale));
            TMP_Text count = MarkerLabel(badge.rectTransform, army.Row.Count.ToString(), Color.white, Vector2.zero, 24f * scale);
            count.rectTransform.sizeDelta = badge.rectTransform.sizeDelta;
            // Номер отряда под фигуркой — их может быть несколько
            TMP_Text number = MarkerLabel(root, $"<size=75%>отряд</size> {army.Number}", new Color(0.88f, 0.95f, 1f),
                new Vector2(0f, -cellSize * 0.54f * scale), 22f * scale);
            number.rectTransform.sizeDelta = new Vector2(cellSize * 1.2f, cellSize * 0.3f);
            if (army.MovesLeft > 0 && !army.AttackedThisTurn)
            {
                TMP_Text moves = MarkerLabel(root, $"{GameVisuals.IconMove}{army.MovesLeft}", new Color(0.7f, 0.88f, 1f),
                    new Vector2(cellSize * 0.28f * scale, cellSize * 0.34f * scale), 22f * scale);
                moves.rectTransform.sizeDelta = new Vector2(cellSize * 0.5f, cellSize * 0.4f);
            }
        }

        private Image MarkerImage(string name, Transform parent, Sprite sprite, Color color, Vector2 position, Vector2 size)
        {
            RectTransform rect = NewRect(name, parent);
            rect.anchorMin = rect.anchorMax = parent == powersRoot ? new Vector2(0f, 1f) : new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false; // клик идёт в карту и выбирает клетку
            return image;
        }

        private TMP_Text MarkerLabel(RectTransform parent, string text, Color color, Vector2 position, float fontSize)
        {
            RectTransform rect = NewRect("Name", parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(cellSize * 2.4f, cellSize * 0.4f);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            TMP_Text fontSource = panelTitle != null ? panelTitle : summaryText;
            if (fontSource != null) label.font = fontSource.font;
            label.text = text;
            label.fontSize = fontSize;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false;
            label.color = color;
            label.outlineWidth = 0.25f;
            label.outlineColor = Color.black;
            label.raycastTarget = false;
            return label;
        }

        /// <summary>Что где строится — подсказка для карты.</summary>
        private const string TerrainGuide =
            "Местность важна: на камнях — плавильня (больше воинов), в лесу — лесопилка (больше лучников), " +
            "на редкой горе — монастырь (без него нет монахов), на редком волшебном источнике у воды — башня магов (без неё нет магов).";

        /// <summary>Подсказка для пустой клетки в границах: что только здесь строится и сколько символов забрали усилители.</summary>
        private string EmptyCellInfo(List<BuildingConfig> options)
        {
            var lines = new List<string>
            {
                options.Count > 0 ? "Что здесь построить? Новые здания и чудеса света открывает наука." : "Здесь ничего не построить.",
            };
            TerrainType terrain = city.World.TerrainAt(selected);
            List<string> onlyHere = city.buildings
                .Where(c => c != null && c.buildable && c.placeOn != null && c.placeOn.Contains(terrain) &&
                            !(c.unique && city.Buildings.Any(b => b.Type == c.type)))
                .Select(c => c.title)
                .ToList();
            if (onlyHere.Count > 0)
                lines.Add($"<color=#{RareHex}>Только на этой местности: {string.Join(", ", onlyHere)}</color>");
            if (options.Any(c => CityManager.BoostedClass(c) != UnitClass.None))
            {
                lines.Add($"Усилители (плавильня, лесопилка, храм) — каждый ещё спин вербовки, где много бойцов его класса. " +
                          $"Стоит {city.Boosters} из {CityManager.NeutralParts}.");
            }
            if (onlyHere.Count == 0 && options.Count > 0) lines.Add(TerrainGuide);
            return string.Join("\n", lines);
        }

        /// <summary>Пометка для особой клетки за границей: «здесь гора — после покупки можно поставить монастырь».</summary>
        private string SpecialPlaceNote(Vector2Int cell)
        {
            TerrainType terrain = city.World.TerrainAt(cell);
            List<string> onlyHere = city.buildings
                .Where(c => c != null && c.buildable && c.placeOn != null && c.placeOn.Contains(terrain) && c.unique)
                .Select(c => c.title)
                .ToList();
            return onlyHere.Count == 0
                ? string.Empty
                : $"\n<color=#{RareHex}>Редкое место! Когда клетка станет городской, здесь можно поставить: {string.Join(", ", onlyHere)}</color>";
        }

        /// <summary>«Купить клетку» за 🪙 — для клетки за границей.</summary>
        private void AddBuyCellAction()
        {
            string reason = city.WhyCannotBuyCell(selected);
            string label = reason == null || reason.StartsWith("Нужно ")
                ? $"Купить клетку · {city.CellPrice} {GameVisuals.IconGold}"
                : $"Купить клетку — {reason}";
            Vector2Int cell = selected;
            AddAction(label, reason == null, () => Report(city.TryBuyCell(cell, out string message), message),
                $"Клетка станет частью города. Каждая следующая дороже на {city.cellGoldStep} {GameVisuals.IconGold}");
        }

        private void Report(bool success, string message)
        {
            if (game != null && !string.IsNullOrEmpty(message)) game.ShowMessage(message);
        }

        private void SetPanel(string title, string info)
        {
            if (panelTitle != null) panelTitle.text = title;
            if (panelInfo != null) panelInfo.text = info;
        }

        /// <summary>«Расширить границы» за ⚒ (в ратуше и на клетках за рамкой), пока они не самые широкие.</summary>
        private void AddBorderAction()
        {
            if (city.TerritoryRadius >= city.maxCityRadius) return;
            int border = (city.TerritoryRadius + 1) * 2 + 1;
            AddAction($"Расширить границы до {border}×{border} · {city.BorderExpansionCost} {GameVisuals.IconProduction}",
                city.WhyCannotExpandBorders() == null, () => Report(city.TryExpandBorders(out string message), message), city.NextBorderRingText());
        }

        private void ClearActions()
        {
            if (actionsContainer == null) return;
            for (int i = actionsContainer.childCount - 1; i >= 0; i--) Destroy(actionsContainer.GetChild(i).gameObject);
            // Список действий прокручивается — новое здание показываем с начала
            ScrollRect scroll = actionsContainer.GetComponentInParent<ScrollRect>();
            if (scroll != null) scroll.verticalNormalizedPosition = 1f;
        }

        /// <summary>Кнопка действия; detail — пояснение мелким шрифтом под названием (кнопка становится выше).</summary>
        private void AddAction(string label, bool interactable, UnityAction onClick, string detail = null)
        {
            if (actionsContainer == null || actionButtonPrefab == null) return;
            Button button = Instantiate(actionButtonPrefab, actionsContainer);
            button.interactable = interactable;
            button.onClick.AddListener(onClick);
            TMP_Text text = button.GetComponentInChildren<TMP_Text>();
            if (text == null) return;
            if (string.IsNullOrEmpty(detail))
            {
                text.text = label;
                return;
            }
            text.fontStyle = FontStyles.Normal;
            text.text = $"<b>{label}</b>\n<size=75%><color=#D9DEE8>{detail}</color></size>";
            var rect = (RectTransform)button.transform;
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, detail.Length > LongDetailLength ? LongDetailedActionHeight : DetailedActionHeight);
        }

        // =====================================================================
        //  Окно карты: слои создаются кодом
        // =====================================================================

        private void EnsureMap()
        {
            if (content != null || mapContainer == null) return;

            // Маска и приём мыши — у самого окна
            if (mapContainer.GetComponent<RectMask2D>() == null) mapContainer.gameObject.AddComponent<RectMask2D>();
            Image frameImage = mapContainer.GetComponent<Image>();
            if (frameImage != null) frameImage.raycastTarget = true;
            CityMapInput input = mapContainer.GetComponent<CityMapInput>();
            if (input == null) input = mapContainer.gameObject.AddComponent<CityMapInput>();
            input.owner = this;

            // Слой карты: левый верхний угол — клетка (0, 0)
            content = NewRect("WorldMap", mapContainer);
            content.anchorMin = content.anchorMax = new Vector2(0.5f, 0.5f);
            content.pivot = new Vector2(0f, 1f);
            content.SetAsFirstSibling();

            // Арт местности из атласа, поверх — текстура с сеткой, границами и туманом
            if (TerrainLayer.Atlas != null)
            {
                var artRect = NewRect("TerrainArt", content);
                Stretch(artRect);
                terrainArt = artRect.gameObject.AddComponent<TerrainLayer>();
                terrainArt.raycastTarget = false;
            }

            var terrainRect = NewRect("Terrain", content);
            Stretch(terrainRect);
            terrainImage = terrainRect.gameObject.AddComponent<RawImage>();
            terrainImage.color = Color.white;

            buildingsRoot = NewRect("Buildings", content);
            Stretch(buildingsRoot);

            var frameRect = NewRect("Selection", content);
            frameRect.anchorMin = frameRect.anchorMax = new Vector2(0f, 1f);
            frameRect.pivot = new Vector2(0.5f, 0.5f);
            selectionFrame = frameRect.gameObject.AddComponent<Image>();
            selectionFrame.sprite = GameVisuals.RoundedRect;
            selectionFrame.type = Image.Type.Sliced;
            selectionFrame.color = new Color(1f, 0.92f, 0.55f, 0.45f);
            selectionFrame.raycastTarget = false;
            selectionFrame.enabled = false;

            // Обзор выбранного вражеского отряда: где он заметит армию или наши границы
            var sightRect = NewRect("Sight", content);
            sightRect.anchorMin = sightRect.anchorMax = new Vector2(0f, 1f);
            sightRect.pivot = new Vector2(0.5f, 0.5f);
            sightArea = sightRect.gameObject.AddComponent<Image>();
            sightArea.color = new Color(1f, 0.3f, 0.25f, 0.16f);
            sightArea.raycastTarget = false;
            sightArea.enabled = false;

            powersRoot = NewRect("Powers", content); // лагеря, города, пути — поверх наших зданий и рамки выбора
            Stretch(powersRoot);
            squadsRoot = NewRect("Squads", content); // отряды, караваны и армия — поверх всего
            Stretch(squadsRoot);

            // Кнопки в правом верхнем углу окна: «К городу», «Армия», масштаб
            MapButton("Btn_CenterCity", "К городу", new Vector2(-12f, -12f), new Vector2(190f, 64f), CenterOnCity);
            MapButton("Btn_Army", "Отряд", new Vector2(-12f, -84f), new Vector2(190f, 64f), SelectArmy);
            MapButton("Btn_ZoomIn", "+", new Vector2(-109f, -156f), new Vector2(93f, 64f), ZoomIn);
            MapButton("Btn_ZoomOut", "−", new Vector2(-12f, -156f), new Vector2(93f, 64f), ZoomOut);
        }

        /// <summary>Кнопка в правом верхнем углу окна карты.</summary>
        private void MapButton(string name, string caption, Vector2 position, Vector2 size, UnityAction onClick)
        {
            RectTransform rect = NewRect(name, mapContainer);
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = GameVisuals.RoundedRect;
            image.type = Image.Type.Sliced;
            image.color = new Color(0.16f, 0.18f, 0.24f, 0.92f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            RectTransform labelRect = NewRect("Label", rect);
            Stretch(labelRect);
            var label = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
            TMP_Text fontSource = panelTitle != null ? panelTitle : summaryText;
            if (fontSource != null) label.font = fontSource.font;
            label.text = caption;
            label.fontSize = caption.Length <= 1 ? 40f : 28f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.raycastTarget = false;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0f, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // =====================================================================
        //  Рисунок местности
        // =====================================================================

        /// <summary>
        /// Нарисовать карту в текстуру: сетка, границы города, туман войны — поверх арта местности (TerrainLayer);
        /// без атласа — и саму местность процедурно.
        /// </summary>
        private void BakeTerrain(WorldMap world)
        {
            bool art = terrainArt != null;
            if (art) terrainArt.Show(world);
            int tpc = Mathf.Clamp(texelsPerCell, 4, 32);
            int width = world.Width * tpc;
            int height = world.Height * tpc;
            if (terrainTexture == null || terrainTexture.width != width || terrainTexture.height != height)
            {
                if (terrainTexture != null) Destroy(terrainTexture);
                terrainTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
                {
                    name = "WorldMapTerrain",
                    filterMode = art ? FilterMode.Point : FilterMode.Bilinear, // поверх арта — чёткие линии границ
                    wrapMode = TextureWrapMode.Clamp,
                };
            }

            int border = Mathf.Max(1, tpc / 12);
            int shore = Mathf.Max(1, tpc / 8);
            var pixels = new Color32[width * height];
            for (int cy = 0; cy < world.Height; cy++)
            {
                for (int cx = 0; cx < world.Width; cx++)
                {
                    var cell = new Vector2Int(cx, cy);
                    TerrainType type = world.TerrainAt(cell);
                    CellVisibility visibility = world.VisibilityAt(cell);
                    bool water = world.IsWater(cell);
                    bool inside = city.InTerritory(cell);
                    // Стороны: граница города (снаружи соседа) и берег (у воды сосед — суша)
                    bool borderLeft = inside && !city.InTerritory(new Vector2Int(cx - 1, cy));
                    bool borderRight = inside && !city.InTerritory(new Vector2Int(cx + 1, cy));
                    bool borderTop = inside && !city.InTerritory(new Vector2Int(cx, cy - 1));
                    bool borderBottom = inside && !city.InTerritory(new Vector2Int(cx, cy + 1));
                    // Земля чужого города — рамка его цвета (враг — красная, торговый — бирюзовая)
                    Power land = powers != null ? powers.CityLandAt(cell) : null;
                    bool foreignLeft = land != null && powers.CityLandAt(new Vector2Int(cx - 1, cy)) != land;
                    bool foreignRight = land != null && powers.CityLandAt(new Vector2Int(cx + 1, cy)) != land;
                    bool foreignTop = land != null && powers.CityLandAt(new Vector2Int(cx, cy - 1)) != land;
                    bool foreignBottom = land != null && powers.CityLandAt(new Vector2Int(cx, cy + 1)) != land;
                    Color foreignColor = land != null && land.Hostile ? HostileBorderColor : TradeBorderColor;
                    bool shoreLeft = !art && water && IsLand(world, cx - 1, cy); // у арта берега нарисованы
                    bool shoreRight = !art && water && IsLand(world, cx + 1, cy);
                    bool shoreTop = !art && water && IsLand(world, cx, cy - 1);
                    bool shoreBottom = !art && water && IsLand(world, cx, cy + 1);

                    for (int py = 0; py < tpc; py++)
                    {
                        for (int px = 0; px < tpc; px++)
                        {
                            // С артом слой прозрачный, и всё ниже ложится поверх клеток атласа
                            Color color = art ? new Color(1f, 1f, 1f, 0f) : TerrainTexel(type, cx, cy, px, py, tpc);
                            if ((shoreLeft && px < shore) || (shoreRight && px >= tpc - shore) || (shoreTop && py < shore) || (shoreBottom && py >= tpc - shore))
                                color = ShoreColor * (0.95f + 0.08f * Hash(cx * tpc + px, cy * tpc + py));
                            if (px == 0 || py == 0) color = Over(color, Color.black, art ? 0.2f : 0.14f); // сетка клеток
                            if (inside)
                            {
                                if (!art) color = Over(color, Color.white, 0.08f); // поверх арта не осветляем — хватает рамки
                                if ((borderLeft && px < border) || (borderRight && px >= tpc - border) ||
                                    (borderTop && py < border) || (borderBottom && py >= tpc - border))
                                    color = BorderColor;
                            }
                            if (land != null && ((foreignLeft && px < border) || (foreignRight && px >= tpc - border) ||
                                                 (foreignTop && py < border) || (foreignBottom && py >= tpc - border)))
                                color = foreignColor;
                            if (visibility == CellVisibility.Fogged) color = Over(color, FogColor, 0.55f);
                            else if (visibility == CellVisibility.Hidden) color = HiddenColor;
                            if (!art) color.a = 1f;

                            int tx = cx * tpc + px;
                            int ty = (world.Height - 1 - cy) * tpc + (tpc - 1 - py); // текстура снизу вверх, карта — сверху вниз
                            pixels[ty * width + tx] = color;
                        }
                    }
                }
            }
            terrainTexture.SetPixels32(pixels);
            terrainTexture.Apply(false);
            terrainImage.texture = terrainTexture;
        }

        /// <summary>Наложить цвет top с непрозрачностью alpha поверх color (с учётом прозрачности color).</summary>
        private static Color Over(Color color, Color top, float alpha)
        {
            float outAlpha = alpha + color.a * (1f - alpha);
            if (outAlpha <= 0f) return new Color(top.r, top.g, top.b, 0f);
            Color mixed = (top * alpha + color * (color.a * (1f - alpha))) / outAlpha;
            mixed.a = outAlpha;
            return mixed;
        }

        private static bool IsLand(WorldMap world, int x, int y)
        {
            var cell = new Vector2Int(x, y);
            return world.InBounds(cell) && !world.IsWater(cell);
        }

        /// <summary>Цвет точки (px, py) внутри клетки (cx, cy): трава с травинками, кроны леса, камни, гора, источник, вода с бликами.</summary>
        private static Color TerrainTexel(TerrainType type, int cx, int cy, int px, int py, int tpc)
        {
            float u = (px + 0.5f) / tpc;
            float v = (py + 0.5f) / tpc;
            float noise = Hash(cx * tpc + px, cy * tpc + py);
            switch (type)
            {
                case TerrainType.Forest:
                {
                    for (int i = 0; i < 4; i++)
                    {
                        // Кроны: четыре дерева в клетке, слегка сдвинутые случайно
                        float tx = (i % 2 == 0 ? 0.3f : 0.7f) + (Hash(cx * 4 + i, cy * 7) - 0.5f) * 0.16f;
                        float ty = (i < 2 ? 0.32f : 0.7f) + (Hash(cx * 5, cy * 3 + i) - 0.5f) * 0.16f;
                        float radius = 0.19f + Hash(cx + i * 17, cy - i * 11) * 0.05f;
                        float distance = Mathf.Sqrt((u - tx) * (u - tx) + (v - ty) * (v - ty));
                        if (distance < radius)
                        {
                            float light = 1f - distance / radius;
                            Color crown = Color.Lerp(new Color(0.09f, 0.28f, 0.11f), new Color(0.22f, 0.50f, 0.20f), light * 0.75f);
                            return v < ty ? crown * 1.08f : crown;
                        }
                    }
                    return new Color(0.24f, 0.43f, 0.21f) * (0.94f + 0.1f * noise);
                }
                case TerrainType.Rocks:
                {
                    for (int i = 0; i < 3; i++)
                    {
                        float sx = 0.25f + 0.25f * i + (Hash(cx * 3 + i, cy * 9) - 0.5f) * 0.12f;
                        float sy = (i == 1 ? 0.3f : 0.68f) + (Hash(cx * 11, cy * 5 + i) - 0.5f) * 0.14f;
                        float radius = 0.16f + Hash(cx - i * 13, cy + i * 7) * 0.08f;
                        float distance = Mathf.Sqrt((u - sx) * (u - sx) + (v - sy) * (v - sy));
                        if (distance < radius)
                        {
                            if (distance > radius * 0.82f) return new Color(0.33f, 0.31f, 0.28f); // контур камня
                            return v < sy ? new Color(0.78f, 0.76f, 0.70f) : new Color(0.64f, 0.62f, 0.57f);
                        }
                    }
                    return new Color(0.55f, 0.51f, 0.43f) * (0.92f + 0.12f * noise);
                }
                case TerrainType.Mountain:
                {
                    // Одна вершина: левый склон светлее, правый темнее, сверху снег
                    float apexX = 0.5f + (Hash(cx * 7, cy * 13) - 0.5f) * 0.12f;
                    const float apexY = 0.1f, baseY = 0.9f;
                    float half = (v - apexY) / (baseY - apexY) * 0.46f;
                    if (v >= apexY && v <= baseY && Mathf.Abs(u - apexX) < half)
                    {
                        if (Mathf.Abs(u - apexX) > half - 0.035f || v > baseY - 0.03f) return new Color(0.22f, 0.20f, 0.19f); // контур
                        if (v < apexY + (baseY - apexY) * 0.3f) return u < apexX ? new Color(0.97f, 0.97f, 1f) : new Color(0.82f, 0.85f, 0.92f);
                        return (u < apexX ? new Color(0.56f, 0.52f, 0.48f) : new Color(0.40f, 0.37f, 0.35f)) * (0.95f + 0.08f * noise);
                    }
                    return new Color(0.50f, 0.46f, 0.39f) * (0.92f + 0.12f * noise);
                }
                case TerrainType.Spring:
                {
                    // Светящийся родник посреди травы, с искрами
                    float distance = Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f));
                    if (distance < 0.3f)
                    {
                        if (noise > 0.93f) return Color.white;
                        return Color.Lerp(new Color(0.72f, 0.98f, 1f), new Color(0.30f, 0.48f, 0.95f), distance / 0.3f);
                    }
                    if (distance < 0.36f) return new Color(0.66f, 0.42f, 0.92f);
                    Color meadow = new Color(0.43f, 0.66f, 0.36f) * (0.93f + 0.11f * noise);
                    return noise > 0.97f ? new Color(0.85f, 0.75f, 1f) : meadow;
                }
                case TerrainType.River:
                {
                    Color water = new Color(0.25f, 0.52f, 0.86f) * (0.95f + 0.07f * noise);
                    return noise > 0.95f ? new Color(0.62f, 0.80f, 0.97f) : water;
                }
                case TerrainType.Lake:
                {
                    Color water = new Color(0.18f, 0.40f, 0.75f) * (0.95f + 0.06f * noise);
                    return noise > 0.965f ? new Color(0.55f, 0.74f, 0.95f) : water;
                }
                default:
                {
                    Color grass = new Color(0.43f, 0.64f, 0.31f) * (0.93f + 0.11f * noise);
                    return noise > 0.94f ? new Color(0.55f, 0.75f, 0.38f) : grass; // травинки
                }
            }
        }

        private static float Hash(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFu) / 65535f;
            }
        }
    }
}
