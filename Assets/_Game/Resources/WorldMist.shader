Shader "CoOp/WorldMist"
{
    Properties { _MainTex("Soft mask",2D)="white" {} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma target 4.5
            #pragma fragment Frag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "../Shaders/WorldVision.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 colour:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; float2 uv:TEXCOORD1; half4 colour:COLOR; };
            Varyings Vert(Attributes i) { Varyings o; o.world=TransformObjectToWorld(i.positionOS.xyz); o.positionCS=TransformWorldToHClip(o.world); o.uv=i.uv; o.colour=i.colour; return o; }
            half4 Frag(Varyings i):SV_Target
            {
                float lightness=0.18+dot(SampleSH(float3(0,1,0)),float3(0.299,0.587,0.114));
                InputData inputData=(InputData)0;
                inputData.positionWS=i.world;
                inputData.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);
                uint count=GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(count)
                {
                    Light light=GetAdditionalLight(lightIndex,i.world);
                    lightness+=dot(light.color,float3(0.299,0.587,0.114))*light.distanceAttenuation*0.1;
                }
                LIGHT_LOOP_END
                half4 colour=i.colour*SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv);
                colour.rgb*=clamp(lightness,0.18,1.1);
                colour.a*=ElecVisibility(i.world);
                return colour;
            }
            ENDHLSL
        }
    }
}
