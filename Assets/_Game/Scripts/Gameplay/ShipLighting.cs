using UnityEngine;

namespace CoOpHorror.Gameplay
{
    // The ship's interior lights follow the shared power grid in steps: dark below 30 %, then 30 / 50 / 70 /
    // 100 % brightness as the grid fills (and back down as it drains). Changes fade in and out over a second
    // or so, with a short flicker when a step is crossed. Runs on every peer from the replicated grid level;
    // presentation only.
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
            var grid = PowerGrid.Instance;
            var step = grid != null && grid.IsSpawned ? StepFor(grid.Fraction) : 0f;
            if (!Mathf.Approximately(step, lastStep))
            {
                if (lastStep >= 0f) flickerUntil = Time.time + 0.35f;
                lastStep = step;
            }
            level = Mathf.MoveTowards(level, step, FadeSpeed * Time.deltaTime);
            Apply(level, Time.time < flickerUntil);
        }

        private void Apply(float value, bool flicker)
        {
            if (Lights == null) return;
            if (flicker && Random.value < 0.4f) value *= 0.3f;
            for (var i = 0; i < Lights.Length; i++)
            {
                var light = Lights[i];
                if (light == null) continue;
                light.intensity = full[i] * value;
                light.enabled = value > 0.01f;
            }
        }
    }
}
