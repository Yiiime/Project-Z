using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static Seabreeze.UIPrimitives;

namespace Seabreeze
{
    /// <summary>
    /// Displays the authored island map and supplies non-navigational hover notes.
    /// The image is intentionally the source of truth: these markers only identify
    /// what is already drawn and never imply a playable destination or quest.
    /// </summary>
    public sealed class IslandMapView : MonoBehaviour
    {
        public sealed class MapLocation
        {
            public string Name { get; }
            public string Description { get; }
            /// <summary>Normalized image-space rectangle, with (0,0) at the image's bottom-left.</summary>
            public Rect NormalizedRect { get; }
            internal int Priority { get; }

            public MapLocation(string name, string description, Rect normalizedRect, int priority)
            {
                Name = name;
                Description = description;
                NormalizedRect = normalizedRect;
                Priority = priority;
            }

            public Vector2 Center => NormalizedRect.center;
        }

        const float ImageWidth = 1672f;
        const float ImageHeight = 941f;
        const float PanelPadding = 18f;
        const float TooltipWidth = 372f;
        const float TooltipPadding = 18f;
        const float TooltipOffset = 20f;
        static readonly Vector2 TL = new Vector2(0, 1);
        static readonly Vector2 C = new Vector2(.5f, .5f);

        static readonly MapLocation[] locations = BuildLocations();
        public static IReadOnlyList<MapLocation> Locations => locations;

        RectTransform hostRect;
        RectTransform panelRect;
        RectTransform mapImageRect;
        RectTransform tooltipRect;
        RectTransform highlightRect;
        Image mapImage;
        Image highlightImage;
        TextMeshProUGUI tooltipTitle;
        TextMeshProUGUI tooltipDescription;
        Sprite mapSprite;
        Texture2D mapTexture;
        float tooltipWidth = TooltipWidth;
        bool initialized;
        int hoveredLocationIndex = -1;
        Rect renderedMapRect;

        /// <summary>Number of authored map labels exposed for hover inspection.</summary>
        public int LocationCount => locations.Length;
        public int HoveredLocationIndex => hoveredLocationIndex;
        public string HoveredLocation => hoveredLocationIndex >= 0 ? locations[hoveredLocationIndex].Name : string.Empty;
        public string HoveredDescription => hoveredLocationIndex >= 0 ? locations[hoveredLocationIndex].Description : string.Empty;
        public bool TooltipVisible => tooltipRect != null && tooltipRect.gameObject.activeSelf && hoveredLocationIndex >= 0;
        public RectTransform ImageRect => mapImageRect;
        public RectTransform TooltipRect => tooltipRect;
        /// <summary>Fitted map-image bounds in the host's local coordinate space.</summary>
        public Rect RenderedMapRect => renderedMapRect;
        /// <summary>Fitted map-image bounds in screen coordinates.</summary>
        public Rect ScreenRenderedMapRect => ScreenRect(mapImageRect);

        public static MapLocation GetLocation(int index)
        {
            return index >= 0 && index < locations.Length ? locations[index] : null;
        }

        public static Rect GetLocationRect(int index)
        {
            return GetLocation(index)?.NormalizedRect ?? default;
        }

        public static string GetLocationName(int index)
        {
            return GetLocation(index)?.Name ?? string.Empty;
        }

        /// <summary>
        /// Builds the view inside an already-sized map viewport. The second argument
        /// is the panel that owns the viewport, so the tooltip can be clamped to the
        /// full panel rather than the image's padded area.
        /// </summary>
        public void Initialize(RectTransform viewport, RectTransform ownerPanel)
        {
            if (initialized)
                return;

            hostRect = viewport != null ? viewport : (RectTransform)transform;
            panelRect = ownerPanel != null ? ownerPanel : hostRect.parent as RectTransform;
            initialized = true;

            mapTexture = Resources.Load<Texture2D>("Map/IslandMap");
            BuildMapImage();
            BuildHighlight();
            BuildTooltip();
            RebuildLayout();
            HideTooltip();
        }

        void OnEnable()
        {
            if (initialized)
                HideTooltip();
        }

        void OnDisable()
        {
            HideTooltip();
        }

        void OnDestroy()
        {
            if (mapSprite != null)
                Destroy(mapSprite);
        }

        void OnRectTransformDimensionsChange()
        {
            if (initialized)
                RebuildLayout();
        }

