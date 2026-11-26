using CoOpHorror.Localization;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    // A ceiling lamp hanging low on its cord. On from the start; the more of its power noses drink, the dimmer
    // it gets (with a nervous flicker near the end) until it goes dark. Lit lamps also clear the dark vision
    // around them (WorldVision.hlsl), so a lit area can be seen from far away.
    public sealed class PowerLamp : InstalledDevice
    {
        public const int MaxShaderLamps = 16;
        public const float VisionReach = 6f;   // metres of cleared dark around a fully lit lamp

        public string NameKey = "lamp.ceiling";
        public Light Bulb;
        public Renderer Glass;
        public GameObject Fixture;

        public const float Brightness = 0.65f;   // scale on the scene bulb (lamps were too bright)
        private MaterialPropertyBlock block;
        private float fullIntensity;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int LampsId = Shader.PropertyToID("_ElecLamps");
        private static readonly int LampCountId = Shader.PropertyToID("_ElecLampCount");
        private static readonly Vector4[] Packed = new Vector4[MaxShaderLamps];
        private static readonly System.Collections.Generic.List<PowerLamp> Lit = new();
        private static int publishedFrame = -1;
        private static int publishedCount;

        public override string DisplayName => GameText.Get(NameKey);

        // brightness: falls faster than the power (a half-drained lamp is clearly dim), flickers when low
        public float Level
        {
            get
            {
                var f = Online ? Fraction : 0f;
                var level = f * f;
                if (f > 0f && f < 0.35f) level *= 0.6f + 0.4f * Mathf.PerlinNoise(Time.time * 9f, transform.position.x);
                return level;
            }
        }

        private Vector3 BulbPosition => Bulb != null ? Bulb.transform.position : transform.position;

        protected override void Awake()
        {
            base.Awake();
            block = new MaterialPropertyBlock();
            if (Bulb != null) fullIntensity = Bulb.intensity * Brightness;
        }

        private void Update()
        {
            Publish();
            var level = Level;
            if (Bulb != null)
            {
                Bulb.enabled = level > 0.01f;
                Bulb.intensity = fullIntensity * level;
            }
            if (Glass == null) return;
            Glass.GetPropertyBlock(block);
            block.SetColor(BaseColor, Color.Lerp(new Color(0.08f, 0.08f, 0.07f), new Color(1f, 0.9f, 0.65f), level));
            Glass.SetPropertyBlock(block);
        }

        // Once a frame: the lit lamps nearest the camera go to the vision shader.
        private static void Publish()
        {
            if (publishedFrame == Time.frameCount) return;
            publishedFrame = Time.frameCount;
            var cam = Camera.main;
            var eye = cam != null ? cam.transform.position : Vector3.zero;
            Lit.Clear();
            foreach (var d in Devices)
                if (d is PowerLamp lamp && lamp.IsSpawned && lamp.Online) Lit.Add(lamp);
            Lit.Sort((a, b) => (a.BulbPosition - eye).sqrMagnitude.CompareTo((b.BulbPosition - eye).sqrMagnitude));
            publishedCount = Mathf.Min(Lit.Count, MaxShaderLamps);
            for (var i = 0; i < MaxShaderLamps; i++)
            {
                if (i < publishedCount)
                {
                    var p = Lit[i].BulbPosition;
                    Packed[i] = new Vector4(p.x, p.y, p.z, VisionReach * Mathf.Sqrt(Lit[i].Level));
                }
                else Packed[i] = Vector4.zero;
            }
            Shader.SetGlobalVectorArray(LampsId, Packed);
            Shader.SetGlobalFloat(LampCountId, publishedCount);
        }

        // Same as the shader's ElecLampLight (HUD labels use it so they show in lit areas too).
        public static float LightAt(Vector3 position)
        {
            var lit = 0f;
            for (var i = 0; i < publishedCount; i++)
            {
                var reach = Packed[i].w;
                var near = Mathf.Clamp01(1f - Vector3.Distance(position, Packed[i]) / Mathf.Max(reach, 0.01f));
                lit = Mathf.Max(lit, near * near * (3f - 2f * near));
            }
            return lit;
        }
    }
}
