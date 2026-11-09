Shader "Hidden/Seabreeze/FrostedDialogueScene"
{
    Properties { _MainTex ("Scene", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float4 _Direction;
            float _RadiusScale;
            float4 frag(v2f_img i) : SV_Target
            {
                // Contiguous Gaussian taps, sigma 4. Unlike widely spaced sparse taps,
                // this prefilters edges before magnification instead of repeating them.
                float2 stepUV=abs(_MainTex_TexelSize.xy)*_Direction.xy*_RadiusScale;
                float2 inset=abs(_MainTex_TexelSize.xy)*0.5;
                float4 sum=0;
                float weightSum=0;
                [unroll] for(int tap=-8;tap<=8;tap++)
                {
                    float weight=exp(-float(tap*tap)/32.0);
                    float2 uv=clamp(i.uv+stepUV*tap,inset,1.0-inset);
                    sum+=tex2D(_MainTex,uv)*weight;
                    weightSum+=weight;
                }
                return sum/weightSum;
            }
            ENDCG
        }
    }
}
