using System;
using UnityEngine;
using UnityEngine.UIElements;
using Seabreeze;
using Image = UnityEngine.UIElements.Image;
using Button = UnityEngine.UIElements.Button;

namespace ZLive
{
    // Native port of the supplied 2026-11-14 invitation. PNG pixels and live text stay sharp.
    public sealed class ArrivalGiftView : IDisposable
    {
        public readonly VisualElement Root;
        readonly VisualElement stage, ticket, sheen;
        readonly Image closed, lid, box;
        readonly Label recipient, message, heading;
        readonly Button action;
        readonly VisualElement[] sparks = new VisualElement[32];
        static ArrivalGiftView warming;
        string phase = "enter";
        bool suspended, disposed;
        float elapsed, total, previous;
        public bool Ready => phase == "idle";
        public bool Opened { get; private set; }
        public bool Revealed => phase == "reward" || phase == "collect" || phase == "done";
        public bool Collected { get; private set; }
        public double PlaybackTime => total;
        public string PlayerName { get; private set; }
        public bool VideoPrepared => !disposed; // Kept for the existing prewarm contract; no decoder required.
        public string Phase => phase;
        public static void Prewarm(string value)
        {
            value = PlayerIdentity.Normalize(value);
            if (string.IsNullOrWhiteSpace(value)) return;
            if (warming == null || warming.disposed) warming = new ArrivalGiftView(new VisualElement(), value, true);
            else warming.SetName(value);
        }
        public static ArrivalGiftView TakePrepared(VisualElement parent, string value)
        {
            Prewarm(value);
            var view = warming ?? new ArrivalGiftView(new VisualElement(), value, true);
            warming = null;
            parent.Add(view.Root); view.SetPaused(false); return view;
        }
        public ArrivalGiftView(VisualElement parent, string playerName, bool prepareOnly = false)
        {
            suspended = prepareOnly; previous = Time.unscaledTime;
            Root = new VisualElement { name = "arrival-gift-overlay" };
            Fill(Root); Root.style.backgroundColor = new Color(.086f, .075f, .141f, .60f);
            Root.style.alignItems = Align.Center; Root.style.justifyContent = Justify.Center; Root.style.overflow = Overflow.Hidden;
            // Fixed design coordinates; scale once at the panel boundary.
            stage = new VisualElement { name = "arrival-native-stage" };
            stage.style.width = 1600; stage.style.height = 972; stage.style.flexShrink = 0;
            Root.Add(stage);
            Root.RegisterCallback<GeometryChangedEvent>(e => stage.transform.scale = Vector3.one * Mathf.Min(e.newRect.width / 1600, e.newRect.height / 972));
            var sprites = Resources.Load<Texture2D>("FirstDay/Invitation20261114/gift-sprites");
            var paper = Resources.Load<Texture2D>("FirstDay/Invitation20261114/invitation");
            if (sprites == null || paper == null) throw new InvalidOperationException("Invitation artwork is missing");
            closed = Sprite(sprites, 0); box = Sprite(sprites, 2); lid = Sprite(sprites, 1);
            for (int i = 0; i < sparks.Length; i++)
            {
                var s = new Label("✦"); s.style.position = Position.Absolute; s.style.fontSize = 12 + i % 5 * 3;
                s.style.color = new Color(1, .91f, .71f); s.pickingMode = PickingMode.Ignore; stage.Add(s); sparks[i] = s;
            }
            ticket = new VisualElement { name = "arrival-invitation" };
            ticket.style.position = Position.Absolute; ticket.style.left = 104; ticket.style.top = 186;
            ticket.style.width = 992; ticket.style.height = 567; ticket.style.overflow = Overflow.Hidden; stage.Add(ticket);
            var art = new Image { image = paper, scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore }; Fill(art); ticket.Add(art);
            Label Detail(string name, string text, float y) { var label = Text(ticket, name, text, 139, y, 490, 46, 29.7f); label.style.color = new Color(.125f, .20f, .255f); label.style.unityFontStyleAndWeight = FontStyle.Bold; return label; }
            Detail("arrival-inviter", "邀 请 人：Z", 238);
            recipient = Detail("arrival-winner-name", "", 286);
            Detail("arrival-island", "前往岛屿：月亮岛", 334);
            Detail("arrival-greeter", "接 待 人：Z", 382);
            message = Text(ticket, "arrival-personal-message", "", 119, 457, 635, 72, 20.8f);
            message.style.color = new Color(.137f, .227f, .282f); message.style.whiteSpace = WhiteSpace.Normal;
            sheen = new VisualElement(); sheen.style.position = Position.Absolute; sheen.style.top = 30; sheen.style.width = 24; sheen.style.height = 500;
            sheen.style.backgroundColor = new Color(1, .97f, .85f, .13f); sheen.transform.rotation = Quaternion.Euler(0, 0, 24); sheen.pickingMode = PickingMode.Ignore; ticket.Add(sheen);
            heading = Text(stage, "arrival-heading", "一份来自月亮岛的惊喜", 160, 150, 880, 70, 48); heading.style.unityTextAlign = TextAnchor.MiddleCenter;
            action = new Button(() => { if (Ready) ActivateGift(); else Collect(); }) { name = "arrival-gift-action", text = "点击开启" };
            action.style.position = Position.Absolute; action.style.left = 465; action.style.top = 807; action.style.width = 270; action.style.height = 76;
            action.style.fontSize = 29; action.style.color = Color.white; action.style.backgroundColor = new Color(.19f, .16f, .23f);
            action.style.borderTopLeftRadius = action.style.borderTopRightRadius = action.style.borderBottomLeftRadius = action.style.borderBottomRightRadius = 100;
            action.style.borderTopColor = action.style.borderRightColor = action.style.borderBottomColor = action.style.borderLeftColor = new Color(.91f, .80f, 1);
            action.style.borderTopWidth = action.style.borderRightWidth = action.style.borderBottomWidth = action.style.borderLeftWidth = 1.5f;
            stage.Add(action); closed.RegisterCallback<ClickEvent>(_ => ActivateGift());
            parent.Add(Root); SetName(playerName); Render();
        }
        static void Fill(VisualElement e) { e.style.position = Position.Absolute; e.style.left = e.style.right = e.style.top = e.style.bottom = 0; }
        static Label Text(VisualElement parent, string name, string value, float x, float y, float width, float height, float size)
        {
            var label = new Label(value) { name = name, pickingMode = PickingMode.Ignore, enableRichText = false };
            label.style.position = Position.Absolute; label.style.left = x; label.style.top = y; label.style.width = width; label.style.height = height; label.style.fontSize = size;
            label.style.color = Color.white; label.style.unityTextAlign = TextAnchor.MiddleLeft; parent.Add(label); return label;
        }
        Image Sprite(Texture2D texture, int index)
        {
            var image = new Image { image = texture, uv = new Rect(index / 3f, 0, 1 / 3f, 1), scaleMode = ScaleMode.StretchToFill };
            image.style.position = Position.Absolute; stage.Add(image); return image;
        }
        void SetName(string value)
        {
            PlayerName = PlayerIdentity.Normalize(value);
            recipient.text = "被邀请人：" + PlayerName;
            // Keep the complete name visible even for the longest permitted player name.
            recipient.style.fontSize = Mathf.Min(29.7f, 480f / Mathf.Max(16, recipient.text.Length));
            message.text = "码头的灯会为你亮着。" + PlayerName + "，我们岛上见。";
        }
        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3 - 2 * t); }
        static void Pose(VisualElement e, float x, float y, float size, float degrees = 0, float opacity = 1)
        {
            e.style.left = x - size / 2; e.style.top = y - size / 2; e.style.width = e.style.height = size;
            e.transform.rotation = Quaternion.Euler(0, 0, degrees); e.style.opacity = opacity;
        }
        void Change(string value) { phase = value; elapsed = 0; if (Ready || phase == "reward") action.Focus(); }
        public void SetPaused(bool value) { if(suspended==value)return; suspended = value; previous = Time.unscaledTime; }
        public void ActivateGift() { if (!Ready) return; Opened = true; Change("charge"); }
        public void Collect() { if (phase == "reward") Change("collect"); }
        public void Tick(float idle, float animation)
        {
            float now = Time.unscaledTime, dt = Mathf.Clamp(now - previous, 0, .1f); previous = now;
            if (suspended || disposed) return;
            elapsed += dt; total += dt;
            if (phase == "enter" && elapsed >= .65f) Change("idle");
            else if (phase == "charge" && elapsed >= 2.4f) Change("opening");
            else if (phase == "opening" && elapsed >= 1.65f) Change("reward");
            else if (phase == "collect" && elapsed >= .5f) { Change("done"); Collected = true; }
            Render();
        }
        void Render()
        {
            bool gift = phase == "enter" || phase == "idle" || phase == "charge";
            closed.style.display = gift ? DisplayStyle.Flex : DisplayStyle.None;
            box.style.display = lid.style.display = phase == "opening" ? DisplayStyle.Flex : DisplayStyle.None;
            ticket.style.display = phase == "opening" || Revealed ? DisplayStyle.Flex : DisplayStyle.None;
            action.style.display = Ready || phase == "reward" ? DisplayStyle.Flex : DisplayStyle.None;
            action.text = Ready ? "点击开启" : "收下邀请";
            heading.style.display = gift ? DisplayStyle.Flex : DisplayStyle.None;
            float power = phase == "charge" ? Smooth(elapsed / 2.4f) : 0;
            float wiggle = Mathf.Sin(elapsed * (9 + power * 20)) * power;
            float scale = phase == "enter" ? Mathf.Max(.001f, 1 + 2.5f * Mathf.Pow(Mathf.Clamp01(elapsed / .65f) - 1, 3) + 1.5f * Mathf.Pow(Mathf.Clamp01(elapsed / .65f) - 1, 2)) : 1;
            Pose(closed, 600 + wiggle * 4, 490 + Mathf.Sin(elapsed * 2.6f) * 4 + (1 - scale) * 60, 560 * scale, wiggle * 1.43f);
            float p = Mathf.Clamp01(elapsed / 1.65f), front = Smooth((p - .12f) / .7f), fly = 1 - Mathf.Pow(1 - Mathf.Clamp01(p / .55f), 3), fade = 1 - Smooth((p - .3f) / .42f);
            if (phase == "opening")
            {
                Pose(box, 600, 580 + 100 * p * p, 550, 0, fade);
                Pose(lid, 600 + fly * 290, 390 - fly * 190 + 120 * p * p, 560, fly * 37.2f, fade);
                ticket.transform.scale = new Vector3(Mathf.Lerp(.12f, 1, front), Mathf.Lerp(.12f, 1, front) * Mathf.Lerp(.24f, 1, front), 1);
                ticket.transform.rotation = Quaternion.Euler(0, 0, Mathf.Lerp(-18, 0, front)); ticket.style.opacity = Smooth(p / .14f);
            }
            else { float collect = phase == "collect" ? Smooth(elapsed / .5f) : Collected ? 1 : 0; ticket.transform.scale = Vector3.one * Mathf.Lerp(1, .86f, collect); ticket.transform.rotation = Quaternion.identity; ticket.style.opacity = 1 - collect; }
            sheen.style.opacity = phase == "reward" && elapsed % 5 > 1 && elapsed % 5 < 2.8f ? .3f : 0;
            sheen.style.left = Mathf.Lerp(-100, 1000, Mathf.Clamp01(((elapsed % 5) - 1) / 1.8f));
            for (int i = 0; i < sparks.Length; i++)
            {
                var s = sparks[i]; bool visible = phase == "charge" || phase == "opening"; s.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                float a = i * 2.399f + elapsed * .5f, progress = (elapsed * .8f + i * .173f) % 1;
                float r = phase == "charge" ? 70 + (1 - progress) * 190 : fly * (180 + i * 67 % 230);
                s.style.left = 600 + Mathf.Cos(a) * r; s.style.top = 535 + Mathf.Sin(a) * r * .65f;
                s.style.opacity = phase == "charge" ? Mathf.Sin(Mathf.PI * progress) * power : fade;
            }
        }
        public void Dispose() { disposed = true; Root.RemoveFromHierarchy(); }
    }
}
