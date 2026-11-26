#ifndef ELECMOLE_WORLD_VISION
#define ELECMOLE_WORLD_VISION
float _ElecTorch, _ElecVisionActive;
float4 _ElecEye, _ElecForward;
// Lit ceiling lamps (xyz = bulb, w = reach in metres, 0 when dark), nearest first; set by PowerLamp.
#define ELEC_MAX_LAMPS 16
float4 _ElecLamps[ELEC_MAX_LAMPS];
float _ElecLampCount;

float ElecPlayerView()
{
    float3 delta = _WorldSpaceCameraPos - _ElecEye.xyz;
    return saturate(_ElecVisionActive) * (1.0 - step(0.25, dot(delta, delta)));
}

// How much a lit lamp clears the dark around this point (0..1).
float ElecLampLight(float3 world)
{
    float lit = 0.0;
    for (int i = 0; i < ELEC_MAX_LAMPS; i++)
    {
        if (i >= (int)_ElecLampCount) break;
        float reach = _ElecLamps[i].w;
        float near = saturate(1.0 - length(world - _ElecLamps[i].xyz) / max(reach, 0.01));
        lit = max(lit, near * near * (3.0 - 2.0 * near));
    }
    return lit;
}

// Exponential optical depth, shared by opaque scenery and transparent world effects.
// No hard sight wall: beyond a short range, shapes dissolve into dusty dark. The torch only pushes it out a
// little; areas lit by ceiling lamps stay visible from much further away.
float ElecVisibility(float3 world)
{
    float3 offset = world - _ElecEye.xyz;
    float distance = length(offset);
    float cone = smoothstep(0.76, 0.94, dot(offset / max(distance, 0.001), _ElecForward.xyz));
    float torch = saturate(_ElecTorch) * cone;
    float density = lerp(0.33, 0.2, torch);
    float mist = 1.0 + 0.085 * sin(world.x * 0.71 + world.z * 0.43 + _Time.y * 0.12)
                           * sin(world.y * 1.31 - world.z * 0.35);
    float optical = max(0.0, distance - 1.2) * density * mist * (1.0 - 0.85 * ElecLampLight(world));
    return lerp(1.0, exp(-optical * optical), ElecPlayerView());
}
#endif
