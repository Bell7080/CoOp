Shader "CoOp/RadarCRT"
{
    Properties { [PerRendererData] _MainTex ("Level capture", 2D) = "black" {} }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Cull Off ZWrite Off ZTest Always Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _MainTex_TexelSize;
            float4 _Rooms[36]; float _RoomCount;
            float cross2(float2 a,float2 b) { return a.x*b.y-a.y*b.x; }
            float lum(float2 p) { return dot(tex2D(_MainTex,p).rgb,float3(0.299,0.587,0.114)); }
            float hash(float2 p) { return frac(sin(dot(p,float2(12.9898,78.233)))*43758.5453); }
            fixed4 frag(v2f_img i):SV_Target
            {
                float2 uv=i.uv,p=(uv-0.5)*float2(1.5,1);
                float covered=0;
                for(int n=0;n<9;n++)
                {
                    if(n>=_RoomCount) break;
                    float2 a=_Rooms[n*4].xy,b=_Rooms[n*4+1].xy,c=_Rooms[n*4+2].xy,d=_Rooms[n*4+3].xy;
                    float4 sides=float4(cross2(b-a,uv-a),cross2(c-b,uv-b),cross2(d-c,uv-c),cross2(a-d,uv-d));
                    covered=max(covered, max(step(0,min(min(sides.x,sides.y),min(sides.z,sides.w))),step(max(max(sides.x,sides.y),max(sides.z,sides.w)),0)));
                }
                float angle=atan2(p.y,p.x);
                float sweep=frac((angle+3.141593)/6.283185-_Time.y*0.16);
                float phosphor=exp(-sweep*6);
                float2 t=_MainTex_TexelSize.xy*1.6;
                float edges=abs(lum(uv+t*float2(1,0))-lum(uv-t*float2(1,0)))+abs(lum(uv+t*float2(0,1))-lum(uv-t*float2(0,1)));
                float image=(lum(uv)*0.28+edges*2.8)*covered*(0.38+phosphor*0.85);
                float rings=pow(1-abs(sin(length(p)*50)),18)*0.055;
                float grid=(step(frac(uv.x*30),0.025)+step(frac(uv.y*20),0.025))*0.025;
                float scan=0.965+0.035*sin(uv.y*950);
                float noise=(hash(floor(uv*float2(600,400))+floor(_Time.y*12))-0.5)*0.015;
                float brightness=(image+rings+grid+noise+phosphor*0.035)*scan;
                float vignette=saturate(1.15-dot(p,p)*0.8);
                return fixed4(float3(0.015,0.035,0.028)+float3(0.25,1,0.65)*brightness*vignette,1);
            }
            ENDCG
        }
    }
}
