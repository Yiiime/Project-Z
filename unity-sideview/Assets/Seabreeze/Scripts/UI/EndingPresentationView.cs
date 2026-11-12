using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Seabreeze.UIPrimitives;

namespace Seabreeze
{
    /// <summary>
    /// Whole-game closing sequence. The caller owns time, skip confirmation and completion.
    /// Two independent cards dissolve; neither artwork nor album files are modified.
    /// </summary>
    public sealed class EndingPresentationView : MonoBehaviour
    {
        public const float Duration = 72f;
        const float Dissolve = 1.5f;
        static readonly Color Paper = new Color(.94f, .94f, .89f);
        static readonly Color Quiet = new Color(.64f, .73f, .76f);
        static readonly Color Gold = new Color(.80f, .69f, .47f);

        sealed class Shot
        {
            public float Start, End;
            public string Role, Names, Caption, Subtext, AlbumPath;
            public Texture2D Texture;
            public bool PhotoRight, FullBleed;
        }
        sealed class Card
        {
            public RectTransform Root, Photo, Words;
            public CanvasGroup Group;
            public RawImage Image;
            public Texture2D OwnedTexture;
            public int Index = -1;
        }

        readonly List<Shot> shots = new List<Shot>();
        readonly Card[] cards = new Card[2];
        RectTransform canvas, stage;
        Texture2D veil;
        float renderedTime;

        public void Initialize(RectTransform parent, PhoneSaveState album)
        {
            canvas = parent;
            stage = Rect("Ending film stage", parent, Vector2.one * .5f, Vector2.one * .5f, Vector2.zero, new Vector2(1920, 1080));
            var art = Resources.Load<EndingPresentationConfig>("UI/EndingPresentation");
            var fallback = Resources.Load<StartPageConfig>("UI/StartPage")?.background;
            Texture2D Coast(Texture2D image) => image != null ? image : fallback;
            Add(0, 8, "A STORY OF ENCOUNTERS", "来自月亮的 Z", "海风，记得每一次相遇。", "一段关于陪伴的故事", Coast(art?.harbor), false, true);
            Add(8, 18, "共同创作  /  CREATED TOGETHER", "李霆懿    郑绎    杨力源\n刘洋    代子晴    周泓杰", "海滨木质栈道", "把寻常的日子，写成我们的故事。", Coast(art?.boardwalk));
            Add(18, 26, "技术总监  /  TECHNICAL DIRECTOR", "余星亮", "咖啡店", "一盏灯，一扇窗，一个可以停留的地方。", Coast(art?.cafe), true);
            Add(26, 34, "友情支持  /  WITH THE SUPPORT OF", "阮黎光    程路维\n夏雨欣", "古树广场", "那些被认真听见的话，仍在这里回响。", Coast(art?.square));
            Add(34, 42, "游戏指导  /  GAME ADVISOR", "卫彤", "森林小径", "沿着小路，慢慢走。", Coast(art?.forest), true);
            Add(42, 50, "特别鸣谢  /  SPECIAL THANKS", "祝青青", "海滨街", "谢谢你，让每一次相遇都有回声。", Coast(art?.seafront));

            // Limit disk decoding to two photographs and prefer the player's favourites.
            // Stored screenshots retain their original UI/colour; these are not staged game captures.
            var photos = SelectPhotos(album);
            Add(50, 56, "留在心里的片刻", "有些风景，\n因为你而不同。", "山顶平台", "岛屿印象", Coast(art?.summit), true);
            Add(56, 62, "留在心里的片刻", "有些相遇，\n会陪我们很久。", "海滨木质栈道", "岛屿印象", Coast(art?.boardwalk));
            for (int i = 0; i < photos.Count; i++)
            {
                shots[6 + i].AlbumPath = PhotoCaptureService.ResolvePath(album, photos[i].fileName);
                shots[6 + i].Caption = "你留下的照片";
                shots[6 + i].Subtext = "这一次，把回忆留给自己。";
            }
            Add(62, Duration, "来自月亮的 Z", "谢谢你，来到这里。", "", "希望在这个越来越快速的时代，\n找回一些原初的情感联系和交流。", Coast(art?.harbor), false, true);
            veil = MakeVeil();
            Render(0);
        }

        void Add(float start, float end, string role, string names, string caption, string subtext, Texture2D texture, bool right = false, bool full = false)
            => shots.Add(new Shot { Start = start, End = end, Role = role, Names = names, Caption = caption, Subtext = subtext, Texture = texture, PhotoRight = right, FullBleed = full });

