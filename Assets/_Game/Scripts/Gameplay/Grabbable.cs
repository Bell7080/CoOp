using System.Collections.Generic;
using CoOpHorror.Localization;
using CoOpHorror.Player;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    // A physics object players lift with "current telekinesis": hold the left button on it and it floats,
    // wobbling, toward the point in front of each holder, joined to each holder's paw by a crackling current
    // beam. Gravity still pulls: heavy things (Weight) sag, and with too few holders they are only half
    // lifted and dragged, slowly. Hold the right button and move the mouse to turn it. Hard knocks and
    // drops are reported to subclasses (value loss).
    // The host simulates the Rigidbody; a server-authority NetworkTransform replicates it.
    [RequireComponent(typeof(Rigidbody))]
    public abstract class Grabbable : SkillCheckedInteractable
    {
        public const int MaxHolders = 3;
        public const float HoldDistance = 1.7f;
        public const float Spring = 24f;          // acceleration per metre of error (soft: things lag behind and bob)
        public const float Damping = 4.6f;        // under-damped: a jump or a sudden turn makes the load swing
        public const float TurnRate = 10f;
        public const float MaxSpeed = 11f;
        public const float BreakDistance = 3.2f;  // stuck this far from the hold point: that holder lets go
        public const float ImpactSpeed = 2.6f;    // collisions (or drops) faster than this count as a hit

        public static readonly List<Grabbable> All = new();

        public int Weight = 1;
        public NetworkList<ulong> Holders;        // NetworkObjectIds of holding players
        public readonly NetworkVariable<float> TurnYaw = new(0f);   // relative to the first holder's yaw
        public readonly NetworkVariable<float> TurnPitch = new(0f);

        protected Rigidbody Body { get; private set; }
        private NetworkTransform sync;
        private Collider[] solid;
        private Vector3 homePosition;
        private Quaternion homeRotation;
        private bool resetPending;
        private float nextImpactFx;
        private readonly List<PlayerInteractor> holders = new();
        private readonly HashSet<CharacterController> ignored = new();

        public bool IsHeld => IsSpawned && Holders.Count > 0;
        public float Lift => Mathf.Clamp01(Holders.Count / (float)Mathf.Max(1, Weight));
        public override bool StickyHold => true;
        public override bool UsesSkillChecks => false;
        public override float HoldRange => 5.5f;
        public abstract string DisplayName { get; }

        protected override void Awake()
        {
            base.Awake();
            All.Add(this);
            Holders = new NetworkList<ulong>();
            Body = GetComponent<Rigidbody>();
            sync = GetComponent<NetworkTransform>();
            var list = new List<Collider>();
            foreach (var c in GetComponentsInChildren<Collider>(true)) if (!c.isTrigger) list.Add(c);
            solid = list.ToArray();
            homePosition = transform.position;
            homeRotation = transform.rotation;
        }

        public override void OnDestroy()
        {
            All.Remove(this);
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Holders.OnListChanged += OnHoldersChanged;
            if (!IsServer) return;
            Holders.Clear();
            TurnYaw.Value = TurnPitch.Value = 0f;
            resetPending = ResetToHomeOnSpawn;
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            Holders.OnListChanged -= OnHoldersChanged;
            RestoreCollisions();
        }

        // In-scene objects go back to where the scene placed them when a new room starts.
        protected virtual bool ResetToHomeOnSpawn => true;

        public bool HeldBy(PlayerInteractor who) => who != null && IsSpawned && Holders.Contains(who.NetworkObjectId);

        // Server: may this player start holding it now?
        protected virtual bool CanGrab(PlayerInteractor who) => true;

        // Server: grabbing is a sticky hold. Returns false to end it.
        protected bool ServerGrabTick(PlayerInteractor who)
        {
            if (HeldBy(who)) return true;
            if (!CanGrab(who) || Holders.Count >= MaxHolders || who.HandsFull) return false;
            if (Holders.Count == 0)
            {
                // Keep the current facing relative to the first holder.
                TurnYaw.Value = Mathf.DeltaAngle(who.transform.eulerAngles.y, transform.eulerAngles.y);
                TurnPitch.Value = 0f;
            }
            Holders.Add(who.NetworkObjectId);
            return true;
        }

        public override void ServerHoldEnded(PlayerInteractor who)
        {
            if (IsServer && IsSpawned) Holders.Remove(who.NetworkObjectId);
        }

        public void ServerReleaseAll()
        {
            if (!IsServer || !IsSpawned) return;
            // Ending a hold triggers OnHoldersChanged, which reuses ResolveHolders' list.
            // Iterate a snapshot so destroying a carried item can release every holder safely.
            foreach (var p in ResolveHolders().ToArray()) p.ServerForceEndHold(this);
            Holders.Clear();
        }

        // Server: the first holder turns it with the right mouse button.
        public void ServerTurn(PlayerInteractor who, float yaw, float pitch)
        {
            if (!IsServer || !HeldBy(who) || float.IsNaN(yaw) || float.IsNaN(pitch)) return;
            TurnYaw.Value = Mathf.Repeat(TurnYaw.Value + Mathf.Clamp(yaw, -90f, 90f) + 180f, 360f) - 180f;
            TurnPitch.Value = Mathf.Clamp(TurnPitch.Value + Mathf.Clamp(pitch, -90f, 90f), -80f, 80f);
        }

        // Places it where the player holds things (inventory take-out). Server only.
        public void ServerPlaceAtHold(PlayerInteractor who)
        {
            if (!IsServer) return;
            var point = who.GrabPoint();
            Body.position = point;
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            transform.position = point;
            if (sync != null && sync.CanCommitToTransform) sync.Teleport(point, transform.rotation, transform.localScale);
        }

        protected List<PlayerInteractor> ResolveHolders()
        {
            holders.Clear();
            if (!IsSpawned || NetworkManager == null || NetworkManager.SpawnManager == null) return holders;
            foreach (var id in Holders)
                if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out var obj) && obj.TryGetComponent<PlayerInteractor>(out var p))
                    holders.Add(p);
            return holders;
        }

        protected virtual void FixedUpdate()
        {
            if (!IsServer || !IsSpawned) return;
            if (resetPending)
            {
                if (sync != null && !sync.CanCommitToTransform) return;
                Body.position = homePosition;
                Body.rotation = homeRotation;
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
                transform.SetPositionAndRotation(homePosition, homeRotation);
                if (sync != null) sync.Teleport(homePosition, homeRotation, transform.localScale);
                resetPending = false;
            }
            if (!Body.isKinematic && Holders.Count == 0) ServerBump();
            if (Body.isKinematic || Holders.Count == 0) return;

            var list = ResolveHolders();
            if (list.Count == 0) return;
            var target = Vector3.zero;
            foreach (var p in list) target += p.GrabPoint();
            target /= list.Count;

            var center = Body.worldCenterOfMass;
            // Holders whose hold point is far from the object (stuck behind a wall) let go.
            // (letting go changes the holder list, which ResolveHolders reuses: walk a copy, then re-read it)
            var lettingGo = false;
            foreach (var p in list.ToArray())
                if ((p.GrabPoint() - center).sqrMagnitude > BreakDistance * BreakDistance) { p.ServerForceEndHold(this); lettingGo = true; }
            if (lettingGo)
            {
                list = ResolveHolders();
                if (list.Count == 0) return;
            }

            var lift = Lift;
            // Precarious: the hold point drifts a little.
            var t = Time.time * 1.7f + NetworkObjectId;
            target += new Vector3(Mathf.PerlinNoise(t, 0f) - 0.5f, Mathf.PerlinNoise(0f, t) - 0.5f, Mathf.PerlinNoise(t, t) - 0.5f) * 0.12f;

            // Weight: the pull weakens with too few holders and gravity is only partly cancelled, so heavy
            // things sag and, under-held, get half-lifted and dragged with effort.
            var pull = (0.35f + 0.65f * lift) / Mathf.Sqrt(Mathf.Max(1, Weight)); // heavier = lazier
            // Damp only relative to the holder's own motion, so the load follows with a delay and overshoots.
            var holderVelocity = Vector3.zero;
            foreach (var p in list) holderVelocity += p.MotionVelocity;
            holderVelocity /= list.Count;
            var accel = (target - center) * (Spring * pull) - (Body.linearVelocity - holderVelocity * 0.35f) * Damping;
            // Gravity is never fully cancelled: everything droops a little and reacts to bumps.
            var carried = lift >= 1f ? (Weight > 1 ? 0.88f : 0.95f) : lift * 0.8f;
            if (lift < 1f) accel.y = Mathf.Min(accel.y, 5f * lift);
            accel -= Physics.gravity * carried;
            Body.AddForce(accel, ForceMode.Acceleration);
            var maxSpeed = MaxSpeed * (0.45f + 0.55f * lift);
            if (Body.linearVelocity.sqrMagnitude > maxSpeed * maxSpeed) Body.linearVelocity = Body.linearVelocity.normalized * maxSpeed;

            if (lift >= 1f)
            {
                var desired = Quaternion.Euler(TurnPitch.Value, list[0].transform.eulerAngles.y + TurnYaw.Value, 0f);
                (desired * Quaternion.Inverse(Body.rotation)).ToAngleAxis(out var angle, out var axis);
                if (angle > 180f) angle -= 360f;
                if (!float.IsNaN(axis.x) && Mathf.Abs(angle) > 0.1f) Body.angularVelocity = axis * (angle * Mathf.Deg2Rad * TurnRate);
            }
        }

        // Loose objects are touchy: walking or running into one shoves it (players' controllers don't push
        // rigidbodies on their own), monsters barging past knock things over.
        private void ServerBump()
        {
            var bounds = Body.GetComponent<Collider>() != null ? Body.GetComponent<Collider>().bounds : new Bounds(Body.worldCenterOfMass, Vector3.one * 0.5f);
            var reach = Mathf.Max(bounds.extents.x, bounds.extents.z) + 0.5f;
            void Shove(Vector3 position, Vector3 velocity, float strength)
            {
                var offset = Body.worldCenterOfMass - position;
                offset.y = 0f;
                var distance = offset.magnitude;
                if (distance > reach || distance < 0.001f) return;
                var dir = offset / distance;
                var into = Vector3.Dot(velocity, dir);
                if (into <= 0.2f) return;
                var push = dir * (into * strength / Mathf.Max(1, Weight)) + Vector3.up * (into * 0.25f);
                Body.AddForceAtPosition(push, Body.worldCenterOfMass + Vector3.up * 0.15f, ForceMode.Acceleration);
            }
            foreach (var p in PlayerHealth.All)
                if (p != null && p.IsSpawned && !p.IsDead.Value && p.TryGetComponent<PlayerInteractor>(out var pi))
                    Shove(p.transform.position, pi.MotionVelocity, 9f);
            foreach (var m in TempMonster.All)
                if (m != null && m.IsSpawned && m.TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var agent))
                    Shove(m.transform.position, agent.velocity, 16f);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!IsServer || !IsSpawned) return;
            var speed = collision.relativeVelocity.magnitude;
            if (speed < ImpactSpeed) return;
            ServerImpact(speed);
            if (Time.time < nextImpactFx) return;
            nextImpactFx = Time.time + 0.25f;
            ImpactFxRpc(Mathf.Clamp01((speed - ImpactSpeed) / 8f));
        }

        // Every peer: a gas shell around the object and a gas stream with a crackling current from each paw.
        protected virtual void LateUpdate()
        {
            var list = IsSpawned ? ResolveHolders() : holders;
            if (!IsSpawned) list.Clear();
            if (list.Count == 0 && aura == null) return;
            if (aura == null) aura = TelekinesisAura.For(transform);
            pawPoints.Clear();
            foreach (var p in list) pawPoints.Add(p.TryGetComponent<CoOpHorror.Player.MoleRig>(out var rig) && rig.ArmR2 != null ? rig.RightPaw : p.EyePosition + p.AimRotation * new Vector3(0.12f, -0.3f, 0.38f)); // from the real paw
            aura.Show(pawPoints, Lift < 1f ? 1f : (Weight > 1 ? 0.5f : 0f));
        }

        private TelekinesisAura aura;
        private readonly List<Vector3> pawPoints = new();

        // Where beams attach, in local space (subclasses aim at their body's centre).
        protected virtual Vector3 BeamTargetLocal => Vector3.up * 0.25f;

        // Server: a hard knock (subclasses lose value, etc.).
        protected virtual void ServerImpact(float speed) { }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        private void ImpactFxRpc(float strength) => ProtoAudio.Play(ProtoSound.Clunk, transform.position, 0.4f + strength * 0.6f);

        // Every peer: holders' own bodies must not collide with what they float in front of them.
        private void OnHoldersChanged(NetworkListEvent<ulong> change)
        {
            RestoreCollisions();
            foreach (var p in ResolveHolders())
            {
                if (!p.TryGetComponent<CharacterController>(out var cc)) continue;
                foreach (var c in solid) if (c != null) Physics.IgnoreCollision(cc, c, true);
                ignored.Add(cc);
            }
        }

        private void RestoreCollisions()
        {
            foreach (var cc in ignored)
                if (cc != null) foreach (var c in solid) if (c != null) Physics.IgnoreCollision(cc, c, false);
            ignored.Clear();
        }

        protected string GrabPrompt(PlayerInteractor who)
        {
            if (who.HandsFull) return GameText.Get("interact.handsFull");
            if (Weight > 1) return GameText.Format("interact.grabHeavy", DisplayName, Holders.Count, Weight);
            return GameText.Format("interact.grab", DisplayName);
        }
    }
}
