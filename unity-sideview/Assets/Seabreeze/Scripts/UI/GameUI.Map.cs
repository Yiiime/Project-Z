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
        static readonly Vector2 MapImageSize = new Vector2(1440, 1024);
        static readonly Color MapText = new Color(.94f, .92f, .98f);
        static readonly Color MapHover = new Color(.85f, .78f, .98f, .18f);
        static readonly Vector2 MapTooltipSize = new Vector2(320, 106);

        readonly struct MapPoint
        {
            public readonly Rect Bounds;
            public readonly string Name, Description;
            public readonly PlaceId Place;
            public readonly PlaceId? EntranceFrom;
            public readonly bool StopAtEntrance;
            public readonly float X;

            public MapPoint(Rect bounds, string name, string description, PlaceId place, float x = 0)
            {
                Bounds = bounds;
                Name = name;
                Description = description;
                Place = place;
                EntranceFrom = null;
                StopAtEntrance = false;
                X = x;
            }

            public MapPoint(Rect bounds, string name, string description, PlaceId place, PlaceId entranceFrom, bool stopAtEntrance = false)
                : this(bounds, name, description, place)
            {
                EntranceFrom = entranceFrom;
                StopAtEntrance = stopAtEntrance;
            }
        }

        // Hotspot bounds are measured from the top left of IslandOverview.png.
        // Icon bounds follow the SVG transforms. Destinations use the authored panorama routes.
        static readonly MapPoint[] MapPoints =
        {
            new MapPoint(new Rect(354.400f, 388.605f, 36.800f, 43.890f), "留洋的家", "船长留洋的住处，门开向居民街。", PlaceId.CaptainHome, PlaceId.ResidentialCenter, stopAtEntrance: true),
            new MapPoint(new Rect(471.200f, 375.020f, 36.800f, 43.890f), "李庭的家", "李庭在居民街的家，和公共街上的咖啡店隔着一条街。", PlaceId.LiTingHome, PlaceId.ResidentialCenter, stopAtEntrance: true),
            new MapPoint(new Rect(592.000f, 390.695f, 36.800f, 43.890f), "晴野的家", "晴野在居民街的住处。花店在南边的公共街。", PlaceId.ZiqingHome, PlaceId.ResidentialCenter, stopAtEntrance: true),
            new MapPoint(new Rect(712.800f, 403.235f, 36.800f, 43.890f), "Z 的家", "到 Z 家门口后按 E 进入客厅。卧室的入口就在旁边。", PlaceId.ZHome, PlaceId.ResidentialCenter, stopAtEntrance: true),
            new MapPoint(new Rect(832.800f, 381.290f, 36.800f, 43.890f), "周伯伯的家", "周伯伯在居民街东段的住处，靠近森林入口。", PlaceId.ZhouHome, PlaceId.ResidentialCenter, stopAtEntrance: true),
            new MapPoint(new Rect(752.000f, 428.315f, 68.800f, 29.260f), "居民街", "几户岛民的家沿街排开，往东走可到森林入口。", PlaceId.ResidentialCenter),
            new MapPoint(new Rect(938.400f, 415.775f, 32.000f, 36.575f), "森林入口", "居民街东端的岔口，从这里可以上山。", PlaceId.Forest, -1.35f),
            new MapPoint(new Rect(876.800f, 349.940f, 36.800f, 43.890f), "梨园的家", "林间步道旁的木屋，往上山的方向可到研究所。", PlaceId.CabinInterior, PlaceId.ForestCabin, stopAtEntrance: true),
            new MapPoint(new Rect(910.400f, 295.600f, 36.800f, 43.890f), "阿里的研究所", "阿里在山顶的工作地点，旁边是山顶观景台。", PlaceId.ResearchInterior, PlaceId.Research),
            new MapPoint(new Rect(986.400f, 251.710f, 36.800f, 43.890f), "山顶平台", "从山顶眺望海面，沿路可以走到研究所。", PlaceId.Summit),
            new MapPoint(new Rect(988.000f, 379.200f, 68.000f, 41.800f), "森林", "林间步道连接居民街、梨园的木屋和山顶。", PlaceId.ForestPath1),

            new MapPoint(new Rect(287.200f, 546.400f, 36.800f, 43.890f), "码头", "岛上的靠船点，也是留洋常来的地方。", PlaceId.Harbor, -14.8f),
            new MapPoint(new Rect(403.200f, 528.635f, 36.800f, 43.890f), "游客中心", "来岛后可以在这里了解岛上的情况，正义经常在这里。", PlaceId.VisitorCenter, PlaceId.Harbor),
            new MapPoint(new Rect(529.600f, 529.680f, 36.800f, 43.890f), "街角花园", "公共街西段的小花园，往东就是花店。", PlaceId.Street, -17f),
            new MapPoint(new Rect(616.800f, 527.590f, 36.800f, 43.890f), "花店", "公共街上的花店，是晴野的常驻点。", PlaceId.FlowerShop, PlaceId.Street),
            new MapPoint(new Rect(705.600f, 547.445f, 36.800f, 43.890f), "咖啡店", "李庭常在这里。进店可以喝杯咖啡，歇一会儿。", PlaceId.Cafe, PlaceId.Street),
            new MapPoint(new Rect(794.400f, 536.995f, 36.800f, 43.890f), "照相馆", "周伯伯常来的照相馆，位于公共街东段。", PlaceId.PhotoStudio, PlaceId.Street),
            new MapPoint(new Rect(892.000f, 527.590f, 36.800f, 43.890f), "古树广场", "市民广场中央有一棵老树。广场往东接通往海边的路。", PlaceId.Square, -3.2f),
            new MapPoint(new Rect(341.600f, 574.615f, 68.800f, 29.260f), "公共街", "岛上几家店铺集中在这里，西到码头，东到市民广场。", PlaceId.Street),
            new MapPoint(new Rect(948.800f, 565.210f, 28.000f, 36.575f), "广场东口", "市民广场东侧的路口，可以去居民街或沙滩入口。", PlaceId.EastCrossing, 4.2f),

            new MapPoint(new Rect(328.800f, 703.150f, 36.800f, 43.890f), "海滨木质栈道", "从海滨街往西走，木栈道沿海岸伸展。", PlaceId.Boardwalk, -14.05f),
            new MapPoint(new Rect(541.600f, 702.105f, 36.800f, 43.890f), "海岛杂货铺", "海滨步道右侧的杂货铺，走到蓝门前按 E 进店。", PlaceId.GeneralStore, PlaceId.Boardwalk, stopAtEntrance: true),
            new MapPoint(new Rect(642.400f, 698.970f, 36.800f, 43.890f), "海风酒吧", "海滨街上的酒吧，与海湾餐厅在同一段街上。", PlaceId.BarInterior, PlaceId.Seafront),
            new MapPoint(new Rect(740.000f, 727.185f, 44.000f, 31.350f), "海滨观景点", "餐厅外有一处面朝大海的露台，沿海堤可以去灯塔。", PlaceId.Seafront, 14.5f),
            new MapPoint(new Rect(820.800f, 690.610f, 36.800f, 43.890f), "海湾餐厅", "面朝海湾的餐厅，出门就是海滨街。", PlaceId.RestaurantInterior, PlaceId.Seafront),
            new MapPoint(new Rect(669.600f, 732.410f, 68.800f, 29.260f), "海滨街", "这段沿海的街道把步道、酒吧、餐厅和沙滩串在一起。", PlaceId.Seafront),
            new MapPoint(new Rect(858.400f, 715.690f, 28.800f, 37.620f), "沙滩入口", "海滨街走到这里，往东可到沙滩，往北可回广场东口。", PlaceId.BeachEntrance, -5.55f),
            new MapPoint(new Rect(620.000f, 793.020f, 112.000f, 39.710f), "沙滩", "小岛东南面的沙滩，沿海岸走可以返回海滨街。", PlaceId.Beach)
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

            MapLabel("Map date", map, new Rect(76, 53, 620, 27),
                "第 " + session.Clock.Day + " 天 · " + PeriodName(session.Clock.Period), 19, MapText);
            MapLabel("Current location", map, new Rect(760, 149, 584, 28),
                "当前 · " + PlaceName(session.CurrentPlace), 18, MapText);

            // Shared panoramas can contain several destinations, so mark the nearest one.
            MapPoint? current = null;
            float distance = float.MaxValue;
            float playerX = WorldDirector.Instance.Player.position.x;
            foreach (var point in MapPoints)
            {
                float pointX = point.X;
                if (point.Place != session.CurrentPlace)
                {
                    if (!point.StopAtEntrance || point.EntranceFrom != session.CurrentPlace) continue;
                    var entrance = MapEntrance(point);
                    if (entrance == null) continue;
                    pointX = entrance.x;
                }
                float delta = Mathf.Abs(pointX - playerX);
                if (delta >= distance) continue;
                current = point;
                distance = delta;
            }
            if (current.HasValue)
            {
                var bounds = current.Value.Bounds;
                var marker = MapRect("Current map marker", map,
                    new Rect(bounds.center.x - 5, bounds.yMax + 5, 10, 10)).gameObject.AddComponent<Image>();
                marker.sprite = CircleSprite;
                marker.color = new Color(.85f, .78f, .98f);
                marker.raycastTarget = false;
            }

            var tooltip = Rect("Location tooltip", map, C, C, Vector2.zero, Vector2.zero);
            var tooltipPanel = tooltip.gameObject.AddComponent<RoundedPanel>();
            tooltipPanel.color = new Color(.14f, .16f, .25f, .99f);
            tooltipPanel.CornerRadius = 12;
            tooltipPanel.SetBorder(new Color(.63f, .59f, .74f, .9f), 1);
            tooltipPanel.raycastTarget = false;
            var title = MapTextLabel("Location name", tooltip, new Rect(.055f, .08f, .89f, .29f), "", 23, MapText);
            var description = MapTextLabel("Location description", tooltip, new Rect(.055f, .40f, .89f, .51f), "", 18, MapText);
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

            var close = MapRect("Close island map", map, new Rect(1328, 94, 48, 48)).gameObject.AddComponent<RoundedPanel>();
            close.CornerRadius = 32;
            MapButton(close, CloseOverlay);
            var back = MapRect("Back from island map", map, new Rect(65, 94, 38, 48)).gameObject.AddComponent<RoundedPanel>();
            MapButton(back, CloseOverlay);
            tooltip.SetAsLastSibling();
        }

        void TravelFromMap(MapPoint point)
        {
            var world = WorldDirector.Instance;
            if (!world || overlayBusy || IsTransitioning) return;
            if (point.EntranceFrom.HasValue)
            {
                var entrance = MapEntrance(point);
                if (point.StopAtEntrance)
                {
                    if (entrance != null) CloseOverlayThen(() => world.TravelToMapPoint(entrance.from, entrance.x));
                    return;
                }
                if (point.Place == session.CurrentPlace)
                {
                    CloseOverlay();
                    return;
                }
                if (entrance != null) CloseOverlayThen(() => world.TravelTo(point.Place, entrance.arrivalId));
            }
            else
            {
                CloseOverlayThen(() => world.TravelToMapPoint(point.Place, point.X));
            }
        }

        static IslandMapLink MapEntrance(MapPoint point)
        {
            return placeCatalog == null ? null : Array.Find(placeCatalog.routes, route =>
                route.from == point.EntranceFrom && route.to == point.Place);
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
            colors.pressedColor = new Color(.85f, .78f, .98f, .30f);
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