        static List<PhoneSavedPhoto> SelectPhotos(PhoneSaveState album)
        {
            var result = new List<PhoneSavedPhoto>();
            if (album?.photos == null) return result;
            for (int pass = 0; pass < 2 && result.Count < 2; pass++)
                for (int i = album.photos.Count - 1; i >= 0 && result.Count < 2; i--)
                {
                    var p = album.photos[i];
                    if (p == null || result.Contains(p) || (pass == 0 && album.favoriteMemoryIds?.Contains(p.id) != true)) continue;
                    string path = PhotoCaptureService.ResolvePath(album, p.fileName);
                    if (!string.IsNullOrEmpty(path) && File.Exists(path)) result.Add(p);
                }
            // Show memories in capture order rather than suggesting the newest was the earliest.
            result.Reverse();
            return result;
        }

        public void Render(float time)
        {
            if (stage == null || shots.Count == 0) return;
            renderedTime = Mathf.Clamp(time, 0, Duration);
            float scale = Mathf.Min(canvas.rect.width / 1920f, canvas.rect.height / 1080f);
            stage.localScale = Vector3.one * Mathf.Max(.01f, scale);
            int index = shots.Count - 1;
            for (int i = 0; i < shots.Count; i++) if (renderedTime < shots[i].End) { index = i; break; }
            int current = index % 2, previous = 1 - current;
            if (cards[current]?.Index != index) Build(current, index);
            float blend = Mathf.SmoothStep(0, 1, (renderedTime - shots[index].Start) / Dissolve);
            cards[current].Group.alpha = blend;
            Animate(cards[current], shots[index]);
            if (cards[previous] != null)
            {
                cards[previous].Group.alpha = cards[previous].Index == index - 1 && blend < 1 ? 1 : 0;
                if (cards[previous].Group.alpha > 0) Animate(cards[previous], shots[cards[previous].Index]);
            }
            float fadeOut = 1 - Mathf.SmoothStep(0, 1, (renderedTime - (Duration - 1.6f)) / 1.6f);
            if (index == shots.Count - 1) cards[current].Group.alpha *= fadeOut;
        }

        void Build(int slot, int index)
        {
            Release(cards[slot]);
            var shot = shots[index];
            var rt = Rect("Ending shot " + index, stage, Vector2.one * .5f, Vector2.one * .5f, Vector2.zero, new Vector2(1920, 1080));
            var card = new Card { Root = rt, Group = rt.gameObject.AddComponent<CanvasGroup>(), Index = index };
            cards[slot] = card;
            card.Group.blocksRaycasts = false; card.Group.interactable = false;
            var black = Stretch("Ink", rt, Vector2.zero, Vector2.zero).gameObject.AddComponent<Image>();
            black.color = new Color(.018f, .035f, .055f); black.raycastTarget = false;
            float photoX = shot.PhotoRight ? 826 : 84;
            card.Photo = Rect("Island photograph", rt, Vector2.zero, Vector2.zero,
                shot.FullBleed ? Vector2.zero : new Vector2(photoX, 230), shot.FullBleed ? new Vector2(1920,1080) : new Vector2(1010,620));
            card.Image = card.Photo.gameObject.AddComponent<RawImage>();
            card.Image.raycastTarget = false; card.Image.texture = shot.Texture;
            if (!string.IsNullOrEmpty(shot.AlbumPath))
            {
                card.OwnedTexture = LoadPhotograph(shot.AlbumPath);
                if (card.OwnedTexture != null) card.Image.texture = card.OwnedTexture;
            }
            card.Image.color = shot.FullBleed ? new Color(.52f,.59f,.65f,1) : Color.white;
            if (shot.FullBleed)
            {
                var gradient = Stretch("Film shadow", rt, Vector2.zero, Vector2.zero).gameObject.AddComponent<RawImage>();
                gradient.texture = veil; gradient.raycastTarget = false;
                Label("Film mark", rt, Vector2.up, Vector2.up, new Vector2(112,-88), new Vector2(1500,40), shot.Role, index == 0 ? 17 : 23, Gold).characterSpacing = 5;
                Label("Closing title", rt, new Vector2(0,.5f), new Vector2(0,.5f), new Vector2(112,0), new Vector2(1600,110), shot.Names, index == 0 ? 78 : 62, Paper);
                Line(rt, Vector2.zero, Vector2.zero, new Vector2(116,350), new Vector2(62,1), Gold);
                Label("Closing thought", rt, Vector2.zero, Vector2.zero, new Vector2(112,180), new Vector2(1500,125), shot.Subtext, 28, Paper).lineSpacing = 14;
                Label("Closing caption", rt, Vector2.zero, Vector2.zero, new Vector2(112,105), new Vector2(1500,32), shot.Caption, 19, Quiet);
            }
            else
            {
                Label("Film mark", rt, Vector2.up, Vector2.up, new Vector2(84,-88), new Vector2(1500,35), "来自月亮的 Z   /   END CREDITS", 16, Quiet).characterSpacing = 3;
                Line(rt, Vector2.up, Vector2.up, new Vector2(84,-145), new Vector2(1752,1), new Color(.47f,.60f,.64f,.21f));
                string caption = card.OwnedTexture != null ? shot.Caption : (index >= 6 ? "岛屿印象" : shot.Caption);
                Label("Photograph caption", rt, Vector2.zero, Vector2.zero, new Vector2(photoX,180), new Vector2(1010,30), $"{index:00}    /    {caption}", 17, Quiet).characterSpacing = 2;
                float textX = shot.PhotoRight ? 112 : 1190;
                card.Words = Rect("Cast typography", rt, Vector2.zero, Vector2.zero, new Vector2(textX,280), new Vector2(610,490));
                Label("Role", card.Words, Vector2.up, Vector2.up, Vector2.zero, new Vector2(610,72), shot.Role, 18, Gold).characterSpacing = 1.3f;
                Line(card.Words, Vector2.up, Vector2.up, new Vector2(0,-110), new Vector2(46,1), Gold);
                var names = Label("Names", card.Words, Vector2.up, Vector2.up, new Vector2(0,-151), new Vector2(610,150), shot.Names, index == 1 ? 31 : 40, Paper);
                names.lineSpacing = 22;
                Label("Cast thought", card.Words, Vector2.up, Vector2.up, new Vector2(0,-354), new Vector2(570,90),
                    card.OwnedTexture == null && index >= 6 ? "岛屿印象" : shot.Subtext, 20, Quiet).lineSpacing = 12;
            }
        }

