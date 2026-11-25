using System.Collections.Generic;
using CoOpHorror.Localization;
using CoOpHorror.Player;
using Unity.Netcode;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    public enum TransmitterPhase : byte { Dormant, Active, Settling, Sinking, Gone }

    // Power it first (hold, with skill checks), then plug charged appliances' cords into every socket.
    // When the last socket is filled: the plugged appliances are yanked in along their cords, the transmitter
    // sinks into the ground and the map blacks out. Socket use is derived from each CordPlug (single source
    // of truth); the phase clock is host-written and every peer animates from it.
    public sealed class Transmitter : SkillCheckedInteractable
    {
        public const float ChargeRate = 20f;
        public const float FailStall = 2.5f;
        public const float FailNoiseRadius = 12f;
        public const float PullDuration = 2.6f;
        public const float SinkDuration = 3.5f;
        public const float SinkDepth = 5.5f;
        public const float FlyingHitRadius = 0.9f;
        public const int FlyingHitDamage = 15;

        public static Transmitter Instance { get; private set; }

        public float Capacity = 30f;
        public Transform Body;
        public Transform Mouth;
        public GameObject Hole;
        public Transform[] Sockets;
        public Renderer[] SocketLamps;
        public Renderer PowerLamp;
        public Light Beacon;

        public readonly NetworkVariable<float> Charge = new(0f);
        public readonly NetworkVariable<TransmitterPhase> Phase = new(TransmitterPhase.Dormant);
        public readonly NetworkVariable<double> PhaseStart = new(0d);

        private MaterialPropertyBlock block;
        private Vector3 bodyHome;
        private Collider[] bodyColliders;
        private readonly HashSet<(ulong, ulong)> flyingHits = new();
        private int settledValue;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        public int Required => Sockets != null ? Sockets.Length : 0;
        public int ChargePercent => Mathf.FloorToInt(Mathf.Clamp01(Charge.Value / Capacity) * 100f);
        public bool IsActive => Phase.Value == TransmitterPhase.Active;
        public float PhaseElapsed => IsSpawned ? (float)(NetTime - PhaseStart.Value) : 0f;
        public Vector3 MouthPosition => Mouth != null ? Mouth.position : transform.position + Vector3.up * 1.5f;
        public override bool UsesSkillChecks => Phase.Value == TransmitterPhase.Dormant;

        // 0..1 while plugged appliances fly into the mouth; 1 afterwards.
        public float PullProgress => !IsSpawned ? 0f
            : Phase.Value == TransmitterPhase.Settling ? Mathf.Clamp01(PhaseElapsed / PullDuration)
            : Phase.Value > TransmitterPhase.Settling ? 1f : 0f;

        public int PluggedCount
        {
            get
            {
                var count = 0;
                foreach (var p in CordPlug.All) if (p.IsSocketed) count++;
                return count;
            }
        }

        // Settlement estimate: current value of everything plugged in.
        public int PluggedValue
        {
            get
            {
                var sum = 0;
                foreach (var p in CordPlug.All) if (p.IsSocketed && p.Appliance != null) sum += p.Appliance.Value.Value;
                return sum;
            }
        }

        public bool IsComplete => Required > 0 && PluggedCount >= Required;

        protected override void Awake()
        {
            base.Awake();
            Instance = this;
            block = new MaterialPropertyBlock();
            if (Body != null)
            {
                bodyHome = Body.localPosition;
                bodyColliders = Body.GetComponentsInChildren<Collider>(true);
            }
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (!IsServer) return;
            Charge.Value = 0f;
            Phase.Value = TransmitterPhase.Dormant;
            PhaseStart.Value = NetTime;
            flyingHits.Clear();
        }

        public Pose SocketPose(int index)
        {
            if (Sockets == null || index < 0 || index >= Sockets.Length || Sockets[index] == null) return new Pose(transform.position, transform.rotation);
            return new Pose(Sockets[index].position, Sockets[index].rotation);
        }

        private bool SocketUsed(int index)
        {
            foreach (var p in CordPlug.All) if (p.IsSocketed && p.Socket.Value == index) return true;
            return false;
        }

        private int FreeSocket()
        {
            for (var i = 0; i < Required; i++) if (!SocketUsed(i)) return i;
            return -1;
        }

        public override InteractKind Describe(PlayerInteractor who, out string prompt)
        {
            switch (Phase.Value)
            {
                case TransmitterPhase.Dormant:
                    if (IsStalled) { prompt = GameText.Format("interact.transmitterStalled", ChargePercent); return InteractKind.Hold; }
                    var grid = PowerGrid.Instance;
                    if (grid == null || grid.IsEmpty) { prompt = GameText.Format("interact.transmitterNoPower", ChargePercent); return InteractKind.None; }
                    prompt = GameText.Format("interact.transmitterCharge", ChargePercent);
                    return InteractKind.Hold;
                case TransmitterPhase.Active:
                    var plug = who.HeldPlug;
                    if (plug != null && plug.Appliance != null)
                    {
                        if (!plug.Appliance.IsCharged)
                        {
                            prompt = GameText.Format("interact.plugNotCharged", plug.DisplayName);
                            return InteractKind.None;
                        }
                        prompt = GameText.Format("interact.plug", plug.DisplayName);
                        return InteractKind.Press;
                    }
                    prompt = GameText.Format("interact.transmitterStatus", PluggedCount, Required, PluggedValue);
                    return InteractKind.None;
                default:
                    prompt = GameText.Get("interact.transmitterDone");
                    return InteractKind.None;
            }
        }

        // Powering it up.
        public override bool ServerHold(PlayerInteractor who, float dt)
        {
            if (Phase.Value != TransmitterPhase.Dormant) return false;
            if (IsStalled) return true;
            var grid = PowerGrid.Instance;
            if (grid == null) return false;
            var taken = grid.ServerTake(Mathf.Min(ChargeRate * dt, Capacity - Charge.Value));
            Charge.Value = Mathf.Min(Capacity, Charge.Value + taken);
            if (Charge.Value < Capacity - 0.01f) return true;
            ServerSetPhase(TransmitterPhase.Active);
            return false;
        }

        public override void ServerSkillCheck(PlayerInteractor who, SkillCheckResult result)
        {
            if (Phase.Value != TransmitterPhase.Dormant || IsStalled) return;
            if (result == SkillCheckResult.Miss) ServerFail(FailStall, FailNoiseRadius);
            else if (result == SkillCheckResult.Great) Charge.Value = Mathf.Min(Capacity - 0.02f, Charge.Value + Capacity * 0.05f);
        }

        // Plug in the carried cord.
        public override void ServerUse(PlayerInteractor who)
        {
            if (Phase.Value != TransmitterPhase.Active) return;
            var plug = who.HeldPlug;
            if (plug == null || plug.Appliance == null || !plug.Appliance.IsCharged) return;
            var socket = FreeSocket();
            if (socket < 0) return;
            plug.ServerSocket(socket);
            ProtoAudio.Play(ProtoSound.Clunk, transform.position, 1f);
            if (!IsComplete) return;
            Debug.Log("[ElecMole] Transmitter complete; settling");
            settledValue = PluggedValue;
            flyingHits.Clear();
            ServerSetPhase(TransmitterPhase.Settling);
        }

        private void ServerSetPhase(TransmitterPhase phase)
        {
            Phase.Value = phase;
            PhaseStart.Value = NetTime;
        }

        private void ServerTick()
        {
            switch (Phase.Value)
            {
                case TransmitterPhase.Settling:
                    ServerFlyingHits();
                    if (PhaseElapsed >= PullDuration) ServerSetPhase(TransmitterPhase.Sinking);
                    break;
                case TransmitterPhase.Sinking:
                    if (PhaseElapsed < SinkDuration) break;
                    if (StageDirector.Instance != null) StageDirector.Instance.ServerRecordEarnings(settledValue);
                    ServerSetPhase(TransmitterPhase.Gone);
                    Debug.Log("[ElecMole] Transmitter gone; blackout");
                    if (StageDirector.Instance != null) StageDirector.Instance.ServerBlackout();
                    break;
            }
        }

        // Appliances flying into the mouth hurt whoever they pass through (once per appliance and player).
        private void ServerFlyingHits()
        {
            foreach (var appliance in Appliance.All)
            {
                if (!appliance.IsSpawned || !appliance.IsPlugged || appliance.Swallowed.Value) continue;
                var pose = new Pose(appliance.transform.position, appliance.transform.rotation);
                foreach (var player in PlayerHealth.All)
                {
                    if (player == null || !player.IsSpawned || player.IsDead.Value) continue;
                    var key = (appliance.NetworkObjectId, player.NetworkObjectId);
                    if (flyingHits.Contains(key)) continue;
                    var closest = player.GetComponent<Collider>() is { } body ? body.ClosestPoint(pose.position) : player.transform.position;
                    if ((closest - pose.position).sqrMagnitude > FlyingHitRadius * FlyingHitRadius) continue;
                    flyingHits.Add(key);
                    var push = (player.transform.position - pose.position).normalized * 4f + Vector3.up * 2f;
                    player.ServerApplyDamage(FlyingHitDamage, push);
                }
            }
        }

        private void Update()
        {
            if (IsServer && IsSpawned) ServerTick();

            var phase = IsSpawned ? Phase.Value : TransmitterPhase.Dormant;
            var elapsed = PhaseElapsed;

            if (Body != null)
            {
                var sink = phase == TransmitterPhase.Sinking ? Mathf.Clamp01(elapsed / SinkDuration) : phase == TransmitterPhase.Gone ? 1f : 0f;
                var shaking = phase == TransmitterPhase.Settling || phase == TransmitterPhase.Sinking;
                var shake = shaking ? new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)) * 0.04f : Vector3.zero;
                Body.localPosition = bodyHome + Vector3.down * (SinkDepth * sink * sink) + shake;
                Body.gameObject.SetActive(phase != TransmitterPhase.Gone);
                if (bodyColliders != null) foreach (var c in bodyColliders) if (c != null) c.enabled = phase < TransmitterPhase.Sinking;
            }
            if (Hole != null) Hole.SetActive(phase >= TransmitterPhase.Sinking);

            if (SocketLamps != null)
                for (var i = 0; i < SocketLamps.Length; i++)
                {
                    if (SocketLamps[i] == null) continue;
                    var color = phase == TransmitterPhase.Dormant ? new Color(0.12f, 0.03f, 0.03f)
                        : IsSpawned && SocketUsed(i) ? new Color(0.3f, 1f, 0.45f) : new Color(0.5f, 0.06f, 0.05f);
                    Tint(SocketLamps[i], color);
                }
            if (PowerLamp != null)
            {
                var t = IsSpawned ? Mathf.Clamp01(Charge.Value / Capacity) : 0f;
                Tint(PowerLamp, IsStalled && Random.value < 0.4f ? new Color(1f, 0.3f, 0.1f) : Color.Lerp(new Color(0.15f, 0.04f, 0.03f), new Color(0.3f, 0.6f, 1f), t));
            }
            if (Beacon != null)
            {
                Beacon.enabled = phase != TransmitterPhase.Gone;
                switch (phase)
                {
                    case TransmitterPhase.Dormant:
                        Beacon.color = new Color(0.8f, 0.2f, 0.15f);
                        Beacon.intensity = 0.5f + Mathf.PingPong(Time.time, 0.4f);
                        break;
                    case TransmitterPhase.Active:
                        Beacon.color = new Color(0.3f, 0.55f, 1f);
                        Beacon.intensity = 1.4f;
                        break;
                    default:
                        Beacon.color = new Color(0.85f, 0.95f, 1f);
                        Beacon.intensity = 3f + Mathf.PingPong(Time.time * 8f, 3f);
                        break;
                }
            }
        }

        private void Tint(Renderer r, Color color)
        {
            r.GetPropertyBlock(block);
            block.SetColor(BaseColor, color);
            r.SetPropertyBlock(block);
        }
    }
}