        void BuildMapImage()
        {
            var frame = Panel("Map frame", hostRect, C, C, Vector2.zero, Vector2.zero, new Color(.018f, .041f, .082f, .86f));
            frame.rectTransform.anchorMin = Vector2.zero;
            frame.rectTransform.anchorMax = Vector2.one;
            frame.rectTransform.offsetMin = Vector2.zero;
            frame.rectTransform.offsetMax = Vector2.zero;

            mapImageRect = Rect("Island map artwork", hostRect, C, C, Vector2.zero, Vector2.one);
            mapImage = mapImageRect.gameObject.AddComponent<Image>();
            mapImage.raycastTarget = false;
            mapImage.preserveAspect = true;
            mapImage.color = Color.white;
            if (mapTexture != null)
            {
                mapSprite = Sprite.Create(mapTexture, new Rect(0, 0, mapTexture.width, mapTexture.height), new Vector2(.5f, .5f), 100f);
                mapSprite.name = "Island map runtime sprite";
                mapImage.sprite = mapSprite;
            }
            else
            {
                Label("Map unavailable", hostRect, C, C, Vector2.zero, new Vector2(500, 80), "地图图像暂不可用", 28, Paper, TextAlignmentOptions.Midline);
            }
        }

        void BuildHighlight()
        {
            highlightRect = Rect("Hovered map location", mapImageRect, C, C, Vector2.zero, Vector2.zero);
            highlightImage = highlightRect.gameObject.AddComponent<Image>();
            highlightImage.color = new Color(.98f, .79f, .40f, .15f);
            highlightImage.raycastTarget = false;
            highlightRect.gameObject.AddComponent<Outline>().effectColor = new Color(.99f, .86f, .52f, .46f);
            highlightRect.gameObject.GetComponent<Outline>().effectDistance = new Vector2(1.5f, 1.5f);
            highlightRect.gameObject.SetActive(false);
        }

        void BuildTooltip()
        {
            Transform parent = panelRect != null ? panelRect : hostRect;
            tooltipWidth = panelRect != null ? Mathf.Min(TooltipWidth, Mathf.Max(240f, panelRect.rect.width - 32f)) : TooltipWidth;
            tooltipRect = Rect("Map location tooltip", parent, C, C, Vector2.zero, new Vector2(tooltipWidth, 112));
            var background = Panel("Tooltip backing", tooltipRect, C, C, Vector2.zero, tooltipRect.sizeDelta, new Color(.018f, .042f, .085f, .97f));
            background.raycastTarget = false;
            Line(tooltipRect, TL, TL, new Vector2(0, 0), new Vector2(tooltipWidth, 2), new Color(.96f, .77f, .45f, .68f));
            tooltipTitle = Label("Tooltip title", tooltipRect, TL, TL, new Vector2(TooltipPadding, -13), new Vector2(tooltipWidth - TooltipPadding * 2, 31), "", 23, Accent);
            tooltipDescription = Label("Tooltip description", tooltipRect, TL, TL, new Vector2(TooltipPadding, -49), new Vector2(tooltipWidth - TooltipPadding * 2, 52), "", 18, Paper);
            tooltipDescription.textWrappingMode = TextWrappingModes.Normal;
            tooltipDescription.overflowMode = TextOverflowModes.Ellipsis;
            tooltipRect.gameObject.SetActive(false);
        }

        void RebuildLayout()
        {
            if (!initialized || hostRect == null || mapImageRect == null)
                return;

            Rect host = hostRect.rect;
            float availableWidth = Mathf.Max(1f, host.width - PanelPadding * 2f);
            float availableHeight = Mathf.Max(1f, host.height - PanelPadding * 2f);
            float imageAspect = mapTexture != null && mapTexture.height > 0 ? (float)mapTexture.width / mapTexture.height : ImageWidth / ImageHeight;
            float imageWidth = availableWidth;
            float imageHeight = imageWidth / imageAspect;
            if (imageHeight > availableHeight)
            {
                imageHeight = availableHeight;
                imageWidth = imageHeight * imageAspect;
            }

            mapImageRect.anchorMin = mapImageRect.anchorMax = C;
            mapImageRect.pivot = C;
            mapImageRect.anchoredPosition = Vector2.zero;
            mapImageRect.sizeDelta = new Vector2(imageWidth, imageHeight);
            renderedMapRect = new Rect(-imageWidth * .5f, -imageHeight * .5f, imageWidth, imageHeight);

            if (panelRect != null && tooltipRect != null && tooltipTitle != null && tooltipDescription != null)
            {
                float nextTooltipWidth = Mathf.Min(TooltipWidth, Mathf.Max(240f, panelRect.rect.width - 32f));
                if (!Mathf.Approximately(nextTooltipWidth, tooltipWidth))
                {
                    tooltipWidth = nextTooltipWidth;
                    tooltipRect.sizeDelta = new Vector2(tooltipWidth, tooltipRect.sizeDelta.y);
                    ((RectTransform)tooltipRect.GetChild(0)).sizeDelta = tooltipRect.sizeDelta;
                    tooltipTitle.rectTransform.sizeDelta = new Vector2(tooltipWidth - TooltipPadding * 2, tooltipTitle.rectTransform.sizeDelta.y);
                    tooltipDescription.rectTransform.sizeDelta = new Vector2(tooltipWidth - TooltipPadding * 2, tooltipDescription.rectTransform.sizeDelta.y);
                }
            }

            if (hoveredLocationIndex >= 0)
                PositionTooltip();
        }

