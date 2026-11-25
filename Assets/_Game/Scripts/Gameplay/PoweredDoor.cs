using CoOpHorror.Localization;
using CoOpHorror.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace CoOpHorror.Gameplay
{
    // A closed door that opens once it is fed enough shared power (hold the left button).
    // The panel slides up; while closed it also blocks monster paths (NavMeshObstacle).
    public sealed class PoweredDoor : Interactable
    {
        public const float ChargeRate = 20f;
        public const float OpenHeight = 3.9f;
        public const float SlideSpeed = 3f;

        public float Capacity = 8f;
        public Transform Panel;
        public Renderer Indicator;

        public readonly NetworkVariable<float> Charge = new(0f);
        public readonly NetworkVariable<bool> Open = new(false);

        private Vector3 panelHome;
        private float slide;
        private NavMeshObstacle obstacle;
        private MaterialPropertyBlock block;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        public int ChargePercent => Mathf.FloorToInt(Mathf.Clamp01(Charge.Value / Capacity) * 100f);

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
            if (!IsServer) return;
            Charge.Value = 0f;
            Open.Value = false;
        }

        public override InteractKind Describe(PlayerInteractor who, out string prompt)
        {
            if (Open.Value) { prompt = string.Empty; return InteractKind.None; }
            var grid = PowerGrid.Instance;
            if (grid == null || grid.IsEmpty)
            {
                prompt = GameText.Format("interact.doorNoPower", ChargePercent);
                return InteractKind.None;
            }
            prompt = GameText.Format("interact.doorCharge", ChargePercent);
            return InteractKind.Hold;
        }

        public override bool ServerHold(PlayerInteractor who, float dt)
        {
            if (Open.Value) return false;
            var grid = PowerGrid.Instance;
            if (grid == null) return false;
            var taken = grid.ServerTake(Mathf.Min(ChargeRate * dt, Capacity - Charge.Value));
            Charge.Value = Mathf.Min(Capacity, Charge.Value + taken);
            if (Charge.Value < Capacity - 0.01f) return true;
            Open.Value = true;
            return false;
        }

        // Ship leaving: shut it for good.
        public void ServerClose()
        {
            if (!IsServer) return;
            Open.Value = false;
            Charge.Value = 0f;
        }

        private void Update()
        {
            var open = IsSpawned && Open.Value;
            slide = Mathf.MoveTowards(slide, open ? 1f : 0f, SlideSpeed / OpenHeight * Time.deltaTime);
            if (Panel != null) Panel.localPosition = panelHome + Vector3.up * (OpenHeight * slide);
            if (obstacle != null) obstacle.enabled = slide < 0.5f;
            if (Indicator != null)
            {
                var t = IsSpawned ? Mathf.Clamp01(Charge.Value / Capacity) : 0f;
                Indicator.GetPropertyBlock(block);
                block.SetColor(BaseColor, open ? new Color(0.3f, 1f, 0.45f) : Color.Lerp(new Color(0.6f, 0.08f, 0.05f), new Color(1f, 0.8f, 0.2f), t));
                Indicator.SetPropertyBlock(block);
            }
        }
    }
}
