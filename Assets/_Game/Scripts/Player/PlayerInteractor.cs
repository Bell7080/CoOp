using CoOpHorror.Gameplay;
using CoOpHorror.Localization;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoOpHorror.Player
{
    // Left mouse button: use what you look at — press, hold (charge, crank) or keep holding to lift it with
    // telekinesis (sticky: you may look around). Right mouse + mouse while lifting: turn it. Q: drop a plug or
    // let go of something taken out of the inventory. Space answers skill checks while holding.
    // The owner only requests; the host checks range, life and target state before anything changes.
    public sealed class PlayerInteractor : NetworkBehaviour
    {
        public const float PressOrHoldDelay = 0.28f; // keep the button down this long to grab instead of hit
        public const float TurnSendInterval = 0.05f;
        public const float TurnSensitivity = 0.3f;

        public enum SkillAutomation { Off, Perfect, Miss }

        public static PlayerInteractor Local { get; private set; }

        // Owner-side view for the HUD.
        public Interactable Target { get; private set; }
        public InteractKind TargetKind { get; private set; }
        public string Prompt { get; private set; } = string.Empty;
        public bool HoldingUse => ownerHold != null;
        public bool RotatingHeld { get; private set; }
        public SkillCheck Check { get; } = new();

        // Automation hooks for CLI / end-to-end tests (owner only; real input is unaffected).
        public Interactable AutomationTarget { get; set; }
        public bool AutomationHold { get; set; }
        public SkillAutomation AutomationSkill { get; set; }
        public Vector2 AutomationTurn { get; set; }
        private bool automationPress, automationDrop, automationSkill;
        public void AutomationPress() => automationPress = true;
        public void AutomationDrop() => automationDrop = true;
        public void AutomationSkillPress() => automationSkill = true;
        public int SkillChecksSeen { get; private set; }
        public int SkillChecksMissed { get; private set; }

        private FirstPersonController motor;
        private PlayerHealth health;
        private Interactable ownerHold;   // owner: what we told the host we are holding
        private bool ownerHoldSticky;
        private bool ownerHoldLatched;    // taken out of the inventory: held until the next click or Q
        private bool blockUntilRelease;   // a hold ended while the button is still down
        private Interactable pressTarget;
        private float pressTime;
        private Vector2 turnAccumulated;
        private int holdSerial;           // owner: increments per requested hold
        private float nextResync;
        private int serverHoldSerial;     // host: serial of the current hold
        private float nextTurnSend;
        private SkillCheck.State lastCheckState;
        private Interactable serverHold;  // host: what this player is holding
        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        public Vector3 EyePosition => motor != null && motor.AimPivot != null ? motor.AimPivot.position : transform.position + Vector3.up * 0.72f;
        public Quaternion AimRotation => motor != null && motor.AimPivot != null ? motor.AimPivot.rotation : transform.rotation;
        public Vector3 AimForward => AimRotation * Vector3.forward;

        // Where telekinesis holds things: in front of the eyes along the look direction.
        public Vector3 GrabPoint() => EyePosition + AimForward * Grabbable.HoldDistance;

        // Where a carried plug sits: low on the right.
        public Vector3 HandPoint() => EyePosition + AimRotation * new Vector3(0.28f, -0.32f, 0.45f);

        public Grabbable HeldGrabbable
        {
            get
            {
                if (!IsSpawned) return null;
                foreach (var g in Grabbable.All) if (g != null && g.HeldBy(this)) return g;
                return null;
            }
        }

        public CordPlug HeldPlug
        {
            get
            {
                if (!IsSpawned) return null;
                foreach (var p in CordPlug.All) if (p != null && p.HeldBy(this)) return p;
                return null;
            }
        }

        public bool HandsFull => HeldGrabbable != null || HeldPlug != null;

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
                ServerLetGoOfEverything();
            }
            ownerHold = null;
            if (Local == this) Local = null;
        }

        private void OnServerDeadChanged(bool previous, bool dead)
        {
            if (dead) ServerLetGoOfEverything();
        }

        private void ServerLetGoOfEverything()
        {
            ServerEndHold();
            var plug = HeldPlug;
            if (plug != null) plug.ServerDrop(this);
            foreach (var g in Grabbable.All) if (g != null && g.HeldBy(this)) g.ServerHoldEnded(this);
            var inventory = GetComponent<PlayerInventory>();
            if (inventory != null) inventory.ServerDropAll();
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
            var skill = (ready && k != null && k.spaceKey.wasPressedThisFrame) || automationSkill || AutoSkillPress();
            var rotate = (ready && m != null && m.rightButton.isPressed) || AutomationTurn != Vector2.zero;
            automationPress = automationDrop = automationSkill = false;
            if (!held) blockUntilRelease = false;

            var consumed = false;
            if (ownerHold != null)
            {
                bool keep;
                if (!ownerHold.IsSpawned || !Alive) keep = false;
                else if (ownerHoldLatched)
                {
                    keep = !pressed && !drop;
                    consumed = !keep;
                }
                else if (ownerHoldSticky) keep = held;
                else keep = held && Target == ownerHold && (TargetKind == InteractKind.Hold || TargetKind == InteractKind.PressOrHold);
                if (!keep) EndOwnerHold();
            }

            if (!consumed && HeldPlug != null && (drop || (pressed && !(Target is Transmitter && TargetKind == InteractKind.Press))))
            {
                DropRpc();
                consumed = true;
            }

            if (ownerHold == null && !consumed && !blockUntilRelease && Target != null)
            {
                switch (TargetKind)
                {
                    case InteractKind.Hold:
                        if (held) BeginOwnerHold(Target);
                        break;
                    case InteractKind.Press:
                        if (pressed && !Target.OwnerUse(this)) UseRpc(Target.NetworkObject);
                        break;
                    case InteractKind.PressOrHold:
                        if (pressed)
                        {
                            UseRpc(Target.NetworkObject);
                            pressTarget = Target;
                            pressTime = Time.time;
                        }
                        else if (held && pressTarget == Target && Time.time - pressTime >= PressOrHoldDelay)
                        {
                            pressTarget = null;
                            BeginOwnerHold(Target);
                        }
                        break;
                }
            }
            if (!held) pressTarget = null;

            // Resync: the host still has us lifting something we no longer hold here.
            if (ownerHold == null && Time.unscaledTime >= nextResync && HeldGrabbable != null)
            {
                nextResync = Time.unscaledTime + 0.5f;
                EndHoldRpc();
            }

            UpdateTurning(rotate, m);

            var checkHolding = ownerHold != null && ownerHold.UsesSkillChecks;
            var result = Check.Tick(checkHolding, skill);
            if (Check.Current == SkillCheck.State.Warning && lastCheckState == SkillCheck.State.Idle) SkillChecksSeen++;
            lastCheckState = Check.Current;
            if (!result.HasValue || ownerHold == null) return;
            if (result.Value == SkillCheckResult.Miss) SkillChecksMissed++;
            SkillCheckRpc(ownerHold.NetworkObject, result.Value);
        }

        // Automation: hit the great zone (Perfect) or let the needle run out (Miss).
        private bool AutoSkillPress()
        {
            if (AutomationSkill != SkillAutomation.Perfect || Check.Current != SkillCheck.State.Running) return false;
            // First frame inside the zone (great if the frame rate allows).
            var a = Check.NeedleAngle;
            return a >= Check.ZoneStart + 1f && a <= Check.ZoneStart + SkillCheck.ZoneWidth - 2f;
        }

        private void UpdateTurning(bool rotate, Mouse m)
        {
            RotatingHeld = rotate && ownerHold is Grabbable && ownerHoldSticky;
            if (!RotatingHeld)
            {
                turnAccumulated = Vector2.zero;
                return;
            }
            var delta = AutomationTurn != Vector2.zero ? AutomationTurn * Time.deltaTime : (m != null ? m.delta.ReadValue() * TurnSensitivity : Vector2.zero);
            turnAccumulated += new Vector2(delta.x, -delta.y);
            if (Time.unscaledTime < nextTurnSend || turnAccumulated == Vector2.zero) return;
            nextTurnSend = Time.unscaledTime + TurnSendInterval;
            TurnRpc(turnAccumulated.x, turnAccumulated.y);
            turnAccumulated = Vector2.zero;
        }

        private void BeginOwnerHold(Interactable target)
        {
            ownerHold = target;
            ownerHoldSticky = target.StickyHold;
            ownerHoldLatched = false;
            BeginHoldRpc(target.NetworkObject, ++holdSerial);
        }

        private void EndOwnerHold()
        {
            ownerHold = null;
            ownerHoldLatched = false;
            EndHoldRpc();
            var m = Mouse.current;
            if ((m != null && m.leftButton.isPressed) || AutomationHold) blockUntilRelease = true;
        }

        private void UpdateTarget()
        {
            Interactable found = AutomationTarget;
            if (found == null)
            {
                var cam = Camera.main;
                var ray = cam != null && cam.transform.IsChildOf(transform)
                    ? cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))
                    : new Ray(EyePosition, AimForward);
                // Collide with triggers too: plugs are triggers. Things we carry do not block the view.
                var count = Physics.RaycastNonAlloc(ray, Hits, Interactable.UseRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
                var nearest = float.MaxValue;
                var carried = (Interactable)HeldGrabbable ?? HeldPlug;
                for (var i = 0; i < count; i++)
                {
                    var c = Hits[i].collider;
                    if (c == null || c.transform.IsChildOf(transform) || Hits[i].distance >= nearest) continue;
                    var interactable = c.GetComponentInParent<Interactable>();
                    if (carried != null && interactable == carried) continue;
                    if (c.isTrigger && interactable == null) continue;
                    nearest = Hits[i].distance;
                    found = interactable; // a wall or player in front blocks
                }
            }
            if (found != null && !found.IsSpawned) found = null;

            Target = found;
            var prompt = string.Empty;
            TargetKind = found != null && Alive ? found.Describe(this, out prompt) : InteractKind.None;
            var carrying = CarryingPrompt();
            if (!string.IsNullOrEmpty(carrying))
            {
                // While lifting something the target does not matter.
                if (ownerHoldSticky && ownerHold != null) prompt = carrying;
                else prompt = string.IsNullOrEmpty(prompt) ? carrying : prompt + "\n" + carrying;
            }
            Prompt = prompt ?? string.Empty;
        }

        private string CarryingPrompt()
        {
            var plug = HeldPlug;
            if (plug != null) return GameText.Format("interact.carryingPlug", plug.DisplayName, plug.Stretch.ToString("0.0"), plug.Length.ToString("0"));
            var held = HeldGrabbable;
            if (held == null) return string.Empty;
            var line = GameText.Format(ownerHoldLatched ? "interact.carryingLatched" : "interact.carrying", held.DisplayName, held.Holders.Count, held.Weight);
            if (held is Appliance appliance)
            {
                line += "\n" + GameText.Format("interact.value", appliance.Value.Value, appliance.BaseValue);
                if (appliance.CanStore) line += " · " + GameText.Get("interact.canStore");
            }
            return line;
        }

        // ---------- host ----------

        private void ServerTickHold()
        {
            if (serverHold == null) return;
            if (!Alive || !serverHold.IsSpawned || !serverHold.ServerInRange(this, serverHold.HoldRange) || !serverHold.ServerHold(this, Time.deltaTime))
                ServerForceEndHold(serverHold);
        }

        private void ServerEndHold()
        {
            if (serverHold == null) return;
            var ended = serverHold;
            serverHold = null;
            ended.ServerHoldEnded(this);
        }

        // Host ended a hold on its own (charged, out of range, stuck): tell the owner.
        public void ServerForceEndHold(Interactable target)
        {
            if (serverHold != target || target == null) return;
            ServerEndHold();
            HoldEndedRpc(target.NetworkObject, serverHoldSerial);
        }

        // Host: start a hold without a button press (inventory take-out). Held until the next click or Q.
        public void ServerBeginLatchedHold(Grabbable target)
        {
            ServerEndHold();
            if (!target.ServerHold(this, 0f)) return;
            serverHold = target;
            serverHoldSerial = -1;
            LatchHoldRpc(target.NetworkObject);
        }

        private bool TryResolve(NetworkObjectReference reference, out Interactable target)
        {
            target = null;
            if (!Alive || !reference.TryGet(out var obj)) return false;
            target = obj.GetComponent<Interactable>();
            return target != null && target.ServerInRange(this);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void BeginHoldRpc(NetworkObjectReference reference, int serial)
        {
            ServerEndHold();
            serverHoldSerial = serial;
            if (TryResolve(reference, out var target)) serverHold = target;
            else if (reference.TryGet(out var obj)) HoldEndedRpc(obj, serial);
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
        private void TurnRpc(float yaw, float pitch)
        {
            if (serverHold is Grabbable grabbable) grabbable.ServerTurn(this, yaw, pitch);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void DropRpc()
        {
            var plug = HeldPlug;
            if (plug != null) plug.ServerDrop(this);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void HoldEndedRpc(NetworkObjectReference reference, int serial)
        {
            // Ignore a late notice about an older hold (-1 = a latched inventory hold).
            if (ownerHold == null || !reference.TryGet(out var obj) || obj != ownerHold.NetworkObject) return;
            if (serial >= 0 && serial != holdSerial) return;
            ownerHold = null;
            ownerHoldLatched = false;
            var m = Mouse.current;
            if ((m != null && m.leftButton.isPressed) || AutomationHold) blockUntilRelease = true;
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void LatchHoldRpc(NetworkObjectReference reference)
        {
            if (!reference.TryGet(out var obj) || !obj.TryGetComponent<Interactable>(out var target)) return;
            ownerHold = target;
            ownerHoldSticky = true;
            ownerHoldLatched = true;
        }
    }
}
