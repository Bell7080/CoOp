using System.Collections.Generic;
using CoOpHorror.Localization;
using CoOpHorror.Player;
using Unity.Netcode;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    // A wall-mounted CCTV camera. Outside players power it once (hold the left button on it or its junction
    // box); from then on it is linked to the ship's CCTV panel. The viewer inside the ship can pan, tilt and
    // zoom it; the host keeps the shared aim so everyone sees the camera head turn. The blackout cuts it off.
    public sealed class CctvCamera : Interactable
    {
        public const float ChargeRate = 15f;
        public const float YawLimit = 80f;
        public const float PitchMin = -20f; // looking up (Euler X is positive downward)
        public const float PitchMax = 65f;  // looking down
        public const float AimSendInterval = 0.1f;

        public static readonly List<CctvCamera> All = new();

        public string NameKey = "cctv.cam1";
        public int Order;
        public float Capacity = 6f;
        public float DefaultYaw;
        public float DefaultPitch = 25f;
        public Transform Head;
        public Transform Lens;
        public Renderer Led;

        public readonly NetworkVariable<float> Charge = new(0f);
        public readonly NetworkVariable<float> Yaw = new(0f);
        public readonly NetworkVariable<float> Pitch = new(0f);

        public bool Online => IsSpawned && Charge.Value >= Capacity - 0.01f;
        public int ChargePercent => Mathf.FloorToInt(Mathf.Clamp01(Charge.Value / Capacity) * 100f);
        public string DisplayName => GameText.Get(NameKey);

        // Local viewer override so the view turns without waiting for the host.
        private bool localAim;
        private float localYaw, localPitch;
        private float nextSend;
        private MaterialPropertyBlock block;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        public static int OnlineCount
        {
            get
            {
                var count = 0;
                foreach (var c in All) if (c.Online) count++;
                return count;
            }
        }

        protected override void Awake()
        {
            base.Awake();
            All.Add(this);
            All.Sort((a, b) => a.Order.CompareTo(b.Order));
            block = new MaterialPropertyBlock();
        }

        public override void OnDestroy()
        {
            All.Remove(this);
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            localAim = false;
            if (!IsServer) return;
            Charge.Value = 0f;
            Yaw.Value = DefaultYaw;
            Pitch.Value = DefaultPitch;
        }

        public override InteractKind Describe(PlayerInteractor who, out string prompt)
        {
            if (Online) { prompt = GameText.Format("interact.cctvOnline", DisplayName); return InteractKind.None; }
            var grid = PowerGrid.Instance;
            if (grid == null || grid.IsEmpty)
            {
                prompt = GameText.Format("interact.cctvNoPower", DisplayName, ChargePercent);
                return InteractKind.None;
            }
            prompt = GameText.Format("interact.cctvCharge", DisplayName, ChargePercent);
            return InteractKind.Hold;
        }

        public override bool ServerHold(PlayerInteractor who, float dt)
        {
            if (Online) return false;
            var grid = PowerGrid.Instance;
            if (grid == null) return false;
            var taken = grid.ServerTake(Mathf.Min(ChargeRate * dt, Capacity - Charge.Value));
            Charge.Value = Mathf.Min(Capacity, Charge.Value + taken);
            return !Online;
        }

        // Blackout: the camera loses its power like every other device.
        public void ServerDischarge()
        {
            if (IsServer) Charge.Value = 0f;
        }

        public float CurrentYaw => localAim ? localYaw : Yaw.Value;
        public float CurrentPitch => localAim ? localPitch : Pitch.Value;

        // Viewer (any client): turn the camera now and tell the host at a limited rate.
        public void LocalAim(float yaw, float pitch)
        {
            localAim = true;
            localYaw = Mathf.Clamp(yaw, DefaultYaw - YawLimit, DefaultYaw + YawLimit);
            localPitch = Mathf.Clamp(pitch, PitchMin, PitchMax);
            if (!IsSpawned || Time.unscaledTime < nextSend) return;
            nextSend = Time.unscaledTime + AimSendInterval;
            AimRpc(localYaw, localPitch);
        }

        public void ReleaseLocalAim()
        {
            if (!localAim) return;
            localAim = false;
            if (IsSpawned) AimRpc(localYaw, localPitch);
        }

        public float ClampYaw(float yaw) => Mathf.Clamp(yaw, DefaultYaw - YawLimit, DefaultYaw + YawLimit);
        public float ClampPitch(float pitch) => Mathf.Clamp(pitch, PitchMin, PitchMax);

        // Only a living player inside the ship (at the panel) may turn a powered camera.
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void AimRpc(float yaw, float pitch, RpcParams rpcParams = default)
        {
            if (!Online || float.IsNaN(yaw) || float.IsNaN(pitch)) return;
            var sender = rpcParams.Receive.SenderClientId;
            if (!NetworkManager.ConnectedClients.TryGetValue(sender, out var client) || client.PlayerObject == null) return;
            var health = client.PlayerObject.GetComponent<PlayerHealth>();
            if (health == null || health.IsDead.Value) return;
            var stage = StageDirector.Instance;
            if (stage != null && !stage.InShip(client.PlayerObject.transform.position)) return;
            Yaw.Value = ClampYaw(yaw);
            Pitch.Value = ClampPitch(pitch);
        }

        private void Update()
        {
            if (Head != null)
            {
                var target = Quaternion.Euler(IsSpawned ? CurrentPitch : DefaultPitch, IsSpawned ? CurrentYaw : DefaultYaw, 0f);
                Head.localRotation = localAim ? target : Quaternion.Slerp(Head.localRotation, target, 1f - Mathf.Exp(-10f * Time.deltaTime));
            }
            if (Led == null) return;
            var color = Online ? (Mathf.PingPong(Time.time, 1f) > 0.5f ? new Color(1f, 0.1f, 0.05f) : new Color(0.3f, 0.02f, 0.02f)) : new Color(0.05f, 0.05f, 0.05f);
            Led.GetPropertyBlock(block);
            block.SetColor(BaseColor, color);
            Led.SetPropertyBlock(block);
        }
    }
}
