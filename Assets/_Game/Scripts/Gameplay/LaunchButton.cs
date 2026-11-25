using CoOpHorror.Localization;
using CoOpHorror.Player;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    // The ship's launch button. Usable after the blackout; starts the departure countdown.
    public sealed class LaunchButton : Interactable
    {
        public Renderer Lamp;

        private MaterialPropertyBlock block;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        protected override void Awake()
        {
            base.Awake();
            block = new MaterialPropertyBlock();
        }

        public override InteractKind Describe(PlayerInteractor who, out string prompt)
        {
            var stage = StageDirector.Instance;
            var phase = stage != null && stage.IsSpawned ? stage.Phase.Value : StagePhase.Exploring;
            switch (phase)
            {
                case StagePhase.Blackout:
                    prompt = GameText.Get("interact.launch");
                    return InteractKind.Press;
                case StagePhase.Launching:
                    prompt = GameText.Format("interact.launching", Mathf.CeilToInt(stage.LaunchRemaining));
                    return InteractKind.None;
                case StagePhase.Exploring:
                    prompt = GameText.Get("interact.launchLocked");
                    return InteractKind.None;
                default:
                    prompt = string.Empty;
                    return InteractKind.None;
            }
        }

        public override void ServerUse(PlayerInteractor who)
        {
            var stage = StageDirector.Instance;
            if (stage != null && stage.ServerArmLaunch()) ProtoAudio.Play(ProtoSound.Clunk, transform.position, 1f);
        }

        private void Update()
        {
            if (Lamp == null) return;
            var stage = StageDirector.Instance;
            var phase = stage != null && stage.IsSpawned ? stage.Phase.Value : StagePhase.Exploring;
            var color = phase switch
            {
                StagePhase.Blackout => Mathf.PingPong(Time.time * 2f, 1f) > 0.5f ? new Color(0.3f, 1f, 0.4f) : new Color(0.05f, 0.25f, 0.08f),
                StagePhase.Launching => new Color(1f, 0.8f, 0.2f),
                StagePhase.Escaped => new Color(0.3f, 1f, 0.4f),
                _ => new Color(0.25f, 0.04f, 0.03f),
            };
            Lamp.GetPropertyBlock(block);
            block.SetColor(BaseColor, color);
            Lamp.SetPropertyBlock(block);
        }
    }
}