        void Update()
        {
            if (!initialized || mapImageRect == null || !gameObject.activeInHierarchy)
                return;

            var mouse = Mouse.current;
            if (mouse == null)
            {
                SetHoveredLocation(-1);
                return;
            }

            int next = HitTestScreenPoint(mouse.position.ReadValue());
            SetHoveredLocation(next);
        }

        public int HitTest(Vector2 normalizedImagePoint)
        {
            return HitTestLocation(normalizedImagePoint);
        }

        public static int HitTestLocation(Vector2 normalizedImagePoint)
        {
            if (normalizedImagePoint.x < 0f || normalizedImagePoint.x > 1f || normalizedImagePoint.y < 0f || normalizedImagePoint.y > 1f)
                return -1;

            int best = -1;
            int bestPriority = int.MaxValue;
            float bestArea = float.MaxValue;
            for (int i = 0; i < locations.Length; i++)
            {
                var location = locations[i];
                if (!location.NormalizedRect.Contains(normalizedImagePoint))
                    continue;
                float area = location.NormalizedRect.width * location.NormalizedRect.height;
                if (location.Priority < bestPriority || (location.Priority == bestPriority && area < bestArea))
                {
                    best = i;
                    bestPriority = location.Priority;
                    bestArea = area;
                }
            }
            return best;
        }

        public int HitTestScreenPoint(Vector2 screenPoint)
        {
            if (mapImageRect == null || !mapImageRect.gameObject.activeInHierarchy)
                return -1;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(mapImageRect, screenPoint, null, out Vector2 local))
                return -1;

            Rect imageRect = mapImageRect.rect;
            if (!imageRect.Contains(local))
                return -1;
            Vector2 normalized = new Vector2(
                Mathf.InverseLerp(imageRect.xMin, imageRect.xMax, local.x),
                Mathf.InverseLerp(imageRect.yMin, imageRect.yMax, local.y));
            return HitTest(normalized);
        }

        void SetHoveredLocation(int index)
        {
            if (index == hoveredLocationIndex)
            {
                if (index >= 0)
                    PositionTooltip();
                return;
            }

            hoveredLocationIndex = index;
            if (index < 0)
            {
                HideTooltip();
                return;
            }

            var location = locations[index];
            highlightRect.anchorMin = location.NormalizedRect.min;
            highlightRect.anchorMax = location.NormalizedRect.max;
            highlightRect.offsetMin = Vector2.zero;
            highlightRect.offsetMax = Vector2.zero;
            highlightRect.gameObject.SetActive(true);

            tooltipTitle.text = location.Name;
            tooltipDescription.text = location.Description;
            float descriptionHeight = Mathf.Clamp(tooltipDescription.GetPreferredValues(location.Description, tooltipWidth - TooltipPadding * 2f, 0f).y, 24f, 66f);
            float tooltipHeight = 57f + descriptionHeight;
            tooltipRect.sizeDelta = new Vector2(tooltipWidth, tooltipHeight);
            ((RectTransform)tooltipRect.GetChild(0)).sizeDelta = tooltipRect.sizeDelta;
            tooltipTitle.rectTransform.sizeDelta = new Vector2(tooltipWidth - TooltipPadding * 2f, 31);
            tooltipDescription.rectTransform.sizeDelta = new Vector2(tooltipWidth - TooltipPadding * 2f, descriptionHeight + 4f);
            tooltipRect.gameObject.SetActive(true);
            tooltipRect.SetAsLastSibling();
            PositionTooltip();
        }

        void HideTooltip()
        {
            hoveredLocationIndex = -1;
            if (highlightRect != null)
                highlightRect.gameObject.SetActive(false);
            if (tooltipRect != null)
                tooltipRect.gameObject.SetActive(false);
        }

        void PositionTooltip()
        {
            if (hoveredLocationIndex < 0 || tooltipRect == null || !tooltipRect.gameObject.activeSelf || mapImageRect == null)
                return;

            Vector2 mapLocal = new Vector2(
                Mathf.Lerp(renderedMapRect.xMin, renderedMapRect.xMax, locations[hoveredLocationIndex].Center.x),
                Mathf.Lerp(renderedMapRect.yMin, renderedMapRect.yMax, locations[hoveredLocationIndex].Center.y));
            Vector3 world = mapImageRect.TransformPoint(mapLocal);
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, world);
            RectTransform targetParent = tooltipRect.parent as RectTransform;
            if (targetParent == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(targetParent, screen, null, out Vector2 targetLocal))
                return;

