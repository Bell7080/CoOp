// CCTV feed: remaps a wide rectilinear render into a curvilinear (five-point / fisheye) view, then adds
// low-res surveillance treatment (green-grey tint, scanlines, grain, vignette, "no signal" static).
// Used both by the full-screen CCTV view (uGUI RawImage) and by the ship's monitor mesh.
Shader "CoOp/CctvFisheye"
{
    Properties
    {
        [PerRendererData] _MainTex ("Feed", 2D) = "black" {}
        _Color ("Tint", Color) = (0.72, 0.95, 0.8, 1)
        _HalfAngle ("Output half angle at the side edge (deg)", Float) = 70
        _SrcHalfTan ("tan(source horizontal half FOV)", Float) = 2.7475
        _SrcAspect ("Source aspect (w/h)", Float) = 1.3333
        _Aspect ("Output aspect (w/h)", Float) = 1.7778
        _Zoom ("Zoom", Float) = 1
        _Signal ("Signal", Range(0, 1)) = 1
        _Lines ("Scanlines", Float) = 220
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" }
        Cull Off
        ZWrite Off
        ZTest [_ZTest]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _Color;
            float _HalfAngle, _SrcHalfTan, _SrcAspect, _Aspect, _Zoom, _Signal, _Lines;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            float hash(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }

            fixed4 frag(v2f i) : SV_Target
            {
                // Output point in units of half the width, centred.
                float2 p = (i.uv - 0.5) * 2.0;
                p.y /= _Aspect;
                float rho = length(p);
                // Equidistant fisheye: distance from centre is proportional to the viewing angle.
                float theta = rho * radians(_HalfAngle) / max(_Zoom, 0.01);
                float2 dir = rho > 1e-5 ? p / rho : float2(0, 0);
                float lens = step(theta, 1.5);
                float r = tan(min(theta, 1.5)) / _SrcHalfTan;
                float2 s = dir * r;
                s.y *= _SrcAspect;
                float2 suv = s * 0.5 + 0.5;
                lens *= step(0.0, suv.x) * step(suv.x, 1.0) * step(0.0, suv.y) * step(suv.y, 1.0);

                float3 col = tex2D(_MainTex, saturate(suv)).rgb;
                float lum = dot(col, float3(0.299, 0.587, 0.114));
                col = lerp(col, lum * _Color.rgb * 1.25, 0.6);

                float t = _Time.y;
                float grain = hash(floor(i.uv * float2(320, 180)) + frac(t * 7.13));
                col = col * (0.86 + 0.14 * sin(i.uv.y * _Lines * 3.14159)) + (grain - 0.5) * 0.09;
                // A slow rolling bar like an old monitor.
                col *= 0.93 + 0.07 * smoothstep(0.0, 0.08, abs(frac(i.uv.y * 0.5 - t * 0.12) - 0.5));
                col *= lens * saturate(1.25 - rho * 0.6);

                float noise = hash(floor(i.uv * float2(200, 120)) + floor(t * 24.0));
                col = lerp(noise.xxx * 0.55, col, _Signal);
                return fixed4(col, i.color.a);
            }
            ENDCG
        }
    }
}
