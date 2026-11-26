using CoOpHorror.Gameplay;
using UnityEngine;

namespace CoOpHorror.Player
{
    // Every peer hears the observed players. Cosmetic cadence does not send movement/gameplay RPCs.
    public sealed class PlayerFoley : MonoBehaviour
    {
        private FirstPersonController motor;
        private PlayerHealth health;
        private Vector3 previous;
        private bool initialized,wasGrounded;
        private float stride,airSince;
        private void Awake(){motor=GetComponent<FirstPersonController>();health=GetComponent<PlayerHealth>();}
        private void LateUpdate()
        {
            var position=transform.position;
            if(!motor.IsSpawned||health.IsDead.Value){initialized=false;return;}
            if(!initialized){previous=position;initialized=true;stride=0f;return;}
            var offset=position-previous;previous=position;
            if(offset.sqrMagnitude>4f){stride=0f;wasGrounded=false;return;}
            var grounded=Physics.Raycast(position+Vector3.up*0.05f,Vector3.down,out var floor,1.14f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
            if(!grounded&&wasGrounded){airSince=Time.time;if(offset.y>0.01f)ProtoAudio.Play(ProtoSound.Jump,position,0.16f);}
            if(grounded&&!wasGrounded&&Time.time-airSince>0.18f)ProtoAudio.Play(ProtoSound.Land,position,0.3f);
            wasGrounded=grounded;
            if(!grounded)return;
            var flat=new Vector2(offset.x,offset.z).magnitude;
            if(flat<0.001f)return;
            stride+=flat;var speed=flat/Mathf.Max(Time.deltaTime,0.001f);
            var threshold=motor.Crouched.Value?1.2f:speed>5f?1.8f:1.45f;
            if(stride<threshold)return;
            stride%=threshold;
            var stage=StageDirector.Instance;
            var metal=stage!=null&&position.x>=stage.ShipMin.x&&position.x<=stage.ShipMax.x&&position.z>=stage.ShipMin.z&&position.z<=stage.ShipMax.z;
            var volume=motor.Crouched.Value?0.09f:speed>5f?0.42f:0.24f;
            ProtoAudio.PlayPitched(metal?ProtoSound.FootstepMetal:ProtoSound.FootstepConcrete,floor.point,volume,Random.Range(0.92f,1.08f));
        }
    }
}
