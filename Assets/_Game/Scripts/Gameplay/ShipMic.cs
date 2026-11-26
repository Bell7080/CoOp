using CoOpHorror.Localization;
using CoOpHorror.Player;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    // The ship's broadcast microphone. Look at it and hold V: every player hears you like an earpiece.
    public sealed class ShipMic : Interactable
    {
        public static ShipMic Instance { get; private set; }

        public Renderer Lamp;

        private MaterialPropertyBlock block;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        protected override void Awake()
        {
            base.Awake();
            Instance = this;
            block = new MaterialPropertyBlock();
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        public override InteractKind Describe(PlayerInteractor who, out string prompt)
        {
            prompt = GameText.Get("interact.shipMic");
            return InteractKind.None;
        }

        private void Update()
        {
            if (Lamp == null) return;
            var live = false;
            foreach (var p in PlayerHealth.All)
                if (p != null && p.TryGetComponent<PlayerRadio>(out var radio) && radio.IsSpawned && radio.Mode.Value == RadioMode.ShipMic) live = true;
            Lamp.GetPropertyBlock(block);
            block.SetColor(BaseColor, live ? new Color(1f, 0.15f, 0.1f) : new Color(0.25f, 0.05f, 0.04f));
            Lamp.SetPropertyBlock(block);
        }
    }
}
