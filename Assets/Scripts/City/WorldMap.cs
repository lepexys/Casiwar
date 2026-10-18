using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Casiwar
{
    /// <summary>Местность клетки карты мира.</summary>
    public enum TerrainType
    {
        Grass,  // трава — обычная клетка
        Forest, // лес
        Rocks,  // каменистая клетка
        River,  // река
        Lake,   // озеро / пруд
        Mountain, // гора — редкая вершина среди камней: только под монастырь (монахи)
        Spring,   // волшебный источник — редкий, у рек и озёр: только под башню магов (маги)
    }

    /// <summary>Видимость клетки (туман войны).</summary>
    public enum CellVisibility
    {
        Hidden,  // ещё не видели — скрыта полностью
        Fogged,  // были рядом, но ушли — местность видна сквозь туман
        Visible, // видна сейчас
    }

    /// <summary>
    /// Карта мира как в «Цивилизации»: Width×Height клеток с местностью и видимостью.
    /// Генерация простая: пятна леса и камней (шум Перлина), несколько небольших озёр и 1–2 петляющие реки
    /// от края до края карты (клетки реки соединены сторонами, как русло). Город занимает свои клетки
    /// на этой карте (CityManager). Местность решает, что где строить: обычные здания — на траве, в лесу и на камнях,
    /// плавильня — только на камнях, лесопилка — в лесу; редкие горы (вершины каменных пятен) — под монастырь,
    /// редкие волшебные источники (у воды) — под башню магов. На воде не строят ничего.
    /// Видимость (туман войны) — задел на потом: сейчас все клетки открыты.
    /// Координаты: x — вправо, y — вниз (строка 0 — верх карты).
    /// </summary>
    public sealed class WorldMap
    {
        public readonly int Width;
        public readonly int Height;
        public readonly int Seed;

        private readonly TerrainType[,] terrain;
        private readonly CellVisibility[,] visibility;

        /// <summary>Растёт при любом изменении местности или видимости (вид перерисовывает карту).</summary>
        public int Version { get; private set; }

        public WorldMap(int width, int height, int seed)
        {
            Width = Mathf.Max(1, width);
            Height = Mathf.Max(1, height);
            Seed = seed;
            terrain = new TerrainType[Width, Height];
            visibility = new CellVisibility[Width, Height];
            SetAllVisibility(CellVisibility.Visible);
        }

        public bool InBounds(Vector2Int cell) => cell.x >= 0 && cell.y >= 0 && cell.x < Width && cell.y < Height;

        public TerrainType TerrainAt(Vector2Int cell) => InBounds(cell) ? terrain[cell.x, cell.y] : TerrainType.Lake;

        public bool IsWater(Vector2Int cell)
        {
            TerrainType type = TerrainAt(cell);
            return type == TerrainType.River || type == TerrainType.Lake;
        }

        /// <summary>Обычная суша (трава, лес, камни): тут можно ставить дома и обычные здания.</summary>
        public bool IsPlainLand(Vector2Int cell)
        {
            if (!InBounds(cell)) return false;
            TerrainType type = TerrainAt(cell);
            return type == TerrainType.Grass || type == TerrainType.Forest || type == TerrainType.Rocks;
        }

        /// <summary>Рядом (по сторонам) река или озеро.</summary>
        public bool TouchesWater(Vector2Int cell)
        {
            foreach (Vector2Int step in Sides)
            {
                if (InBounds(cell + step) && IsWater(cell + step)) return true;
            }
            return false;
        }

        private static readonly Vector2Int[] Sides = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        public void SetTerrain(Vector2Int cell, TerrainType type)
        {
            if (!InBounds(cell) || terrain[cell.x, cell.y] == type) return;
            terrain[cell.x, cell.y] = type;
            Version++;
        }

        public static string TerrainName(TerrainType type)
        {
            switch (type)
            {
                case TerrainType.Forest: return "Лес";
                case TerrainType.Rocks: return "Камни";
                case TerrainType.River: return "Река";
                case TerrainType.Lake: return "Озеро";
                case TerrainType.Mountain: return "Гора";
                case TerrainType.Spring: return "Волшебный источник";
                default: return "Трава";
            }
        }

        // =====================================================================
        //  Видимость (туман войны) — пока все клетки открыты
        // =====================================================================

        public CellVisibility VisibilityAt(Vector2Int cell) => InBounds(cell) ? visibility[cell.x, cell.y] : CellVisibility.Hidden;

        public void SetVisibility(Vector2Int cell, CellVisibility value)
        {
            if (!InBounds(cell) || visibility[cell.x, cell.y] == value) return;
            visibility[cell.x, cell.y] = value;
            Version++;
        }

        public void SetAllVisibility(CellVisibility value)
        {
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    visibility[x, y] = value;
            Version++;
        }

        /// <summary>Открыть клетки в радиусе (видны сейчас).</summary>
        public void Reveal(Vector2Int center, int radius)
        {
            for (int x = center.x - radius; x <= center.x + radius; x++)
                for (int y = center.y - radius; y <= center.y + radius; y++)
                    SetVisibility(new Vector2Int(x, y), CellVisibility.Visible);
        }

        /// <summary>Всё, что видно сейчас, уходит в туман (видели, но ушли).</summary>
        public void FogVisible()
        {
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    if (visibility[x, y] == CellVisibility.Visible) visibility[x, y] = CellVisibility.Fogged;
            Version++;
        }

        // =====================================================================
        //  Генерация
        // =====================================================================

        /// <summary>Новая карта: трава, пятна леса и камней, небольшие озёра и 1–2 реки от края до края.</summary>
        public static WorldMap Generate(int width, int height, int seed)
        {
            var map = new WorldMap(width, height, seed);
            var random = new System.Random(seed);
            float Offset() => (float)random.NextDouble() * 1000f;

            // 1) Лес и камни — пятнами (шум Перлина), остальное — трава
            float forestX = Offset(), forestY = Offset(), rocksX = Offset(), rocksY = Offset();
            var rockHeight = new float[map.Width, map.Height];
            for (int x = 0; x < map.Width; x++)
            {
                for (int y = 0; y < map.Height; y++)
                {
                    float forest = Mathf.PerlinNoise((x + forestX) * 0.11f, (y + forestY) * 0.11f);
                    float rocks = Mathf.PerlinNoise((x + rocksX) * 0.16f, (y + rocksY) * 0.16f);
                    rockHeight[x, y] = rocks;
                    map.terrain[x, y] = rocks > 0.7f ? TerrainType.Rocks : forest > 0.58f ? TerrainType.Forest : TerrainType.Grass;
                }
            }

            // Горы — редкие одиночные вершины: самые высокие клетки каменных пятен, далеко друг от друга
            var peaks = new List<Vector2Int>();
            int mountains = 3 + random.Next(3);
            foreach (Vector2Int cell in AllCells(map.Width, map.Height)
                         .Where(c => map.terrain[c.x, c.y] == TerrainType.Rocks)
                         .OrderByDescending(c => rockHeight[c.x, c.y]))
            {
                if (peaks.Count >= mountains) break;
                if (peaks.Any(peak => Mathf.Max(Mathf.Abs(peak.x - cell.x), Mathf.Abs(peak.y - cell.y)) < MountainSpacing)) continue;
                peaks.Add(cell);
                map.terrain[cell.x, cell.y] = TerrainType.Mountain;
            }

            // 2) Небольшие озёра — неровные пятна
            int lakes = 3 + random.Next(3);
            for (int i = 0; i < lakes && map.Width > 8 && map.Height > 8; i++)
            {
                var center = new Vector2Int(random.Next(4, map.Width - 4), random.Next(4, map.Height - 4));
                float radius = 1.1f + (float)random.NextDouble() * 1.6f;
                float noiseX = Offset(), noiseY = Offset();
                for (int dx = -3; dx <= 3; dx++)
                {
                    for (int dy = -3; dy <= 3; dy++)
                    {
                        var cell = new Vector2Int(center.x + dx, center.y + dy);
                        if (!map.InBounds(cell)) continue;
                        float wobble = (Mathf.PerlinNoise((cell.x + noiseX) * 0.6f, (cell.y + noiseY) * 0.6f) - 0.5f) * 1.4f;
                        if (Mathf.Sqrt(dx * dx + dy * dy) + wobble <= radius) map.terrain[cell.x, cell.y] = TerrainType.Lake;
                    }
                }
            }

            // 3) Реки: петляют от одного края карты к противоположному
            int rivers = 1 + random.Next(2);
            bool vertical = random.Next(2) == 0;
            for (int i = 0; i < rivers; i++)
            {
                map.CarveRiver(random, vertical);
                vertical = !vertical; // вторая река течёт поперёк первой
            }

            // 4) Волшебные источники: редкие клетки суши у рек и озёр
            int springs = 2 + random.Next(3);
            for (int attempt = 0; springs > 0 && attempt < 4000; attempt++)
            {
                var cell = new Vector2Int(random.Next(map.Width), random.Next(map.Height));
                if (!map.IsPlainLand(cell) || !map.TouchesWater(cell)) continue;
                map.terrain[cell.x, cell.y] = TerrainType.Spring;
                springs--;
            }
            map.Version++;
            return map;
        }

        /// <summary>Горы стоят не ближе этого друг к другу (по Чебышёву).</summary>
        private const int MountainSpacing = 8;

        private static IEnumerable<Vector2Int> AllCells(int width, int height)
        {
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    yield return new Vector2Int(x, y);
        }

        /// <summary>
        /// Гарантия для города: на расстоянии minRadius…maxRadius от центра (за стартовыми границами, но в пределах
        /// расширения) есть хотя бы одна клетка feature (гора или волшебный источник). Нет — ставим сами:
        /// гору — лучше на камни, источник — лучше на берег (если воды рядом нет, рядом появляется пруд).
        /// </summary>
        public void EnsureFeatureNear(TerrainType feature, Vector2Int center, int minRadius, int maxRadius)
        {
            var random = new System.Random(Seed ^ (int)feature * 7919);
            var ring = new List<Vector2Int>();
            for (int r = minRadius; r <= maxRadius; r++)
            {
                foreach (Vector2Int cell in Ring(center, r))
                {
                    if (!InBounds(cell)) continue;
                    if (TerrainAt(cell) == feature) return; // уже есть
                    ring.Add(cell);
                }
            }

            bool Preferred(Vector2Int cell) => feature == TerrainType.Mountain
                ? TerrainAt(cell) == TerrainType.Rocks
                : IsPlainLand(cell) && TouchesWater(cell);
            List<Vector2Int> candidates = ring.Where(Preferred).ToList();
            if (candidates.Count == 0) candidates = ring.Where(IsPlainLand).ToList();
            if (candidates.Count == 0) return;

            Vector2Int site = candidates[random.Next(candidates.Count)];
            SetTerrain(site, feature);
            if (feature == TerrainType.Mountain)
            {
                // Гора не торчит посреди луга: вокруг — камни (там же встанет плавильня)
                foreach (Vector2Int step in Sides)
                {
                    Vector2Int cell = site + step;
                    if (cell != center && IsPlainLand(cell)) SetTerrain(cell, TerrainType.Rocks);
                }
                return;
            }
            if (TouchesWater(site)) return;
            // Источник без воды — рядом пруд (подальше от города)
            Vector2Int pond = Sides.Select(step => site + step)
                .Where(cell => InBounds(cell) && IsPlainLand(cell))
                .OrderByDescending(cell => Mathf.Max(Mathf.Abs(cell.x - center.x), Mathf.Abs(cell.y - center.y)))
                .DefaultIfEmpty(site)
                .First();
            if (pond != site) SetTerrain(pond, TerrainType.Lake);
        }

        /// <summary>
        /// Русло: шаг за шагом от края к противоположному краю — чаще вперёд, иногда вбок (меандры),
        /// никогда назад. Шаги только по сторонам клеток, поэтому река непрерывная.
        /// </summary>
        private void CarveRiver(System.Random random, bool vertical)
        {
            int along = vertical ? Height : Width;
            int across = vertical ? Width : Height;
            if (along < 2 || across < 8) return;

            int start = random.Next(across / 5, across - across / 5);
            bool forwardPositive = random.Next(2) == 0;
            int a = forwardPositive ? 0 : along - 1;
            int b = start;
            int side = random.Next(2) == 0 ? -1 : 1;
            int maxSteps = along * 4;

            for (int step = 0; step < maxSteps; step++)
            {
                var cell = vertical ? new Vector2Int(b, a) : new Vector2Int(a, b);
                if (!InBounds(cell)) break;
                if (terrain[cell.x, cell.y] != TerrainType.Lake) terrain[cell.x, cell.y] = TerrainType.River;
                if (forwardPositive ? a >= along - 1 : a <= 0) break; // дошли до другого края

                double roll = random.NextDouble();
                if (roll < 0.58)
                {
                    a += forwardPositive ? 1 : -1;
                }
                else
                {
                    if (random.NextDouble() < 0.3) side = -side;
                    // Не уходим к боковым краям и слишком далеко от начала
                    if (b + side < 1 || b + side > across - 2 || Mathf.Abs(b + side - start) > across / 4) side = -side;
                    b += side;
                }
            }
        }

        /// <summary>
        /// Место для города: ближайшая к центру карты клетка суши, вокруг которой суша (3×3)
        /// и хватает места под постройки (в квадрате 5×5 почти нет воды).
        /// </summary>
        public Vector2Int FindCitySite()
        {
            var center = new Vector2Int(Width / 2, Height / 2);
            int maxRadius = Mathf.Max(Width, Height);
            for (int r = 0; r <= maxRadius; r++)
            {
                foreach (Vector2Int cell in Ring(center, r))
                {
                    if (!IsPlainLand(cell)) continue;
                    if (CountLand(cell, 1) < 9 || CountLand(cell, 2) < 20) continue;
                    return cell;
                }
            }

            // Подходящего места нет (вся карта в воде?) — осушаем центр
            for (int x = center.x - 1; x <= center.x + 1; x++)
                for (int y = center.y - 1; y <= center.y + 1; y++)
                    SetTerrain(new Vector2Int(x, y), TerrainType.Grass);
            return center;
        }

        /// <summary>Сколько клеток суши в квадрате радиуса radius вокруг cell (за краем карты — не суша).</summary>
        public int CountLand(Vector2Int cell, int radius)
        {
            int land = 0;
            for (int x = cell.x - radius; x <= cell.x + radius; x++)
                for (int y = cell.y - radius; y <= cell.y + radius; y++)
                {
                    var other = new Vector2Int(x, y);
                    if (InBounds(other) && !IsWater(other)) land++;
                }
            return land;
        }

        /// <summary>Клетки «кольца» на расстоянии r (по Чебышёву) от центра.</summary>
        public static IEnumerable<Vector2Int> Ring(Vector2Int center, int r)
        {
            if (r == 0)
            {
                yield return center;
                yield break;
            }
            for (int x = center.x - r; x <= center.x + r; x++)
            {
                yield return new Vector2Int(x, center.y - r);
                yield return new Vector2Int(x, center.y + r);
            }
            for (int y = center.y - r + 1; y <= center.y + r - 1; y++)
            {
                yield return new Vector2Int(center.x - r, y);
                yield return new Vector2Int(center.x + r, y);
            }
        }
    }
}
