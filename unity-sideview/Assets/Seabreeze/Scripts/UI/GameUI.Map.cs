using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static Seabreeze.UIPrimitives;

namespace Seabreeze
{
    public sealed partial class GameUI
    {
        static readonly Vector2 MapImageSize = new Vector2(2016, 1344);
        static readonly Color MapText = new Color(.08f, .24f, .21f);
        static readonly Color MapHover = new Color(.08f, .39f, .33f, .15f);
        static readonly Vector2 MapTooltipSize = new Vector2(405, 132);

        readonly struct MapPoint
        {
            public readonly Rect Bounds;
            public readonly string Name, Description;
            public readonly PlaceId Place;
            public readonly PlaceId? EntranceFrom;
            public readonly float X;

            public MapPoint(Rect bounds, string name, string description, PlaceId place, float x = 0)
            {
                Bounds = bounds;
                Name = name;
                Description = description;
                Place = place;
                EntranceFrom = null;
                X = x;
            }

            public MapPoint(Rect bounds, string name, string description, PlaceId place, PlaceId entranceFrom)
                : this(bounds, name, description, place)
            {
                EntranceFrom = entranceFrom;
            }
        }

        // Hotspot bounds are measured from the top left of IslandOverview.png.
        // Outdoor positions use world X on the corresponding 1110 panorama.
        static readonly MapPoint[] MapPoints =
        {
            new MapPoint(new Rect(303, 409, 155, 111), "留洋的家", "船长留洋的住处，门开向居民街。", PlaceId.CaptainHome, PlaceId.ResidentialCenter),
            new MapPoint(new Rect(522, 409, 157, 111), "李庭的家", "李庭在居民街的家，和公共街上的咖啡店隔着一条街。", PlaceId.LiTingHome, PlaceId.ResidentialCenter),
            new MapPoint(new Rect(738, 409, 154, 111), "晴野的家", "晴野在居民街的住处。花店在南边的公共街。", PlaceId.ZiqingHome, PlaceId.ResidentialCenter),
            new MapPoint(new Rect(949, 409, 154, 111), "Z 的家", "这里通往 Z 家客厅。卧室从居民街上的另一扇门进入。", PlaceId.ZHome, PlaceId.ResidentialCenter),
            new MapPoint(new Rect(1158, 409, 155, 111), "周伯伯的家", "周伯伯在居民街东段的住处，靠近森林入口。", PlaceId.ZhouHome, PlaceId.ResidentialCenter),
            new MapPoint(new Rect(829, 545, 165, 55), "居民街", "几户岛民的家沿街排开，往东走可到森林入口。", PlaceId.ResidentialCenter),
            new MapPoint(new Rect(1391, 546, 47, 49), "森林入口", "居民街东端的岔口，从这里可以上山。", PlaceId.Forest, -1.35f),
            new MapPoint(new Rect(1269, 298, 142, 109), "梨园的家", "林间步道旁的木屋，往上山的方向可到研究所。", PlaceId.CabinInterior, PlaceId.ForestCabin),
            new MapPoint(new Rect(1319, 137, 180, 116), "阿里的研究所", "阿里在山顶的工作地点，旁边是山顶观景台。", PlaceId.ResearchInterior, PlaceId.Research),
            new MapPoint(new Rect(1594, 171, 155, 115), "山顶平台", "从山顶眺望海面，沿路可以走到研究所。", PlaceId.Summit),
            new MapPoint(new Rect(1566, 337, 131, 76), "森林", "林间步道连接居民街、梨园的木屋和山顶。", PlaceId.ForestPath1),

            new MapPoint(new Rect(252, 779, 127, 128), "码头", "岛上的靠船点，也是留洋常来的地方。", PlaceId.Harbor, -14.8f),
            new MapPoint(new Rect(449, 779, 128, 128), "游客中心", "来岛后可以在这里了解岛上的情况，正义经常在这里。", PlaceId.VisitorCenter, PlaceId.Harbor),
            new MapPoint(new Rect(642, 720, 123, 113), "街角花园", "公共街西段的小花园，往东就是花店。", PlaceId.Street, -17f),
            new MapPoint(new Rect(780, 720, 124, 113), "花店", "公共街上的花店，是晴野的常驻点。", PlaceId.FlowerShop, PlaceId.Street),
            new MapPoint(new Rect(946, 720, 139, 113), "咖啡店", "李庭常在这里。进店可以喝杯咖啡，歇一会儿。", PlaceId.Cafe, PlaceId.Street),
            new MapPoint(new Rect(1109, 720, 136, 113), "照相馆", "周伯伯常来的照相馆，位于公共街东段。", PlaceId.PhotoStudio, PlaceId.Street),
            new MapPoint(new Rect(1274, 692, 130, 142), "古树广场", "市民广场中央有一棵老树。广场往东接通往海边的路。", PlaceId.Square, -3.2f),
            new MapPoint(new Rect(765, 840, 143, 52), "公共街", "岛上几家店铺集中在这里，西到码头，东到市民广场。", PlaceId.Street),
            new MapPoint(new Rect(1392, 839, 47, 49), "广场东口", "市民广场东侧的路口，可以去居民街或沙滩入口。", PlaceId.EastCrossing, 4.2f),

            new MapPoint(new Rect(290, 1047, 304, 112), "海滨木质栈道", "从海滨街往西走，木栈道沿海岸伸展。", PlaceId.Boardwalk, -14.05f),
            new MapPoint(new Rect(624, 943, 191, 128), "海岛杂货铺", "沿海滨步道能走到杂货铺门前。", PlaceId.Boardwalk, 13.8f),
            new MapPoint(new Rect(816, 945, 190, 125), "海风酒吧", "海滨街上的酒吧，与海湾餐厅在同一段街上。", PlaceId.BarInterior, PlaceId.Seafront),
            new MapPoint(new Rect(1040, 978, 105, 66), "海滨观景点", "餐厅外有一处面朝大海的露台，沿海堤可以去灯塔。", PlaceId.Seafront, 14.5f),
            new MapPoint(new Rect(1180, 945, 195, 125), "海湾餐厅", "面朝海湾的餐厅，出门就是海滨街。", PlaceId.RestaurantInterior, PlaceId.Seafront),
            new MapPoint(new Rect(811, 1078, 192, 50), "海滨街", "这段沿海的街道把步道、酒吧、餐厅和沙滩串在一起。", PlaceId.Seafront),
            new MapPoint(new Rect(1392, 1075, 47, 49), "沙滩入口", "海滨街走到这里，往东可到沙滩，往北可回广场东口。", PlaceId.BeachEntrance, -5.55f),
            new MapPoint(new Rect(1601, 990, 177, 90), "沙滩", "小岛东南面的沙滩，沿海岸走可以返回海滨街。", PlaceId.Beach)
        };

