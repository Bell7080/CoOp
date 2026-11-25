using CoOpHorror.Gameplay;
using CoOpHorror.Localization;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoOpHorror.Player
{
    // Left mouse button: use what you look at (press or hold, decided by the target). Q: drop what you carry.
    // While holding a skill-checked target, timing checks appear (Space). The owner only requests;
    // the host checks range, life and target state before anything changes.
    public sealed class PlayerInteractor : NetworkBehaviour
    {
        public static PlayerInteractor Local { get; private set; }

        // Owner-side view for the HUD.
        public Interactable Target { get; private set; }
        public InteractKind TargetKind { get; private set; }
        public string Prompt { get; private set; } = string.Empty;
        public bool HoldingUse => ownerHold != null;
        public SkillCheck Check { get; } = new();

        // Automation hooks for CLI verification (owner only; real input is unaffected).
        public Interactable AutomationTarget { get; set; }
        public bool AutomationHold { get; set; }
        private bool automationPress;
        private bool automationDrop;
        private bool automationSkill;
        public void AutomationPress() => automationPress = true;
        public void AutomationDrop() => automationDrop = true;
        public void AutomationSkillPress() => automationSkill = true;

        private FirstPersonController motor;
        private PlayerHealth health;
        private Interactable ownerHold;  // owner: what we told the host we are holding the button on
        private Interactable serverHold; // host: what this player is holding the button on
        private static readonly RaycastHit[] Hits = new RaycastHit[12];

        public Vector3 EyePosition => motor != null && motor.AimPivot != null ? motor.AimPivot.position : transform.position + Vector3.up * 0.72f;

        // The appliance this player carries (derived from Appliance.Carrier, the single source of truth).
        public Appliance Carrying
        {
            get
            {
                if (!IsSpawned) return null;
                foreach (var a in Appliance.All)
                    if (a.IsSpawned && a.Carrier.Value == NetworkObjectId) return a;
                return null;
            }
        }

        // Where a carried appliance sits: in front of the chest, following body yaw (not pitch).
        public Pose HoldPose()
        {
            var forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            var position = EyePosition + forward * 0.7f + Vector3.down * 1f;
            return new Pose(position, Quaternion.LookRotation(forward));
        }

        private void Awake()
        {
            motor = GetComponent<FirstPersonController>();
            health = GetComponent<PlayerHealth>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsOwner) Local = this;
            if (IsServer && health != null) health.IsDead.OnValueChanged += OnServerDeadChanged;
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer)
            {
                if (health != null) health.IsDead.OnValueChanged -= OnServerDeadChanged;
                ServerEndHold();
                var carried = Carrying;
                if (carried != null) carried.ServerDrop(this);
            }
            ownerHold = null;
            if (Local == this) Local = null;
        }

        private void OnServerDeadChanged(bool previous, bool dead)
        {
            if (!dead) return;
            ServerEndHold();
            var carried = Carrying;
            if (carried != null) carried.ServerDrop(this);
        }

        private bool Alive => health == null || !health.IsDead.Value;

        private void Update()
        {
            if (!IsSpawned) return;
            if (IsServer) ServerTickHold();
            if (IsOwner) OwnerUpdate();
        }

        private void OwnerUpdate()
        {
            UpdateTarget();
            var k = Keyboard.current;
            var m = Mouse.current;
            var ready = FirstPersonController.GameplayInputReady && Alive;
            var held = (ready && m != null && m.leftButton.isPressed) || AutomationHold;
            var pressed = (ready && m != null && m.leftButton.wasPressedThisFrame) || automationPress;
            var drop = (ready && k != null && k.qKey.wasPressedThisFrame) || automationDrop;
            var skill = (ready && k != null && k.spaceKey.wasPressedThisFrame) || automationSkill;
            automationPress = automationDrop = automationSkill = false;

            if (ownerHold != null && (!held || Target != ownerHold || TargetKind != InteractKind.Hold))
            {
                ownerHold = null;
                EndHoldRpc();
            }
            if (ownerHold == null && held && Target != null && TargetKind == InteractKind.Hold)
            {
                ownerHold = Target;
                BeginHoldRpc(Target.NetworkObject);
            }
            if (pressed && Target != null && TargetKind == InteractKind.Press && !Target.OwnerUse(this)) UseRpc(Target.NetworkObject);
            if (drop && Carrying != null) DropRpc();

            var result = Check.Tick(ownerHold != null && ownerHold.UsesSkillChecks, skill);
            if (result.HasValue && ownerHold != null) SkillCheckRpc(ownerHold.NetworkObject, result.Value);
        }

        private void UpdateTarget()
        {
            Interactable found = AutomationTarget;
            if (found == null)
            {
                var cam = Camera.main;
                var ray = cam != null && cam.transform.IsChildOf(transform)
                    ? cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))
                    : new Ray(EyePosition, motor != null && motor.AimPivot != null ? motor.AimPivot.forward : transform.forward);
                var count = Physics.RaycastNonAlloc(ray, Hits, Interactable.UseRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                var nearest = float.MaxValue;
                for (var i = 0; i < count; i++)
                {
                    var c = Hits[i].collider;
                    if (c == null || c.transform.IsChildOf(transform) || Hits[i].distance >= nearest) continue;
                    nearest = Hits[i].distance;
                    found = c.GetComponentInParent<Interactable>(); // a wall or player in front blocks
                }
            }
            if (found != null && !found.IsSpawned) found = null;

            Target = found;
            var prompt = string.Empty;
            TargetKind = found != null && Alive ? found.Describe(this, out prompt) : InteractKind.None;
            var carried = Carrying;
            if (carried != null)
            {
                var carry = GameText.Format("interact.carrying", carried.DisplayName);
                prompt = string.IsNullOrEmpty(prompt) ? carry : prompt + "\n" + carry;
            }
            Prompt = prompt ?? string.Empty;
        }

        // ---------- host ----------

        private void ServerTickHold()
        {
            if (serverHold == null) return;
            if (!Alive || !serverHold.IsSpawned || !serverHold.ServerInRange(this) || !serverHold.ServerHold(this, Time.deltaTime))
                ServerEndHold();
        }

        private void ServerEndHold()
        {
            if (serverHold == null) return;
            var ended = serverHold;
            serverHold = null;
            ended.ServerHoldEnded(this);
        }

        private bool TryResolve(NetworkObjectReference reference, out Interactable target)
        {
            target = null;
            if (!Alive || !reference.TryGet(out var obj)) return false;
            target = obj.GetComponent<Interactable>();
            return target != null && target.ServerInRange(this);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void BeginHoldRpc(NetworkObjectReference reference)
        {
            ServerEndHold();
            if (TryResolve(reference, out var target)) serverHold = target;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void EndHoldRpc() => ServerEndHold();

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void UseRpc(NetworkObjectReference reference)
        {
            if (TryResolve(reference, out var target)) target.ServerUse(this);
        }

        // Only counts for the target this player is actually holding on the host.
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SkillCheckRpc(NetworkObjectReference reference, SkillCheckResult result)
        {
            if (serverHold == null || !reference.TryGet(out var obj) || obj != serverHold.NetworkObject) return;
            if (serverHold.UsesSkillChecks) serverHold.ServerSkillCheck(this, result);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void DropRpc()
        {
            var carried = Carrying;
            if (carried != null) carried.ServerDrop(this);
        }
    }
}
