using System.Collections.Generic;
using CoOpHorror.Localization;
using CoOpHorror.Player;
using Unity.Netcode;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    // A household appliance: charge it from the shared grid (hold the left button; several players can charge
    // together and share a miss penalty), pick it up once charged (left click), carry it and plug it into the
    // transmitter (left click on the transmitter), or drop it (Q).
    // No NetworkTransform: every peer places it from replicated state (rest pose / carrier / socket),
    // so a carried appliance follows the carrier's own smoothly-synced transform.
    public sealed class Appliance : SkillCheckedInteractable
    {
        public const float ChargeRate = 25f;      // power per second per charging player
        public const float FailStall = 2f;
        public const float FailNoiseRadius = 9f;
        public const ulong NoCarrier = ulong.MaxValue;

        public static readonly List<Appliance> All = new();

        public string NameKey = "appliance.tv";
        public float Capacity = 30f;
        public int BaseValue = 100;               // expected settlement value when undamaged
        public Renderer Indicator;
        public Light Glow;

        public readonly NetworkVariable<float> Charge = new(0f);
        public readonly NetworkVariable<ulong> Carrier = new(NoCarrier); // NetworkObjectId of the carrying player
        public readonly NetworkVariable<int> Socket = new(-1);           // transmitter socket index when plugged
        public readonly NetworkVariable<Vector3> RestPosition = new();
        public readonly NetworkVariable<float> RestYaw = new();

        public bool IsCharged => Charge.Value >= Capacity - 0.01f;
        public bool IsCarried => Carrier.Value != NoCarrier;
        public bool IsPlugged => Socket.Value >= 0;
        public int ChargePercent => Mathf.FloorToInt(Mathf.Clamp01(Charge.Value / Capacity) * 100f);
        public string DisplayName => GameText.Get(NameKey);
        public override bool UsesSkillChecks => !IsCharged && !IsCarried && !IsPlugged;

        private Vector3 homePosition;
        private float homeYaw;
        private Vector3 homeScale;
        private Collider[] bodyColliders;
        private Renderer[] renderers;
        private MaterialPropertyBlock block;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly Color Empty = new(0.18f, 0.05f, 0.04f);
        private static readonly Color Full = new(0.35f, 1f, 0.85f);

        protected override void Awake()
        {
            base.Awake();
            All.Add(this);
            homePosition = transform.position;
            homeYaw = transform.eulerAngles.y;
            homeScale = transform.localScale;
            bodyColliders = GetComponentsInChildren<Collider>(true);
            renderers = GetComponentsInChildren<Renderer>(true);
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
            if (!IsServer) return;
            // Every new room starts from the scene layout.
            Charge.Value = 0f;
            Carrier.Value = NoCarrier;
            Socket.Value = -1;
            RestPosition.Value = homePosition;
            RestYaw.Value = homeYaw;
        }

        public override InteractKind Describe(PlayerInteractor who, out string prompt)
        {
            if (IsPlugged) { prompt = GameText.Format("interact.plugged", DisplayName); return InteractKind.None; }
            if (IsCarried) { prompt = string.Empty; return InteractKind.None; }
            if (IsCharged)
            {
                if (who.Carrying != null) { prompt = GameText.Get("interact.handsFull"); return InteractKind.None; }
                prompt = GameText.Format("interact.pickUp", DisplayName, BaseValue);
                return InteractKind.Press;
            }
            if (IsStalled)
            {
                prompt = GameText.Format("interact.chargeStalled", DisplayName, ChargePercent);
                return InteractKind.Hold;
            }
            var grid = PowerGrid.Instance;
            if (grid == null || grid.IsEmpty)
            {
                prompt = GameText.Format("interact.noPower", DisplayName, ChargePercent);
                return InteractKind.None;
            }
            prompt = GameText.Format("interact.charge", DisplayName, ChargePercent);
            return InteractKind.Hold;
        }

        // Charging: pulls from the shared grid while the player holds the button. Stalled = nobody progresses.
        public override bool ServerHold(PlayerInteractor who, float dt)
        {
            if (IsPlugged || IsCarried || IsCharged) return false;
            if (IsStalled) return true;
            var grid = PowerGrid.Instance;
            if (grid == null) return false;
            var taken = grid.ServerTake(Mathf.Min(ChargeRate * dt, Capacity - Charge.Value));
            Charge.Value = Mathf.Min(Capacity, Charge.Value + taken);
            return !IsCharged;
        }

        public override void ServerSkillCheck(PlayerInteractor who, SkillCheckResult result)
        {
            if (IsStalled || IsCharged) return;
            if (result == SkillCheckResult.Miss) ServerFail(FailStall, FailNoiseRadius);
            else if (result == SkillCheckResult.Great) Charge.Value = Mathf.Min(Capacity, Charge.Value + Capacity * 0.05f);
        }

        // Pick up.
        public override void ServerUse(PlayerInteractor who)
        {
            if (!IsCharged || IsCarried || IsPlugged || who.Carrying != null) return;
            Carrier.Value = who.NetworkObjectId;
        }

        public void ServerDrop(PlayerInteractor who)
        {
            if (!IsServer || Carrier.Value != who.NetworkObjectId) return;
            var body = who.transform;
            var eye = who.EyePosition;
            var forward = Vector3.ProjectOnPlane(body.forward, Vector3.up).normalized;
            // Do not push it into a wall in front of the player.
            var distance = 1f;
            if (Physics.Raycast(eye, forward, out var wall, 1.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                distance = Mathf.Max(0f, wall.distance - 0.45f);
            var spot = body.position + forward * distance;
            var feet = body.position.y - 1f;
            if (Physics.Raycast(spot + Vector3.up * 0.9f, Vector3.down, out var ground, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                spot.y = ground.point.y;
            else
                spot.y = feet;
            RestPosition.Value = spot;
            RestYaw.Value = body.eulerAngles.y;
            Carrier.Value = NoCarrier;
        }

        public void ServerPlug(int socket)
        {
            if (!IsServer) return;
            Carrier.Value = NoCarrier;
            Socket.Value = socket;
        }

        // Blackout after the transmission: every appliance still on the map loses its power.
        public void ServerDischarge()
        {
            if (IsServer && !IsPlugged) Charge.Value = 0f;
        }

        public bool TryGetCarrier(out PlayerInteractor carrier)
        {
            carrier = null;
            if (!IsCarried || NetworkManager == null || NetworkManager.SpawnManager == null) return false;
            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(Carrier.Value, out var obj)) return false;
            carrier = obj.GetComponent<PlayerInteractor>();
            return carrier != null;
        }

        // Where this appliance is right now on every peer (also used by the host for flying-object hits).
        public Pose CurrentPose(out float scale, out bool swallowed)
        {
            scale = 1f;
            swallowed = false;
            var transmitter = Transmitter.Instance;
            if (IsPlugged && transmitter != null)
            {
                var socket = transmitter.SocketPose(Socket.Value);
                var t = transmitter.PullProgress;
                if (t <= 0f) return socket;
                // Sucked into the transmitter: accelerate toward the mouth, spin and shrink.
                var eased = t * t;
                scale = Mathf.Lerp(1f, 0.15f, eased);
                swallowed = t >= 1f;
                var spin = Quaternion.Euler(eased * 200f, eased * 540f, 0f);
                return new Pose(Vector3.Lerp(socket.position, transmitter.MouthPosition, eased), socket.rotation * spin);
            }
            if (TryGetCarrier(out var carrier)) return carrier.HoldPose();
            return new Pose(RestPosition.Value, Quaternion.Euler(0f, RestYaw.Value, 0f));
        }

        private void Update()
        {
            if (!IsSpawned) return; // outside a room it stays where it was

            var pose = CurrentPose(out var scale, out var swallowed);
            transform.SetPositionAndRotation(pose.position, pose.rotation);
            transform.localScale = homeScale * scale;
            foreach (var r in renderers) if (r != null) r.enabled = !swallowed;

            // A carried appliance must not push its carrier's CharacterController or block look rays.
            foreach (var c in bodyColliders) if (c != null) c.enabled = !IsCarried && !swallowed;

            if (Indicator != null)
            {
                var t = Mathf.Clamp01(Charge.Value / Capacity);
                Color color;
                if (IsStalled) color = Random.value < 0.4f ? new Color(1f, 0.3f, 0.1f) : Empty;
                else if (IsCharged) color = Color.Lerp(Full, Color.white, Mathf.PingPong(Time.time * 1.5f, 0.35f));
                else color = Color.Lerp(Empty, new Color(0.9f, 0.75f, 0.1f), t);
                Indicator.GetPropertyBlock(block);
                block.SetColor(BaseColor, color);
                Indicator.SetPropertyBlock(block);
            }
            if (Glow != null) Glow.enabled = IsCharged && !swallowed;
        }
    }
}
