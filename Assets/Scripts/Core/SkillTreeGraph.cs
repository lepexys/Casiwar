using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Casiwar
{
    /// <summary>Состояние узла дерева прокачки.</summary>
    public enum SkillState
    {
        Locked, // не открыт: нужны узлы выше или наука
        Open,   // открыт, но сейчас не купить (не хватает золота / очков знаний, нет здания)
        Ready,  // можно купить прямо сейчас
        Done,   // куплен / изучен
    }

    /// <summary>Узел дерева прокачки для SkillTreeGraph.</summary>
    public sealed class SkillNode
    {
        public string Id;
        /// <summary>Место: x — столбец (0 — середина), y — строка сверху.</summary>
        public Vector2 Pos;
        public string Title;
        /// <summary>Цена на плашке под кружком («45 🪙»); пусто — без плашки.</summary>
        public string Price;
        /// <summary>Рисунок в кружке (нет — IconText).</summary>
        public Sprite Icon;
        public string IconText;
        public Color Accent = Color.gray;
        /// <summary>Ключ: крупный узел, где сходятся пути.</summary>
        public bool Big;
        public string[] Parents = new string[0];
        /// <summary>Цвет линий в этот узел (гибриды — цветом своего класса); null — обычный.</summary>
        public Color? EdgeTint;
        public SkillState State;
    }

    /// <summary>Подпись на дереве (названия веток, гибридов).</summary>
    public sealed class SkillLabel
    {
        public string Text;
        public Vector2 Pos;
        public Color Color = Color.white;
    }

    /// <summary>
    /// Дерево прокачки, как в казуальных tower defense: узлы — кружки с рисунком, ценой и подписью, связи — плавные
    /// кривые от узла (из-под его подписи) к узлу ниже (в верх кружка, там точка) — у каждой связи своя линия, цвет —
    /// ветки, откуда она идёт. Купленный путь горит золотом, доступный узел пульсирует, закрытые — тусклые.
    /// Наведи (или кликни) на узел — его связи ярче и толще, остальные гаснут: видно, что куда идёт.
    /// Дерево прокручивается колесом и перетаскиванием; ширина — по окну.
    /// Строится кодом внутри заданной области (Build), состояния — Refresh. Клик по узлу — событие Clicked.
    /// </summary>
    public class SkillTreeGraph : MonoBehaviour
    {
        private static readonly Color DoneColor = new Color(1f, 0.80f, 0.30f);
        private static readonly Color LockedDisc = new Color(0.15f, 0.16f, 0.20f);
        private static readonly Color LockedRing = new Color(0.32f, 0.34f, 0.40f);
        private static readonly Color EdgeLocked = new Color(0.27f, 0.29f, 0.35f, 0.9f);
        private static readonly Color EdgeOpen = new Color(0.78f, 0.82f, 0.90f, 0.85f);

        private sealed class NodeView
        {
            public SkillNode Node;
            public RectTransform Root;
            public Image Halo;
            public Image Disc;
            public Image Icon;
            public TMP_Text IconText;
            public Image Ring;
            public Image Selection;
            public Image PricePlate;
            public TMP_Text Price;
            public TMP_Text Title;
        }

        private sealed class EdgeView
        {
            public string From;
            public string To;
            public RectTransform Root;
            public readonly List<Image> Segments = new List<Image>();
            public Image Dot;
            public Color Color;
            public int Rank; // 0 — закрыта, 1 — открыта, 2 — пройдена
        }

        [Tooltip("Высота строки дерева, px")]
        [Min(60f)] public float rowHeight = 170f;
        [Tooltip("Диаметр кружка, px (ключ — на четверть больше)")]
        [Min(30f)] public float nodeSize = 74f;
        [Min(60f)] public float titleWidth = 160f;
        [Min(1f)] public float edgeWidth = 6f;
        [Tooltip("Запас по краям, в столбцах")]
        [Min(0f)] public float sidePadding = 0.6f;
        [Min(0f)] public float topPadding = 60f;
        [Tooltip("Связи между дальними столбцами (устарело: связи теперь — кривые)")]
        [Min(0f)] public float laneOffset = 0f;

        public TMP_FontAsset font;

        private RectTransform viewport;
        private RectTransform content;
        private RectTransform edgesRoot;
        private RectTransform nodesRoot;
        private ScrollRect scroll;
        private readonly Dictionary<string, NodeView> nodes = new Dictionary<string, NodeView>();
        private readonly List<EdgeView> edges = new List<EdgeView>();
        private readonly List<(SkillLabel label, TMP_Text text)> labels = new List<(SkillLabel label, TMP_Text text)>();
        private float laidOutWidth = -1f;
        private string selected;
        private string hovered;

        /// <summary>Клик по узлу (id).</summary>
        public event Action<string> Clicked;

        private void LateUpdate()
        {
            if (viewport != null && !Mathf.Approximately(viewport.rect.width, laidOutWidth)) Layout();
        }

        /// <summary>Построить дерево в области area (прокрутка, линии, узлы, подписи). Повторный вызов — заново.</summary>
        public void Build(RectTransform area, IList<SkillNode> skillNodes, IList<SkillLabel> skillLabels = null)
        {
            if (viewport != null) Destroy(viewport.gameObject);
            nodes.Clear();
            edges.Clear();
            labels.Clear();

            viewport = NewRect("TreeViewport", area);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = viewport.offsetMax = Vector2.zero;
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f); // ловит колесо и перетаскивание
            content = NewRect("TreeContent", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 45f;
            scroll.inertia = true;
            edgesRoot = NewRect("Edges", content);
            Stretch(edgesRoot);
            nodesRoot = NewRect("Nodes", content);
            Stretch(nodesRoot);

            foreach (SkillNode node in skillNodes) nodes[node.Id] = CreateNode(node);
            foreach (SkillNode node in skillNodes)
            {
                foreach (string parent in node.Parents ?? new string[0])
                {
                    if (!nodes.ContainsKey(parent)) continue;
                    var edge = new EdgeView { From = parent, To = node.Id, Root = NewRect($"Edge_{parent}_{node.Id}", edgesRoot) };
                    Stretch(edge.Root);
                    edges.Add(edge);
                }
            }
            if (skillLabels != null)
            {
                foreach (SkillLabel label in skillLabels)
                {
                    TMP_Text text = NewText("Label", content, 24f, label.Color);
                    text.text = label.Text;
                    text.fontStyle = FontStyles.Bold;
                    labels.Add((label, text));
                }
            }
            Layout();
            Refresh(skillNodes);
        }

        /// <summary>Обновить состояния, цены и подписи узлов (и цвета линий).</summary>
        public void Refresh(IEnumerable<SkillNode> skillNodes)
        {
            foreach (SkillNode node in skillNodes)
            {
                if (nodes.TryGetValue(node.Id, out NodeView view))
                {
                    view.Node = node;
                    Style(view);
                }
            }
            foreach (EdgeView edge in edges)
            {
                if (!nodes.TryGetValue(edge.From, out NodeView from) || !nodes.TryGetValue(edge.To, out NodeView to)) continue;
                bool fromDone = from.Node.State == SkillState.Done, toDone = to.Node.State == SkillState.Done;
                Color source = Color.Lerp(to.Node.EdgeTint ?? from.Node.Accent, Color.white, 0.25f);
                edge.Rank = fromDone && toDone ? 2 : fromDone ? 1 : 0;
                edge.Color = edge.Rank == 2 ? DoneColor
                    : edge.Rank == 1 ? new Color(source.r, source.g, source.b, 0.95f)
                    : Color.Lerp(source, EdgeLocked, 0.6f);
            }
            ApplyFocus();
        }

        /// <summary>
        /// Связи узла под мышью (или выбранного) — ярче и толще, поверх остальных; остальные при этом гаснут.
        /// Без фокуса пройденные и открытые связи лежат поверх закрытых.
        /// </summary>
        private void ApplyFocus()
        {
            string focus = hovered ?? selected;
            foreach (EdgeView edge in edges.OrderBy(e => FocusRank(e, focus)))
            {
                bool lit = focus != null && (edge.From == focus || edge.To == focus);
                Color color = edge.Color;
                if (lit) color = new Color(Mathf.Min(1f, color.r * 1.25f + 0.1f), Mathf.Min(1f, color.g * 1.25f + 0.1f), Mathf.Min(1f, color.b * 1.25f + 0.1f), 1f);
                else if (focus != null) color.a *= 0.3f;
                float thickness = lit ? 1.7f : 1f;
                foreach (Image segment in edge.Segments)
                {
                    segment.color = color;
                    segment.rectTransform.localScale = new Vector3(1f, thickness, 1f);
                }
                if (edge.Dot != null)
                {
                    edge.Dot.color = color;
                    edge.Dot.rectTransform.localScale = Vector3.one * (lit ? 1.4f : 1f);
                }
                edge.Root.SetAsLastSibling();
            }
        }

        private static int FocusRank(EdgeView edge, string focus) =>
            focus != null && (edge.From == focus || edge.To == focus) ? 10 : edge.Rank;

        /// <summary>Мышь над узлом (null — ушла).</summary>
        private void Hover(string id)
        {
            if (hovered == id) return;
            hovered = id;
            ApplyFocus();
        }

        /// <summary>Выделить узел (рамка); null — снять.</summary>
        public void Select(string id)
        {
            selected = id;
            foreach (NodeView view in nodes.Values) view.Selection.enabled = view.Node.Id == selected;
            ApplyFocus();
        }

        /// <summary>Прокрутить так, чтобы узел был виден.</summary>
        public void ScrollTo(string id)
        {
            if (!nodes.TryGetValue(id, out NodeView view) || content == null || viewport == null) return;
            float y = -view.Root.anchoredPosition.y - viewport.rect.height * 0.4f;
            content.anchoredPosition = new Vector2(0f, Mathf.Clamp(y, 0f, Mathf.Max(0f, content.rect.height - viewport.rect.height)));
        }

        // =====================================================================
        //  Узлы
        // =====================================================================

        private NodeView CreateNode(SkillNode node)
        {
            float size = node.Big ? nodeSize * 1.25f : nodeSize;
            var view = new NodeView { Node = node, Root = NewRect($"Node_{node.Id}", nodesRoot) };
            view.Root.anchorMin = view.Root.anchorMax = new Vector2(0.5f, 1f);
            view.Root.sizeDelta = new Vector2(size, size);

            view.Halo = NewImage("Halo", view.Root, GameVisuals.SoftCircle, size * 1.7f);
            view.Selection = NewImage("Selected", view.Root, GameVisuals.Ring, size * 1.32f);
            view.Selection.color = Color.white;
            view.Selection.enabled = false;
            view.Disc = NewImage("Disc", view.Root, GameVisuals.Circle, size);
            view.Disc.raycastTarget = true;
            var button = view.Disc.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            string id = node.Id;
            button.onClick.AddListener(() => Clicked?.Invoke(id));
            SkillNodeHover hover = view.Disc.gameObject.AddComponent<SkillNodeHover>();
            hover.Entered = () => Hover(id);
            hover.Exited = () => { if (hovered == id) Hover(null); };
            view.Icon = NewImage("Icon", view.Root, node.Icon, size * 0.72f);
            view.Icon.preserveAspect = true;
            view.Icon.enabled = node.Icon != null;
            view.IconText = NewText("IconText", view.Root, size * 0.42f, Color.white);
            view.IconText.rectTransform.sizeDelta = new Vector2(size, size);
            view.IconText.text = node.Icon == null ? node.IconText ?? string.Empty : string.Empty;
            view.IconText.fontStyle = FontStyles.Bold;
            view.Ring = NewImage("Ring", view.Root, GameVisuals.Ring, size * 1.06f);
            if (node.Big)
            {
                Image outer = NewImage("KeyRing", view.Root, GameVisuals.Ring, size * 1.18f);
                outer.color = new Color(1f, 0.85f, 0.4f, 0.55f);
            }

            view.PricePlate = NewImage("PricePlate", view.Root, GameVisuals.RoundedRect, 0f);
            view.PricePlate.type = Image.Type.Sliced;
            view.PricePlate.rectTransform.sizeDelta = new Vector2(size * 0.95f, 28f);
            view.PricePlate.rectTransform.anchoredPosition = new Vector2(0f, -size * 0.5f + 2f);
            view.Price = NewText("Price", view.PricePlate.rectTransform, 18f, Color.white);
            view.Price.rectTransform.sizeDelta = new Vector2(size * 0.95f, 28f);
            view.Price.enableAutoSizing = true;
            view.Price.fontSizeMin = 10f;
            view.Price.fontSizeMax = 18f;

            view.Title = NewText("Title", view.Root, 18f, Color.white);
            view.Title.rectTransform.anchoredPosition = new Vector2(0f, -size * 0.5f - 34f);
            view.Title.rectTransform.sizeDelta = new Vector2(titleWidth, 40f);
            view.Title.enableWordWrapping = true;
            view.Title.enableAutoSizing = true;
            view.Title.fontSizeMin = 11f;
            view.Title.fontSizeMax = 18f;
            view.Title.text = node.Title;
            return view;
        }

        private void Style(NodeView view)
        {
            SkillNode node = view.Node;
            Color accent = node.Accent;
            Color disc, ring, title;
            float iconAlpha = 1f;
            switch (node.State)
            {
                case SkillState.Done:
                    disc = Color.Lerp(accent, Color.black, 0.2f);
                    ring = DoneColor;
                    title = new Color(1f, 0.88f, 0.55f);
                    break;
                case SkillState.Ready:
                    disc = Color.Lerp(accent, Color.black, 0.35f);
                    ring = Color.white;
                    title = Color.white;
                    break;
                case SkillState.Open:
                    disc = Color.Lerp(accent, new Color(0.1f, 0.11f, 0.14f), 0.6f);
                    ring = Color.Lerp(accent, Color.white, 0.2f);
                    title = new Color(0.82f, 0.85f, 0.9f);
                    iconAlpha = 0.85f;
                    break;
                default:
                    disc = LockedDisc;
                    ring = LockedRing;
                    title = new Color(0.55f, 0.58f, 0.64f);
                    iconAlpha = 0.32f;
                    break;
            }
            view.Disc.color = disc;
            view.Ring.color = ring;
            view.Halo.color = node.State == SkillState.Ready ? new Color(accent.r, accent.g, accent.b, 0.55f)
                : node.State == SkillState.Done ? new Color(1f, 0.8f, 0.3f, 0.22f) : Color.clear;
            view.Icon.color = new Color(1f, 1f, 1f, iconAlpha);
            view.IconText.color = new Color(1f, 1f, 1f, iconAlpha);
            view.Title.color = title;
            view.Title.text = node.Title;
            bool showPrice = node.State != SkillState.Done && !string.IsNullOrEmpty(node.Price);
            view.PricePlate.enabled = showPrice;
            view.Price.enabled = showPrice;
            if (showPrice)
            {
                view.Price.text = node.Price;
                view.PricePlate.color = node.State == SkillState.Ready ? new Color(0.25f, 0.55f, 0.3f, 0.95f) : new Color(0.08f, 0.09f, 0.12f, 0.92f);
                view.Price.color = node.State == SkillState.Locked ? new Color(0.6f, 0.62f, 0.68f) : Color.white;
            }
            AttentionPulse.Set(view.Ring, node.State == SkillState.Ready);
            view.Selection.enabled = node.Id == selected;
        }

        // =====================================================================
        //  Раскладка
        // =====================================================================

        /// <summary>Узлы и подписи — по ширине окна, линии — «трубами»; высота содержимого — по нижнему узлу.</summary>
        private void Layout()
        {
            if (viewport == null || nodes.Count == 0) return;
            laidOutWidth = viewport.rect.width;
            float width = Mathf.Max(200f, laidOutWidth);
            float minX = nodes.Values.Min(v => v.Node.Pos.x);
            float maxX = nodes.Values.Max(v => v.Node.Pos.x);
            float maxY = nodes.Values.Max(v => v.Node.Pos.y);
            float unit = width / Mathf.Max(1f, maxX - minX + sidePadding * 2f);
            float centerX = (minX + maxX) * 0.5f;
            Vector2 Place(Vector2 pos) => new Vector2((pos.x - centerX) * unit, -(topPadding + pos.y * rowHeight));

            float titleSpace = Mathf.Min(titleWidth, unit * 0.98f);
            foreach (NodeView view in nodes.Values)
            {
                view.Root.anchoredPosition = Place(view.Node.Pos);
                view.Title.rectTransform.sizeDelta = new Vector2(titleSpace, 40f);
            }
            foreach ((SkillLabel label, TMP_Text text) in labels)
            {
                RectTransform rect = text.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
                rect.anchoredPosition = Place(label.Pos);
                rect.sizeDelta = new Vector2(unit * 2.2f, 34f);
            }

            foreach (EdgeView edge in edges)
            {
                for (int i = edge.Root.childCount - 1; i >= 0; i--) Destroy(edge.Root.GetChild(i).gameObject);
                edge.Segments.Clear();
                float childRadius = (nodes[edge.To].Node.Big ? nodeSize * 1.25f : nodeSize) * 0.5f;
                float parentRadius = (nodes[edge.From].Node.Big ? nodeSize * 1.25f : nodeSize) * 0.5f;
                Vector2 center = Place(nodes[edge.From].Node.Pos);
                Vector2 target = Place(nodes[edge.To].Node.Pos);
                // Из-под подписи родителя — в верх кружка потомка (там точка): видно, откуда и куда идёт связь
                var a = new Vector2(center.x, center.y - parentRadius - 56f);
                var b = new Vector2(target.x, target.y + childRadius + 3f);
                if (Mathf.Abs(a.x - b.x) < 1f)
                {
                    AddSegment(edge, a, b);
                }
                else
                {
                    // Плавно уходит вниз и плавно входит сверху (на длинных связях изгиб не растягивается на всю высоту)
                    float pull = Mathf.Clamp((a.y - b.y) * 0.5f, 20f, 80f);
                    var c1 = new Vector2(a.x, a.y - pull);
                    var c2 = new Vector2(b.x, b.y + pull);
                    int steps = Mathf.Clamp(Mathf.RoundToInt(Vector2.Distance(a, b) / 18f), 8, 40);
                    Vector2 previous = a;
                    for (int i = 1; i <= steps; i++)
                    {
                        Vector2 point = Bezier(a, c1, c2, b, i / (float)steps);
                        AddSegment(edge, previous, point);
                        previous = point;
                    }
                }
                edge.Dot = AddDot(edge, b);
            }
            ApplyFocus();
            content.sizeDelta = new Vector2(0f, topPadding + maxY * rowHeight + nodeSize + 80f);
        }

        private static Vector2 Bezier(Vector2 a, Vector2 c1, Vector2 c2, Vector2 b, float t)
        {
            float u = 1f - t;
            return u * u * u * a + 3f * u * u * t * c1 + 3f * u * t * t * c2 + t * t * t * b;
        }

        /// <summary>Точка, где связь входит в узел.</summary>
        private Image AddDot(EdgeView edge, Vector2 at)
        {
            RectTransform rect = NewRect("Dot", edge.Root);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.anchoredPosition = at;
            rect.sizeDelta = new Vector2(edgeWidth * 2.2f, edgeWidth * 2.2f);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = GameVisuals.Circle;
            image.raycastTarget = false;
            return image;
        }

        private void AddSegment(EdgeView edge, Vector2 a, Vector2 b)
        {
            RectTransform rect = NewRect("Segment", edge.Root);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            Vector2 delta = b - a;
            rect.anchoredPosition = (a + b) * 0.5f;
            rect.sizeDelta = new Vector2(delta.magnitude + edgeWidth, edgeWidth);
            rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = GameVisuals.RoundedRect;
            image.type = Image.Type.Sliced;
            image.raycastTarget = false;
            image.color = EdgeLocked;
            edge.Segments.Add(image);
        }

        // =====================================================================
        //  Помощники
        // =====================================================================

        private Image NewImage(string name, RectTransform parent, Sprite sprite, float size)
        {
            RectTransform rect = NewRect(name, parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

        private TMP_Text NewText(string name, RectTransform parent, float size, Color color)
        {
            RectTransform rect = NewRect(name, parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) text.font = font;
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.Center;
            text.color = color;
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            text.outlineWidth = 0.15f;
            text.outlineColor = new Color32(0, 0, 0, 200);
            return text;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }
    }
}
