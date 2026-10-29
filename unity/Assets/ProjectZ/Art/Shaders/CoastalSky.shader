Shader "ProjectZ/Coastal Sky"
{
    Properties { _Top("Sky",Color)=(.37,.65,.8,1) _Horizon("Horizon",Color)=(.83,.91,.89,1) }
    SubShader
    {
        Tags {"Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline"}
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _Top,_Horizon;
            CBUFFER_END
            struct A{float4 p:POSITION;};struct V{float4 p:SV_POSITION;float3 direction:TEXCOORD0;};
            float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
            float noise(float2 p)
            {
                float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);
                return lerp(lerp(hash(a),hash(a+float2(1,0)),f.x),lerp(hash(a+float2(0,1)),hash(a+1),f.x),f.y);
            }
            V vert(A v){V o;o.p=TransformObjectToHClip(v.p.xyz);o.direction=v.p.xyz;return o;}
            half4 frag(V i):SV_Target
            {
                float3 d=normalize(i.direction);float up=saturate(d.y);
                float3 color=lerp(_Horizon.rgb,_Top.rgb,pow(up,.55));
                float2 q=d.xz/max(.10,d.y)*1.6+float2(_Time.y*.004,0);
                float cloud=noise(q)*.60+noise(q*2.05)*.27+noise(q*4.1)*.13;
                float mask=smoothstep(.52,.68,cloud)*smoothstep(.12,.25,d.y);
                color=lerp(color,lerp(float3(.75,.83,.89),float3(1,.98,.94),smoothstep(.56,.72,cloud)),mask*.88);
                return half4(color,1);
            }
            ENDHLSL
        }
    }
}
