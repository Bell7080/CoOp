using CoOpHorror.Localization;
using CoOpHorror.Player;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    // The ship's launch button. Works once the transmitter is full: fires the transmitter (blackout) and starts
    // the departure countdown. Before that it only says what is missing.
    public sealed class LaunchButton : Interactable
    {
        public Renderer Lamp;
        public Transform Cap;              // the red mushroom button of the model: sinks in when pressed
        public float PressDepth = 0.07f;   // metres

        private MaterialPropertyBlock block;
        private Vector3 capHome;
        private float press;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        protected override void Awake()
        {
            base.Awake();
            block = new MaterialPropertyBlock();
            if (Cap != null) capHome = Cap.localPosition;
        }

        public override InteractKind Describe(PlayerInteractor who, out string prompt)
        {
            var stage = StageDirector.Instance;
            var phase = stage != null && stage.IsSpawned ? stage.Phase.Value : StagePhase.Exploring;
            switch (phase)
            {

                case StagePhase.Launching:
                    prompt = GameText.Format("interact.launching", Mathf.CeilToInt(stage.LaunchRemaining));
                    return InteractKind.None;
                case StagePhase.Exploring:
                    if (stage != null && stage.LaunchReady)
                    {
                        prompt = GameText.Get("interact.launch");
                        return InteractKind.Press;
                    }
                    var tx = Transmitter.Instance;
                    prompt = tx != null ? GameText.Format("interact.launchLocked", tx.StoredWatts, tx.RequiredWatts) : string.Empty;
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
            var stage = StageDirector.Instance;
            var phase = stage != null && stage.IsSpawned ? stage.Phase.Value : StagePhase.Exploring;
            // pushed in once the launch is armed (everyone sees it go down), and it stays in
            if (Cap != null)
            {
                var down = phase == StagePhase.Launching || phase == StagePhase.Escaped;
                press = Mathf.MoveTowards(press, down ? 1f : 0f, Time.deltaTime * (down ? 12f : 3f));
                Cap.localPosition = capHome + Cap.parent.InverseTransformVector(Vector3.down * (PressDepth * press));
            }
            if (Lamp == null) return;
            var ready = stage != null && stage.LaunchReady;
            var color = phase switch
            {
                StagePhase.Exploring when ready => Mathf.PingPong(Time.time * 2f, 1f) > 0.5f ? new Color(0.3f, 1f, 0.4f) : new Color(0.05f, 0.25f, 0.08f),
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
