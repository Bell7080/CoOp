#ifndef ELECMOLE_WORLD_VISION
#define ELECMOLE_WORLD_VISION
float _ElecTorch, _ElecVisionActive;
float4 _ElecEye, _ElecForward;

float ElecPlayerView()
{
    float3 delta = _WorldSpaceCameraPos - _ElecEye.xyz;
    return saturate(_ElecVisionActive) * (1.0 - step(0.25, dot(delta, delta)));
}

// Exponential optical depth, shared by opaque scenery and transparent world effects.
// No hard sight wall: beyond the useful range, shapes dissolve gradually into dusty air.
float ElecVisibility(float3 world)
{
    float3 offset = world - _ElecEye.xyz;
    float distance = length(offset);
    float cone = smoothstep(0.76, 0.94, dot(offset / max(distance, 0.001), _ElecForward.xyz));
    float torch = saturate(_ElecTorch) * cone;
    float density = lerp(0.255, 0.115, torch);
    float mist = 1.0 + 0.085 * sin(world.x * 0.71 + world.z * 0.43 + _Time.y * 0.12)
                           * sin(world.y * 1.31 - world.z * 0.35);
    float optical = max(0.0, distance - 1.7) * density * mist;
    return lerp(1.0, exp(-optical * optical), ElecPlayerView());
}
#endif
