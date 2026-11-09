using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Seabreeze
{
    /// <summary>Local frosted glass for the phone. The rest of the scene remains live and unobscured.</summary>
    public sealed class PhoneSceneGlass : MonoBehaviour
    {
        RectTransform shell,clip;
        RawImage surface;
        RenderTexture snapshot;
        Material blur;
        readonly Vector3[] corners=new Vector3[4];
        Rect lastUv;

        // Call before fading in the phone overlay. Captures once; the overlay must still have zero alpha.
        public IEnumerator Capture(RectTransform target)
        {
            Release();shell=target;
            if(shell==null)yield break;
            if(clip==null)
            {
                // Leave two canvas pixels for the shell's own outline.
                clip=UIPrimitives.Stretch("Phone frosted glass clip",shell,new Vector2(2,2),new Vector2(-2,-2));
                clip.SetAsFirstSibling();
                var shape=clip.gameObject.AddComponent<RoundedPanel>();shape.CornerRadius=54;shape.color=Color.white;shape.raycastTarget=false;shape.SetBorder(Color.clear,0);
                var mask=clip.gameObject.AddComponent<Mask>();mask.showMaskGraphic=false;
                var imageRect=UIPrimitives.Stretch("Blurred scene",clip,Vector2.zero,Vector2.zero);
                surface=imageRect.gameObject.AddComponent<RawImage>();surface.raycastTarget=false;surface.enabled=false;
                var tint=UIPrimitives.Stretch("Blue glass tint",clip,Vector2.zero,Vector2.zero).gameObject.AddComponent<Image>();
                tint.color=new Color(.055f,.09f,.16f,.86f);tint.raycastTarget=false;
            }
            yield return new WaitForEndOfFrame();
            var capture=ScreenCapture.CaptureScreenshotAsTexture();
            if(capture==null)yield break;
            int width=Mathf.Max(16,Screen.width/4),height=Mathf.Max(16,Screen.height/4);
            snapshot=new RenderTexture(width,height,0,RenderTextureFormat.ARGB32){filterMode=FilterMode.Bilinear,name="Phone scene glass snapshot"};snapshot.Create();
            var temporary=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGB32);temporary.filterMode=FilterMode.Bilinear;
            try
            {
                Graphics.Blit(capture,snapshot);
                var shader=Resources.Load<Shader>("Dialogue/SceneBlur");
                if(shader!=null)
                {
                    if(blur==null)blur=new Material(shader){name="Phone scene softening"};
                    blur.SetVector("_Direction",new Vector4(1,0,0,0));Graphics.Blit(snapshot,temporary,blur);
                    blur.SetVector("_Direction",new Vector4(0,1,0,0));Graphics.Blit(temporary,snapshot,blur);
                }
                surface.texture=snapshot;surface.enabled=true;RefreshUv();
            }
            finally{RenderTexture.ReleaseTemporary(temporary);Destroy(capture);}
        }
        void LateUpdate(){if(snapshot!=null)RefreshUv();}
        void RefreshUv()
        {
            if(clip==null||surface==null||Screen.width<=0||Screen.height<=0)return;
            var canvas=clip.GetComponentInParent<Canvas>();
            var camera=canvas!=null&&canvas.renderMode!=RenderMode.ScreenSpaceOverlay?canvas.worldCamera:null;
            clip.GetWorldCorners(corners);
            Vector2 bottomLeft=RectTransformUtility.WorldToScreenPoint(camera,corners[0]);
            Vector2 topRight=RectTransformUtility.WorldToScreenPoint(camera,corners[2]);
            var uv=new Rect(bottomLeft.x/Screen.width,bottomLeft.y/Screen.height,(topRight.x-bottomLeft.x)/Screen.width,(topRight.y-bottomLeft.y)/Screen.height);
            if(uv!=lastUv){surface.uvRect=uv;lastUv=uv;}
        }
        void Release()
        {
            if(surface!=null){surface.enabled=false;surface.texture=null;}
            if(snapshot!=null){snapshot.Release();Destroy(snapshot);snapshot=null;}
        }
        void OnDestroy(){Release();if(blur!=null)Destroy(blur);}
    }
}