        void BuildMap()
        {
            var frame = Stretch("Island map frame", overlayRoot, new Vector2(20, 20), new Vector2(-20, -20));
            var map = Rect("Island overview", frame, C, C, Vector2.zero, Vector2.zero);
            var artwork = Resources.Load<Texture2D>("Art/IslandOverview");
            var image = map.gameObject.AddComponent<RawImage>();
            image.texture = artwork;
            image.raycastTarget = false;
            var aspect = map.gameObject.AddComponent<AspectRatioFitter>();
            aspect.aspectRatio = MapImageSize.x / MapImageSize.y;
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;

            // Replace the sample marker baked into the reference with the real location in the header.
            var markerBounds = new Rect(961, 838, 90, 76);
            var patch = MapRect("Sample marker cover", map, markerBounds).gameObject.AddComponent<RawImage>();
            patch.texture = artwork;
            patch.uvRect = MapUV(new Rect(1058, markerBounds.y, markerBounds.width, markerBounds.height));
            patch.raycastTarget = false;
            MapLabel("Current location", map, new Rect(185, 237, 810, 39),
                "当前位置 · " + PlaceName(session.CurrentPlace), 30, MapText);
            MapLabel("Map controls", map, new Rect(185, 278, 850, 37),
                "悬停查看地点   ·   点击前往   ·   M / Esc 收起", 25, MapText);

            var tooltip = Rect("Location tooltip", map, C, C, Vector2.zero, Vector2.zero);
            var tooltipPanel = tooltip.gameObject.AddComponent<RoundedPanel>();
            tooltipPanel.color = new Color(1f, .98f, .92f, .99f);
            tooltipPanel.CornerRadius = 12;
            tooltipPanel.SetBorder(new Color(.28f, .49f, .41f, .7f), 1);
            tooltipPanel.raycastTarget = false;
            var title = MapTextLabel("Location name", tooltip, new Rect(.055f, .08f, .89f, .29f), "", 30, MapText);
            var description = MapTextLabel("Location description", tooltip, new Rect(.055f, .40f, .89f, .51f), "", 25, MapText);
            tooltip.gameObject.SetActive(false);

            foreach (var point in MapPoints)
            {
                var target = MapRect(point.Name, map, point.Bounds).gameObject.AddComponent<RoundedPanel>();
                target.CornerRadius = 10;
                var button = MapButton(target, () => TravelFromMap(point));
                var hover = button.gameObject.AddComponent<EventTrigger>();
                AddMapHover(hover, EventTriggerType.PointerEnter, () =>
                {
                    title.text = point.Name;
                    description.text = point.Description;
                    float x = Mathf.Clamp(point.Bounds.center.x - MapTooltipSize.x / 2, 85, MapImageSize.x - MapTooltipSize.x - 85);
                    float y = point.Bounds.yMin - MapTooltipSize.y - 12;
                    if (y < 76) y = point.Bounds.yMax + 12;
                    SetMapRect(tooltip, MapUV(new Rect(new Vector2(x, y), MapTooltipSize)));
                    tooltip.gameObject.SetActive(true);
                });
                AddMapHover(hover, EventTriggerType.PointerExit, () => tooltip.gameObject.SetActive(false));
            }

            var close = MapRect("Close island map", map, new Rect(1835, 90, 77, 78)).gameObject.AddComponent<RoundedPanel>();
            close.CornerRadius = 32;
            MapButton(close, CloseOverlay);
            tooltip.SetAsLastSibling();
        }