        void Animate(Card card, Shot shot)
        {
            var image = card.Image;
            if (image.texture == null) return;
            float progress = Mathf.Clamp01((renderedTime - shot.Start) / (shot.End - shot.Start));
            float aspect = (float)image.texture.width / image.texture.height;
            float display = card.Photo.rect.width / card.Photo.rect.height;
            float zoom = Mathf.Lerp(1.035f, 1.075f, progress);
            // Album screenshots are fit in full so their composition and embedded UI are not cut off.
            if (card.OwnedTexture != null)
            {
                float width = 1010, height = width / aspect;
                if (height > 620) { height = 620; width = height * aspect; }
                card.Photo.sizeDelta = new Vector2(width,height);
                card.Photo.anchoredPosition = new Vector2((shot.PhotoRight ? 826 : 84) + (1010-width)*.5f,230+(620-height)*.5f);
                image.uvRect = new Rect(0,0,1,1); return;
            }
            float w = Mathf.Min(1, display / aspect) / zoom, h = Mathf.Min(1, aspect / display) / zoom;
            float pan = Mathf.Lerp(.42f,.58f,shot.PhotoRight ? 1-progress : progress);
            image.uvRect = new Rect((1-w)*pan,(1-h)*.5f,w,h);
        }

        static Texture2D LoadPhotograph(string path)
        {
            Texture2D image = null;
            try
            {
                if (new FileInfo(path).Length > 40 * 1024 * 1024) return null;
                byte[] bytes = File.ReadAllBytes(path);
                // PNG dimensions are checked before the decoder can allocate a huge external image.
                if (bytes.Length < 24 || bytes[0] != 137 || bytes[1] != 80 || bytes[2] != 78 || bytes[3] != 71) return null;
                long Width(int at) => ((long)bytes[at] << 24) | ((long)bytes[at+1] << 16) | ((long)bytes[at+2] << 8) | bytes[at+3];
                long width = Width(16), height = Width(20);
                if (width < 1 || height < 1 || width > 8192 || height > 8192 || width * height > 20000000) return null;
                image = new Texture2D(2,2,TextureFormat.RGBA32,false) { name = "Ending album photograph", hideFlags = HideFlags.DontSave };
                if (image.LoadImage(bytes,true)) return image;
            }
            catch (Exception exception) { Debug.LogWarning("Ending photograph unavailable: " + exception.Message); }
            if (image != null) Destroy(image);
            return null;
        }

        static Texture2D MakeVeil()
        {
            const int size = 64;
            var texture = new Texture2D(size,size,TextureFormat.RGBA32,false) { name="Ending soft vignette",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear,hideFlags=HideFlags.DontSave };
            var pixels = new Color[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float u=x/(float)(size-1),v=y/(float)(size-1);
                float alpha=.12f+.52f*(1-Mathf.SmoothStep(0,1,u))+.25f*(1-v);
                pixels[y*size+x]=new Color(.012f,.025f,.04f,alpha);
            }
            texture.SetPixels(pixels);texture.Apply(false,true);return texture;
        }
        static void Release(Card card)
        {
            if (card == null) return;
            if (card.Root != null) { card.Root.gameObject.SetActive(false); Destroy(card.Root.gameObject); }
            if (card.OwnedTexture != null) Destroy(card.OwnedTexture);
        }
        void OnDestroy()
        {
            foreach (var card in cards) Release(card);
            if (veil != null) Destroy(veil);
            if (stage != null) Destroy(stage.gameObject);
        }
    }
}
