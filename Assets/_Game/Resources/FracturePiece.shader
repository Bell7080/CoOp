Shader "CoOp/FracturePiece"
{
    Properties { _BaseMap("Texture",2D)="white" {} _BaseColor("Tint",Color)=(0.5,0.5,0.5,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma target 4.5
            #pragma fragment Frag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 local:TEXCOORD0; float2 uv:TEXCOORD1; float3 normal:TEXCOORD2; float3 world:TEXCOORD3; };
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            float4 _BaseColor; float3 _PieceMin,_PieceMax;
            Varyings Vert(Attributes i) { Varyings o; o.positionCS=TransformObjectToHClip(i.positionOS.xyz); o.world=TransformObjectToWorld(i.positionOS.xyz); o.local=i.positionOS.xyz; o.uv=i.uv; o.normal=TransformObjectToWorldNormal(i.normalOS); return o; }
            half4 Frag(Varyings i):SV_Target
            {
                float3 lo=i.local-_PieceMin,hi=_PieceMax-i.local;
                clip(min(min(min(lo.x,lo.y),lo.z),min(min(hi.x,hi.y),hi.z)));
                Light light=GetMainLight(TransformWorldToShadowCoord(i.world)); float3 normal=normalize(i.normal);
                float3 illumination=max(SampleSH(normal),0.025)+light.color*saturate(dot(normal,light.direction))*light.shadowAttenuation;
                InputData inputData=(InputData)0;
                inputData.positionWS=i.world;
                inputData.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);
                uint count=GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(count)
                {
                    Light localLight=GetAdditionalLight(lightIndex,i.world,half4(1,1,1,1));
                    illumination+=localLight.color*saturate(dot(normal,localLight.direction))*localLight.distanceAttenuation*localLight.shadowAttenuation;
                }
                LIGHT_LOOP_END
                float3 colour=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb*_BaseColor.rgb;
                return half4(min(colour*illumination,0.75),1);
            }
            ENDHLSL
        }
    }
}
