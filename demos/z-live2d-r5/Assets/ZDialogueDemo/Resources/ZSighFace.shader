Shader "ZDialogueDemo/SighFace"
{
    Properties
    {
        [PerRendererData] _MainTex ("Original Cubism Texture", 2D) = "white" {}
        _SighTex ("Sigh B Features", 2D) = "white" {}
        _SighWeight ("Sigh Weight", Range(0, 1)) = 0
        _OriginalUvRect ("Original UV Rect", Vector) = (0, 0, 1, 1)
        _SighUvRect ("Sigh UV Rect", Vector) = (0, 0, 1, 1)
        _SighVerticalCenter ("Sigh Vertical Center", Float) = 0
        _SighVerticalScale ("Sigh Vertical Scale", Float) = 1
        _Color ("Color", Color) = (1, 1, 1, 1)

        [PerRendererData] cubism_ModelOpacity ("Cubism Model Opacity", Float) = 1
        [PerRendererData] cubism_MultiplyColor ("Cubism Multiply Color", Color) = (1, 1, 1, 1)
        [PerRendererData] cubism_ScreenColor ("Cubism Screen Color", Color) = (0, 0, 0, 1)

        // R5's URP pass submits an identity matrix and supplies the drawable
        // transform through this MaterialPropertyBlock.
        [PerRendererData] _OffsetScale ("OffsetScale", Vector) = (0, 0, 1, 1)
        [PerRendererData] _RotationQuaternion ("Rotation Quaternion", Vector) = (0, 0, 0, 1)
        [PerRendererData] _ZOffset ("Z Offset", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            // DrawMesh(pass: 0) in CubismRenderPass executes this pass
            // explicitly. The unknown LightMode keeps the same material from
            // being selected a second time by URP's regular MeshRenderer path.
            Tags { "LightMode" = "CubismSigh" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _SighTex;
            float4 _MainTex_ST;
            float4 _OriginalUvRect;
            float4 _SighUvRect;
            float _SighWeight;
            float _SighVerticalCenter;
            float _SighVerticalScale;
            fixed4 _Color;
            float cubism_ModelOpacity;
            fixed4 cubism_MultiplyColor;
            fixed4 cubism_ScreenColor;
            float4 _OffsetScale;
            float4 _RotationQuaternion;
            float _ZOffset;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            float3 quaternion(float4 quat, float3 vec)
            {
                float4 normalizedQuat = normalize(quat);
                float3 qv = normalizedQuat.xyz;
                float qw = normalizedQuat.w;
                float3 crossVec = cross(qv, vec);
                return vec + 2.0 * (qw * crossVec + cross(qv, crossVec));
            }

            v2f vert(appdata input)
            {
                v2f output;
                float amount = saturate(_SighWeight);
                float scale = lerp(1.0, max(0.001, _SighVerticalScale), amount);
                input.vertex.y = _SighVerticalCenter +
                    (input.vertex.y - _SighVerticalCenter) * scale;

                // Match CubismRendererUsingBlendMode.ApplyTransform. The R5
                // command buffer uses Matrix4x4.identity, so ignoring these
                // values puts the feature on the wrong drawable transform.
                float4 vertex = input.vertex;
                vertex.xy *= _OffsetScale.zw;
                vertex.xyz = quaternion(_RotationQuaternion, vertex.xyz);
                vertex.xy += _OffsetScale.xy;
                vertex.z += _ZOffset;

                output.vertex = UnityObjectToClipPos(vertex);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color * _Color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float2 originalUv = input.uv;
                float2 originalSize = max(_OriginalUvRect.zw - _OriginalUvRect.xy,
                    float2(0.00001, 0.00001));
                float2 normalized = saturate((originalUv - _OriginalUvRect.xy) / originalSize);
                float2 sighUv = lerp(_SighUvRect.xy, _SighUvRect.zw, normalized);

                fixed4 original = tex2D(_MainTex, originalUv);
                fixed4 sigh = tex2D(_SighTex, sighUv);
                fixed4 blended = lerp(original, sigh, saturate(_SighWeight));

                // Preserve the color extensions that the stock R5 drawable
                // shader receives through its property block.
                blended.rgb *= cubism_MultiplyColor.rgb;
                blended.rgb = (blended.rgb + cubism_ScreenColor.rgb) -
                    (blended.rgb * cubism_ScreenColor.rgb);
                blended *= input.color;
                blended.a *= cubism_ModelOpacity;
                return blended;
            }
            ENDCG
        }
    }
}
