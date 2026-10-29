Shader "ProjectZ/Coastal Water"
{
    Properties
    {
        _Shallow("Shallow",Color)=(.34,.74,.73,1)
        _Deep("Deep",Color)=(.16,.45,.59,1)
        _Foam("Foam",Color)=(.89,.96,.9,1)
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"}
        Pass
        {
            Tags {"LightMode"="UniversalForward"}
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _Shallow,_Deep,_Foam;
            CBUFFER_END
            struct A{float4 p:POSITION;};
            struct V{float4 p:SV_POSITION;float3 world:TEXCOORD0;float fog:TEXCOORD1;};
            V vert(A v){V o;o.world=TransformObjectToWorld(v.p.xyz);o.p=TransformWorldToHClip(o.world);o.fog=ComputeFogFactor(o.p.z);return o;}
            half4 frag(V i):SV_Target
            {
                float2 p=i.world.xz;
                float a=atan2(p.y,p.x);float da=atan2(sin(a+.76),cos(a+.76));
                float coast=1-.24*exp(-da*da/.18)+.025*sin(a*7)+.018*cos(a*11);
                float radius=length(p/float2(14,11.8))/coast;
                float depth=smoothstep(.95,1.65,radius);
                float wave=sin(p.x*.8+p.y*.5+_Time.y*.7)*sin(p.y*1.7-_Time.y*.4);
                float3 color=lerp(_Shallow.rgb,_Deep.rgb,depth)+wave*.008*(1-depth);
                float foam=pow(saturate(sin(radius*100-_Time.y*.8+sin(a*18)*.7)),12);
                foam*=1-smoothstep(1.03,1.13,radius);foam*=smoothstep(.97,1.01,radius);
                foam*=smoothstep(-.6,.3,sin(p.x*1.3+sin(p.y*1.8))*sin(p.y*.9));
                float glint=pow(saturate(sin(p.x*4+p.y*7+_Time.y)*sin(p.y*5-_Time.y*.7)),35)*.18;
                color=lerp(color,_Foam.rgb,foam*.75)+glint*(1-depth)*.5;
                float3 view=normalize(GetWorldSpaceViewDir(i.world));
                float horizon=pow(1-saturate(view.y),5);
                color=lerp(color,float3(.45,.64,.73),horizon*.65);
                return half4(MixFog(color,i.fog),1);
            }
            ENDHLSL
        }
    }
}
