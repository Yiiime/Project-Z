Shader "Hidden/ProjectZ/ArrivalNameComposite"
{
 Properties { _MainTex("Video",2D)="black" {} _Overlay("Player name",2D)="black" {} }
 SubShader { Cull Off ZWrite Off ZTest Always Pass {
 CGPROGRAM
 #pragma vertex vert_img
 #pragma fragment frag
 #include "UnityCG.cginc"
 sampler2D _MainTex, _Overlay;
 float4 _H0,_H1,_H2,_NameRect;
 float4 _MainTex_TexelSize;
 float edge(float2 p,float4 r){float2 d=min(p-r.xy,r.xy+r.zw-p);return saturate(min(d.x,d.y)/6); }
 float _Visible;
 fixed4 frag(v2f_img i):SV_Target {
  fixed4 video=tex2D(_MainTex,i.uv);
  float2 d=_MainTex_TexelSize.xy*2;
  fixed3 neighbors=(tex2D(_MainTex,i.uv+float2(d.x,0)).rgb+tex2D(_MainTex,i.uv-float2(d.x,0)).rgb+tex2D(_MainTex,i.uv+float2(0,d.y)).rgb+tex2D(_MainTex,i.uv-float2(0,d.y)).rgb)*.25;
  video.rgb=saturate(video.rgb+(video.rgb-neighbors)*.4);
  float3 p=float3(i.uv.x*1280,(1-i.uv.y)*778,1);
  float z=dot(_H2.xyz,p);
  float2 original=float2(dot(_H0.xyz,p),dot(_H1.xyz,p))/z;
  float2 uv=float2(original.x/1280,1-original.y/778);
  fixed4 overlay=tex2D(_Overlay,uv);
  float inside=step(0,uv.x)*step(uv.x,1)*step(0,uv.y)*step(uv.y,1);
  float feather=max(max(edge(original,_NameRect),edge(original,float4(172,509,512,52))),edge(original,float4(320,443,90,44)));
  return fixed4(lerp(video.rgb,overlay.rgb,overlay.a*_Visible*inside*feather),1);
 }
 ENDCG
 } }
}
