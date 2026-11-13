#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.UIElements;
using UButton = UnityEngine.UI.Button;
using Object = UnityEngine.Object;

namespace Seabreeze.Editor
{
    [InitializeOnLoad]
    public static class OpeningInteractionVerification
    {
        const string Key="Seabreeze.OpeningInteractionVerification";
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        static readonly Vector2Int[] Sizes={new Vector2Int(1280,800),new Vector2Int(800,600),new Vector2Int(1920,1080),new Vector2Int(2560,1080),new Vector2Int(640,900)};
        static JArray checks=new JArray(),errors=new JArray();
        static bool capturedMotion;
        static int stage,index,entered;static double next,deadline;static string output;
        static StartPageView title;static FirstDayOverlay letter;static ZLive.ArrivalGiftView gift;static UIDocument document;
        static OpeningInteractionVerification(){EditorApplication.update+=Tick;Application.logMessageReceived+=(m,s,t)=>{if(SessionState.GetBool(Key,false)&&t==LogType.Exception&&!s.Contains("UnityEditor.Search."))errors.Add(m);};}
        public static void Run(){output="/private/tmp/z-opening-interaction-evidence";Directory.CreateDirectory(output);SessionState.SetBool(Key,true);SessionState.SetString(Key+"Output",output);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);SetSize(Sizes[0]);EditorApplication.EnterPlaymode();}
        static void SetSize(Vector2Int size){
            var assembly=typeof(UnityEditor.Editor).Assembly;var sizesType=assembly.GetType("UnityEditor.GameViewSizes");
            var single=typeof(ScriptableSingleton<>).MakeGenericType(sizesType).GetProperty("instance",BindingFlags.Public|BindingFlags.Static|BindingFlags.FlattenHierarchy).GetValue(null);
            var groupType=sizesType.GetProperty("currentGroupType",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(single);
            var group=sizesType.GetMethod("GetGroup").Invoke(single,new[]{groupType});
            var sizeType=assembly.GetType("UnityEditor.GameViewSize");var mode=Enum.ToObject(assembly.GetType("UnityEditor.GameViewSizeType"),1);
            var entry=Activator.CreateInstance(sizeType,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{mode,size.x,size.y,"Opening verification"},null);
            group.GetType().GetMethod("AddCustomSize").Invoke(group,new[]{entry});
            int count=(int)group.GetType().GetMethod("GetTotalCount").Invoke(group,null);
            var view=EditorWindow.GetWindow(assembly.GetType("UnityEditor.GameView"));view.Show();view.Focus();
            view.GetType().GetProperty("selectedSizeIndex",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).SetValue(view,count-1);view.Repaint();
        }
        static void Require(bool ok,string label){if(!ok)throw new Exception(label);checks.Add(label);}
        static void Capture(string name){
            Canvas.ForceUpdateCanvases();
            var type=typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");var view=EditorWindow.GetWindow(type);
            var field=type.BaseType.GetField("m_TargetTexture",BindingFlags.Instance|BindingFlags.NonPublic);
            var render=field?.GetValue(view) as RenderTexture;
            if(render==null)throw new Exception("GameView render texture unavailable");
            var previous=RenderTexture.active;RenderTexture.active=render;
            var texture=new Texture2D(render.width,render.height,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,render.width,render.height),0,0);texture.Apply();
            // Metal GameView render targets use a top-origin texture; normalize exported evidence.
            if(SystemInfo.graphicsUVStartsAtTop){var pixels=texture.GetPixels32();int width=texture.width,height=texture.height;for(int y=0;y<height/2;y++)for(int x=0;x<width;x++){int a=y*width+x,b=(height-1-y)*width+x;var value=pixels[a];pixels[a]=pixels[b];pixels[b]=value;}texture.SetPixels32(pixels);texture.Apply();}
            RenderTexture.active=previous;File.WriteAllBytes(Path.Combine(output,name+".png"),texture.EncodeToPNG());Object.Destroy(texture);
        }
        static Rect Bounds(RectTransform rect){var corners=new Vector3[4];rect.GetWorldCorners(corners);var a=RectTransformUtility.WorldToScreenPoint(null,corners[0]);var b=RectTransformUtility.WorldToScreenPoint(null,corners[2]);return Rect.MinMaxRect(a.x,a.y,b.x,b.y);}
        static void CheckCard(RectTransform card){Canvas.ForceUpdateCanvases();var r=Bounds(card);var viewport=card.GetComponentInParent<Canvas>().pixelRect;Require((int)viewport.width==Sizes[index].x&&(int)viewport.height==Sizes[index].y,"Requested viewport is rendered");Require(r.xMin>=15&&r.yMin>=15&&r.xMax<=viewport.width-15&&r.yMax<=viewport.height-15,"Modal fits "+viewport.width+"x"+viewport.height+" bounds="+r+" scale="+card.localScale+" canvas="+card.GetComponentInParent<Canvas>().pixelRect);
            foreach(var b in card.GetComponentsInChildren<UButton>()){var bnd=Bounds((RectTransform)b.transform);Require(bnd.xMin>=r.xMin&&bnd.xMax<=r.xMax&&bnd.yMin>=r.yMin&&bnd.yMax<=r.yMax,"Button remains inside card: "+b.name);}
            foreach(var text in card.GetComponentsInChildren<TextMeshProUGUI>()){text.ForceMeshUpdate();Require(!text.isTextOverflowing,"Text fits: "+text.name);}
        }
        static void Tick(){
            if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
            try{
                output=SessionState.GetString(Key+"Output","");if(deadline==0)deadline=EditorApplication.timeSinceStartup+130;if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Opening verification timeout stage "+stage);
                if(EditorApplication.timeSinceStartup<next)return;
                if(stage==0){var camera=new GameObject("Preview camera").AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.11f,.17f);
                    new GameObject("EventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule));UIPrimitives.Font=Resources.Load<TMP_FontAsset>("Fonts/SeabreezeChinese");
                    title=new GameObject("Title preview").AddComponent<StartPageView>();title.Initialize(false,null,_=>entered++);typeof(StartPageView).GetMethod("Choose",Private).Invoke(title,new object[]{true});stage=1;next=EditorApplication.timeSinceStartup+1;return;}
                if(stage==1){CheckCard(GameObject.Find("Title modal").GetComponent<RectTransform>());Capture("name-"+Sizes[index].x+"x"+Sizes[index].y);if(++index<Sizes.Length){SetSize(Sizes[index]);next=EditorApplication.timeSinceStartup+.6;return;}
                    var field=Object.FindFirstObjectByType<TMP_InputField>();Require(!field.richText,"Name input retains IME-safe plain text");field.text="小海";GameObject.Find("Confirm player name").GetComponent<UButton>().onClick.Invoke();Require(entered==1&&PlayerIdentity.PendingName=="小海","Name confirmation calls entry exactly once with the entered name");Object.Destroy(title.gameObject);stage=2;index=0;SetSize(Sizes[0]);next=EditorApplication.timeSinceStartup+.6;return;}
                if(stage==2){letter=new GameObject("Letter preview").AddComponent<FirstDayOverlay>();letter.Initialize();letter.Show("月亮岛七天体验邀请","恭喜小海获得第1000场直播的登岛名额。来月亮岛和 Z 一起生活七天，听海风，认识新朋友。","确认登岛",()=>{});stage=3;next=EditorApplication.timeSinceStartup+.5;return;}
                if(stage==3){CheckCard(GameObject.Find("A letter from the island").GetComponent<RectTransform>());Capture("invitation-"+Sizes[index].x+"x"+Sizes[index].y);if(++index<Sizes.Length){SetSize(Sizes[index]);next=EditorApplication.timeSinceStartup+.6;return;}Object.Destroy(letter.gameObject);SetSize(Sizes[0]);stage=4;next=EditorApplication.timeSinceStartup+.6;return;}
                if(stage==4){document=new GameObject("Gift preview").AddComponent<UIDocument>();var settings=ScriptableObject.CreateInstance<PanelSettings>();settings.scaleMode=PanelScaleMode.ScaleWithScreenSize;settings.referenceResolution=new Vector2Int(1280,800);settings.themeStyleSheet=Resources.Load<ThemeStyleSheet>("LiveTheme");document.panelSettings=settings;document.rootVisualElement.style.unityFont=Resources.Load<Font>("Fonts/NotoSansSC-Regular");gift=new ZLive.ArrivalGiftView(document.rootVisualElement,"小海");stage=5;next=EditorApplication.timeSinceStartup+.6;return;}
                if(stage>=5)gift.Tick(0,0);
                if(stage==5){if(!gift.Ready)return;gift.Tick(180,180);gift.Collect();Require(!gift.Opened&&!gift.Collected,"Waiting three minutes cannot draw or collect automatically");Capture("gift-awaiting-click");gift.ActivateGift();gift.ActivateGift();Require(gift.Opened&&!gift.Collected,"Click starts the original video exactly once");stage=6;return;}
                if(stage==6){if(!capturedMotion&&gift.PlaybackTime>=5.0&&gift.PlaybackTime<5.4){Capture("gift-opening-motion");capturedMotion=true;}if(!gift.Revealed)return;Require(gift.PlayerName=="小海","Original invitation uses the actual player name");Require(gift.PlaybackTime>=8.8&&gift.PlaybackTime<9,"Reference invitation pauses at 8.80 seconds");stage=7;next=EditorApplication.timeSinceStartup+.6;return;}
                if(stage==7){Capture("gift-player-result");Require(!gift.Collected,"Visible invitation still waits for explicit collection");gift.Collect();Require(!gift.Collected,"Collect plays the original outro before completion");stage=8;return;}
                if(stage==8){if(!gift.Collected)return;Capture("gift-collected");Require(gift.Collected,"Original outro completes the explicit collection");gift.Dispose();Finish(true);}

            }catch(Exception e){errors.Add(e.ToString());Finish(false);}
        }
        static void Finish(bool ok){SessionState.SetBool(Key,false);File.WriteAllText(Path.Combine(output,"report.json"),new JObject{["passed"]=ok&&errors.Count==0,["checks"]=checks,["errors"]=errors,["scope"]="Actual Unity layouts and event handlers; backend/model not substituted into a full story run."}.ToString());EditorApplication.Exit(ok&&errors.Count==0?0:1);}
    }
}
#endif
