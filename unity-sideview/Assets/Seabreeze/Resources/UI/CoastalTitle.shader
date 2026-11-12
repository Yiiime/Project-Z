Shader "Seabreeze/UI/CoastalTitle"
{
    Properties
    {
        [PerRendererData] _MainTex ("Painted coast", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _SeaRect ("Open water source UV bounds", Vector) = (.65,.015,1,.185)
        _WaveStrength ("Water displacement", Float) = .0022
        _CoastTime ("Unscaled presentation time", Float) = 0
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float4 world : TEXCOORD1; };
            sampler2D _MainTex;
            fixed4 _Color, _TextureSampleAdd;
            float4 _ClipRect, _SeaRect;
            float _CoastTime, _WaveStrength;
            v2f vert(appdata v)
            {
                v2f o; o.world=v.vertex; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color*_Color; return o;
            }
            float paintedCloud(float2 uv, float2 center, float2 size, float variant, out fixed3 tint)
            {
                float2 p=(uv-center)/size+.5;
                float2 edge=min(p,1-p);
                float bounds=smoothstep(0,.10,edge.x)*smoothstep(0,.12,edge.y);
                // Reuse the illustration's own warm cloud brushwork. Color separation
                // removes blue sky/green foliage without editing or duplicating the source art.
                tint=tex2D(_MainTex,float2(.205+variant*.055,.902)+saturate(p)*float2(.12-variant*.025,.096)).rgb;
                float warm=min(tint.r,tint.g)-tint.b*.76;
                return bounds*smoothstep(.04,.18,warm)*smoothstep(.25,.45,min(tint.r,min(tint.g,tint.b)));
            }
            fixed4 frag(v2f i) : SV_Target
            {
                // Follow the diagonal open-water edge instead of warping the cliff or horizon.
                // All masks use source UVs, so they remain aligned at every window aspect ratio.
                float depth=saturate((_SeaRect.w-i.uv.y)/max(.001,_SeaRect.w-_SeaRect.y));
                float left=_SeaRect.x-.14*(1-depth);
                float water=smoothstep(left,left+.035,i.uv.x)*smoothstep(_SeaRect.y,_SeaRect.y+.016,i.uv.y)
                    *(1-smoothstep(_SeaRect.w-.025,_SeaRect.w,i.uv.y));
                float a=sin(i.uv.y*225 + i.uv.x*8 + _CoastTime*1.35);
                float b=sin(i.uv.y*370 - i.uv.x*13 - _CoastTime*.92);
                float2 uv=i.uv+water*_WaveStrength*(.35+.65*depth)*float2(a+.35*b,.28*b);

                // Small wind movement only on green foliage in the upper-left canopy.
                // Stone, trunk, lighthouse and UI never follow this displacement.
                fixed3 source=tex2D(_MainTex,i.uv).rgb;
                float leaves=(1-smoothstep(.18,.255,i.uv.x))*smoothstep(.49,.62,i.uv.y)
                    *smoothstep(.025,.09,source.g-source.r*.91)*smoothstep(.015,.10,source.g-source.b);
                uv+=leaves*float2(sin(_CoastTime*1.2+i.uv.y*21)*.0013,sin(_CoastTime*.9+i.uv.x*34)*.00055);
                fixed4 color=(tex2D(_MainTex,uv)+_TextureSampleAdd)*i.color;
                color.rgb*=1+water*.028*a*b;

                // Independently drifting painted clouds; no global camera pan or image morph.
                float sky=smoothstep(.37,.43,i.uv.x)*smoothstep(.29,.37,i.uv.y)
                    *smoothstep(.025,.12,source.b-source.r);
                for(int layer=0;layer<2;layer++)
                {
                    float travel=frac(_CoastTime/(110+layer*19)+.21+layer*.31);
                    float2 center=float2(.29+travel*.93,.90-layer*.10);
                    fixed3 cloudTint;
                    float cloud=paintedCloud(i.uv,center,float2(.25+layer*.045,.10-layer*.02),layer,cloudTint);
                    float fade=smoothstep(0,.12,travel)*(1-smoothstep(.85,1,travel));
                    color.rgb=lerp(color.rgb,cloudTint*i.color.rgb,cloud*sky*fade*(.55-layer*.07));
                }
                #ifdef UNITY_UI_CLIP_RECT
                color.a*=UnityGet2DClipping(i.world.xy,_ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a-.001);
                #endif
                return color;
            }
            ENDCG
        }
    }
}
