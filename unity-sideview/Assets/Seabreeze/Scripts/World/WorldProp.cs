using UnityEngine;

namespace Seabreeze
{
    public class WorldProp : MonoBehaviour
    {
        Transform visual;
        SpriteRenderer glint;
        Interactable marker;
        void Start()
        {
            marker = GetComponent<Interactable>();
            if (marker == null || marker.npc || marker.isDoor)
                return;
            visual = new GameObject("Independent prop artwork").transform;
            visual.SetParent(transform, false);
            Color ink = new Color(.13f, .2f, .29f), wood = new Color(.43f, .32f, .24f), paper = new Color(.9f, .83f, .65f), metal = new Color(.26f, .38f, .46f);
            switch (marker.id)
            {
                case "postcard":
                    Rect("Postcard", new Vector2(0, 1.37f), new Vector2(.5f, .24f), paper);
                    Rect("Stamp", new Vector2(.15f, 1.39f), new Vector2(.09f, .1f), metal);
                    break;
                case "letterbox":
                    break;
                case "noticeboard":
                    Rect("Timber post", new Vector2(0, .6f), new Vector2(.1f, 1.2f), wood);
                    Rect("Timber frame", new Vector2(0, 1.4f), new Vector2(.95f, .85f), wood);
                    Rect("Paper notices", new Vector2(0, 1.4f), new Vector2(.8f, .69f), paper);
                    for (int i = 0; i < 3; i++)
                        Rect("Handwritten line", new Vector2(-.03f, 1.55f - i * .15f), new Vector2(.5f, .016f), metal);
                    break;
                case "bicycle":
                    Wheel(new Vector2(-.43f, .4f), ink);
                    Wheel(new Vector2(.43f, .4f), ink);
                    Bar(new Vector2(-.43f, .4f), new Vector2(-.12f, .83f), metal);
                    Bar(new Vector2(-.12f, .83f), new Vector2(.14f, .4f), metal);
                    Bar(new Vector2(.14f, .4f), new Vector2(-.43f, .4f), metal);
                    Bar(new Vector2(-.12f, .83f), new Vector2(.33f, .84f), metal);
                    Bar(new Vector2(.33f, .84f), new Vector2(.14f, .4f), metal);
                    Bar(new Vector2(.33f, .84f), new Vector2(.43f, .4f), metal);
                    Rect("Saddle", new Vector2(-.13f, .88f), new Vector2(.28f, .06f), wood);
                    break;
                case "bench":
                case "lighthouse_bench":
                    break;
            }

            glint = WorldArt.Sprite("Interaction glint", WorldArt.Oval, new Vector2(0, marker.id == "postcard" ? 1.7f : 1.15f), new Vector2(.065f, .065f), new Color(1, .84f, .55f, .7f), 35, visual);
        }

        void Rect(string n, Vector2 p, Vector2 size, Color c)
        {
            WorldArt.Sprite(n, WorldArt.Pixel, p, size, c, 20, visual);
        }

        void Wheel(Vector2 p, Color c)
        {
            var r = WorldArt.Sprite("Bicycle wheel rim", WorldArt.Oval, p, Vector2.one * .65f, c, 20, visual);
            WorldArt.Sprite("Wheel center", WorldArt.Oval, p, Vector2.one * .54f, new Color(.44f, .5f, .53f, .45f), 21, visual);
        }

        void Bar(Vector2 a, Vector2 b, Color c)
        {
            var r = WorldArt.Sprite("Bicycle frame", WorldArt.Pixel, (a + b) * .5f, new Vector2(Vector2.Distance(a, b), .035f), c, 22, visual);
            r.transform.localEulerAngles = new Vector3(0, 0, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
        }

        void Update()
        {
            if (marker != null && marker.id == "postcard" && GameSession.Instance != null && GameSession.Instance.HasFlag("postcard_found"))
            {
                foreach (var r in visual.GetComponentsInChildren<SpriteRenderer>())
                    r.enabled = false;
            }

            if (glint == null)
                return;
            var c = glint.color;
            c.a = .35f + Mathf.Sin(Time.time * 2 + transform.position.x) * .25f;
            glint.color = c;
        }
    }
}
