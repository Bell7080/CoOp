using System;
using System.Collections.Generic;
using CoOpHorror.Localization;
using CoOpHorror.Player;
using Unity.Netcode;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    public enum DragPartKind : byte
    {
        Hinge, // the pivot turns about its own up axis (degrees): doors, cupboard and locker doors
        Slide, // the pivot slides along its own forward axis (metres): drawers
    }

    // One moving panel of a piece of furniture or a door. The pivot sits on the hinge line (hinges) or at the
    // drawer's closed position (slides); its rest pose is the closed pose.
    [Serializable]
    public sealed class DragPart
    {
        public Transform Pivot;
        public DragPartKind Kind = DragPartKind.Hinge;
        public float Min;                 // degrees (hinge) or metres (slide); the closed pose is 0
        public float Max = 100f;
        public float Mass = 1f;           // heavier panels follow the nose more lazily and keep swinging longer
        public float Friction = 1.4f;     // per second, once let go (low = a door swings on its own momentum)
        public float Gravity;             // degrees (or metres) per second² pulling it further open once it is off the latch
        public float LatchBelow = 3f;     // closer than this to 0 with nothing pulling, it clicks shut and stays
    }

    // R.E.P.O.-style physical opening: hold the left button on a door, a cupboard door or a drawer and the nose
    // latches onto the spot you looked at; then turn your view (or walk) to drag it. It has weight and momentum:
    // yank it and let go and it swings on, slows with friction, bounces off its stops (with a slam heard by
    // monsters). Several players can pull the same panel. Reusable: put it on any NetworkObject and list its
    // moving parts; map builders only have to make the pivots.
    // The host simulates every part and writes Positions (one writer); every peer only animates the pivots.
    public class DragOpenable : Interactable, INoseTarget
    {
        public const float Spring = 70f;           // acceleration per metre between the nose spot and where you look
        public const float Damping = 11f;
        public const float MaxAcceleration = 60f;  // m/s² at the grab spot: no instant teleport-slams
        public const float MinGrabDistance = 0.7f, MaxGrabDistance = 2.4f;
        public const float BreakDistance = 1.6f;   // the view this far away from the nose spot: the nose slips off
        public const float StopBounce = 0.3f;
        public const float SlamSpeed = 1.6f;       // m/s at the panel's edge hitting a stop: a loud slam

        public string NameKey = "openable.cupboard"; // GameText key of what it is (prompt)
        public DragPart[] Parts = Array.Empty<DragPart>();

        public NetworkList<float> Positions;
        public NetworkList<DragGrab> Grabs;

        public static readonly List<DragOpenable> AllOpenables = new();

        private Vector3[] restPosition;
        private Quaternion[] restRotation;
        private float[] shown;
        private float[] velocity;                  // host
        private float[] reach;                     // hinge: distance from the hinge line to the far edge (for slam speed)
        private readonly Dictionary<ulong, PlayerInteractor> grabbers = new(); // host
        private readonly List<ulong> slipping = new();

        public override bool StickyHold => true;
        public override float HoldRange => UseRange + 1f;

        protected override void Awake()
        {
            base.Awake();
            AllOpenables.Add(this);
            Positions = new NetworkList<float>();
            Grabs = new NetworkList<DragGrab>();
            var n = Parts.Length;
            restPosition = new Vector3[n];
            restRotation = new Quaternion[n];
            shown = new float[n];
            velocity = new float[n];
            reach = new float[n];
            for (var i = 0; i < n; i++)
            {
                var pivot = Parts[i].Pivot;
                if (pivot == null) continue;
                restPosition[i] = pivot.localPosition;
                restRotation[i] = pivot.localRotation;
                var far = 0.3f;
                foreach (var r in pivot.GetComponentsInChildren<Renderer>(true))
                {
                    var b = r.bounds;
                    for (var k = 0; k < 8; k++)
                    {
                        var corner = b.center + Vector3.Scale(b.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                        var local = pivot.InverseTransformPoint(corner);
                        far = Mathf.Max(far, new Vector2(local.x, local.z).magnitude);
                    }
                }
                reach[i] = far;
            }
        }

        public override void OnDestroy()
        {
            AllOpenables.Remove(this);
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (!IsServer) return;
            grabbers.Clear();
            Grabs.Clear();
            Positions.Clear();
            for (var i = 0; i < Parts.Length; i++)
            {
                Positions.Add(0f);
                velocity[i] = 0f;
            }
        }

        public float Position(int part) => IsSpawned && part >= 0 && part < Positions.Count ? Positions[part] : 0f;

        // Any part off its latch.
        public bool IsOpen
        {
            get
            {
                for (var i = 0; i < Parts.Length; i++)
                    if (Mathf.Abs(Position(i)) > (Parts[i].Kind == DragPartKind.Hinge ? 15f : 0.08f)) return true;
                return false;
            }
        }

        public bool GrabbedBy(PlayerInteractor who)
        {
            if (who == null || !IsSpawned) return false;
            foreach (var g in Grabs) if (g.Player == who.NetworkObjectId) return true;
            return false;
        }

        // ---------- prompt (every peer) ----------

        public override InteractKind Describe(PlayerInteractor who, out string prompt)
        {
            var what = GameText.Get(NameKey);
            if (GrabbedBy(who)) prompt = GameText.Format("interact.dragHolding", what);
            else prompt = GameText.Format(IsOpen ? "interact.dragClose" : "interact.dragOpen", what);
            return InteractKind.Hold;
        }

        // ---------- host ----------

        public override bool ServerHold(PlayerInteractor who, float dt)
        {
            if (grabbers.ContainsKey(who.NetworkObjectId)) return true;
            if (!ServerFindGrab(who, out var part, out var local, out var distance)) return false;
            grabbers[who.NetworkObjectId] = who;
            Grabs.Add(new DragGrab { Player = who.NetworkObjectId, Part = (byte)part, Local = local, Distance = distance });
            return true;
        }

        public override void ServerHoldEnded(PlayerInteractor who)
        {
            if (!IsServer || who == null) return;
            grabbers.Remove(who.NetworkObjectId);
            for (var i = Grabs.Count - 1; i >= 0; i--)
                if (Grabs[i].Player == who.NetworkObjectId) Grabs.RemoveAt(i);
        }

        // Which panel the player looks at and the spot on it the nose bites into (in the pivot's space).
        private bool ServerFindGrab(PlayerInteractor who, out int part, out Vector3 local, out float distance)
        {
            part = -1;
            local = Vector3.zero;
            distance = 1.2f;
            var ray = new Ray(who.EyePosition, who.AimForward);
            var hits = Physics.RaycastAll(ray, UseRange + ServerRangeSlack, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            var nearest = float.MaxValue;
            foreach (var hit in hits)
            {
                if (hit.collider.transform.IsChildOf(who.transform) || hit.distance >= nearest) continue;
                var index = PartOf(hit.collider.transform);
                if (index < 0 && !hit.collider.transform.IsChildOf(transform)) continue;
                nearest = hit.distance;
                part = index;
                local = index >= 0 ? Parts[index].Pivot.InverseTransformPoint(hit.point) : Vector3.zero;
                distance = hit.distance;
            }
            if (part < 0)
            {
                // looking at the carcass (or lag): take the nearest panel, at its closest point to the view
                var best = float.MaxValue;
                for (var i = 0; i < Parts.Length; i++)
                {
                    if (Parts[i].Pivot == null) continue;
                    foreach (var c in Parts[i].Pivot.GetComponentsInChildren<Collider>())
                    {
                        if (!c.enabled || c.isTrigger) continue;
                        var along = Mathf.Max(0.3f, Vector3.Dot(c.bounds.center - ray.origin, ray.direction));
                        var point = c.ClosestPoint(ray.GetPoint(along));
                        var d = Vector3.Distance(point, ray.GetPoint(along));
                        if (d >= best) continue;
                        best = d;
                        part = i;
                        local = Parts[i].Pivot.InverseTransformPoint(point);
                        distance = Vector3.Distance(ray.origin, point);
                    }
                }
                if (part < 0 || best > 0.9f) return false;
            }
            distance = Mathf.Clamp(distance, MinGrabDistance, MaxGrabDistance);
            return true;
        }

        private int PartOf(Transform t)
        {
            for (var i = 0; i < Parts.Length; i++)
                if (Parts[i].Pivot != null && t.IsChildOf(Parts[i].Pivot)) return i;
            return -1;
        }

        // Host: kick a part (a gust, a creature barging in). Speed in degrees (or metres) per second.
        public void ServerKick(int part, float speed)
        {
            if (!IsServer || part < 0 || part >= Parts.Length || float.IsNaN(speed)) return;
            velocity[part] += speed;
        }

        // Host: extra drive on a part this tick (subclasses: bodies walking into a door). Returns a speed the
        // part is shoved towards (degrees or metres per second), or NaN for none.
        protected virtual float ServerShove(int part) => float.NaN;

        protected virtual void FixedUpdate()
        {
            if (!IsServer || !IsSpawned || Positions.Count != Parts.Length) return;
            var dt = Time.fixedDeltaTime;
            for (var i = 0; i < Parts.Length; i++)
            {
                var p = Parts[i];
                if (p.Pivot == null) continue;
                var q = Positions[i];
                var accel = 0f;
                var pulled = false;
                for (var g = 0; g < Grabs.Count; g++)
                {
                    var grab = Grabs[g];
                    if (grab.Part != i || !grabbers.TryGetValue(grab.Player, out var who) || who == null) continue;
                    pulled = true;
                    accel += GrabAcceleration(i, q, grab, who);
                }
                var shove = ServerShove(i);
                if (!float.IsNaN(shove)) velocity[i] = Mathf.Lerp(velocity[i], shove, 0.35f);
                else if (!pulled)
                {
                    var closedish = Mathf.Abs(q) < p.LatchBelow && Mathf.Abs(velocity[i]) < 25f * Unit(i);
                    if (closedish) { velocity[i] = 0f; q = Mathf.MoveTowards(q, 0f, 20f * Unit(i) * dt); }
                    else accel += p.Gravity * Mathf.Sign(OpenSide(i, q));
                }
                velocity[i] += accel / Mathf.Max(0.2f, p.Mass) * dt;
                velocity[i] *= Mathf.Exp(-(pulled ? 0f : p.Friction) * dt);
                q += velocity[i] * dt;
                if (q < p.Min) { q = p.Min; HitStop(i, velocity[i] < 0f); }
                else if (q > p.Max) { q = p.Max; HitStop(i, velocity[i] > 0f); }
                if (!Mathf.Approximately(q, Positions[i])) Positions[i] = q;
            }
            // noses that slipped off (the view is too far from the grabbed spot)
            for (var i = 0; i < slipping.Count; i++)
                if (grabbers.TryGetValue(slipping[i], out var who) && who != null) who.ServerForceEndHold(this);
            slipping.Clear();
        }

        // Which way is "more open" from here (for gravity): away from closed, or towards Max if exactly shut.
        private float OpenSide(int i, float q) => Mathf.Abs(q) > 0.0001f ? q : (Parts[i].Max >= -Parts[i].Min ? 1f : -1f);

        private float Unit(int i) => Parts[i].Kind == DragPartKind.Hinge ? 1f : 0.01f;

        // Pull of one nose on a part: a spring from the bitten spot to the point in front of the eyes, projected
        // on what the part can do (turn or slide), so dragging feels the same for doors and drawers.
        private float GrabAcceleration(int i, float q, DragGrab grab, PlayerInteractor who)
        {
            var pivot = Parts[i].Pivot;
            // the simulated pose, not the smoothed one on screen (a lagging anchor would make the spring ring)
            PivotPose(i, q, out var pivotPosition, out var pivotRotation);
            var anchor = pivotPosition + pivotRotation * Vector3.Scale(grab.Local, pivot.lossyScale);
            var target = who.EyePosition + who.AimForward * grab.Distance;
            var error = target - anchor;
            if (error.sqrMagnitude > BreakDistance * BreakDistance && !slipping.Contains(grab.Player)) slipping.Add(grab.Player);
            Vector3 dir;   // movement of the anchor per unit of q
            if (Parts[i].Kind == DragPartKind.Hinge)
            {
                var axis = pivotRotation * Vector3.up;
                var r = Vector3.ProjectOnPlane(anchor - pivotPosition, axis);
                var length = Mathf.Max(0.15f, r.magnitude);
                dir = Vector3.Cross(axis, r).normalized * (length * Mathf.Deg2Rad);
            }
            else dir = pivotRotation * Vector3.forward;
            var anchorVelocity = dir * velocity[i];
            var linear = error * Spring - (anchorVelocity - who.MotionVelocity * 0.5f) * Damping;
            if (linear.sqrMagnitude > MaxAcceleration * MaxAcceleration) linear = linear.normalized * MaxAcceleration;
            return Vector3.Dot(linear, dir) / Mathf.Max(0.0001f, dir.sqrMagnitude);
        }

        // Where the pivot is for a given q (world space).
        private void PivotPose(int i, float q, out Vector3 position, out Quaternion rotation)
        {
            var parent = Parts[i].Pivot.parent;
            var parentRotation = parent != null ? parent.rotation : Quaternion.identity;
            if (Parts[i].Kind == DragPartKind.Hinge)
            {
                rotation = parentRotation * restRotation[i] * Quaternion.Euler(0f, q, 0f);
                position = parent != null ? parent.TransformPoint(restPosition[i]) : restPosition[i];
            }
            else
            {
                rotation = parentRotation * restRotation[i];
                var local = restPosition[i] + restRotation[i] * Vector3.forward * q;
                position = parent != null ? parent.TransformPoint(local) : local;
            }
        }

        private void HitStop(int i, bool intoStop)
        {
            if (!intoStop) return;
            var edgeSpeed = Mathf.Abs(velocity[i]) * (Parts[i].Kind == DragPartKind.Hinge ? reach[i] * Mathf.Deg2Rad : 1f);
            velocity[i] = -velocity[i] * StopBounce;
            if (edgeSpeed < SlamSpeed) return;
            var loud = Mathf.Clamp01((edgeSpeed - SlamSpeed) / 4f);
            Noise.Emit(Parts[i].Pivot.position + Vector3.up, Noise.SwingDoorRadius * (0.6f + loud), NoiseKind.Door);
            SlamFxRpc(i, loud);
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        private void SlamFxRpc(int part, float strength)
        {
            if (part < 0 || part >= Parts.Length || Parts[part].Pivot == null) return;
            ProtoAudio.PlayPitched(ProtoSound.Clunk, Parts[part].Pivot.position, 0.35f + strength * 0.65f, 0.85f + strength * 0.2f);
        }

        // ---------- every peer ----------

        protected virtual void Update()
        {
            var k = 1f - Mathf.Exp(-22f * Time.deltaTime);
            for (var i = 0; i < Parts.Length; i++)
            {
                var pivot = Parts[i].Pivot;
                if (pivot == null) continue;
                shown[i] = Mathf.Lerp(shown[i], Position(i), k);
                if (Parts[i].Kind == DragPartKind.Hinge)
                    pivot.localRotation = restRotation[i] * Quaternion.Euler(0f, shown[i], 0f);
                else
                    pivot.localPosition = restPosition[i] + restRotation[i] * Vector3.forward * shown[i];
            }
        }

        // The nose bites the spot its owner grabbed; before the host answers, the panel's nearest point.
        public Vector3 NosePoint(Vector3 from)
        {
            if (IsSpawned && NetworkManager != null && NetworkManager.SpawnManager != null)
            {
                var best = float.MaxValue;
                var point = Vector3.zero;
                foreach (var g in Grabs)
                {
                    if (g.Part >= Parts.Length || Parts[g.Part].Pivot == null) continue;
                    if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(g.Player, out var obj)) continue;
                    var d = (obj.transform.position - from).sqrMagnitude;
                    if (d >= best) continue;
                    best = d;
                    point = Parts[g.Part].Pivot.TransformPoint(g.Local);
                }
                if (best < 9f) return point;
            }
            var nearest = transform.position + Vector3.up;
            var nearestDistance = float.MaxValue;
            foreach (var part in Parts)
            {
                if (part.Pivot == null) continue;
                foreach (var c in part.Pivot.GetComponentsInChildren<Collider>())
                {
                    if (!c.enabled || c.isTrigger) continue;
                    var p = c.ClosestPoint(from);
                    var d = (p - from).sqrMagnitude;
                    if (d < nearestDistance) { nearestDistance = d; nearest = p; }
                }
            }
            return nearest;
        }
    }

    // One nose holding a panel: who, which part, the bitten spot (pivot space) and how far in front of the
    // eyes it is held.
    public struct DragGrab : INetworkSerializable, IEquatable<DragGrab>
    {
        public ulong Player;
        public byte Part;
        public Vector3 Local;
        public float Distance;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Player);
            serializer.SerializeValue(ref Part);
            serializer.SerializeValue(ref Local);
            serializer.SerializeValue(ref Distance);
        }

        public bool Equals(DragGrab other) => Player == other.Player && Part == other.Part && Local == other.Local && Distance.Equals(other.Distance);
        public override bool Equals(object obj) => obj is DragGrab other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Player, Part, Local, Distance);
    }
}
