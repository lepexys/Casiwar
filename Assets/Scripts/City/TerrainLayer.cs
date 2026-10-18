using UnityEngine;
using UnityEngine.UI;

namespace Casiwar
{
    /// <summary>
    /// Рисунок местности карты мира из атласа клеток Art/Resources/Terrain/terrain_atlas (Tools/slice_art.py режет его
    /// из TerrainAssets.png): один меш на всю карту.
    /// • Суша (трава, лес, камни, гора, источник) — клетка атласа как нарисована, без поворотов.
    /// • Вода — сплошная бесшовная текстура: озеро синее, река бирюзовая.
    /// • Берег — прямоугольная полоска песка по тем сторонам водной клетки, где суша (и уголок, где суша только по диагонали).
    ///   Песок привязан к клетке, а не повёрнут, поэтому полоски соседних клеток и углы сходятся без швов.
    /// Границы города, сетку и туман рисует поверх CityMapView.
    /// </summary>
    public class TerrainLayer : MaskableGraphic
    {
        /// <summary>Слоты атласа 4×2 (порядок — как в Tools/slice_art.py).</summary>
        public enum Tile
        {
            Grass, Forest, Rocks, Mountain,
            Spring, WaterLake, WaterRiver, Sand,
        }

        private const int AtlasColumns = 4;
        private const float SlotPixels = 256f;
        private const float PadPixels = 16f;
        private const float TilePixels = 224f;
        /// <summary>Толщина полоски берега — доля клетки.</summary>
        private const float ShoreShare = 0.12f;

        private static Texture2D atlas;
        private WorldMap world;
        private readonly Vector2[] uv = new Vector2[4];
        private readonly Vector3[] corners = new Vector3[4];
        private Rect tileUv;

        /// <summary>Атлас клеток (null — арта нет, карта рисуется по-старому).</summary>
        public static Texture2D Atlas => atlas != null ? atlas : (atlas = Resources.Load<Texture2D>("Terrain/terrain_atlas"));

        public override Texture mainTexture => Atlas != null ? Atlas : s_WhiteTexture;

        /// <summary>Перерисовать местность карты.</summary>
        public void Show(WorldMap map)
        {
            world = map;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (world == null || Atlas == null) return;

            Rect rect = rectTransform.rect;
            float cellWidth = rect.width / world.Width;
            float cellHeight = rect.height / world.Height;

            // Сначала клетки, потом полоски берега поверх воды
            for (int y = 0; y < world.Height; y++)
            {
                for (int x = 0; x < world.Width; x++)
                {
                    float left = rect.xMin + x * cellWidth;
                    float top = rect.yMax - y * cellHeight; // строка 0 — верх карты
                    SetTile(TileOf(world.TerrainAt(new Vector2Int(x, y))));
                    AddPart(vh, left, top, cellWidth, cellHeight, 0f, 0f, 1f, 1f);
                }
            }

            // Берег: полоски песка поверх воды со стороны суши; уголки — где суша только по диагонали
            SetTile(Tile.Sand);
            const float a = ShoreShare, b = 1f - ShoreShare;
            for (int y = 0; y < world.Height; y++)
            {
                for (int x = 0; x < world.Width; x++)
                {
                    var cell = new Vector2Int(x, y);
                    if (!world.IsWater(cell)) continue;
                    float left = rect.xMin + x * cellWidth;
                    float top = rect.yMax - y * cellHeight;
                    bool north = IsLand(cell + Vector2Int.down), south = IsLand(cell + Vector2Int.up); // y растёт вниз
                    bool west = IsLand(cell + Vector2Int.left), east = IsLand(cell + Vector2Int.right);
                    if (north) AddPart(vh, left, top, cellWidth, cellHeight, 0f, 0f, 1f, a);
                    if (south) AddPart(vh, left, top, cellWidth, cellHeight, 0f, b, 1f, 1f);
                    if (west) AddPart(vh, left, top, cellWidth, cellHeight, 0f, 0f, a, 1f);
                    if (east) AddPart(vh, left, top, cellWidth, cellHeight, b, 0f, 1f, 1f);
                    if (!north && !west && IsLand(cell + new Vector2Int(-1, -1))) AddPart(vh, left, top, cellWidth, cellHeight, 0f, 0f, a, a);
                    if (!north && !east && IsLand(cell + new Vector2Int(1, -1))) AddPart(vh, left, top, cellWidth, cellHeight, b, 0f, 1f, a);
                    if (!south && !west && IsLand(cell + new Vector2Int(-1, 1))) AddPart(vh, left, top, cellWidth, cellHeight, 0f, b, a, 1f);
                    if (!south && !east && IsLand(cell + new Vector2Int(1, 1))) AddPart(vh, left, top, cellWidth, cellHeight, b, b, 1f, 1f);
                }
            }
        }

        /// <summary>За краем карты — вода (реки уходят за край), поэтому каёмки там нет.</summary>
        private bool IsLand(Vector2Int cell) => world.InBounds(cell) && !world.IsWater(cell);

        private static Tile TileOf(TerrainType type)
        {
            switch (type)
            {
                case TerrainType.Forest: return Tile.Forest;
                case TerrainType.Rocks: return Tile.Rocks;
                case TerrainType.Mountain: return Tile.Mountain;
                case TerrainType.Spring: return Tile.Spring;
                case TerrainType.River: return Tile.WaterRiver;
                case TerrainType.Lake: return Tile.WaterLake;
                default: return Tile.Grass;
            }
        }

        /// <summary>Выбрать клетку атласа (её UV-прямоугольник без полей).</summary>
        private void SetTile(Tile tile)
        {
            int slot = (int)tile;
            float width = SlotPixels * AtlasColumns;
            float height = Atlas.height;
            float x = (slot % AtlasColumns) * SlotPixels + PadPixels;
            float y = (slot / AtlasColumns) * SlotPixels + PadPixels; // от верха картинки
            float u0 = (x + 0.5f) / width, u1 = (x + TilePixels - 0.5f) / width;
            float vTop = 1f - (y + 0.5f) / height, vBottom = 1f - (y + TilePixels - 0.5f) / height;
            tileUv = Rect.MinMaxRect(u0, vBottom, u1, vTop);
        }

        /// <summary>Часть клетки (доли от левого верхнего угла: x0..x1 вправо, y0..y1 вниз) с той же частью рисунка.</summary>
        private void AddPart(VertexHelper vh, float left, float top, float width, float height, float x0, float y0, float x1, float y1)
        {
            for (int i = 0; i < 4; i++)
            {
                float fx = i == 1 || i == 2 ? x1 : x0;
                float fy = i >= 2 ? y1 : y0;
                uv[i] = new Vector2(Mathf.Lerp(tileUv.xMin, tileUv.xMax, fx), Mathf.Lerp(tileUv.yMax, tileUv.yMin, fy));
            }
            AddQuad(vh, left + width * x0, top - height * y0, left + width * x1, top - height * y1);
        }

        private void AddQuad(VertexHelper vh, float left, float top, float right, float bottom)
        {
            corners[0] = new Vector3(left, top);
            corners[1] = new Vector3(right, top);
            corners[2] = new Vector3(right, bottom);
            corners[3] = new Vector3(left, bottom);
            Color32 tint = color;
            int start = vh.currentVertCount;
            for (int i = 0; i < 4; i++) vh.AddVert(corners[i], tint, uv[i]);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
