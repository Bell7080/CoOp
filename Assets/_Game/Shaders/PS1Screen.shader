// Full-screen PS1 look (URP Full Screen Pass renderer feature): the frame is already rendered at a low
// render scale and upscaled with point filtering; this pass reduces the colour depth with an ordered 4x4
// dither and darkens the corners. Runs before transparents so screen-space-camera UI stays legible.
Shader "CoOp/PS1Screen"
{
    Properties
    {
        _Levels ("Colour levels per channel", Float) = 22
        _Dither ("Dither strength", Range(0, 1.5)) = 0.85
        _Vignette ("Vignette", Range(0, 2)) = 1.1
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

            float _Levels;
            float _Dither;
            float _Vignette;

            static const float Bayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half3 colour = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv).rgb;
                uint2 cell = uint2(input.positionCS.xy) % 4;
                float threshold = (Bayer[cell.y * 4 + cell.x] + 0.5) / 16.0 - 0.5;
                // Dither only where there is light: blacks stay black instead of a dot grid.
                float lum = dot(colour, float3(0.299, 0.587, 0.114));
                colour = floor(colour * _Levels + 0.5 + threshold * _Dither * saturate(lum * _Levels * 0.5)) / _Levels;
                float2 d = uv - 0.5;
                colour *= saturate(1.0 - dot(d, d) * _Vignette);
                return half4(max(colour, 0.0), 1.0);
            }
            ENDHLSL
        }
    }
}
