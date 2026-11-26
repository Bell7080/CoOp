using System.Collections.Generic;
using CoOpHorror.Localization;
using CoOpHorror.Player;
using Unity.Netcode;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    public enum PlugState : byte { Stowed, Held, Retracting, Socketed }

    // An appliance's plug on a floppy cord. Left-click it to pull it out and carry it; left-click the
    // transmitter to plug it in. Let go (Q, or click anything else) and the cord reels back into the
    // appliance like a tape measure. Walk further than the cord length and the appliance is dragged after
    // you, scraping (value loss); pull much further and the plug is torn from your hand and reels in.
    // A separate scene-root NetworkObject so the moving trigger is not part of the appliance's Rigidbody.
    // Every peer places the plug and draws the cord from replicated state.
    public sealed class CordPlug : Interactable
    {
        public const int CordPoints = 18;
        public const float PullStrength = 40f;   // acceleration per metre beyond the cord length
        public const float MaxPull = 45f;
        public const float YankSlack = 4f;       // beyond length + this the plug comes loose
        public const float RetractSeconds = 0.75f;
        public const ulong NoHolder = ulong.MaxValue;

        public static readonly List<CordPlug> All = new();

        public Appliance Appliance;
        public LineRenderer Cord;      // fallback flat line when there is no tube mesh
        public MeshFilter CordMesh;    // the cord as a round rubber tube (vertices in this object's local space)
        public Transform Visual;

        public const int TubeSides = 6;
        public const float TubeRadius = 0.011f;

        public readonly NetworkVariable<PlugState> State = new(PlugState.Stowed);
        public readonly NetworkVariable<ulong> Holder = new(NoHolder);
        public readonly NetworkVariable<int> Socket = new(-1);
        // What the socket belongs to: NoHolder = the transmitter, otherwise a power strip's NetworkObjectId.
        public readonly NetworkVariable<ulong> SocketHost = new(NoHolder);
        public readonly NetworkVariable<Vector3> DropPosition = new();   // where a reel-in started
        public readonly NetworkVariable<double> RetractStart = new(0d);

        private readonly Vector3[] points = new Vector3[CordPoints];
        private Mesh tube;
        private Vector3[] tubeVerts;
        private Vector3[] tubeNormals;
        private Renderer[] renderers;
        private static readonly RaycastHit[] Ground = new RaycastHit[6];

        public bool IsSocketed => IsSpawned && State.Value == PlugState.Socketed;
        public bool InTransmitter => IsSocketed && SocketHost.Value == NoHolder;

        // The power strip this plug is in (null when it is in the transmitter or not plugged in).
        public Appliance HostTap
        {
            get
            {
                if (!IsSocketed || SocketHost.Value == NoHolder || NetworkManager == null || NetworkManager.SpawnManager == null) return null;
                return NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(SocketHost.Value, out var obj) ? obj.GetComponent<Appliance>() : null;
            }
        }

        // A plugged-in cord can be pulled back out of a power strip any time, and out of the transmitter until
        // the settlement has started.
        public bool CanUnplug => IsSocketed && (!InTransmitter || (Transmitter.Instance != null && Transmitter.Instance.IsActive));
        public bool HeldBy(PlayerInteractor who) => who != null && IsSpawned && State.Value == PlugState.Held && Holder.Value == who.NetworkObjectId;
        public string DisplayName => Appliance != null ? Appliance.DisplayName : string.Empty;
        public float Length => Appliance != null ? Appliance.CordLength : 5f;
        public Vector3 AnchorPosition => Appliance == null ? transform.position
            : Appliance.CordAnchor != null ? Appliance.CordAnchor.position : Appliance.transform.position;
        public float Stretch => Vector3.Distance(AnchorPosition, transform.position);

        protected override void Awake()
        {
            base.Awake();
            All.Add(this);
            if (Cord != null)
            {
                Cord.positionCount = CordPoints;
                Cord.useWorldSpace = true;
            }
            if (CordMesh != null)
            {
                if (Cord != null) Cord.enabled = false;
                BuildTube();
            }
            // only what is drawn in the scene: primitives hidden under the plug model (and the flat line, when the
            // tube is used) stay off
            var shown = new List<Renderer>();
            foreach (var r in GetComponentsInChildren<Renderer>(true)) if (r.enabled) shown.Add(r);
            renderers = shown.ToArray();
        }

        public override void OnDestroy()
        {
            All.Remove(this);
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            State.OnValueChanged += PlugSound;
            if (!IsServer) return;
            State.Value = PlugState.Stowed;
            Holder.Value = NoHolder;
            Socket.Value = -1;
            SocketHost.Value = NoHolder;
        }

        public override void OnNetworkDespawn()
        {
            State.OnValueChanged -= PlugSound;
            base.OnNetworkDespawn();
        }
        private void PlugSound(PlugState previous,PlugState current)
        {
            if(current==PlugState.Socketed)ProtoAudio.Play(ProtoSound.PlugIn,transform.position,0.55f);
            else if(previous==PlugState.Socketed)ProtoAudio.Play(ProtoSound.PlugOut,transform.position,0.5f);
        }

        private bool ApplianceGone => Appliance == null || !Appliance.IsSpawned || Appliance.IsGone;

        public override InteractKind Describe(PlayerInteractor who, out string prompt)
        {
            prompt = string.Empty;
            if (ApplianceGone || HeldBy(who)) return InteractKind.None;
            switch (State.Value)
            {
                case PlugState.Socketed:
                    prompt = GameText.Format("interact.plugSocketed", DisplayName);
                    return CanUnplug && !who.HandsFull ? InteractKind.Press : InteractKind.None;
                case PlugState.Held:
                    prompt = GameText.Format("interact.plugHeldByOther", DisplayName);
                    return InteractKind.None;
            }
            if (who.HandsFull) { prompt = GameText.Get("interact.handsFull"); return InteractKind.None; }
            prompt = GameText.Format("interact.plugTake", DisplayName, Length.ToString("0"));
            return InteractKind.Press;
        }

        public override void ServerUse(PlayerInteractor who)
        {
            if (ApplianceGone || who.HandsFull) return;
            if (State.Value == PlugState.Socketed && !CanUnplug) return;
            if (State.Value == PlugState.Held) return;
            Socket.Value = -1;
            SocketHost.Value = NoHolder;
            Holder.Value = who.NetworkObjectId;
            State.Value = PlugState.Held;
        }

        public void ServerDrop(PlayerInteractor who)
        {
            if (!IsServer || !HeldBy(who)) return;
            ServerDropAt(who.HandPoint());
        }

        // Let go: the cord reels back in from here.
        private void ServerDropAt(Vector3 point)
        {
            Socket.Value = -1;
            SocketHost.Value = NoHolder;
            DropPosition.Value = point;
            RetractStart.Value = NetworkManager.ServerTime.Time;
            Holder.Value = NoHolder;
            State.Value = PlugState.Retracting;
        }

        private float RetractProgress => IsSpawned ? Mathf.Clamp01((float)(NetworkManager.ServerTime.Time - RetractStart.Value) / RetractSeconds) : 1f;

        public void ServerRetire()
        {
            if (!IsServer) return;
            Holder.Value = NoHolder; Socket.Value = -1; SocketHost.Value = NoHolder;
            State.Value = PlugState.Stowed;
        }

        public void ServerSocket(int socket)
        {
            if (!IsServer) return;
            SocketHost.Value = NoHolder;
            Socket.Value = socket;
            Holder.Value = NoHolder;
            State.Value = PlugState.Socketed;
        }

        // Into outlet `outlet` of a power strip.
        public void ServerSocketInto(Appliance tap, int outlet)
        {
            if (!IsServer || tap == null) return;
            SocketHost.Value = tap.NetworkObjectId;
            Socket.Value = outlet;
            Holder.Value = NoHolder;
            State.Value = PlugState.Socketed;
        }

        private bool TryGetHolder(out PlayerInteractor holder)
        {
            holder = null;
            if (State.Value != PlugState.Held || NetworkManager == null || NetworkManager.SpawnManager == null) return false;
            return NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(Holder.Value, out var obj) && obj.TryGetComponent(out holder);
        }

        public Pose PlugPose()
        {
            switch (IsSpawned ? State.Value : PlugState.Stowed)
            {
                case PlugState.Held when TryGetHolder(out var holder):
                    var aim = holder.AimRotation;
                    return new Pose(holder.HandPoint(), aim);
                case PlugState.Retracting:
                    // Tape measure: slow start, snapping home, with a little hop.
                    var t = RetractProgress;
                    var eased = t * t * (3f - 2f * t) * t;
                    var home = StowedPose();
                    var p = Vector3.Lerp(DropPosition.Value, home.position, eased) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 0.25f);
                    return new Pose(p, Quaternion.Slerp(Quaternion.LookRotation(home.position - DropPosition.Value + Vector3.forward * 0.001f), home.rotation, eased));
                case PlugState.Socketed when SocketHost.Value != NoHolder:
                    var tap = HostTap;
                    return tap != null ? tap.OutletPose(Socket.Value) : StowedPose();
                case PlugState.Socketed when Transmitter.Instance != null:
                    return Transmitter.Instance.SocketPose(Socket.Value);
                default:
                    return StowedPose();
            }
        }

        private Pose StowedPose()
        {
            var anchor = AnchorPosition;
            var facing = Appliance != null ? -Appliance.transform.forward : Vector3.back;
            return new Pose(anchor + facing * 0.12f + Vector3.down * 0.18f, Quaternion.LookRotation(Vector3.down, facing));
        }

        private void FixedUpdate()
        {
            if (!IsServer || !IsSpawned || ApplianceGone) return;
            var state = State.Value;
            if (state == PlugState.Stowed) return;
            // while the settlement reels everything in, sockets hold (no tension, no popping out)
            if (state == PlugState.Socketed && Transmitter.Instance != null && Transmitter.Instance.Phase.Value >= TransmitterPhase.Settling) return;
            if (state == PlugState.Retracting)
            {
                if (RetractProgress >= 1f) State.Value = PlugState.Stowed;
                return;
            }
            if (state == PlugState.Held && !TryGetHolder(out _)) { ServerDropAt(transform.position); return; }

            var plug = PlugPose().position;
            var anchor = AnchorPosition;
            var offset = plug - anchor;
            var distance = offset.magnitude;
            var excess = distance - Length;
            if (excess <= 0f) return;

            if (excess > YankSlack)
            {
                ServerDropAt(plug); // torn from the hand / popped out of the socket
                return;
            }
            // Pulled taut: drag the appliance after the plug. It scrapes along and loses value.
            Appliance.ServerAccelerate(offset / distance * Mathf.Min(MaxPull, excess * PullStrength));
            if (Appliance.Speed > 0.4f) Appliance.ServerScrape(Time.fixedDeltaTime);
        }

        private void LateUpdate()
        {
            var hidden = ApplianceGone || (Appliance != null && Appliance.InSettlement && Transmitter.Instance != null && Transmitter.Instance.PullProgress >= 1f);
            foreach (var r in renderers) if (r != null) r.enabled = !hidden;
            if (hidden) return;

            var pose = PlugPose();
            transform.SetPositionAndRotation(pose.position, pose.rotation);
            DrawCord(AnchorPosition, pose.position, State.Value == PlugState.Retracting);
        }

        // Floppy cord: sags with the slack, sways a little and rests on the floor.
        private void DrawCord(Vector3 a, Vector3 b, bool reeling)
        {
            if (Cord == null && tube == null) return;
            var distance = Vector3.Distance(a, b);
            // Reeling in, the cord is pulled straight like a tape measure.
            var slack = reeling ? 0f : Mathf.Max(0f, Length - distance);
            var time = Time.time * 2.1f + GetInstanceID() * 0.1f;
            for (var i = 0; i < CordPoints; i++)
            {
                var t = i / (CordPoints - 1f);
                var p = Vector3.Lerp(a, b, t);
                var bow = 4f * t * (1f - t);
                p.y -= slack * 0.5f * bow;
                p += new Vector3(Mathf.Sin(time + t * 7f), 0f, Mathf.Cos(time * 0.8f + t * 5f)) * (0.03f + slack * 0.015f) * bow;
                var count = Physics.RaycastNonAlloc(p + Vector3.up * 0.6f, Vector3.down, Ground, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                var floor = float.MinValue;
                for (var h = 0; h < count; h++)
                {
                    var c = Ground[h].collider;
                    if (c.GetComponentInParent<Grabbable>() != null || c.GetComponentInParent<PlayerHealth>() != null) continue;
                    floor = Mathf.Max(floor, Ground[h].point.y);
                }
                if (floor > float.MinValue && p.y < floor + 0.025f) p.y = floor + 0.025f;
                points[i] = p;
            }
            if (tube != null) UpdateTube();
            else Cord.SetPositions(points);
        }

        // A closed ring of TubeSides vertices around every cord point (triangles fixed, vertices per frame).
        private void BuildTube()
        {
            tube = new Mesh { name = "CordTube" };
            tube.MarkDynamic();
            tubeVerts = new Vector3[CordPoints * TubeSides];
            tubeNormals = new Vector3[tubeVerts.Length];
            var tris = new int[(CordPoints - 1) * TubeSides * 6];
            var k = 0;
            for (var i = 0; i < CordPoints - 1; i++)
            for (var j = 0; j < TubeSides; j++)
            {
                int a = i * TubeSides + j, b = i * TubeSides + (j + 1) % TubeSides, c = a + TubeSides, d = b + TubeSides;
                tris[k++] = a; tris[k++] = c; tris[k++] = b;
                tris[k++] = b; tris[k++] = c; tris[k++] = d;
            }
            tube.vertices = tubeVerts;
            tube.triangles = tris;
            CordMesh.sharedMesh = tube;
        }

        private void UpdateTube()
        {
            var t = CordMesh.transform;
            var side = Vector3.right;
            for (var i = 0; i < CordPoints; i++)
            {
                var along = (points[Mathf.Min(i + 1, CordPoints - 1)] - points[Mathf.Max(i - 1, 0)]);
                if (along.sqrMagnitude < 1e-8f) along = Vector3.forward;
                along.Normalize();
                // parallel-transport the side vector so the tube does not twist
                side = Vector3.Cross(along, Vector3.Cross(side, along));
                if (side.sqrMagnitude < 1e-6f) side = Vector3.Cross(along, Mathf.Abs(along.y) < 0.9f ? Vector3.up : Vector3.right);
                side.Normalize();
                var up = Vector3.Cross(along, side);
                for (var j = 0; j < TubeSides; j++)
                {
                    var angle = j * Mathf.PI * 2f / TubeSides;
                    var n = side * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                    tubeVerts[i * TubeSides + j] = t.InverseTransformPoint(points[i] + n * TubeRadius);
                    tubeNormals[i * TubeSides + j] = t.InverseTransformDirection(n);
                }
            }
            tube.vertices = tubeVerts;
            tube.normals = tubeNormals;
            tube.RecalculateBounds();
        }
    }
}
