using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Casiwar
{
    /// <summary>
    /// Две раскладки одного UI: вертикальная (телефон, 1080×1920) и горизонтальная (компьютер, 1920×1080).
    /// Сборщик сцены записывает для элементов обе позы; при запуске и при смене размеров окна
    /// компонент выбирает раскладку по соотношению сторон экрана.
    /// В горизонтальной раскладке камера подгоняется так, чтобы арена боя легла в рамку arenaFrame,
    /// а панель сообщений переезжает на свободное место текущего экрана (messageDocks).
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class AdaptiveLayout : MonoBehaviour
    {
        [Serializable]
        public struct Pose
        {
            public Vector2 anchorMin;
            public Vector2 anchorMax;
            public Vector2 pivot;
            public Vector2 anchoredPosition;
            public Vector2 sizeDelta;
            public float scale;

            public static Pose Of(RectTransform rect) => new Pose
            {
                anchorMin = rect.anchorMin,
                anchorMax = rect.anchorMax,
                pivot = rect.pivot,
                anchoredPosition = rect.anchoredPosition,
                sizeDelta = rect.sizeDelta,
                scale = rect.localScale.x,
            };

            public void ApplyTo(RectTransform rect)
            {
                rect.anchorMin = anchorMin;
                rect.anchorMax = anchorMax;
                rect.pivot = pivot;
                rect.anchoredPosition = anchoredPosition;
                rect.sizeDelta = sizeDelta;
                float s = scale > 0f ? scale : 1f;
                rect.localScale = new Vector3(s, s, 1f);
            }
        }

        [Serializable]
        public class Entry
        {
            public RectTransform rect;
            public Pose portrait;
            public Pose landscape;
        }

        /// <summary>Поза панели сообщений, пока виден экран whenActive.</summary>
        [Serializable]
        public class Dock
        {
            public GameObject whenActive;
            public Pose pose;
        }

        public CanvasScaler scaler;
        public Vector2 portraitResolution = new Vector2(1080f, 1920f);
        public Vector2 landscapeResolution = new Vector2(1920f, 1080f);
        [Tooltip("Экран шире этого соотношения сторон — горизонтальная раскладка")]
        [Min(0.5f)] public float landscapeAspect = 1.15f;
        public List<Entry> entries = new List<Entry>();

        [Header("Арена боя")]
        public Camera worldCamera;
        public AutoBattleManager battle;
        [Tooltip("Область экрана, в которую вписывается арена")]
        public RectTransform arenaFrame;
        [Tooltip("Запас вокруг арены в клетках (полоски ХП, всплывающие числа)")]
        [Min(0f)] public float arenaPadding = 0.8f;

        [Header("Сообщения (горизонтальная раскладка)")]
        public RectTransform messagePanel;
        [Tooltip("Куда ставить сообщения: берётся первая позиция, чей экран сейчас виден (там, где они ничего не закрывают)")]
        public List<Dock> messageDocks = new List<Dock>();

        public bool IsLandscape { get; private set; }

        private bool applied;
        private int dockIndex = -1;
        private int screenWidth;
        private int screenHeight;
        private Vector3 portraitCameraPosition;
        private float portraitCameraSize;
        private readonly Vector3[] corners = new Vector3[4];

        private void Awake()
        {
            if (worldCamera == null) worldCamera = Camera.main;
            if (worldCamera != null)
            {
                portraitCameraPosition = worldCamera.transform.position;
                portraitCameraSize = worldCamera.orthographicSize;
            }
            Refresh();
        }

        private void Update() => Refresh();

        private void LateUpdate()
        {
            FitArena(); // в обеих раскладках: под ареной — панель наших юнитов
            if (IsLandscape) DockMessages();
        }

        private void DockMessages()
        {
            if (messagePanel == null) return;
            for (int i = 0; i < messageDocks.Count; i++)
            {
                Dock dock = messageDocks[i];
                if (dock?.whenActive == null || !dock.whenActive.activeInHierarchy) continue;
                if (i != dockIndex) dock.pose.ApplyTo(messagePanel);
                dockIndex = i;
                return;
            }
        }

        private void Refresh()
        {
            if (applied && Screen.width == screenWidth && Screen.height == screenHeight) return;
            screenWidth = Screen.width;
            screenHeight = Screen.height;
            bool landscape = screenHeight > 0 && screenWidth > screenHeight * landscapeAspect;
            if (applied && landscape == IsLandscape) return;
            Apply(landscape);
        }

        public void Apply(bool landscape)
        {
            applied = true;
            IsLandscape = landscape;
            dockIndex = -1;
            if (scaler != null) scaler.referenceResolution = landscape ? landscapeResolution : portraitResolution;
            foreach (Entry entry in entries)
            {
                if (entry?.rect != null) (landscape ? entry.landscape : entry.portrait).ApplyTo(entry.rect);
            }
            if (!landscape && worldCamera != null)
            {
                worldCamera.transform.position = portraitCameraPosition;
                worldCamera.orthographicSize = portraitCameraSize;
            }
        }

        /// <summary>Подобрать масштаб и сдвиг ортокамеры, чтобы арена встала в рамку arenaFrame.</summary>
        private void FitArena()
        {
            if (worldCamera == null || battle == null || arenaFrame == null || Screen.height <= 0) return;
            arenaFrame.GetWorldCorners(corners); // Screen Space Overlay: углы — в пикселях экрана
            float framePixelsW = corners[2].x - corners[0].x;
            float framePixelsH = corners[2].y - corners[0].y;
            if (framePixelsW < 1f || framePixelsH < 1f) return;

            float pad = arenaPadding * battle.cellSize * 2f;
            float arenaW = battle.columns * battle.cellSize + pad;
            float arenaH = battle.rows * battle.cellSize + pad;
            float pixelsPerUnit = Mathf.Min(framePixelsW / arenaW, framePixelsH / arenaH);
            worldCamera.orthographicSize = Screen.height * 0.5f / pixelsPerUnit;

            Vector2 frameCenter = (corners[0] + corners[2]) * 0.5f;
            Vector2 offset = (frameCenter - new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)) / pixelsPerUnit;
            Vector3 arena = battle.transform.position;
            worldCamera.transform.position = new Vector3(arena.x - offset.x, arena.y - offset.y, portraitCameraPosition.z);
        }
    }
}
