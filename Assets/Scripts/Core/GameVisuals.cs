using System;
using UnityEngine;

namespace Casiwar
{
    /// <summary>Как выглядит символ в клетке поля: фон-форма, цвет, иконка и подпись-заглушка.</summary>
    public struct SymbolLook
    {
        public Color color;
        public Sprite shape;
        public bool sliced;
        public Sprite icon;
        public string label;
        /// <summary>Отступ подписи от краёв клетки (доля размера): узким фигурам — побольше.</summary>
        public float labelInset;
    }

    /// <summary>
    /// Общие визуальные помощники: цвета, подписи, иконки и процедурные спрайты-заглушки.
    /// Благодаря заглушкам прототип работает без единого арт-ассета — когда появится графика,
    /// просто назначьте спрайты в UnitData / ItemData.
    /// </summary>
    public static class GameVisuals
    {
        // Иконки в тексте TextMeshPro. Этих символов нет в шрифте — TMP берёт их
        // из спрайт-ассета «Casiwar Icons» (его создаёт меню Casiwar → Собрать демо-сцену).
        public const string IconStar = "★";            // ★
        public const string IconProduction = "⚒";      // ⚒ очки производства
        public const string IconGold = "\U0001FA99";        // 🪙 золото
        public const string IconAttack = "⚔";          // ⚔ атака
        public const string IconMove = "➜";            // ➜ перемещение
        public const string IconFood = "🍎";        // 🍎 еда
        public const string IconPopulation = "👤";  // 👤 жители
        public const string IconKnowledge = "📖";   // 📖 очки знаний
        public const string IconBanner = "\U0001F6A9";   // 🚩 Знамя — джокер боевого слота
        public const string IconCandle = "\U0001F56F";   // 🕯 Свеча — 3+ в слоте: «Чудо»

        public static readonly Color BannerSymbolColor = new Color(0.86f, 0.62f, 0.22f);
        public static readonly Color MultiplierSymbolColor = new Color(0.95f, 0.36f, 0.62f);
        public static readonly Color CandleSymbolColor = new Color(0.98f, 0.90f, 0.62f);

        public static readonly Color PlayerColor = new Color(0.25f, 0.6f, 1f);
        public static readonly Color EnemyColor = new Color(1f, 0.32f, 0.3f);
        public static readonly Color GoldColor = new Color(1f, 0.80f, 0.22f);
        public static readonly Color AttackSymbolColor = new Color(0.66f, 0.70f, 0.80f);
        public static readonly Color MoveSymbolColor = new Color(0.22f, 0.62f, 0.78f);
        public static readonly Color BlankSymbolColor = new Color(0.19f, 0.20f, 0.25f);

        // ---------- Классы юнитов ----------

        public static Color ClassColor(UnitClass unitClass)
        {
            switch (unitClass)
            {
                case UnitClass.Warrior: return new Color(0.86f, 0.34f, 0.28f);
                case UnitClass.Archer: return new Color(0.42f, 0.70f, 0.30f);
                case UnitClass.Priest: return new Color(0.63f, 0.42f, 0.88f);
                case UnitClass.Bomber: return new Color(0.92f, 0.56f, 0.18f);
                case UnitClass.Monk: return new Color(0.84f, 0.30f, 0.62f);
                case UnitClass.Mage: return new Color(0.26f, 0.58f, 0.92f);
                case UnitClass.Hero: return new Color(0.96f, 0.78f, 0.26f);
                default: return new Color(0.72f, 0.70f, 0.62f);
            }
        }

        public static string ClassName(UnitClass unitClass)
        {
            switch (unitClass)
            {
                case UnitClass.Warrior: return "Воин";
                case UnitClass.Archer: return "Лучник";
                case UnitClass.Priest: return "Жрец";
                case UnitClass.Bomber: return "Подрывник";
                case UnitClass.Monk: return "Монах";
                case UnitClass.Mage: return "Маг";
                case UnitClass.Hero: return "Богатырь";
                default: return "Нейтрал";
            }
        }

