Shader "Seabreeze/UI/CoastalTitle"
{
    Properties
    {
        [PerRendererData] _MainTex ("Painted coast", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _SeaRect ("Open water source UV bounds", Vector) = (.65,.015,1,.185)
        _WaveStrength ("Water displacement", Float) = .00065
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
            fixed4 frag(v2f i) : SV_Target
            {
                // Soft bounds keep architecture, the coastline and horizon completely still.
                float2 inset=min(i.uv-_SeaRect.xy, _SeaRect.zw-i.uv);
                float water=smoothstep(0,.025,inset.x)*smoothstep(0,.025,inset.y);
                float depth=saturate((_SeaRect.w-i.uv.y)/max(.001,_SeaRect.w-_SeaRect.y));
                float a=sin(i.uv.y*225 + i.uv.x*8 + _CoastTime*.85);
                float b=sin(i.uv.y*370 - i.uv.x*13 - _CoastTime*.54);
                float2 uv=i.uv+water*_WaveStrength*(.35+.65*depth)*float2(a+.35*b,.20*b);
                fixed4 color=(tex2D(_MainTex,uv)+_TextureSampleAdd)*i.color;
                color.rgb*=1+water*.008*a*b;
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
