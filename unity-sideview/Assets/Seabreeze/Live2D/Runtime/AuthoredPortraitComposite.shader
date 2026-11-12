Shader "Seabreeze/UI/Authored Portrait"
{
    Properties
    {
        [PerRendererData] _MainTex ("Portrait", 2D) = "white" {}
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Stencil
        {
            Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp]
            ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask]
        }
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            sampler2D _MainTex;
            float4 _ClipRect;
            struct Input { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct Varyings { float4 position : SV_POSITION; float2 uv : TEXCOORD0; float2 uiPosition : TEXCOORD1; fixed4 color : COLOR; };
            Varyings vert(Input v)
            {
                Varyings o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.uiPosition = v.vertex.xy;
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }
            float4 frag(Varyings i) : SV_Target
            {
                // Cubism blended the original paint values over transparent black.
                // Convert straight RGB to the scene color space, then premultiply for UI.
                float4 p = tex2D(_MainTex, i.uv);
                float3 rgb = saturate(p.rgb / max(p.a, 0.00001));
                #ifndef UNITY_COLORSPACE_GAMMA
                rgb = float3(GammaToLinearSpaceExact(rgb.r), GammaToLinearSpaceExact(rgb.g), GammaToLinearSpaceExact(rgb.b));
                #endif
                float alpha = p.a * i.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                alpha *= UnityGet2DClipping(i.uiPosition, _ClipRect);
                #endif
                return float4(rgb * i.color.rgb * alpha, alpha);
            }
            ENDCG
        }
    }
}
