Shader "ProjectZ/Illustrated Surface"
{
    Properties
    {
        [MainTexture] _BaseMap("Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1,1,1,1)
        _ShadeColor("Shadow tint", Color) = (.65,.67,.75,1)
        _Recolor("Recolor texture luminance", Range(0,1)) = 0
        _Pattern("Pattern: 0 plain, 1 knit, 2 floral, 3 rib, 4 wood", Float) = 0
        _AlphaClip("Cutout", Float) = 0
        _Cutoff("Cutoff", Range(0,1)) = .25
        _Cull("Cull", Float) = 0
        _Wind("Wind strength", Float) = 0
        _Softness("Shading softness", Range(.02,.5)) = .2
        _UnlitBlend("Face fill", Range(0,1)) = .2
        _VertexColor("Use vertex colors", Float) = 0
        _Smoothness("Smoothness", Range(0,1)) = .2
        _Specular("Specular strength", Range(0,1)) = .1
        _Toon("Illustrated light bands", Range(0,1)) = 0
        _ReceiveShadow("Receive direct shadows", Range(0,1)) = 1
        _OcclusionStrength("Contact shadows", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Cull [_Cull]
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST, _BaseColor, _ShadeColor;
            float _Recolor, _Pattern, _AlphaClip, _Cutoff, _Wind, _Softness, _UnlitBlend, _VertexColor;
            float _Smoothness, _Specular, _Toon, _ReceiveShadow, _OcclusionStrength;
        CBUFFER_END
        float hash21(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
        float3 displace(float3 p)
        {
            float strength = saturate(p.y*.45)*_Wind;
            p.x += sin(_Time.y*1.2+p.x*1.7+p.z*.7)*strength;
            p.z += cos(_Time.y*.85+p.x*.8+p.y)*strength*.45;
            return p;
        }
        float3 pattern(float3 baseColor, float2 uv)
        {
            if (_Pattern > .5 && _Pattern < 1.5)
            {
                float2 p=uv*float2(22,36);
                float cable=pow(abs(sin(p.x*3.1416+sin(p.y*3.1416)*.45)),10);
                float stitch=sin(p.x*25.13+sin(p.y*12.56));
                float fade=1-saturate(length(fwidth(p))*2);
                return baseColor*(.92+(.10*cable+.018*stitch)*fade);
            }
            if (_Pattern > 1.5 && _Pattern < 2.5)
            {
                float2 p=uv*float2(25,19);float2 cell=floor(p);float2 q=frac(p)-.5;
                float seed=hash21(cell);float angle=seed*6.283;
                q=mul(float2x2(cos(angle),-sin(angle),sin(angle),cos(angle)),q);
                float flower=0;
                for(int i=0;i<5;i++)
                {
                    float a=i*1.2566;float2 center=float2(cos(a),sin(a))*.105;
                    flower+=1-smoothstep(.04,.082,length(q-center));
                }
                float stem=(1-smoothstep(.008,.018,abs(q.x-.02*sin(q.y*14))))*step(-.36,q.y)*step(q.y,-.1);
                float ink=saturate(flower+stem)*step(.18,seed);
                return lerp(baseColor,float3(.26,.30,.36),ink*.70);
            }
            if (_Pattern > 3.5)
            {
                float grain=sin(uv.y*160+sin(uv.x*10)*2+sin(uv.y*26)*.4);
                return baseColor*(.95+.035*grain+.025*sin(uv.y*440));
            }
            if (_Pattern > 2.5) return baseColor*(.95+.05*sin(uv.x*160));
            return baseColor;
        }
        ENDHLSL
        Pass
        {
            Name "Illustrated"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            struct Attributes { float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;float4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;float2 uv:TEXCOORD2;float fog:TEXCOORD3;float4 color:TEXCOORD4; };
            Varyings vert(Attributes v)
            {
                Varyings o;float3 p=displace(v.positionOS.xyz);
                o.world=TransformObjectToWorld(p);o.positionCS=TransformWorldToHClip(o.world);
                o.normal=TransformObjectToWorldNormal(v.normalOS);o.uv=TRANSFORM_TEX(v.uv,_BaseMap);
                o.fog=ComputeFogFactor(o.positionCS.z);o.color=v.color;return o;
            }
            half4 frag(Varyings i):SV_Target
            {
                half4 tex=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv);
                if(_AlphaClip>.5) clip(tex.a*_BaseColor.a-_Cutoff);
                float lum=dot(tex.rgb,float3(.299,.587,.114));
                float3 albedo=lerp(tex.rgb*_BaseColor.rgb,_BaseColor.rgb*(.80+lum*.40),saturate(_Recolor));
                if(_Recolor>1.5)albedo=_BaseColor.rgb;
                albedo=pattern(albedo,i.uv);
                albedo*=lerp(float3(1,1,1),i.color.rgb,_VertexColor);
                float3 n=normalize(i.normal),view=normalize(GetWorldSpaceViewDir(i.world));
                if(dot(n,view)<0)n=-n;
                Light light=GetMainLight(TransformWorldToShadowCoord(i.world));
                float ndl=dot(n,light.direction);
                float diffuse=lerp(saturate(ndl),smoothstep(.08-_Softness,.08+_Softness,ndl),_Toon);
                float shadow=lerp(1,light.shadowAttenuation,_ReceiveShadow);
                diffuse=lerp(diffuse,.8,_UnlitBlend);
                float3 indirect=max(SampleSH(n),float3(.20,.23,.27));
                AmbientOcclusionFactor ao=GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(i.positionCS));
                indirect*=lerp(1,ao.indirectAmbientOcclusion,_OcclusionStrength);
                float3 result=albedo*(indirect*_ShadeColor.rgb*1.15+light.color*diffuse*shadow*.78);
                float3 halfVector=normalize(light.direction+view);
                float spec=pow(saturate(dot(n,halfVector)),lerp(12,220,_Smoothness));
                result+=light.color*spec*_Specular*shadow;
                result+=albedo*pow(1-saturate(dot(n,view)),4)*.035;
                return half4(MixFog(result,i.fog),1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex depthVert
            #pragma fragment depthFrag
            struct A {float4 p:POSITION;float2 uv:TEXCOORD0;};
            struct V {float4 p:SV_POSITION;float2 uv:TEXCOORD0;};
            V depthVert(A v){V o;o.p=TransformObjectToHClip(displace(v.p.xyz));o.uv=TRANSFORM_TEX(v.uv,_BaseMap);return o;}
            half4 depthFrag(V i):SV_Target
            {if(_AlphaClip>.5)clip(SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).a-_Cutoff);return 0;}
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            float3 _LightDirection;
            struct A { float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0; };
            struct V { float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0; };
            V shadowVert(A v)
            {
                V o;float3 world=TransformObjectToWorld(displace(v.positionOS.xyz));
                float3 normal=TransformObjectToWorldNormal(v.normalOS);
                o.positionCS=TransformWorldToHClip(ApplyShadowBias(world,normal,_LightDirection));
                #if UNITY_REVERSED_Z
                o.positionCS.z=min(o.positionCS.z,UNITY_NEAR_CLIP_VALUE);
                #else
                o.positionCS.z=max(o.positionCS.z,UNITY_NEAR_CLIP_VALUE);
                #endif
                o.uv=TRANSFORM_TEX(v.uv,_BaseMap);return o;
            }
            half4 shadowFrag(V i):SV_Target
            { if(_AlphaClip>.5)clip(SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).a*_BaseColor.a-_Cutoff);return 0; }
            ENDHLSL
        }
    }
}
