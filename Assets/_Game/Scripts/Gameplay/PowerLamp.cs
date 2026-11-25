using CoOpHorror.Localization;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    // A ceiling lamp. Charged once, it lights its area for the rest of the stage (no further drain) until
    // the blackout. It can be ripped off and sold (InstalledDevice).
    public sealed class PowerLamp : InstalledDevice
    {
        public string NameKey = "lamp.ceiling";
        public Light Bulb;
        public Renderer Glass;
        public GameObject Fixture;     // hidden once ripped off

        private MaterialPropertyBlock block;
        private float flickerUntil;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        public override string DisplayName => GameText.Get(NameKey);
        protected override string OnlineLabel => GameText.Format("interact.lampOn", DisplayName);

        protected override void Awake()
        {
            base.Awake();
            block = new MaterialPropertyBlock();
        }

        private void Update()
        {
            var on = Online;
            if (Fixture != null) Fixture.SetActive(!IsSpawned || !Detached.Value);
            if (Bulb != null)
            {
                // Comes on with a few flickers.
                if (on && !Bulb.enabled) flickerUntil = Time.time + 0.6f;
                Bulb.enabled = on && (Time.time > flickerUntil || Random.value > 0.5f);
            }
            if (Glass == null) return;
            var charge = IsSpawned ? Mathf.Clamp01(Charge.Value / Capacity) : 0f;
            Glass.GetPropertyBlock(block);
            block.SetColor(BaseColor, on ? new Color(1f, 0.9f, 0.65f) : Color.Lerp(new Color(0.08f, 0.08f, 0.07f), new Color(0.5f, 0.4f, 0.2f), charge));
            Glass.SetPropertyBlock(block);
        }
    }
}
