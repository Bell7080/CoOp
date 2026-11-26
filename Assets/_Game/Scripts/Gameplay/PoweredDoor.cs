using CoOpHorror.Localization;
using CoOpHorror.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace CoOpHorror.Gameplay
{
    // A sliding door worked from the buttons beside it (one inside, one outside): a click opens or closes it,
    // any time (not while someone stands in the doorway; no power needed any more). The panel slides up; while
    // closed it also blocks monster paths (NavMeshObstacle). Once the ship has left it stays shut.
    public sealed class PoweredDoor : Interactable
    {
        public const float OpenHeight = 3.9f;
        public const float SlideSpeed = 3f;
        public const float DoorwayClearance = 1.1f;

        public Transform Panel;
        public Renderer Indicator;            // inside button
        public Renderer OutsideIndicator;     // outside button
        public Collider[] Buttons;            // only these target the door; the panel is just a wall

        public readonly NetworkVariable<bool> Open = new(false);

        private Vector3 panelHome;
        private float slide;
        private NavMeshObstacle obstacle;
        private MaterialPropertyBlock block;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        private static bool Sealed => StageDirector.Instance != null && StageDirector.Instance.IsSpawned && StageDirector.Instance.Phase.Value == StagePhase.Escaped;

        public override void OnNetworkDespawn()
        {
            Open.OnValueChanged -= DoorSound;
            base.OnNetworkDespawn();
        }

        private void DoorSound(bool previous, bool current)
        {
            if (previous != current) ProtoAudio.Play(ProtoSound.DoorMove, transform.position + Vector3.up, 0.7f);
        }

        public override bool TargetedBy(Collider c) => Buttons == null || Buttons.Length == 0 || System.Array.IndexOf(Buttons, c) >= 0;

        protected override void Awake()
        {
            base.Awake();
            if (Panel != null)
            {
                panelHome = Panel.localPosition;
                obstacle = Panel.GetComponent<NavMeshObstacle>();
            }
            block = new MaterialPropertyBlock();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Open.OnValueChanged += DoorSound;
            if (IsServer) Open.Value = false;
        }

        public override InteractKind Describe(PlayerInteractor who, out string prompt)
        {
            if (Sealed) { prompt = string.Empty; return InteractKind.None; }
            if (Open.Value && SomeoneInDoorway()) { prompt = GameText.Get("interact.doorBlocked"); return InteractKind.None; }
            prompt = GameText.Get(Open.Value ? "interact.doorClose" : "interact.doorOpen");
            return InteractKind.Press;
        }

        public override void ServerUse(PlayerInteractor who)
        {
            if (Sealed || (Open.Value && SomeoneInDoorway())) return;
            Open.Value = !Open.Value;
        }

        private bool SomeoneInDoorway()
        {
            foreach (var p in PlayerHealth.All)
            {
                if (p == null || !p.IsSpawned || p.IsDead.Value) continue;
                var offset = p.transform.position - transform.position;
                offset.y = 0f;
                if (offset.magnitude < DoorwayClearance) return true;
            }
            return false;
        }

        // Ship leaving: shut it for good.
        public void ServerClose()
        {
            if (IsServer) Open.Value = false;
        }

        private void Update()
        {
            var open = IsSpawned && Open.Value;
            slide = Mathf.MoveTowards(slide, open ? 1f : 0f, SlideSpeed / OpenHeight * Time.deltaTime);
            if (Panel != null) Panel.localPosition = panelHome + Vector3.up * (OpenHeight * slide);
            if (obstacle != null) obstacle.enabled = slide < 0.5f;
            var color = open ? new Color(0.3f, 1f, 0.45f) : new Color(1f, 0.55f, 0.15f);
            foreach (var indicator in new[] { Indicator, OutsideIndicator })
            {
                if (indicator == null) continue;
                indicator.GetPropertyBlock(block);
                block.SetColor(BaseColor, color);
                indicator.SetPropertyBlock(block);
            }
        }
    }
}