        void TravelFromMap(MapPoint point)
        {
            var world = WorldDirector.Instance;
            if (!world || overlayBusy || IsTransitioning) return;
            if (point.EntranceFrom.HasValue)
            {
                if (point.Place == session.CurrentPlace)
                {
                    CloseOverlay();
                    return;
                }
                var entrance = placeCatalog == null ? null : Array.Find(placeCatalog.routes, route =>
                    route.from == point.EntranceFrom.Value && route.to == point.Place);
                if (entrance != null) CloseOverlayThen(() => world.TravelTo(point.Place, entrance.arrivalId));
            }
            else
            {
                CloseOverlayThen(() => world.TravelToMapPoint(point.Place, point.X));
            }
        }

        static Button MapButton(RoundedPanel graphic, Action click)
        {
            graphic.SetBorder(Color.clear);
            var button = graphic.gameObject.AddComponent<Button>();
            button.targetGraphic = graphic;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = button.colors;
            colors.normalColor = Color.clear;
            colors.selectedColor = Color.clear;
            colors.highlightedColor = MapHover;
            colors.pressedColor = new Color(.08f, .39f, .33f, .28f);
            button.colors = colors;
            button.onClick.AddListener(() => click());
            return button;
        }

        static void AddMapHover(EventTrigger trigger, EventTriggerType type, Action action)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(_ => action());
            trigger.triggers.Add(entry);
        }

        static TextMeshProUGUI MapLabel(string name, RectTransform parent, Rect pixels, string text, float size, Color color)
            => MapTextLabel(name, parent, new Rect(pixels.position / MapImageSize, pixels.size / MapImageSize), text, size, color);

        static TextMeshProUGUI MapTextLabel(string name, RectTransform parent, Rect normalized, string text, float size, Color color)
        {
            var label = Label(name, parent, C, C, Vector2.zero, Vector2.zero, text, size, color, TextAlignmentOptions.MidlineLeft);
            SetMapRect(label.rectTransform, FlipMapY(normalized));
            label.enableAutoSizing = true;
            label.fontSizeMin = 5;
            label.fontSizeMax = size;
            label.overflowMode = TextOverflowModes.Truncate;
            return label;
        }

        static RectTransform MapRect(string name, RectTransform parent, Rect pixels)
        {
            var rect = Rect(name, parent, C, C, Vector2.zero, Vector2.zero);
            SetMapRect(rect, MapUV(pixels));
            return rect;
        }

        static Rect MapUV(Rect pixels)
            => FlipMapY(new Rect(pixels.position / MapImageSize, pixels.size / MapImageSize));

        static Rect FlipMapY(Rect bounds) => new Rect(bounds.x, 1 - bounds.yMax, bounds.width, bounds.height);

        static void SetMapRect(RectTransform transform, Rect bounds)
        {
            transform.anchorMin = bounds.min;
            transform.anchorMax = bounds.max;
            transform.offsetMin = transform.offsetMax = Vector2.zero;
        }
    }
}
