using System;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using UnityEngine.Video;
using Seabreeze;
using Object=UnityEngine.Object;
using Image=UnityEngine.UIElements.Image;
using Button=UnityEngine.UIElements.Button;

namespace ZLive
{
    // The supplied clickable HTML is a video state machine. Keep its clip and cue times intact.
    public sealed class ArrivalGiftView : IDisposable
    {
        public readonly VisualElement Root;
        readonly VisualElement stage, giftHit;
        readonly Button action;
        readonly VideoPlayer player;
        readonly GameObject host, overlayHost;
        readonly RenderTexture video, composite, names;
        readonly Material material;
        readonly JArray tracking;
        string phase="intro", failure;
        bool prepared, suspended, collectRequested;
        public bool Ready => phase=="idle";
        public bool Opened {get;private set;}
        public bool Revealed => phase=="reward"||phase=="collect"||phase=="done";
        public bool Collected {get;private set;}
        public double PlaybackTime => player!=null?player.time:0;
        public string PlayerName {get;}
        public ArrivalGiftView(VisualElement parent,string playerName)
        {
            PlayerName=playerName;
            Root=new VisualElement{name="arrival-gift-overlay"};
            Root.style.position=Position.Absolute;Root.style.left=Root.style.right=Root.style.top=Root.style.bottom=0;
            Root.style.backgroundColor=new Color(.067f,.075f,.118f);Root.style.alignItems=Align.Center;Root.style.justifyContent=Justify.Center;
            stage=new VisualElement{name="arrival-video-stage"};stage.style.position=Position.Relative;Root.Add(stage);
            video=Target("Arrival original video");composite=Target("Arrival named video");names=Target("Arrival dynamic text");
            var screen=new Image{name="arrival-reference-video",image=composite,scaleMode=ScaleMode.StretchToFill};
            screen.style.position=Position.Absolute;screen.style.left=screen.style.right=screen.style.top=screen.style.bottom=0;screen.pickingMode=PickingMode.Ignore;stage.Add(screen);
            giftHit=new VisualElement{name="arrival-gift"};giftHit.style.position=Position.Absolute;giftHit.style.left=Length.Percent(25);giftHit.style.top=Length.Percent(26);giftHit.style.width=Length.Percent(30);giftHit.style.height=Length.Percent(48);giftHit.RegisterCallback<ClickEvent>(_=>ActivateGift());stage.Add(giftHit);
            action=new Button(()=>{if(Ready)ActivateGift();else Collect();}){name="arrival-gift-action",text="点击开启"};
            action.style.position=Position.Absolute;action.style.left=Length.Percent(32.2f);action.style.top=Length.Percent(83);action.style.width=Length.Percent(15.6f);action.style.height=Length.Percent(8.4f);
            action.style.color=Color.white;action.style.backgroundColor=new Color(.19f,.16f,.22f);
            action.style.borderTopLeftRadius=action.style.borderTopRightRadius=action.style.borderBottomLeftRadius=action.style.borderBottomRightRadius=100;
            action.style.borderTopColor=action.style.borderRightColor=action.style.borderBottomColor=action.style.borderLeftColor=new Color(.94f,.84f,1);
            action.style.borderTopWidth=action.style.borderRightWidth=action.style.borderBottomWidth=action.style.borderLeftWidth=1.5f;stage.Add(action);
            Root.RegisterCallback<GeometryChangedEvent>(e=>{float scale=Mathf.Min(e.newRect.width/1280,e.newRect.height/778);stage.style.width=1280*scale;stage.style.height=778*scale;action.style.fontSize=Mathf.Clamp(1280*scale*.018f,12,25);});
            // The clip already contains the exact glowing button: use a transparent native hit target.
            action.style.color=Color.clear;action.style.backgroundColor=Color.clear;
            action.style.borderTopWidth=action.style.borderRightWidth=action.style.borderBottomWidth=action.style.borderLeftWidth=0;
            parent.Add(Root);ShowAction(false);
            material=new Material(Resources.Load<Shader>("FirstDay/ArrivalNameComposite"));material.SetTexture("_Overlay",names);
            tracking=(JArray)JObject.Parse(Resources.Load<TextAsset>("FirstDay/ArrivalTracking").text)["frames"];
            overlayHost=CreateNameLayer(playerName);host=new GameObject("Original arrival animation");
            player=host.AddComponent<VideoPlayer>();player.playOnAwake=false;player.isLooping=false;player.waitForFirstFrame=true;player.skipOnDrop=false;
            player.audioOutputMode=VideoAudioOutputMode.None;player.renderMode=VideoRenderMode.RenderTexture;player.targetTexture=video;
            player.clip=Resources.Load<VideoClip>("FirstDay/ArrivalReference");
            player.prepareCompleted+=_=>{prepared=true;if(!suspended)player.Play();};player.errorReceived+=(_,error)=>failure=error;
            player.loopPointReached+=_=>{if(phase=="collect"){phase="done";Collected=true;}};player.Prepare();
        }
        static RenderTexture Target(string name){var texture=new RenderTexture(1280,778,24,RenderTextureFormat.ARGB32){name=name};texture.Create();return texture;}
        GameObject CreateNameLayer(string value)
        {
            var root=new GameObject("Arrival name surface");root.layer=31;
            var camera=new GameObject("Name surface camera").AddComponent<Camera>();camera.transform.SetParent(root.transform);camera.transform.localPosition=new Vector3(0,0,-10);camera.orthographic=true;camera.orthographicSize=389;camera.aspect=1280f/778;camera.nearClipPlane=.1f;camera.farClipPlane=30;camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;camera.targetTexture=names;camera.enabled=false;
            var canvasObject=new GameObject("Name surface canvas",typeof(RectTransform),typeof(Canvas));canvasObject.layer=31;canvasObject.transform.SetParent(root.transform,false);
            var canvas=canvasObject.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=camera;((RectTransform)canvas.transform).sizeDelta=new Vector2(1280,778);
            var paper=Resources.Load<Texture2D>("FirstDay/ArrivalNamePaper");
            float nameWidth=Mathf.Clamp(value.Length*27+12,130,350);
            material.SetVector("_NameRect",new Vector4(344,357,nameWidth+16,48));
            Patch(canvas.transform,paper,new Rect(344,357,nameWidth+16,48));Patch(canvas.transform,paper,new Rect(172,509,512,52));
            // Reuse a blank patch of the existing paper; replace only the baked greeter value.
            var greeter=Rect(canvas.transform,"Greeter paper",new Rect(320,443,90,44)).gameObject.AddComponent<RawImage>();
            greeter.texture=paper;greeter.uvRect=new Rect(320f/1280,1-520f/778,90f/1280,44f/778);greeter.raycastTarget=false;
            Text(canvas.transform,"arrival-greeter","Z",new Rect(327,445,76,37),26,26);
            Text(canvas.transform,"arrival-winner-name",value,new Rect(351,363,nameWidth,37),26,21);
            Text(canvas.transform,"arrival-personal-message","码头的灯会为你亮着。"+value+"，我们岛上见。",new Rect(181,518,495,34),21,14);
            Canvas.ForceUpdateCanvases();foreach(var text in canvas.GetComponentsInChildren<TextMeshProUGUI>())text.ForceMeshUpdate();camera.Render();root.SetActive(false);return root;
        }
        static RectTransform Rect(Transform parent,string name,Rect area){var go=new GameObject(name,typeof(RectTransform));go.layer=31;var r=go.GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(area.x,-area.y);r.sizeDelta=area.size;return r;}
        static void Patch(Transform parent,Texture2D paper,Rect area){var raw=Rect(parent,"Repaired original paper",area).gameObject.AddComponent<RawImage>();raw.texture=paper;raw.uvRect=new Rect(area.x/1280,1-(area.y+area.height)/778,area.width/1280,area.height/778);raw.raycastTarget=false;}
        static void Text(Transform parent,string name,string value,Rect area,float max,float min){var text=Rect(parent,name,area).gameObject.AddComponent<TextMeshProUGUI>();text.font=UIPrimitives.Font!=null?UIPrimitives.Font:Resources.Load<TMP_FontAsset>("Fonts/SeabreezeChinese");text.text=value;text.richText=false;text.fontSize=max;text.enableAutoSizing=true;text.fontSizeMax=max;text.fontSizeMin=min;text.fontStyle=FontStyles.Bold;text.color=new Color(.075f,.14f,.20f);text.alignment=TextAlignmentOptions.MidlineLeft;text.textWrappingMode=TextWrappingModes.NoWrap;text.raycastTarget=false;}
        void ShowAction(bool show){action.style.display=show?DisplayStyle.Flex:DisplayStyle.None;giftHit.style.display=phase=="idle"?DisplayStyle.Flex:DisplayStyle.None;}
        public void SetPaused(bool value){if(suspended==value)return;suspended=value;if(!prepared)return;if(value)player.Pause();else if(phase=="intro"||phase=="opening"||phase=="collect")player.Play();}
        public void ActivateGift(){if(!Ready)return;Opened=true;phase="opening";ShowAction(false);player.time=1.75;if(!suspended)player.Play();}
        public void Collect(){if(phase!="reward"||collectRequested)return;collectRequested=true;phase="collect";ShowAction(false);player.time=9.60;if(!suspended)player.Play();}
        public void Tick(float idle,float animation)
        {
            if(failure!=null)throw new InvalidOperationException("Arrival video: "+failure);
            if(!prepared)return;
            // The user removed replay: end before the baked-in replay button appears.
            if(phase=="collect"&&player.time>=10.20){player.Pause();phase="done";Collected=true;ShowAction(false);}
            if(phase=="intro"&&player.time>=1.30){player.Pause();phase="idle";action.text="点击开启";ShowAction(true);action.Focus();}
            if(phase=="opening"&&player.time>=8.80){player.Pause();phase="reward";action.text="收下邀请";ShowAction(true);action.Focus();}
            JToken best=null;double delta=double.MaxValue;foreach(var frame in tracking){double d=Math.Abs((double)frame["t"]-player.time);if(d<delta){delta=d;best=frame;}}
            bool visible=best!=null&&(bool)best["valid"];float opacity=player.time>9.70?Mathf.Clamp01((10.20f-(float)player.time)/.50f):Mathf.Clamp01(((float)player.time-4.50f)/.35f);material.SetFloat("_Visible",visible?opacity:0);
            if(visible){var h=(JArray)best["h"];var matrix=Matrix4x4.identity;matrix.m00=(float)h[0];matrix.m01=(float)h[1];matrix.m03=(float)h[2];matrix.m10=(float)h[3];matrix.m11=(float)h[4];matrix.m13=(float)h[5];matrix.m30=(float)h[6];matrix.m31=(float)h[7];matrix.m33=(float)h[8];var inverse=matrix.inverse;material.SetVector("_H0",new Vector4(inverse.m00,inverse.m01,inverse.m03,0));material.SetVector("_H1",new Vector4(inverse.m10,inverse.m11,inverse.m13,0));material.SetVector("_H2",new Vector4(inverse.m30,inverse.m31,inverse.m33,0));}
            Graphics.Blit(video,composite,material);
        }
        public void Dispose(){Root.RemoveFromHierarchy();if(player!=null)player.Stop();Object.Destroy(host);Object.Destroy(overlayHost);Object.Destroy(material);foreach(var t in new[]{video,composite,names}){t.Release();Object.Destroy(t);}}
    }
}
