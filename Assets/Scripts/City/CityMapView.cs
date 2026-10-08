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
    /// • Местность рисуется одной текстурой: трава, лес, камни, реки и озёра; золотая рамка — границы города;
    ///   клетки в тумане войны тусклые, скрытые — тёмные (пока все клетки открыты).
    /// • Сначала видно только город. Карту двигают, зажав среднюю кнопку мыши (а также левую или пальцем);
    ///   кнопка «К городу» возвращает к ратуше.
    /// • Клик по клетке: здание → улучшения за золото 🪙 и «Починить» за ⚒; пустая клетка в границах города →
    ///   что построить за производство ⚒. На воде и за границами города строить нельзя.
    /// Окно карты (маска, обработка мыши, слои карты) настраивается кодом — подходит и старая сцена.
    /// Дома ставятся сами, когда город растёт. Изучается всё в дереве науки (TechTreeView).
    /// </summary>
    public class CityMapView : MonoBehaviour
    {
        private static readonly Color BorderColor = new Color(1f, 0.84f, 0.32f);
        private static readonly Color FogColor = new Color(0.45f, 0.47f, 0.52f);
        private static readonly Color HiddenColor = new Color(0.05f, 0.06f, 0.08f);
        private static readonly Color ShoreColor = new Color(0.78f, 0.72f, 0.52f);

        [Header("Ссылки")]
        public CityManager city;
        public GameManager game;
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

        [Header("Панель выбранной клетки")]
        public TMP_Text summaryText;
        public TMP_Text panelTitle;
        public TMP_Text panelInfo;
        [Tooltip("Контейнер кнопок действий (VerticalLayoutGroup). Заполняется кодом.")]
        public RectTransform actionsContainer;
        [Tooltip("Префаб кнопки действия: Button с TextMeshPro внутри")]
        public Button actionButtonPrefab;

        private RectTransform content;
        private RawImage terrainImage;
        private RectTransform buildingsRoot;
        private Image selectionFrame;
        private Texture2D terrainTexture;
        private readonly List<CityCellView> buildingViews = new List<CityCellView>();
        private WorldMap bakedWorld;
        private int bakedVersion = -1;
        private int bakedRadius = -1;
        private Vector2Int selected = new Vector2Int(-1, -1);

        /// <summary>Слой карты мира внутри окна (двигается при перетаскивании).</summary>
        public RectTransform Content => content;
        public Vector2Int Selected => selected;

        private void Awake()
        {
            if (city == null) city = FindFirstObjectByType<CityManager>();
            if (game == null) game = FindFirstObjectByType<GameManager>();
            if (city != null) city.Changed += Refresh;
        }

        private void OnDestroy()
        {
            if (city != null) city.Changed -= Refresh;
            if (terrainTexture != null) Destroy(terrainTexture);
        }

        private void OnEnable()
        {
            EnsureMap();
            Refresh();
        }

        public void Select(Vector2Int cell)
        {
            selected = cell;
            Refresh();
        }

        // =====================================================================
        //  Перемещение и клики
        // =====================================================================

        /// <summary>Показать клетку в центре окна.</summary>
        public void CenterOn(Vector2Int cell)
        {
            if (content == null) return;
            content.anchoredPosition = new Vector2(-(cell.x + 0.5f) * cellSize, (cell.y + 0.5f) * cellSize);
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

        public void OnMapClick(PointerEventData eventData)
        {
            // Отпустили кнопку после перетаскивания — это не клик; выбирает клетку только левая кнопка
            if (eventData.dragging || eventData.button != PointerEventData.InputButton.Left || content == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(content, eventData.position, eventData.pressEventCamera, out Vector2 local)) return;
            var cell = new Vector2Int(Mathf.FloorToInt(local.x / cellSize), Mathf.FloorToInt(-local.y / cellSize));
            if (city != null && city.InMap(cell)) Select(cell);
        }

        /// <summary>Карта не уезжает за край окна.</summary>
        private void ClampContent()
        {
            if (content == null || mapContainer == null) return;
            Vector2 view = mapContainer.rect.size;
            Vector2 size = content.sizeDelta;
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

            bool newWorld = bakedWorld != world;
            if (newWorld || bakedVersion != world.Version || bakedRadius != city.TerritoryRadius)
            {
                content.sizeDelta = new Vector2(world.Width * cellSize, world.Height * cellSize);
                BakeTerrain(world);
                bakedWorld = world;
                bakedVersion = world.Version;
                bakedRadius = city.TerritoryRadius;
                if (newWorld)
                {
                    selected = new Vector2Int(-1, -1);
                    CenterOnCity();
                }
            }

            if (summaryText != null)
            {
                string homeless = city.Homeless > 0 ? $" (без крова {city.Homeless})" : string.Empty;
                int border = city.TerritoryRadius * 2 + 1;
                summaryText.text =
                    $"{GameVisuals.IconPopulation} {city.Population} жителей{homeless} · {GameVisuals.IconFood} {city.Food}/{city.GrowthCost} на рост, " +
                    $"едят {city.FoodUpkeep} в день · {GameVisuals.IconKnowledge} +{city.KnowledgePerDay} в день\n" +
                    $"С символа: {GameVisuals.IconGold} {city.GoldPerCoin} · {GameVisuals.IconProduction} {city.ProductionPerSymbol} · " +
                    $"{GameVisuals.IconFood} {city.FoodPerSymbol} · поле {city.BoardSize}×{city.BoardSize}, запас {city.BoardReserve}, " +
                    $"ходов {city.ActionsPerDay}, перемешиваний {city.ReshufflesPerDay} · границы {border}×{border}";
            }

            RefreshBuildings(world);
            RefreshSelection();
            RefreshPanel();
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
                    : null;
                view.Bind(building, building.Cell == selected, note, city.CanUpgradeNow(building));
            }
            for (int i = index; i < buildingViews.Count; i++) buildingViews[i].gameObject.SetActive(false);
        }

        private void RefreshSelection()
        {
            if (selectionFrame == null) return;
            bool show = city.InMap(selected) && city.BuildingAt(selected) == null;
            selectionFrame.enabled = show;
            if (!show) return;
            selectionFrame.rectTransform.anchoredPosition = CellCenter(selected);
            selectionFrame.rectTransform.sizeDelta = new Vector2(cellSize - cellSpacing * 0.5f, cellSize - cellSpacing * 0.5f);
        }

        /// <summary>Центр клетки в координатах слоя карты (левый верхний угол — 0,0; y вниз).</summary>
        private Vector2 CellCenter(Vector2Int cell) => new Vector2((cell.x + 0.5f) * cellSize, -(cell.y + 0.5f) * cellSize);

        private void RefreshPanel()
        {
            ClearActions();
            if (!city.InMap(selected))
            {
                SetPanel("Карта мира", "Нажмите на здание, чтобы улучшить или починить его, или на пустую клетку в золотой рамке, чтобы построить.\n" +
                                       "Карту можно двигать: зажмите среднюю кнопку мыши (или потяните). «К городу» — вернуться к ратуше.\n" +
                                       "Дома ставятся сами, когда город растёт.");
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
            if (building == null)
            {
                if (world.IsWater(selected))
                {
                    SetPanel(terrainName, "Вода — строить здесь нельзя.");
                    return;
                }
                if (!city.InTerritory(selected))
                {
                    SetPanel(terrainName, "Вне границ города: строить можно только внутри золотой рамки.\n" +
                                          $"Границы расширяются сами, когда городу не хватает места для домов, или за {GameVisuals.IconProduction} в ратуше.");
                    AddBorderAction();
                    return;
                }
                SetPanel($"{terrainName} · пустая клетка", "Что здесь построить? Новые здания и чудеса света открывает наука.");
                // Сначала то, что можно построить сейчас, потом — на что не хватает ⚒, в конце — что ещё закрыто наукой
                IEnumerable<BuildingConfig> buildable = city.buildings
                    .Where(c => c != null && c.buildable && !(c.unique && city.Buildings.Any(b => b.Type == c.type)))
                    .OrderBy(c => city.WhyCannotBuild(c, selected) == null ? 0 : city.IsUnlocked(c) ? 1 : 2);
                foreach (BuildingConfig config in buildable.ToList())
                {
                    string reason = city.WhyCannotBuild(config, selected);
                    string label = $"{config.title} · {config.productionCost} {GameVisuals.IconProduction}";
                    if (reason != null && !city.IsUnlocked(config)) label = $"{config.title} — {reason}";
                    BuildingType type = config.type;
                    Vector2Int cell = selected;
                    AddAction(label, reason == null, () => Report(city.TryBuild(type, cell, out string message), message));
                }
                return;
            }

            SetPanel(building.Config.title, city.DescribeBuilding(building) + $"\nМестность: {terrainName}");
            if (building.Config.unitBranch != UnitClass.None && !building.IsRuined)
            {
                // Гильдия: прокачка отряда — на вкладке «Войска»
                AddAction($"Улучшения отряда → «Войска» ({building.Upgrades.Count}/{building.Config.upgrades.Count})", true,
                    () => { if (game != null) game.ShowUnitsTab(); });
            }
            if (building.Type == BuildingType.TownHall) AddBorderAction();
            if (building.Type == BuildingType.Bazaar)
            {
                AddAction($"Караван: {city.caravanProduction} {GameVisuals.IconProduction} → {city.caravanGold} {GameVisuals.IconGold}",
                    city.WhyCannotSendCaravan() == null, () => Report(city.TrySendCaravan(out string message), message));
            }
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
                    ? $"{upgrade.title}: {upgrade.description} · {cost} {GameVisuals.IconGold}"
                    : $"{upgrade.title} — {reason}";
                string id = upgrade.id;
                AddAction(label, reason == null, () => Report(city.TryUpgrade(building, id, out string message), message));
            }
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
                city.WhyCannotExpandBorders() == null, () => Report(city.TryExpandBorders(out string message), message));
        }

        private void ClearActions()
        {
            if (actionsContainer == null) return;
            for (int i = actionsContainer.childCount - 1; i >= 0; i--) Destroy(actionsContainer.GetChild(i).gameObject);
            // Список действий прокручивается — новое здание показываем с начала
            ScrollRect scroll = actionsContainer.GetComponentInParent<ScrollRect>();
            if (scroll != null) scroll.verticalNormalizedPosition = 1f;
        }

        private void AddAction(string label, bool interactable, UnityAction onClick)
        {
            if (actionsContainer == null || actionButtonPrefab == null) return;
            Button button = Instantiate(actionButtonPrefab, actionsContainer);
            button.interactable = interactable;
            button.onClick.AddListener(onClick);
            TMP_Text text = button.GetComponentInChildren<TMP_Text>();
            if (text != null) text.text = label;
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

            CreateCenterButton();
        }

        /// <summary>Кнопка «К городу» в правом верхнем углу окна карты.</summary>
        private void CreateCenterButton()
        {
            RectTransform rect = NewRect("Btn_CenterCity", mapContainer);
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-12f, -12f);
            rect.sizeDelta = new Vector2(190f, 64f);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = GameVisuals.RoundedRect;
            image.type = Image.Type.Sliced;
            image.color = new Color(0.16f, 0.18f, 0.24f, 0.92f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(CenterOnCity);

            RectTransform labelRect = NewRect("Label", rect);
            Stretch(labelRect);
            var label = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
            TMP_Text fontSource = panelTitle != null ? panelTitle : summaryText;
            if (fontSource != null) label.font = fontSource.font;
            label.text = "К городу";
            label.fontSize = 28f;
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

        /// <summary>Нарисовать карту в текстуру: местность, сетка, границы города, туман войны.</summary>
        private void BakeTerrain(WorldMap world)
        {
            int tpc = Mathf.Clamp(texelsPerCell, 4, 32);
            int width = world.Width * tpc;
            int height = world.Height * tpc;
            if (terrainTexture == null || terrainTexture.width != width || terrainTexture.height != height)
            {
                if (terrainTexture != null) Destroy(terrainTexture);
                terrainTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
                {
                    name = "WorldMapTerrain",
                    filterMode = FilterMode.Bilinear,
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
                    bool shoreLeft = water && IsLand(world, cx - 1, cy);
                    bool shoreRight = water && IsLand(world, cx + 1, cy);
                    bool shoreTop = water && IsLand(world, cx, cy - 1);
                    bool shoreBottom = water && IsLand(world, cx, cy + 1);

                    for (int py = 0; py < tpc; py++)
                    {
                        for (int px = 0; px < tpc; px++)
                        {
                            Color color = TerrainTexel(type, cx, cy, px, py, tpc);
                            if ((shoreLeft && px < shore) || (shoreRight && px >= tpc - shore) || (shoreTop && py < shore) || (shoreBottom && py >= tpc - shore))
                                color = ShoreColor * (0.95f + 0.08f * Hash(cx * tpc + px, cy * tpc + py));
                            if (px == 0 || py == 0) color *= 0.86f; // сетка клеток
                            if (inside)
                            {
                                color = Color.Lerp(color, Color.white, 0.08f);
                                if ((borderLeft && px < border) || (borderRight && px >= tpc - border) ||
                                    (borderTop && py < border) || (borderBottom && py >= tpc - border))
                                    color = BorderColor;
                            }
                            if (visibility == CellVisibility.Fogged) color = Color.Lerp(color, FogColor, 0.55f);
                            else if (visibility == CellVisibility.Hidden) color = HiddenColor;
                            color.a = 1f;

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

        private static bool IsLand(WorldMap world, int x, int y)
        {
            var cell = new Vector2Int(x, y);
            return world.InBounds(cell) && !world.IsWater(cell);
        }

        /// <summary>Цвет точки (px, py) внутри клетки (cx, cy): трава с травинками, кроны леса, камни, вода с бликами.</summary>
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
