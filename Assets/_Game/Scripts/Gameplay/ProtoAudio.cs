using System.Collections.Generic;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    public enum ProtoSound { Beep, Click, Buzz, Bang, Zap, Clunk }

    // Placeholder sounds generated in code until real audio exists. Presentation only.
    public static class ProtoAudio
    {
        private const int Rate = 22050;
        private static readonly Dictionary<ProtoSound, AudioClip> Clips = new();

        public static void Play(ProtoSound sound, Vector3 position, float volume)
        {
            if (!Application.isPlaying) return;
            AudioSource.PlayClipAtPoint(Clip(sound), position, volume);
        }

        private static AudioClip Clip(ProtoSound sound)
        {
            if (Clips.TryGetValue(sound, out var cached) && cached != null) return cached;
            var clip = sound switch
            {
                ProtoSound.Beep => Make("Beep", 0.14f, t => Tone(t, 880f) * Fade(t, 0.14f)),
                ProtoSound.Click => Make("Click", 0.08f, t => Tone(t, 1320f) * Fade(t, 0.08f)),
                ProtoSound.Buzz => Make("Buzz", 0.35f, t => Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 110f * t)) * 0.5f * Fade(t, 0.35f)),
                ProtoSound.Bang => Make("Bang", 0.9f, t => (Random.value * 2f - 1f) * Mathf.Exp(-t * 6f) + Tone(t, 55f) * Mathf.Exp(-t * 4f) * 0.6f),
                ProtoSound.Zap => Make("Zap", 0.25f, t => (Random.value * 2f - 1f) * 0.6f * Fade(t, 0.25f) + Tone(t, 2200f - t * 6000f) * 0.4f),
                _ => Make("Clunk", 0.4f, t => Tone(t, 90f) * Mathf.Exp(-t * 9f) + (Random.value * 2f - 1f) * Mathf.Exp(-t * 30f) * 0.5f),
            };
            Clips[sound] = clip;
            return clip;
        }

        private static float Tone(float t, float frequency) => Mathf.Sin(2f * Mathf.PI * frequency * t);
        private static float Fade(float t, float length) => Mathf.Clamp01(1f - t / length);

        private static AudioClip Make(string name, float seconds, System.Func<float, float> wave)
        {
            var samples = new float[Mathf.CeilToInt(seconds * Rate)];
            for (var i = 0; i < samples.Length; i++) samples[i] = Mathf.Clamp(wave(i / (float)Rate), -1f, 1f) * 0.8f;
            var clip = AudioClip.Create("Proto" + name, samples.Length, 1, Rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
