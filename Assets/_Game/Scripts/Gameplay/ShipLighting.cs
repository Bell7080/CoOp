using UnityEngine;

namespace CoOpHorror.Gameplay
{
    // The ship's interior lights: after the crash-landing opening (CrashIntro) they fade up to full and stay
    // there (there is no shared power grid any more). Presentation only.
    public sealed class ShipLighting : MonoBehaviour
    {
        public Light[] Lights;
        public float FadeSpeed = 0.8f; // brightness per second

        private float[] full;
        private float level;
        private float lastStep = -1f;
        private float flickerUntil;

        private void Awake()
        {
            full = new float[Lights != null ? Lights.Length : 0];
            for (var i = 0; i < full.Length; i++)
                if (Lights[i] != null) full[i] = Lights[i].intensity;
            Apply(0f, false);
        }

        public static float StepFor(float fraction)
        {
            if (fraction >= 0.999f) return 1f;
            if (fraction >= 0.7f) return 0.7f;
            if (fraction >= 0.5f) return 0.5f;
            if (fraction >= 0.3f) return 0.3f;
            return 0f;
        }

        private void Update()
        {
            var step = 1f;
            if (!Mathf.Approximately(step, lastStep))
            {
                if (lastStep >= 0f) flickerUntil = Time.time + 0.35f;
                lastStep = step;
            }
            if (CrashIntro.LightOverride >= 0f) level = CrashIntro.LightOverride; // the crash-landing opening
            else level = Mathf.MoveTowards(level, Mathf.Max(step, CrashIntro.EmergencyFloor), FadeSpeed * Time.deltaTime);
            Apply(level, Time.time < flickerUntil);
        }

        private void Apply(float value, bool flicker)
        {
            if (Lights == null) return;
            // (no flicker: lights change smoothly)
            for (var i = 0; i < Lights.Length; i++)
            {
                var light = Lights[i];
                if (light == null) continue;
                if (light.name == "ShipAlarm" && CrashIntro.AlarmOn)
                {
                    // red alarm strobing while the ship falls
                    light.intensity = full[i] * 3f * (Mathf.PingPong(Time.time * 4f, 1f) > 0.5f ? 1f : 0.05f);
                    light.enabled = !CrashIntro.Blackout;
                    continue;
                }
                light.intensity = full[i] * value;
                light.enabled = value > 0.01f;
            }
        }
    }
}
