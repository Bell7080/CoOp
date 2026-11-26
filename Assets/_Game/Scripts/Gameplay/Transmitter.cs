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
        public const float SinkDepth = 14f;      // the lift drops into the shaft and out of sight
        public const float LidOpenAngle = 112f;
        public const float RiseDepth = 3.2f;     // dormant: the lift waits this far down under the closed lids
        public const float RiseDelay = 1.1f;     // powered: the lids swing open first...
        public const float RiseSeconds = 3f;     // ...then the lift climbs up to the floor
        public const float CollapseSeconds = 2.5f;
        public const float FlyingHitRadius = 0.9f;
        public const int FlyingHitDamage = 15;

        public static Transmitter Instance { get; private set; }

        public float Capacity = 30f;
        public int TargetValue = 600;          // settlement goal shown on the lift's monitor
        public Transform Body;
        public Transform Mouth;
        public GameObject Hole;
        public Transform[] Sockets;
        public Renderer[] SocketLamps;
        public Renderer PowerLamp;
        public Light Beacon;
        // The settlement point is a lift sunk in the ground: two hatch lids open once it is powered; when the
        // cords are pulled in, cracks spread and the ground around the hatch breaks into the shaft below.
        public Transform LidA, LidB;          // hinge pivots; open about local Z
        public Transform[] Chunks;            // pieces of floor around the hatch that collapse
        public Transform[] Cracks;            // crack lines that grow while the cords are being pulled

        public readonly NetworkVariable<float> Charge = new(0f);
        public readonly NetworkVariable<TransmitterPhase> Phase = new(TransmitterPhase.Dormant);
        public readonly NetworkVariable<double> PhaseStart = new(0d);

        private MaterialPropertyBlock block;
        private Vector3 bodyHome;
        private Collider[] bodyColliders;
        private readonly HashSet<(ulong, ulong)> flyingHits = new();
        private int settledValue;
        private float lid;
        private Vector3[] chunkHome;
        private Quaternion[] chunkRot;
        private Vector3[] chunkSpin;
        private bool collapsedFx, riseFx;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        public int Required => Sockets != null ? Sockets.Length : 0;
        public int ChargePercent => Mathf.FloorToInt(Mathf.Clamp01(Charge.Value / Capacity) * 100f);
        public bool IsActive => Phase.Value == TransmitterPhase.Active;
        // 0 = down in the shaft (dormant), 1 = up at floor level, ready for cords.
        public float RiseProgress => !IsSpawned ? 0f
            : Phase.Value == TransmitterPhase.Dormant ? 0f
            : Phase.Value == TransmitterPhase.Active ? Mathf.Clamp01((PhaseElapsed - RiseDelay) / RiseSeconds) : 1f;
        public bool IsRaised => IsActive && RiseProgress >= 1f;
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
                foreach (var p in CordPlug.All) if (p.InTransmitter) count++;
                return count;
            }
        }

        // Settlement estimate: current value of everything plugged in.
        public int PluggedValue
        {
            get
            {
                var sum = 0;
                foreach (var p in CordPlug.All) if (p.InTransmitter && p.Appliance != null) sum += p.Appliance.SettleValue;
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
            if (Chunks != null)
            {
                chunkHome = new Vector3[Chunks.Length];
                chunkRot = new Quaternion[Chunks.Length];
                chunkSpin = new Vector3[Chunks.Length];
                var rng = new System.Random(17);
                for (var i = 0; i < Chunks.Length; i++)
                {
                    if (Chunks[i] == null) continue;
                    chunkHome[i] = Chunks[i].localPosition;
                    chunkRot[i] = Chunks[i].localRotation;
                    chunkSpin[i] = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * 260f;
                }
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
            foreach (var p in CordPlug.All) if (p.InTransmitter && p.Socket.Value == index) return true;
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
                    if (!IsRaised) { prompt = string.Empty; return InteractKind.None; }
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
            if (!IsRaised) return;
            var plug = who.HeldPlug;
            if (plug == null || plug.Appliance == null || !plug.Appliance.IsCharged) return;
            var socket = FreeSocket();
            if (socket < 0) return;
            plug.ServerSocket(socket);
            ProtoAudio.Play(ProtoSound.Clunk, transform.position, 1f);
            ServerTrySettle();
        }

        // Settlement only runs once what is plugged in (directly or through power strips) reaches the target.
        // Short of it, nothing happens: pull a cord back out and bring something worth more.
        public bool MeetsTarget => PluggedValue >= TargetValue;

        private void ServerTrySettle()
        {
            if (!IsRaised || !MeetsTarget) return;
            Debug.Log("[ElecMole] Transmitter target reached; settling");
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
                case TransmitterPhase.Active:
                    ServerTrySettle(); // a power strip already in a socket can fill up later
                    break;
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
                if (!appliance.IsSpawned || !appliance.InSettlement || appliance.Swallowed.Value) continue;
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
                // waits below the lids until powered, then climbs up (eased, with a judder on the way)
                var rise = RiseProgress;
                var climbing = rise > 0f && rise < 1f;
                if (climbing) shake = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)) * 0.015f;
                var below = RiseDepth * (1f - Mathf.SmoothStep(0f, 1f, rise));
                Body.localPosition = bodyHome + Vector3.down * (below + SinkDepth * sink * sink) + shake;
                Body.gameObject.SetActive(phase != TransmitterPhase.Gone);
                if (bodyColliders != null) foreach (var c in bodyColliders) if (c != null) c.enabled = phase < TransmitterPhase.Sinking;
            }
            if (Hole != null) Hole.SetActive(phase >= TransmitterPhase.Sinking);

            // hatch lids swing open once powered
            lid = Mathf.MoveTowards(lid, phase >= TransmitterPhase.Active ? 1f : 0f, Time.deltaTime * 0.9f);
            var open = Mathf.SmoothStep(0f, 1f, lid) * LidOpenAngle;
            if (LidA != null) LidA.localRotation = Quaternion.Euler(0f, 0f, -open);
            if (LidB != null) LidB.localRotation = Quaternion.Euler(0f, 0f, open);

            // cracks run out across the floor while the cords are reeled in
            var crack = phase == TransmitterPhase.Settling ? Mathf.Clamp01(elapsed / PullDuration) : phase > TransmitterPhase.Settling ? 1f : 0f;
            if (Cracks != null)
                foreach (var c in Cracks)
                {
                    if (c == null) continue;
                    c.gameObject.SetActive(crack > 0f);
                    c.localScale = new Vector3(c.localScale.x, c.localScale.y, Mathf.Max(0.001f, crack));
                    var inner = c.Find("Inner");
                    if (inner != null) inner.gameObject.SetActive(phase < TransmitterPhase.Sinking);
                }

            // then the ground breaks: the pieces around the hatch drop into the shaft, tumbling
            if (Chunks != null && chunkHome != null)
            {
                var fall = phase == TransmitterPhase.Sinking ? elapsed : phase == TransmitterPhase.Gone ? CollapseSeconds : -1f;
                for (var i = 0; i < Chunks.Length; i++)
                {
                    var chunk = Chunks[i];
                    if (chunk == null) continue;
                    var t = fall - i * 0.06f; // they give way one after another
                    if (t <= 0f) { chunk.localPosition = chunkHome[i]; chunk.localRotation = chunkRot[i]; chunk.gameObject.SetActive(true); }
                    else
                    {
                        chunk.localPosition = chunkHome[i] + Vector3.down * (4.9f * t * t);
                        chunk.localRotation = chunkRot[i] * Quaternion.Euler(chunkSpin[i] * t);
                        chunk.gameObject.SetActive(t < CollapseSeconds);
                    }
                    var col = chunk.GetComponent<Collider>();
                    if (col != null) col.enabled = t <= 0f;
                }
            }
            if (phase == TransmitterPhase.Sinking && !collapsedFx)
            {
                collapsedFx = true;
                ProtoAudio.Play(ProtoSound.Bang, transform.position, 1f);
                ProtoAudio.Play(ProtoSound.Rumble, transform.position, 1f);
                var cam = Camera.main;
                if (cam != null && (cam.transform.position - transform.position).sqrMagnitude < 14f * 14f) CrashIntro.Scare(0.18f, 1.2f);
            }
            if (phase < TransmitterPhase.Sinking) collapsedFx = false;
            if (phase == TransmitterPhase.Active && elapsed >= RiseDelay && elapsed < RiseDelay + RiseSeconds && !riseFx)
            {
                riseFx = true;
                ProtoAudio.Play(ProtoSound.Rumble, transform.position, 0.8f);
            }
            if (phase == TransmitterPhase.Dormant) riseFx = false;

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
                PowerLamp.gameObject.SetActive(phase < TransmitterPhase.Sinking);
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
