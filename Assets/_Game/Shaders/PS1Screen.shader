// Full-screen PS1 look (URP Full Screen Pass renderer feature): the frame is already rendered at a low
// render scale and upscaled with point filtering; this pass reduces the colour depth with an ordered 4x4
// dither and darkens the corners. Runs before transparents so screen-space-camera UI stays legible.
// On top of that (after Lethal Company / R.E.P.O.-style screen treatments): dark depth-edge outlines so
// shapes read in the low-res dark, a slight chromatic aberration toward the edges, and animated film grain.
Shader "CoOp/PS1Screen"
{
    Properties
    {
        _Levels ("Colour levels per channel", Float) = 22
        _Dither ("Dither strength", Range(0, 1.5)) = 0.85
        _Vignette ("Vignette", Range(0, 2)) = 1.1
        _Lift ("Shadow lift (gamma)", Range(0.3, 1)) = 0.7
        _Outline ("Depth outline strength", Range(0, 1)) = 0.55
        _OutlineThreshold ("Outline depth ratio", Range(0.01, 0.5)) = 0.12
        _OutlineFade ("Outline fade distance", Float) = 22
        _Aberration ("Chromatic aberration", Range(0, 0.02)) = 0.004
        _Grain ("Film grain", Range(0, 0.3)) = 0.06
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend Off

        Pass
        {
            Name "PS1Screen"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float _Levels;
            float _Dither;
            float _Vignette;
            float _Lift;
            float _Outline;
            float _OutlineThreshold;
            float _OutlineFade;
            float _Aberration;
            float _Grain;

            float EyeDepth(float2 uv) { return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams); }

            float Hash(float2 p)
            {
                p = frac(p * float2(443.897, 441.423));
                p += dot(p, p.yx + 19.19);
                return frac((p.x + p.y) * p.x);
            }

            static const float Bayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float2 d = uv - 0.5;
                // chromatic aberration: red and blue pulled apart a little, more toward the edges
                float2 shift = d * _Aberration * 2.0;
                half3 colour;
                colour.r = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv + shift).r;
                colour.g = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv).g;
                colour.b = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv - shift).b;
                // Lift the shadows so dim, unlit rooms keep their shapes instead of quantising to black.
                colour = pow(max(colour, 0.0), _Lift);
                // depth-edge outlines: a neighbour much nearer or farther than this pixel marks a silhouette
                float2 texel = _CameraDepthTexture_TexelSize.xy;
                float centre = EyeDepth(uv);
                float n0 = EyeDepth(uv + float2(texel.x, 0));
                float n1 = EyeDepth(uv - float2(texel.x, 0));
                float n2 = EyeDepth(uv + float2(0, texel.y));
                float n3 = EyeDepth(uv - float2(0, texel.y));
                float nearest = min(min(n0, n1), min(n2, n3));
                float edge = saturate(((centre - nearest) / max(centre, 0.001) - _OutlineThreshold) * 8.0);
                edge *= saturate(1.0 - centre / _OutlineFade);
                colour *= 1.0 - edge * _Outline;
                // animated film grain, strongest in the mid-tones
                float lumGrain = dot(colour, float3(0.299, 0.587, 0.114));
                float grain = Hash(input.positionCS.xy + frac(_Time.y * 13.0) * 97.0) - 0.5;
                colour += grain * _Grain * saturate(lumGrain * 3.0) * (1.0 - lumGrain * 0.5);
                uint2 cell = uint2(input.positionCS.xy) % 4;
                float threshold = (Bayer[cell.y * 4 + cell.x] + 0.5) / 16.0 - 0.5;
                // Dither only where there is light: blacks stay black instead of a dot grid.
                float lum = dot(colour, float3(0.299, 0.587, 0.114));
                colour = floor(colour * _Levels + 0.5 + threshold * _Dither * saturate(lum * _Levels * 0.5)) / _Levels;
                colour *= saturate(1.0 - dot(d, d) * _Vignette);
                return half4(max(colour, 0.0), 1.0);
            }
            ENDHLSL
        }
    }
}
