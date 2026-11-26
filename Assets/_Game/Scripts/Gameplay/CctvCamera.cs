using System.Collections.Generic;
using CoOpHorror.Localization;
using CoOpHorror.Player;
using Unity.Netcode;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    // A wall-mounted CCTV camera, linked to the ship's CCTV panel from the start. The viewer inside the ship
    // can pan, tilt and zoom it; the host keeps the shared aim so everyone sees the camera head turn. Noses
    // can drink its power (InstalledDevice): the more is gone, the more static on its feed; empty, it is dead.
    // The blackout cuts it off.
    public sealed class CctvCamera : InstalledDevice
    {
        public const float YawLimit = 80f;
        public const float PitchMin = -20f; // looking up (Euler X is positive downward)
        public const float PitchMax = 65f;  // looking down
        public const float AimSendInterval = 0.1f;
        public const int LiveAtStart = 2;   // cameras picked at random to be powered when the stage starts

        public static readonly List<CctvCamera> All = new();

        public string NameKey = "cctv.cam1";
        public int Order;
        public float DefaultYaw;
        public float DefaultPitch = 25f;
        public Transform Head;
        public GameObject Mount;   // the camera model fixed on the wall bracket (gone with the head when ripped off)
        public Transform Lens;
        public Renderer Led;
        public Light Spot;    // the camera's own lamp; lit only while the CCTV view renders through it
        public Light Fill;    // soft night-vision fill around the camera, also only for the CCTV render
        public Vector3 RoomMin, RoomMax; // floor area of the room it watches (CCTV minimap)

        public readonly NetworkVariable<float> Yaw = new(0f);
        public readonly NetworkVariable<float> Pitch = new(0f);

        public override string DisplayName => GameText.Get(NameKey);

        // Static on its feed: none at full power, heavy just before it dies, full when it is dead.
        public float FeedStatic => Online ? (1f - Fraction) * 0.85f : 1f;

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
            base.OnNetworkSpawn();
            localAim = false;
            if (!IsServer) return;
            // Only LiveAtStart random cameras start powered; the rest start dead until a nose pumps power in.
            Power.Value = PickedLive(this) ? Watts : 0f;
            Yaw.Value = DefaultYaw;
            Pitch.Value = DefaultPitch;
        }

        // Host: the random pick of live cameras, redrawn once per spawn wave (scene objects spawn together in
        // one frame, also on the dev quick restart).
        private static readonly HashSet<CctvCamera> livePick = new();
        private static int pickFrame = -1;

        private static bool PickedLive(CctvCamera camera)
        {
            if (pickFrame != Time.frameCount)
            {
                pickFrame = Time.frameCount;
                livePick.Clear();
                var pool = new List<CctvCamera>();
                foreach (var c in All) if (c != null) pool.Add(c);
                for (var n = 0; n < LiveAtStart && pool.Count > 0; n++)
                {
                    var i = Random.Range(0, pool.Count);
                    livePick.Add(pool[i]);
                    pool.RemoveAt(i);
                }
                var names = new System.Text.StringBuilder();
                foreach (var c in livePick) names.Append(c.name).Append(' ');
                Debug.Log("[ElecMole] CCTV live at start: " + names);
            }
            return livePick.Contains(camera);
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
