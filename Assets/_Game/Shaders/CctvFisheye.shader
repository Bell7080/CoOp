// CCTV feed on a curved CRT monitor, seen with your face right up against the glass.
// 1) The screen bulges like a tube monitor (barrel curvature) inside a dark rounded bezel.
// 2) The camera image is remapped from a wide rectilinear render into a curvilinear (five-point / fisheye) view.
// 3) Light surveillance treatment: slightly desaturated, faint scanlines and grain, a short burst of static
//    when switching cameras (_Static) and full static when the camera has no power (_Signal = 0).
// Used by the full-screen CCTV view (uGUI RawImage) and by the ship's monitor mesh.
Shader "CoOp/CctvFisheye"
{
    Properties
    {
        [PerRendererData] _MainTex ("Feed", 2D) = "black" {}
        _Color ("Tint", Color) = (0.92, 0.95, 0.9, 1)
        _HalfAngle ("Output half angle at the side edge (deg)", Float) = 66
        _SrcHalfTan ("tan(source horizontal half FOV)", Float) = 2.2460
        _Gain ("Exposure gain", Float) = 1.5
        _Lift ("Shadow lift (gamma)", Range(0.3, 1)) = 0.55
        _Grain ("Grain (relative)", Range(0, 0.3)) = 0.07
        _SrcAspect ("Source aspect (w/h)", Float) = 1.3333
        _Aspect ("Output aspect (w/h)", Float) = 1.7778
        _Zoom ("Zoom", Float) = 1
        _Signal ("Signal", Range(0, 1)) = 1
        _Static ("Switching static", Range(0, 1)) = 0
        _Curve ("CRT curvature", Range(0, 0.5)) = 0.16
        _Corner ("Rounded corner size", Range(0.01, 0.5)) = 0.14
        _Lines ("Scanlines", Float) = 260
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
            float _HalfAngle, _SrcHalfTan, _SrcAspect, _Aspect, _Zoom, _Signal, _Static, _Curve, _Corner, _Lines, _Gain, _Grain, _Lift;

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
                // 1) CRT tube: bulge the screen and cut it out with rounded corners.
                float2 c = i.uv * 2.0 - 1.0;
                c += c * (c.yx * c.yx) * _Curve;
                float2 q = abs(c);
                float2 corner = max(q - (1.0 - _Corner), 0.0) / _Corner;
                float screen = 1.0 - smoothstep(0.96, 1.0, length(corner));
                float2 cuv = c * 0.5 + 0.5;

                // 2) Lens: equidistant fisheye from a wide rectilinear render.
                float2 p = (cuv - 0.5) * 2.0;
                p.y /= _Aspect;
                float rho = length(p);
                float theta = rho * radians(_HalfAngle) / max(_Zoom, 0.01);
                float2 dir = rho > 1e-5 ? p / rho : float2(0, 0);
                float r = tan(min(theta, 1.5)) / _SrcHalfTan;
                float2 s = dir * r;
                s.y *= _SrcAspect;
                float2 suv = s * 0.5 + 0.5;
                float lens = step(theta, 1.5) * step(0.0, suv.x) * step(suv.x, 1.0) * step(0.0, suv.y) * step(suv.y, 1.0);

                // Night-vision style: gain, then a gamma lift so dark rooms read instead of going black.
                float3 col = pow(saturate(tex2D(_MainTex, saturate(suv)).rgb * _Gain), _Lift) * lens;

                // 3) Surveillance look, kept light.
                float lum = dot(col, float3(0.299, 0.587, 0.114));
                col = lerp(col, lum.xxx, 0.35) * _Color.rgb;
                col = saturate((col - 0.5) * 1.12 + 0.5 + 0.02);
                float t = _Time.y;
                // Grain scales with brightness so dark rooms stay readable instead of fizzing.
                float grain = hash(floor(cuv * float2(480, 270)) + frac(t * 7.13));
                col *= 0.96 + 0.04 * sin(cuv.y * _Lines * 3.14159);
                col *= 1.0 + (grain - 0.5) * _Grain;
                col += (grain - 0.5) * 0.004;
                col *= 0.97 + 0.03 * smoothstep(0.0, 0.1, abs(frac(cuv.y * 0.5 - t * 0.1) - 0.5));
                float2 edge = cuv - 0.5;
                col *= saturate(1.15 - dot(edge, edge) * 1.6);

                float noise = hash(floor(cuv * float2(240, 135)) + floor(t * 30.0));
                float stat = saturate(max(1.0 - _Signal, _Static));
                col = lerp(col, noise.xxx * 0.6 + 0.05, stat);

                // Dark plastic bezel with a faint glass glare.
                float3 bezel = float3(0.03, 0.032, 0.035) + 0.025 * (1.0 - i.uv.y);
                col += 0.04 * smoothstep(0.35, 0.0, abs(cuv.x + cuv.y - 0.55)) * screen;
                col = lerp(bezel, col, screen);
                return fixed4(col, i.color.a);
            }
            ENDCG
        }
    }
}
