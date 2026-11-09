using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Seabreeze
{
    /// <summary>One scene capture per conversation; high precision, dense Gaussian frosting.</summary>
    public sealed class DialogueSceneBackdrop : MonoBehaviour
    {
        RawImage surface;
        RenderTexture snapshot;
        Material horizontal, vertical;
        public void Initialize(Transform parent)
        {
            var rect=UIPrimitives.Stretch("Soft scene backdrop",parent,Vector2.zero,Vector2.zero);
            surface=rect.gameObject.AddComponent<RawImage>();surface.raycastTarget=true;surface.enabled=false;
            surface.color=new Color(.96f,.975f,1f,1);
            var shader=Resources.Load<Shader>("Dialogue/FrostedScene");
            if(shader!=null&&shader.isSupported)
            {
                horizontal=new Material(shader){name="Dialogue frost horizontal"};
                vertical=new Material(shader){name="Dialogue frost vertical"};
                horizontal.SetVector("_Direction",new Vector4(1,0,0,0));
                vertical.SetVector("_Direction",new Vector4(0,1,0,0));
            }
        }
        public IEnumerator Capture()
        {
            Release();
            // Wait until the HUD has hidden and the dialogue is still transparent.
            yield return new WaitForEndOfFrame();
            var capture=ScreenCapture.CaptureScreenshotAsTexture();
            if(capture==null)yield break;
            RenderTexture temporary=null;
            try
            {
                capture.filterMode=FilterMode.Bilinear;capture.wrapMode=TextureWrapMode.Clamp;
                // Half-resolution retains four times as many samples as the previous quarter-resolution buffer.
                int width=Mathf.Max(16,capture.width/2),height=Mathf.Max(16,capture.height/2);
                var format=SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)?RenderTextureFormat.ARGBHalf:RenderTextureFormat.ARGB32;
                snapshot=new RenderTexture(width,height,0,format){filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,name="Dialogue frosted scene"};snapshot.Create();
                temporary=RenderTexture.GetTemporary(width,height,0,format);
                temporary.filterMode=FilterMode.Bilinear;temporary.wrapMode=TextureWrapMode.Clamp;
                Graphics.Blit(capture,snapshot);
                if(horizontal!=null&&vertical!=null)
                {
                    // Match perceived softness across resolutions, without sparse sampling at 4K.
                    float radiusScale=Mathf.Clamp(capture.height/1080f,.5f,2f);
                    horizontal.SetFloat("_RadiusScale",radiusScale);
                    vertical.SetFloat("_RadiusScale",radiusScale);
                    for(int pass=0;pass<3;pass++)
                    {
                        Graphics.Blit(snapshot,temporary,horizontal);
                        Graphics.Blit(temporary,snapshot,vertical);
                    }
                }
                surface.texture=snapshot;surface.enabled=true;
            }
            finally
            {
                if(temporary!=null)RenderTexture.ReleaseTemporary(temporary);
                Destroy(capture);
            }
        }
        public void Release()
        {
            if(surface!=null){surface.texture=null;surface.enabled=false;}
            if(snapshot!=null){snapshot.Release();Destroy(snapshot);snapshot=null;}
        }
        void OnDestroy(){Release();if(horizontal!=null)Destroy(horizontal);if(vertical!=null)Destroy(vertical);}
    }
}