            Rect panel = targetParent.rect;
            Vector2 tooltipSize = tooltipRect.sizeDelta;
            float right = panel.xMax - tooltipSize.x * .5f - 16f;
            float left = panel.xMin + tooltipSize.x * .5f + 16f;
            float top = panel.yMax - tooltipSize.y * .5f - 16f;
            float bottom = panel.yMin + tooltipSize.y * .5f + 16f;
            Vector2 preferred = targetLocal + new Vector2(TooltipOffset + tooltipSize.x * .5f, TooltipOffset + tooltipSize.y * .5f);
            if (targetLocal.x > panel.center.x)
                preferred.x = targetLocal.x - TooltipOffset - tooltipSize.x * .5f;
            if (targetLocal.y > panel.center.y)
                preferred.y = targetLocal.y - TooltipOffset - tooltipSize.y * .5f;
            tooltipRect.anchoredPosition = new Vector2(Mathf.Clamp(preferred.x, left, right), Mathf.Clamp(preferred.y, bottom, top));
        }

        static Rect ScreenRect(RectTransform rectTransform)
        {
            if (rectTransform == null)
                return default;
            Vector3[] corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);
            Vector2 min = corners[0];
            Vector2 max = corners[2];
            return UnityEngine.Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        static Rect PixelRect(float x, float top, float width, float height)
        {
            return new Rect(x / ImageWidth, (ImageHeight - top - height) / ImageHeight, width / ImageWidth, height / ImageHeight);
        }

        static MapLocation[] BuildLocations()
        {
            // Rectangles are deliberately compact around each illustrated building and
            // its label. Road labels are lower priority so a nearby building wins.
            return new[]
            {
                new MapLocation("船长家", "船长的住处，位于居住街西端。", PixelRect(274, 270, 118, 101), 0),
                new MapLocation("咖啡店老板家", "咖啡店老板的住处，位于林间。", PixelRect(438, 270, 143, 101), 0),
                new MapLocation("花店老板家", "花店老板的住处，靠近花店。", PixelRect(606, 270, 145, 101), 0),
                new MapLocation("Z的家", "Z的住处，位于居住街一带。", PixelRect(782, 270, 118, 101), 0),
                new MapLocation("Z的父母家", "Z父母的住处，位于居住街一带。", PixelRect(918, 270, 125, 101), 0),
                new MapLocation("研究所", "沿山路而上的研究建筑。", PixelRect(1113, 145, 128, 93), 0),
                new MapLocation("山顶平台", "山顶的观景平台。", PixelRect(1368, 145, 129, 92), 0),
                new MapLocation("林间木屋", "树林中的木屋。", PixelRect(1138, 365, 143, 87), 0),
                new MapLocation("游客中心", "靠近码头的小屋，是来访者认识小岛的起点。", PixelRect(287, 516, 144, 120), 0),
                new MapLocation("街角花园", "主街旁的小花园。", PixelRect(477, 514, 148, 122), 0),
                new MapLocation("花店", "主街沿线的花店。", PixelRect(652, 514, 98, 102), 0),
                new MapLocation("咖啡店", "主街沿线的咖啡店。", PixelRect(781, 514, 107, 102), 0),
                new MapLocation("照相馆", "主街沿线的照相馆。", PixelRect(932, 514, 120, 102), 0),
                new MapLocation("古树广场", "大树旁的公共广场。", PixelRect(1065, 440, 171, 198), 0),
                new MapLocation("码头", "岛屿西侧伸向海面的码头。", PixelRect(95, 518, 126, 140), 0),
                new MapLocation("海边活动屋", "南侧海岸的活动屋。", PixelRect(409, 680, 155, 100), 0),
                new MapLocation("海景餐厅", "面向海岸的餐厅。", PixelRect(688, 680, 153, 100), 0),
                new MapLocation("海风酒吧", "海岸一带的酒吧。", PixelRect(973, 680, 157, 100), 0),
                new MapLocation("沙滩", "东南侧的沙滩。", PixelRect(1225, 690, 109, 111), 0),
                new MapLocation("居住街", "连接北侧住家的街道。", PixelRect(578, 377, 155, 84), 1),
                new MapLocation("主街", "穿过岛中心的主要街道。", PixelRect(702, 607, 96, 47), 1),
                new MapLocation("滨海步道", "沿南侧海岸延伸的步道。", PixelRect(690, 777, 154, 84), 1),
            };
        }
    }
}
