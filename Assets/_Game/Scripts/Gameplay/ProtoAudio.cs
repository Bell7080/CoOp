using System.Collections.Generic;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    public enum ProtoSound
    {
        Beep, Click, Buzz, Bang, Zap, Clunk, Pop, Alarm, Rumble, Shatter,
        MonsterNotice, MonsterLunge, MonsterHit, FootstepConcrete, FootstepMetal,
        Jump, Land, DoorMove, FlashSwitch, PlugIn, PlugOut, PlayerHurt, PlayerDeath,
        Revive, Radio, GeneratorCrank, Delivery, StingerMetal, StingerStatic,
        StingerScrape, StingerBreath, AmbientBed, ShipHum, TelekinesisLoop, GeneratorLoop
    }

    // All clips are original ElevenLabs generations; gameplay authority never lives in this audio facade.
    public static class ProtoAudio
    {
        private static readonly Dictionary<ProtoSound, AudioClip> Clips = new();
        public static AudioClip Clip(ProtoSound sound)
        {
            if (Clips.TryGetValue(sound, out var clip) && clip != null) return clip;
            clip = Resources.Load<AudioClip>("Audio/ElevenLabs/" + sound);
            if (clip != null) Clips[sound] = clip;
            return clip;
        }
        public static void Play(ProtoSound sound, Vector3 position, float volume) => PlayPitched(sound, position, volume, 1f);
        public static void PlayPitched(ProtoSound sound, Vector3 position, float volume, float pitch)
        {
            if (Application.isPlaying) HorrorAudio.Ensure().Play(sound, position, volume, pitch);
        }
    }
}
