using UnityEngine;

namespace CoOpHorror.Gameplay
{
    // Opening of every stage: the ship is falling onto the ruined Earth. Red alarm beeping and shaking, the
    // impact, a blackout, then emergency power flickers the cabin back to full brightness while the grid
    // reads 0 %. The emergency cells hold the lights for EmergencyHold seconds and then fade out, after which
    // the cabin depends on the generator (ShipLighting steps). Timed from StageDirector.IntroStart
    // (server clock) so everyone sees it together; presentation only. ShipLighting reads the
    // static override values below.
    public sealed class CrashIntro : MonoBehaviour
    {
        public const float AlarmUntil = 3.2f;     // falling: alarm + growing shake
        public const float ImpactAt = 3.2f;
        public const float BlackoutUntil = 5.4f;
        public const float EmergencyUntil = 8.4f; // emergency power, then hand over to the grid
        public const float Duration = 9.2f;
        public const float EmergencyHold = 120f;  // seconds after the crash the emergency cells keep the lights up
        public const float EmergencyFade = 40f;

        // Minimum ship light level from the emergency cells (0..1); ShipLighting uses max(grid step, this).
        public static float EmergencyFloor { get; private set; }

        // -1 = no override; otherwise the ship light level (0..1) to show instead of the grid step.
        public static float LightOverride { get; private set; } = -1f;
        public static bool AlarmOn { get; private set; }
        public static bool Blackout { get; private set; }

        private float nextBeep;
        private static float scareStrength, scareUntil, scareLength;

        // Local jump-scare / big hit: camera jolt that decays (also used by monster hits).
        public static void Scare(float strength, float seconds)
        {
            scareStrength = strength;
            scareLength = seconds;
            scareUntil = Time.time + seconds;
        }
        private bool impacted;
        private Vector3 shakeOffset;
        private Transform shaken;

        public static float Elapsed()
        {
            var stage = StageDirector.Instance;
            if (stage == null || !stage.IsSpawned || stage.IntroStart.Value <= 0d || stage.NetworkManager == null) return -1f;
            return (float)(stage.NetworkManager.ServerTime.Time - stage.IntroStart.Value);
        }

        private void Update()
        {
            var t = Elapsed();
            var running = t >= 0f && t < Duration;
            AlarmOn = running && t < BlackoutUntil;
            Blackout = running && t >= ImpactAt && t < BlackoutUntil;
            EmergencyFloor = t < EmergencyUntil ? 0f : t < EmergencyHold ? 1f : Mathf.Clamp01(1f - (t - EmergencyHold) / EmergencyFade);
            if (!running) { LightOverride = -1f; impacted = false; return; }

            var listener = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            if (t < ImpactAt)
            {
                LightOverride = 0.25f;
                if (Time.time >= nextBeep) { nextBeep = Time.time + 0.5f; ProtoAudio.Play(ProtoSound.Alarm, listener, 0.5f); }
            }
            else if (t < BlackoutUntil)
            {
                LightOverride = 0f;
                if (!impacted) { impacted = true; ProtoAudio.Play(ProtoSound.Bang, listener, 1f); ProtoAudio.Play(ProtoSound.Rumble, listener, 1f); }
            }
            else if (t < EmergencyUntil)
            {
                // Emergency power flickers on and climbs back to full brightness.
                var k = Mathf.Clamp01((t - BlackoutUntil) / (EmergencyUntil - BlackoutUntil));
                LightOverride = Random.value < 0.3f * (1f - k) ? 0f : Mathf.Lerp(0.3f, 1f, k * k);
            }
            else LightOverride = 1f; // ShipLighting keeps the emergency floor afterwards
        }

        // Camera shake on top of the first-person camera (removed again before the next frame's movement).
        private void LateUpdate()
        {
            if (shaken != null) { shaken.localPosition -= shakeOffset; shakeOffset = Vector3.zero; shaken = null; }
            var t = Elapsed();
            if (Camera.main == null) return;
            var strength = 0f;
            if (t >= 0f && t < BlackoutUntil + 1f)
                strength = t < ImpactAt ? Mathf.Lerp(0.01f, 0.06f, t / ImpactAt) : 0.25f * Mathf.Exp(-(t - ImpactAt) * 2.2f);
            if (Time.time < scareUntil) strength = Mathf.Max(strength, scareStrength * (scareUntil - Time.time) / Mathf.Max(0.01f, scareLength));
            if (strength <= 0f) return;
            shaken = Camera.main.transform;
            shakeOffset = Random.insideUnitSphere * strength;
            shaken.localPosition += shakeOffset;
        }
    }
}