        public static string ClassPlural(UnitClass unitClass)
        {
            switch (unitClass)
            {
                case UnitClass.Warrior: return "Воины";
                case UnitClass.Archer: return "Лучники";
                case UnitClass.Priest: return "Жрецы";
                case UnitClass.Bomber: return "Подрывники";
                case UnitClass.Monk: return "Монахи";
                case UnitClass.Mage: return "Маги";
                case UnitClass.Hero: return "Богатыри";
                default: return "Нейтралы";
            }
        }

        /// <summary>Буква-заглушка на фишке, пока у юнита нет спрайта.</summary>
        public static string ClassLetter(UnitClass unitClass)
        {
            switch (unitClass)
            {
                case UnitClass.Warrior: return "В";
                case UnitClass.Archer: return "Л";
                case UnitClass.Priest: return "Ж";
                case UnitClass.Bomber: return "П";
                case UnitClass.Monk: return "М";
                case UnitClass.Mage: return "Мг";
                case UnitClass.Hero: return "Б";
                default: return "Н";
            }
        }

        /// <summary>Массовый навык класса в боевом слоте.</summary>
        public static string SkillName(UnitClass unitClass)
        {
            switch (unitClass)
            {
                case UnitClass.Warrior: return "Стена щитов";
                case UnitClass.Archer: return "Залп";
                case UnitClass.Priest: return "Молитва";
                case UnitClass.Bomber: return "Динамит";
                case UnitClass.Monk: return "Огненные клинки";
                case UnitClass.Mage: return "Цепная молния";
                default: return string.Empty;
            }
        }

        // ---------- Ресурсы на поле ----------

        public static Color ResourceColor(ResourceType resource)
        {
            switch (resource)
            {
                case ResourceType.Production: return new Color(0.60f, 0.62f, 0.70f);
                case ResourceType.Food: return new Color(0.50f, 0.80f, 0.38f);
                default: return GoldColor;
            }
        }

        public static string ResourceIcon(ResourceType resource)
        {
            switch (resource)
            {
                case ResourceType.Production: return IconProduction;
                case ResourceType.Food: return IconFood;
                default: return IconGold;
            }
        }

        public static string ResourceName(ResourceType resource)
        {
            switch (resource)
            {
                case ResourceType.Production: return "Производство";
                case ResourceType.Food: return "Еда";
                default: return "Золото";
            }
        }

        /// <summary>Вес фокуса символами: 2 → «🪙🪙».</summary>
        public static string FocusIcons(ResourceType resource, int weight)
        {
            return string.Concat(System.Linq.Enumerable.Repeat(ResourceIcon(resource), Mathf.Max(1, weight)));
        }

        // ---------- Текст ----------

        public static string Stars(int stars) => new string('★', Mathf.Clamp(stars, 1, 5));

        /// <summary>Уровень точками: ●●○○○.</summary>
        public static string Pips(int level, int max)
        {
            return new string('●', Mathf.Clamp(level, 0, max)) + new string('○', Mathf.Max(0, max - level));
        }

        public static Color TierColor(int tier)
        {
            if (tier <= 1) return new Color(0.80f, 0.82f, 0.86f);
            return tier == 2 ? new Color(0.35f, 0.72f, 1f) : new Color(0.86f, 0.52f, 1f);
        }

        /// <summary>Чёрный или белый текст — что лучше читается на данном фоне.</summary>
        public static Color ContrastText(Color background)
        {
            float luminance = 0.299f * background.r + 0.587f * background.g + 0.114f * background.b;
            return luminance > 0.6f ? new Color(0.1f, 0.1f, 0.12f) : Color.white;
        }

        // ---------- Вид символов на полях ----------

