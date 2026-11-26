using CoOpHorror.Player;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    // Local presentation of replicated events. Fixed voices, distance culling, occlusion and ambience ducking.
    public sealed class HorrorAudio : MonoBehaviour
    {
        private static HorrorAudio instance;
        public static HorrorAudio Ensure()
        {
            if (instance == null) instance = new GameObject("HorrorAudio").AddComponent<HorrorAudio>();
            return instance;
        }
        private const int VoiceCount = 32;
        private readonly AudioSource[] voices = new AudioSource[VoiceCount];
        private readonly AudioLowPassFilter[] filters = new AudioLowPassFilter[VoiceCount];
        private readonly float[] ends = new float[VoiceCount];
        private readonly float[] lastEvent = new float[System.Enum.GetValues(typeof(ProtoSound)).Length];
        private readonly AudioSource[] loops = new AudioSource[4];
        private float nextStinger, duckUntil;
        private bool wasInRoom;

        private AudioSource Source(string label)
        {
            var go = new GameObject(label); go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>(); source.playOnAwake = false;
            source.rolloffMode = AudioRolloffMode.Logarithmic; source.minDistance = 1.5f;
            source.maxDistance = 26f; source.dopplerLevel = 0f; return source;
        }
        private void Awake()
        {
            instance = this;
            foreach(ProtoSound sound in System.Enum.GetValues(typeof(ProtoSound)))
            {
                var clip=ProtoAudio.Clip(sound);
                if(clip!=null)clip.LoadAudioData();
            }
            for (var i=0;i<lastEvent.Length;i++) lastEvent[i]=float.NegativeInfinity;
            for (var i=0;i<VoiceCount;i++) { voices[i]=Source("WorldVoice"); filters[i]=voices[i].gameObject.AddComponent<AudioLowPassFilter>(); }
            var clips=new[]{ProtoSound.AmbientBed,ProtoSound.ShipHum,ProtoSound.GeneratorLoop,ProtoSound.TelekinesisLoop};
            for(var i=0;i<loops.Length;i++)
            {
                loops[i]=Source(clips[i].ToString()); loops[i].clip=ProtoAudio.Clip(clips[i]);
                loops[i].loop=true; loops[i].volume=0f; loops[i].spatialBlend=i<2?0f:1f;
            }
        }
        private void OnDestroy() { if(instance==this)instance=null; }
        public void Play(ProtoSound sound,Vector3 position,float volume,float pitch=1f)
        {
            var clip=ProtoAudio.Clip(sound); var camera=Camera.main;
            if(clip==null||camera==null||volume<=0f)return;
            var distance=Vector3.Distance(position,camera.transform.position);
            if(distance>30f&&sound!=ProtoSound.TransmitterFull)return;
            // Avoid piles of identical network/physics events becoming a wall of noise.
            var index=(int)sound;
            if(Time.unscaledTime-lastEvent[index]<0.045f)return;
            lastEvent[index]=Time.unscaledTime;
            var slot=-1;
            for(var i=0;i<VoiceCount;i++)if(!voices[i].isPlaying){slot=i;break;}
            if(slot<0)
            {
                if(sound==ProtoSound.FootstepConcrete||sound==ProtoSound.FootstepMetal||sound==ProtoSound.Pop)return;
                slot=0;for(var i=1;i<VoiceCount;i++)if(ends[i]<ends[slot])slot=i;
            }
            var source=voices[slot];source.Stop();source.transform.position=position;
            var stinger=index>=(int)ProtoSound.StingerMetal&&index<=(int)ProtoSound.StingerBreath;
            var creature=index>=(int)ProtoSound.MonsterNotice&&index<=(int)ProtoSound.MonsterHit;
            source.minDistance=stinger?6f:creature?3.5f:1.5f;
            source.clip=clip;source.pitch=Mathf.Clamp(pitch,0.55f,1.7f);var global=sound==ProtoSound.TransmitterFull; // the quest-complete chime is heard by everyone, everywhere
            source.spatialBlend=global||distance<0.65f?0f:1f;
            source.volume=Mathf.Clamp01(volume)*0.68f;
            filters[slot].cutoffFrequency=22000f;
            if(!global&&distance>2f&&Physics.Linecast(position,camera.transform.position,out var hit,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)
                &&hit.collider.GetComponentInParent<PlayerHealth>()==null)
            {source.volume*=0.5f;filters[slot].cutoffFrequency=1800f;}
            ends[slot]=Time.unscaledTime+clip.length/source.pitch;source.Play();
            if(sound==ProtoSound.MonsterNotice||sound==ProtoSound.MonsterLunge||sound==ProtoSound.Shatter||sound==ProtoSound.Bang)
                duckUntil=Time.unscaledTime+1.5f;
        }
        private void Loop(int index,float level,Vector3 position)
        {
            var source=loops[index]; source.transform.position=position;
            source.volume=Mathf.MoveTowards(source.volume,level,Time.unscaledDeltaTime*0.12f);
            if(source.clip==null)return;
            if(source.volume>0.001f&&!source.isPlaying)source.Play();
            if(source.volume<=0.001f&&source.isPlaying)source.Stop();
        }
        private void Update()
        {
            var player=PlayerHealth.Local;var camera=Camera.main;
            var active=player!=null&&player.IsSpawned&&camera!=null;
            if(!active)
            {
                for(var i=0;i<loops.Length;i++)Loop(i,0f,transform.position);
                if(wasInRoom){foreach(var voice in voices)voice.Stop();wasInRoom=false;}
                return;
            }
            if(!wasInRoom){wasInRoom=true;nextStinger=Time.unscaledTime+Random.Range(24f,42f);}
            var at=camera.transform.position;var stage=StageDirector.Instance;
            var cabin=stage!=null&&at.x>=stage.ShipMin.x&&at.x<=stage.ShipMax.x&&at.z>=stage.ShipMin.z&&at.z<=stage.ShipMax.z;
            var duck=Time.unscaledTime<duckUntil?0.4f:1f;
            var alive=!player.IsDead.Value;
            Loop(0,(cabin?0.07f:0.14f)*duck*(alive?1f:0.4f),at);
            Loop(1,cabin?0.085f*duck:0f,at);
            Loop(2,0f,at); // (the ship's generator is gone)
            Grabbable nearest=null;var best=15f*15f;
            foreach(var item in Grabbable.All)
            {
                if(item==null||!item.IsHeld)continue;
                var distance=(item.transform.position-at).sqrMagnitude;
                if(distance<best){best=distance;nearest=item;}
            }
            Loop(3,nearest!=null?0.15f:0f,nearest!=null?nearest.transform.position:at);
            if(Time.unscaledTime<nextStinger)return;
            nextStinger=Time.unscaledTime+Random.Range(24f,48f);
            if(cabin||!alive)return;
            var angle=Random.Range(0f,Mathf.PI*2f);var distanceFrom=Random.Range(7f,13f);
            var point=at+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*distanceFrom;
            Play((ProtoSound)((int)ProtoSound.StingerMetal+Random.Range(0,4)),point,0.45f,Random.Range(0.94f,1.04f));
        }
    }
}
