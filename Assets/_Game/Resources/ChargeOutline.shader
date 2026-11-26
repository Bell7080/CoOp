Shader "CoOp/ChargeOutline"
{
    Properties { _Color ("Colour", Color) = (0.35, 1, 0.8, 0.6) _Width ("Width in metres", Float) = 0.006 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+10" "RenderType"="Transparent" }
        Pass
        {
            Cull Front ZWrite Off ZTest LEqual Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "../Shaders/WorldVision.hlsl"
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; };
            float4 _Color; float _Width;
            Varyings Vert(Attributes i)
            {
                Varyings o;
                float3 world=TransformObjectToWorld(i.positionOS.xyz);
                world+=TransformObjectToWorldNormal(i.normalOS)*_Width;
                o.world=world; o.positionCS=TransformWorldToHClip(world); return o;
            }
            half4 Frag(Varyings i):SV_Target { return half4(_Color.rgb,_Color.a*ElecVisibility(i.world)); }
            ENDHLSL
        }
    }
}