        /// <summary>Фишка поля города / трофеев.</summary>
        public static SymbolLook LookFor(TileData tile)
        {
            var look = new SymbolLook();
            switch (tile.Kind)
            {
                case TileKind.Unit:
                    look.color = ClassColor(tile.Class);
                    look.shape = RoundedRect;
                    look.sliced = true;
                    look.icon = tile.Unit != null ? tile.Unit.sprite : null;
                    look.label = look.icon != null ? string.Empty : ClassLetter(tile.Class);
                    break;
                case TileKind.Resource:
                    look.color = ResourceColor(tile.Resource);
                    look.shape = Circle;
                    look.label = ResourceIcon(tile.Resource); // иконка-спрайт TextMeshPro
                    look.labelInset = 0.12f;
                    break;
                default:
                    ItemData item = tile.Item;
                    look.color = item != null ? ClassColor(item.unitClass) : Color.gray;
                    // Броня — щит, оружие — ромб: тип видно сразу, цвет — чей это предмет
                    look.shape = item != null && item.slot == ItemSlot.Weapon ? Diamond : Shield;
                    look.icon = item != null ? item.icon : null;
                    look.label = look.icon != null || item == null ? string.Empty : item.shortLabel;
                    look.labelInset = 0.16f; // щит и ромб уже квадрата — подпись не должна вылезать за фигуру
                    break;
            }
            return look;
        }

        /// <summary>Символ боевого слота (multiplierValue — сила множителя: ×2 или ×3).</summary>
        public static SymbolLook LookForSlot(BattleSymbol symbol, int multiplierValue = 2)
        {
            var look = new SymbolLook { shape = RoundedRect, sliced = true };
            switch (symbol)
            {
                case BattleSymbol.Attack:
                    look.color = AttackSymbolColor;
                    look.label = IconAttack;
                    break;
                case BattleSymbol.Move:
                    look.color = MoveSymbolColor;
                    look.label = IconMove;
                    break;
                case BattleSymbol.Blank:
                    look.color = BlankSymbolColor;
                    look.label = string.Empty;
                    break;
                case BattleSymbol.Banner:
                    look.color = BannerSymbolColor;
                    look.label = IconBanner;
                    look.labelInset = 0.1f;
                    break;
                case BattleSymbol.Multiplier:
                    look.color = MultiplierSymbolColor;
                    look.shape = Circle;
                    look.sliced = false;
                    look.label = $"×{multiplierValue}";
                    look.labelInset = 0.14f;
                    break;
                case BattleSymbol.Relic:
                    look.color = CandleSymbolColor;
                    look.label = IconCandle;
                    look.labelInset = 0.1f;
                    break;
                default:
                    UnitClass unitClass = BattleSlotMachine.ClassOf(symbol);
                    look.color = ClassColor(unitClass);
                    look.label = ClassLetter(unitClass);
                    break;
            }
            return look;
        }

        // ---------- Процедурные спрайты (создаются один раз при первом обращении) ----------

        private static Sprite square;
        private static Sprite circle;
        private static Sprite roundedRect;
        private static Sprite shield;
        private static Sprite diamond;
        private static Sprite triangle;
        private static Sprite ring;
        private static Sprite sword;

        /// <summary>Белый квадрат 1×1 мировую единицу: клетки арены, полоски ХП, молнии.</summary>
        public static Sprite Square => square != null ? square : (square = CreateSprite(4, (x, y) => 1f, 4f, Vector4.zero, "Square"));

        /// <summary>Белый круг диаметром 1 мировую единицу.</summary>
        public static Sprite Circle => circle != null ? circle : (circle = CreateCircle());

        /// <summary>Скруглённый прямоугольник с 9-slice рамкой (для UI Image типа Sliced).</summary>
        public static Sprite RoundedRect => roundedRect != null ? roundedRect : (roundedRect = CreateRoundedRect());

        /// <summary>Щит (фишки брони, значок брони на скамейке).</summary>
        public static Sprite Shield => shield != null ? shield : (shield = CreatePolygonSprite("Shield", 64, new[]
        {
            new Vector2(0.10f, 0.94f), new Vector2(0.90f, 0.94f), new Vector2(0.90f, 0.55f), new Vector2(0.82f, 0.34f),
            new Vector2(0.66f, 0.16f), new Vector2(0.50f, 0.04f), new Vector2(0.34f, 0.16f), new Vector2(0.18f, 0.34f),
            new Vector2(0.10f, 0.55f),
        }));

        /// <summary>Ромб (фишки оружия).</summary>
        public static Sprite Diamond => diamond != null ? diamond : (diamond = CreatePolygonSprite("Diamond", 64, new[]
        {
            new Vector2(0.50f, 0.98f), new Vector2(0.96f, 0.50f), new Vector2(0.50f, 0.02f), new Vector2(0.04f, 0.50f),
        }));

