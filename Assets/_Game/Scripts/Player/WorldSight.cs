using UnityEngine;

namespace CoOpHorror.Player
{
    // HUD labels obey the same optical-depth curve as WorldVision.hlsl, instead of revealing fog-hidden loot.
    public static class WorldSight
    {
        public static float Visibility(Vector3 position)
        {
            if(Shader.GetGlobalFloat("_ElecVisionActive")<0.5f)return 1f;
            var eye=(Vector3)Shader.GetGlobalVector("_ElecEye");
            var forward=(Vector3)Shader.GetGlobalVector("_ElecForward");
            var offset=position-eye;var distance=offset.magnitude;
            var t=Mathf.Clamp01((Vector3.Dot(offset/Mathf.Max(distance,0.001f),forward)-0.76f)/0.18f);
            var cone=t*t*(3f-2f*t);
            var density=Mathf.Lerp(0.255f,0.115f,Shader.GetGlobalFloat("_ElecTorch")*cone);
            var mist=1f+0.085f*Mathf.Sin(position.x*0.71f+position.z*0.43f+Time.time*0.12f)*Mathf.Sin(position.y*1.31f-position.z*0.35f);
            var optical=Mathf.Max(0,distance-1.7f)*density*mist;
            return Mathf.Exp(-optical*optical);
        }
    }
}
