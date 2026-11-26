using CoOpHorror.Gameplay;
using CoOpHorror.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoOpHorror.Player
{
    // Something the nose can latch onto (an appliance or fixture it drinks from, the transmitter it feeds).
    public interface INoseTarget
    {
        // Where the nose tip sticks, seen from the nose root.
        Vector3 NosePoint(Vector3 from);
    }

    // The mole's long nose and the energy tank behind it.
    // Left mouse: the nose shoots out (even at nothing); on an appliance or fixture it latches on and drinks
    // its power into the tank. Right mouse on the ship's transmitter: it latches on and pumps the tank in.
    // The host owns the tank (Energy) and what the nose is latched onto (Attached, Flow); the owner only says
    // whether the nose is stretched out (Extended, purely visual). The stretching itself happens on the real
    // model: MoleRig bends the deep-rigged nose bones to the goal worked out here, on every peer.
    public sealed class PlayerNose : NetworkBehaviour
    {
        // The tank has no upper limit (anyone may hoard, or steal the transmitter dry: troll play is allowed).
        // GaugeWatts is only how much fills the HUD bar once.
        public const float GaugeWatts = 500f;
        public const float BaseSuckRate = 40f;      // W per second a nose drinks or gives back on stage 1 (flat, no minigame)
        public const float SuckRatePerStage = 0.25f; // +25 % per later stage
        public const float Reach = 1.7f;            // how far the nose stretches into empty air
        public const float LeakOnHit = 0.25f;       // share of the tank spilled when a monster hits you
        public const ulong NoTarget = ulong.MaxValue;
        public const byte FlowNone = 0, FlowIn = 1, FlowOut = 2;

        public static PlayerNose Local { get; private set; }

        public readonly NetworkVariable<float> Energy = new(0f);
        public readonly NetworkVariable<bool> Extended = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<ulong> Attached = new(NoTarget);
        public readonly NetworkVariable<byte> Flow = new(FlowNone);

        // Each player's own tank: one NetworkVariable per player object, written only by the host.
        public float Fraction => IsSpawned ? Mathf.Clamp01(Energy.Value / GaugeWatts) : 0f;

        // How fast every nose drinks right now (grows with the stage number).
        public static float SuckRate
        {
            get
            {
                var stage = StageDirector.Instance;
                var level = stage != null && stage.IsSpawned ? Mathf.Max(1, stage.Level.Value) : 1;
                return BaseSuckRate * (1f + SuckRatePerStage * (level - 1));
            }
        }

        private PlayerInteractor interactor;
        private PlayerHealth health;
        private PlayerAvatarVisual avatar;
        private MoleRig rig;
        private float extension;          // 0 = in, 1 = fully out (or on its target)
        private float pulse;
        private float nextGulp;
        private Vector3 tipVelocity, tipPoint;
        private bool tipValid;

        private void Awake()
        {
            interactor = GetComponent<PlayerInteractor>();
            health = GetComponent<PlayerHealth>();
            avatar = GetComponent<PlayerAvatarVisual>();
            rig = GetComponent<MoleRig>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsOwner) Local = this;
            if (!IsServer) return;
            Energy.Value = 0f;
            Attached.Value = NoTarget;
            Flow.Value = FlowNone;
            if (health != null) health.IsDead.OnValueChanged += OnServerDeadChanged;
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && health != null) health.IsDead.OnValueChanged -= OnServerDeadChanged;
            if (Local == this) Local = null;
        }

        // Dying spills the tank.
        private void OnServerDeadChanged(bool previous, bool dead)
        {
            if (!dead) return;
            Energy.Value = 0f;
            ServerDetach();
        }

        // ---------- host ----------

        // Puts `watts` into the tank (no cap); returns what was added.
        public float ServerAdd(float watts)
        {
            if (!IsServer || watts <= 0f || float.IsNaN(watts) || float.IsInfinity(watts)) return 0f;
            Energy.Value += watts;
            return watts;
        }

        // Takes up to `watts` out of the tank; returns what was there.
        public float ServerTake(float watts)
        {
            if (!IsServer || watts <= 0f) return 0f;
            var given = Mathf.Min(watts, Energy.Value);
            Energy.Value -= given;
            return given;
        }

        public void ServerLeak(float share)
        {
            if (IsServer && share > 0f) Energy.Value = Mathf.Max(0f, Energy.Value - Energy.Value * share);
        }

        public void ServerAttach(NetworkObject target, byte flow)
        {
            if (!IsServer || target == null) return;
            Attached.Value = target.NetworkObjectId;
            Flow.Value = flow;
        }

        public void ServerSetFlow(byte flow)
        {
            if (IsServer && Flow.Value != flow) Flow.Value = flow;
        }

        public void ServerDetach()
        {
            if (!IsServer) return;
            Attached.Value = NoTarget;
            Flow.Value = FlowNone;
        }

        // ---------- every peer ----------

        private void Update()
        {
            if (!IsSpawned) return;
            if (IsOwner)
            {
                var m = Mouse.current;
                var alive = health == null || !health.IsDead.Value;
                var wanted = alive && !CctvView.Viewing && FirstPersonController.GameplayInputReady
                    && ((m != null && (m.leftButton.isPressed || m.rightButton.isPressed)) || (interactor != null && (interactor.AutomationHold || interactor.AutomationHoldRight)));
                if (Extended.Value != wanted) Extended.Value = wanted;
            }
            UpdateNose();
        }

        private INoseTarget AttachedTarget()
        {
            if (!IsSpawned || Attached.Value == NoTarget || NetworkManager == null || NetworkManager.SpawnManager == null) return null;
            return NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(Attached.Value, out var obj) ? obj.GetComponent<INoseTarget>() : null;
        }

        // Works out where the nose reaches for and hands it to the rig (which bends the real nose after the
        // animation has run).
        private void UpdateNose()
        {
            var dead = health != null && health.IsDead.Value;
            var target = dead ? null : AttachedTarget();
            // The owner's nose already reaches for the thing in front of it before the host answers.
            if (target == null && IsOwner && Extended.Value && interactor != null && interactor.Target is INoseTarget aimed
                && (interactor.TargetKind == InteractKind.Hold || interactor.TargetKindRight == InteractKind.Hold)) target = aimed;
            var reaching = !dead && (Extended.Value || target != null);
            var dt = Time.deltaTime;
            extension = Mathf.MoveTowards(extension, reaching ? 1f : 0f, dt * (reaching ? 6f : 4f));

            var flow = dead ? FlowNone : Flow.Value;
            if (flow != FlowNone) pulse += dt * 2.2f;
            if (avatar != null)
                avatar.NoseFlowTint = flow == FlowIn ? new Color(0.6f, 1f, 0.95f) : flow == FlowOut ? new Color(1f, 0.9f, 0.45f) : (Color?)null;
            if (rig == null) return;

            var aim = interactor != null ? interactor.AimRotation : transform.rotation;
            var root = rig.NoseRoot;
            var free = root + aim * new Vector3(0f, -0.05f, Reach);
            var goal = target != null ? target.NosePoint(root) : free;
            // the tip follows its goal with a thick, over-damped lag (no wobble)
            if (!tipValid || extension <= 0.001f) { tipPoint = goal; tipVelocity = Vector3.zero; tipValid = true; }
            tipVelocity += ((goal - tipPoint) * 220f - tipVelocity * 40f) * dt;
            tipPoint += tipVelocity * dt;

            rig.NoseOut = Mathf.SmoothStep(0f, 1f, extension);
            rig.NoseGoal = tipPoint;
            rig.Pulse = pulse;
            rig.PulseDirection = flow == FlowIn ? 1 : flow == FlowOut ? -1 : 0;
            if (flow != FlowNone && Time.time >= nextGulp)
            {
                nextGulp = Time.time + 0.45f;
                ProtoAudio.PlayPitched(ProtoSound.Pop, tipPoint, IsOwner ? 0.18f : 0.12f, flow == FlowIn ? 0.75f : 1.15f);
            }
        }
    }
}