        /// <summary>Треугольник остриём вверх (метка «куда смотрит юнит»).</summary>
        public static Sprite Triangle => triangle != null ? triangle : (triangle = CreatePolygonSprite("Triangle", 64, new[]
        {
            new Vector2(0.50f, 0.96f), new Vector2(0.94f, 0.10f), new Vector2(0.06f, 0.10f),
        }));

        /// <summary>Меч (значок оружия на скамейке).</summary>
        public static Sprite Sword => sword != null ? sword : (sword = CreatePolygonSprite("Sword", 64,
            new[] { new Vector2(0.43f, 0.34f), new Vector2(0.57f, 0.34f), new Vector2(0.57f, 0.84f), new Vector2(0.50f, 0.97f), new Vector2(0.43f, 0.84f) },
            new[] { new Vector2(0.22f, 0.26f), new Vector2(0.78f, 0.26f), new Vector2(0.78f, 0.35f), new Vector2(0.22f, 0.35f) },
            new[] { new Vector2(0.45f, 0.05f), new Vector2(0.55f, 0.05f), new Vector2(0.55f, 0.27f), new Vector2(0.45f, 0.27f) }));

        /// <summary>Кольцо (метка цели «Охоты»).</summary>
        public static Sprite Ring => ring != null ? ring : (ring = CreateRing());

        private static Sprite CreateCircle()
        {
            const int size = 64;
            const float radius = size * 0.5f;
            return CreateSprite(size, (x, y) =>
            {
                float dx = x + 0.5f - radius;
                float dy = y + 0.5f - radius;
                return Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy)); // мягкий край в 1 пиксель
            }, size, Vector4.zero, "Circle");
        }

        private static Sprite CreateRing()
        {
            const int size = 64;
            const float radius = size * 0.5f;
            const float thickness = 6f;
            return CreateSprite(size, (x, y) =>
            {
                float dx = x + 0.5f - radius;
                float dy = y + 0.5f - radius;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                return Mathf.Clamp01(radius - distance) * Mathf.Clamp01(distance - (radius - thickness));
            }, size, Vector4.zero, "Ring");
        }

        private static Sprite CreateRoundedRect()
        {
            const int size = 64;
            const float radius = 18f;
            return CreateSprite(size, (x, y) =>
            {
                float px = x + 0.5f;
                float py = y + 0.5f;
                float cx = Mathf.Clamp(px, radius, size - radius);
                float cy = Mathf.Clamp(py, radius, size - radius);
                float distance = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                return Mathf.Clamp01(radius - distance + 0.5f);
            }, 100f, new Vector4(radius, radius, radius, radius), "RoundedRect");
        }

        /// <summary>Фигура из одного или нескольких многоугольников (координаты 0..1), со сглаживанием 4×4.</summary>
        private static Sprite CreatePolygonSprite(string name, int size, params Vector2[][] polygons)
        {
            const int samples = 4;
            return CreateSprite(size, (x, y) =>
            {
                int inside = 0;
                for (int sy = 0; sy < samples; sy++)
                {
                    for (int sx = 0; sx < samples; sx++)
                    {
                        var point = new Vector2((x + (sx + 0.5f) / samples) / size, (y + (sy + 0.5f) / samples) / size);
                        foreach (Vector2[] polygon in polygons)
                        {
                            if (InsidePolygon(polygon, point))
                            {
                                inside++;
                                break;
                            }
                        }
                    }
                }
                return inside / (float)(samples * samples);
            }, size, Vector4.zero, name);
        }

        public static bool InsidePolygon(Vector2[] polygon, Vector2 point)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                if ((polygon[i].y > point.y) != (polygon[j].y > point.y) &&
                    point.x < (polygon[j].x - polygon[i].x) * (point.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x)
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        private static Sprite CreateSprite(int size, Func<int, int, float> alpha, float pixelsPerUnit, Vector4 border, string name)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name + " (generated)",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha(x, y)) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, a);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();

            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                pixelsPerUnit, 0, SpriteMeshType.FullRect, border);
            sprite.name = name + " (generated)";
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }
    }
}
