using CoOpHorror.Gameplay;
using CoOpHorror.Localization;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoOpHorror.Player
{
    // Left mouse button: use what you look at — press it, or hold (with the nose: drink an appliance's or a
    // fixture's power into your tank). Right mouse button held: the nose pumps your tank OUT into it (the ship's
    // transmitter, or back into an appliance / lamp / camera to switch it on again). A body can still be lifted
    // with telekinesis (left hold; sticky: you may look around; right mouse + mouse turns it).
    // Appliances and fixtures right in front of you pull the nose onto them even when the crosshair is a
    // little off. The owner only requests; the host checks range, life and target state before anything changes.
    public sealed class PlayerInteractor : NetworkBehaviour
    {
        public const float PressOrHoldDelay = 0.28f; // keep the button down this long to grab instead of hit
        public const float TurnSendInterval = 0.05f;
        public const float TurnSensitivity = 0.3f;
        public const float NoseAssistRange = 2.3f;   // things this close in front catch the nose
        public const float NoseAssistAngle = 28f;

        public enum SkillAutomation { Off, Perfect, Miss }

        public static PlayerInteractor Local { get; private set; }

        // Owner-side view for the HUD.
        public Interactable Target { get; private set; }
        public InteractKind TargetKind { get; private set; }        // left button
        public InteractKind TargetKindRight { get; private set; }   // right button
        public string Prompt { get; private set; } = string.Empty;
        public bool HoldingUse => ownerHold != null;
        public bool HoldingRight => ownerHold != null && ownerHoldRight;
        public bool RotatingHeld { get; private set; }
        public SkillCheck Check { get; } = new();

        // Automation hooks for CLI / end-to-end tests (owner only; real input is unaffected).
        public Interactable AutomationTarget { get; set; }
        public bool AutomationHold { get; set; }        // left button held
        public bool AutomationHoldRight { get; set; }   // right button held
        public SkillAutomation AutomationSkill { get; set; }
        public Vector2 AutomationTurn { get; set; }
        private bool automationPress, automationSkill;
        public void AutomationPress() => automationPress = true;
        public void AutomationSkillPress() => automationSkill = true;

        public int SkillChecksSeen { get; private set; }
        public int SkillChecksMissed { get; private set; }

        private FirstPersonController motor;
        private PlayerHealth health;
        private PlayerNose nose;
        private Interactable ownerHold;   // owner: what we told the host we are holding
        private bool ownerHoldRight;      // ...with which button
        private bool ownerHoldSticky;
        private bool blockLeft, blockRight; // a hold ended while that button is still down
        private Interactable pressTarget;
        private float pressTime;
        private Vector2 turnAccumulated;
        private int holdSerial;           // owner: increments per requested hold
        private float nextResync;
        private int serverHoldSerial;     // host: serial of the current hold
        private float nextTurnSend;
        private SkillCheck.State lastCheckState;
        private Interactable serverHold;  // host: what this player is holding
        private bool serverHoldRight;
        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        // How fast this player moves (every peer, from the transform): carried loads lag behind it.
        public Vector3 MotionVelocity { get; private set; }
        private Vector3 motionLast;

        private void FixedUpdate()
        {
            var position = transform.position;
            var velocity = (position - motionLast) / Mathf.Max(Time.fixedDeltaTime, 0.0001f);
            if (velocity.sqrMagnitude > 900f) velocity = Vector3.zero; // teleports
            MotionVelocity = Vector3.Lerp(MotionVelocity, velocity, 0.5f);
            motionLast = position;
        }

        public Vector3 EyePosition => motor != null && motor.AimPivot != null ? motor.AimPivot.position : transform.position + Vector3.up * 0.72f;
        public Quaternion AimRotation => motor != null && motor.AimPivot != null ? motor.AimPivot.rotation : transform.rotation;
        public Vector3 AimForward => AimRotation * Vector3.forward;

        // Where telekinesis holds things: in front of the eyes along the look direction.
        public Vector3 GrabPoint() => EyePosition + AimForward * Grabbable.HoldDistance;

        public Grabbable HeldGrabbable
        {
            get
            {
                if (!IsSpawned) return null;
                foreach (var g in Grabbable.All) if (g != null && g.HeldBy(this)) return g;
                return null;
            }
        }

        public bool HandsFull => HeldGrabbable != null;

        private void Awake()
        {
            motor = GetComponent<FirstPersonController>();
            health = GetComponent<PlayerHealth>();
            nose = GetComponent<PlayerNose>();
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

        public void ServerLetGoOfEverything()
        {
            ServerEndHold();
            foreach (var g in Grabbable.All) if (g != null && g.HeldBy(this)) g.ServerHoldEnded(this);
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
            // A clock spider on your back keeps your hands (and nose) busy.
            var ready = FirstPersonController.GameplayInputReady && Alive && !TempMonster.LocalPinned;
            var heldLeft = (ready && m != null && m.leftButton.isPressed) || AutomationHold;
            var heldRight = (ready && m != null && m.rightButton.isPressed) || AutomationHoldRight;
            var pressed = (ready && m != null && m.leftButton.wasPressedThisFrame) || automationPress;
            var skill = (ready && k != null && k.spaceKey.wasPressedThisFrame) || automationSkill || AutoSkillPress();
            automationPress = automationSkill = false;
            if (!heldLeft) blockLeft = false;
            if (!heldRight) blockRight = false;

            if (ownerHold != null)
            {
                bool keep;
                var held = ownerHoldRight ? heldRight : heldLeft;
                var kind = ownerHoldRight ? TargetKindRight : TargetKind;
                if (!ownerHold.IsSpawned || !Alive) keep = false;
                else if (ownerHoldSticky) keep = held;
                else keep = held && Target == ownerHold && (kind == InteractKind.Hold || kind == InteractKind.PressOrHold);
                if (!keep) EndOwnerHold();
            }

            if (ownerHold == null && Target != null)
            {
                // right button: pump the tank out into it
                if (!blockRight && heldRight && TargetKindRight == InteractKind.Hold) BeginOwnerHold(Target, true);
                else if (!blockLeft)
                {
                    switch (TargetKind)
                    {
                        case InteractKind.Hold:
                            if (heldLeft) BeginOwnerHold(Target, false);
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
                            else if (heldLeft && pressTarget == Target && Time.time - pressTime >= PressOrHoldDelay)
                            {
                                pressTarget = null;
                                BeginOwnerHold(Target, false);
                            }
                            break;
                    }
                }
            }
            if (!heldLeft) pressTarget = null;

            // Resync: the host still has us lifting something we no longer hold here.
            if (ownerHold == null && Time.unscaledTime >= nextResync && HeldGrabbable != null)
            {
                nextResync = Time.unscaledTime + 0.5f;
                EndHoldRpc();
            }

            UpdateTurning(heldRight || AutomationTurn != Vector2.zero, m);

            var checkHolding = ownerHold != null && !ownerHoldRight && ownerHold.UsesSkillChecks;
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
            var a = Check.NeedleAngle;
            return a >= Check.ZoneStart + 1f && a <= Check.ZoneStart + SkillCheck.ZoneWidth - 2f;
        }

        private void UpdateTurning(bool rotate, Mouse m)
        {
            RotatingHeld = rotate && ownerHold is Grabbable && ownerHoldSticky && !ownerHoldRight;
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

        private void BeginOwnerHold(Interactable target, bool right)
        {
            ownerHold = target;
            ownerHoldRight = right;
            ownerHoldSticky = target.StickyHold;
            BeginHoldRpc(target.NetworkObject, ++holdSerial, right);
        }

        private void EndOwnerHold()
        {
            var right = ownerHoldRight;
            ownerHold = null;
            EndHoldRpc();
            BlockWhileDown(right);
        }

        // The button that ran a hold must be released before it starts another one.
        private void BlockWhileDown(bool right)
        {
            var m = Mouse.current;
            if (right) { if ((m != null && m.rightButton.isPressed) || AutomationHoldRight) blockRight = true; }
            else if ((m != null && m.leftButton.isPressed) || AutomationHold) blockLeft = true;
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
                // Things we carry do not block the view.
                var count = Physics.RaycastNonAlloc(ray, Hits, Interactable.UseRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
                var nearest = float.MaxValue;
                var carried = HeldGrabbable;
                for (var i = 0; i < count; i++)
                {
                    var c = Hits[i].collider;
                    if (c == null || c.transform.IsChildOf(transform) || Hits[i].distance >= nearest) continue;
                    var interactable = c.GetComponentInParent<Interactable>();
                    if (interactable != null && !interactable.TargetedBy(c)) interactable = null;
                    if (carried != null && interactable == carried) continue;
                    if (c.isTrigger && interactable == null) continue;
                    nearest = Hits[i].distance;
                    found = interactable; // a wall or player in front blocks
                }
                if (found == null) found = NoseAssist(ray, nearest);
            }
            if (found != null && !found.IsSpawned) found = null;

            Target = found;
            var prompt = string.Empty;
            var promptRight = string.Empty;
            TargetKind = found != null && Alive ? found.Describe(this, out prompt) : InteractKind.None;
            TargetKindRight = found != null && Alive ? found.DescribeRight(this, out promptRight) : InteractKind.None;
            if (!string.IsNullOrEmpty(promptRight)) prompt = string.IsNullOrEmpty(prompt) ? promptRight : prompt + "\n" + promptRight;
            var carrying = CarryingPrompt();
            if (!string.IsNullOrEmpty(carrying))
            {
                // While lifting something the target does not matter.
                if (ownerHoldSticky && ownerHold is Grabbable) prompt = carrying;
                else prompt = string.IsNullOrEmpty(prompt) ? carrying : prompt + "\n" + carrying;
            }
            Prompt = prompt ?? string.Empty;
        }

        // The nose finds an appliance or fixture right in front of you (within reach, in a narrow cone, in plain
        // sight) even when the crosshair is just off it.
        private Interactable NoseAssist(Ray ray, float blockedAt)
        {
            Interactable best = null;
            var bestScore = float.MaxValue;
            void Consider(Interactable target, INoseTarget nosed)
            {
                var point = nosed.NosePoint(ray.origin);
                var offset = point - ray.origin;
                var distance = offset.magnitude;
                if (distance > NoseAssistRange || distance >= blockedAt + 0.3f || distance < 0.01f) return;
                var angle = Vector3.Angle(ray.direction, offset);
                if (angle > NoseAssistAngle) return;
                if (Physics.Raycast(ray.origin, offset / distance, out var hit, distance - 0.05f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && !hit.collider.transform.IsChildOf(target.transform) && !hit.collider.transform.IsChildOf(transform)) return;
                var score = angle + distance * 4f;
                if (score < bestScore) { bestScore = score; best = target; }
            }
            foreach (var a in Appliance.All) if (a != null && a.IsSpawned) Consider(a, a);
            foreach (var d in InstalledDevice.Devices) if (d != null && d.IsSpawned) Consider(d, d);
            foreach (var m in TempMonster.All) if (m != null && m.NoseDrainable) Consider(m, m); // creatures (their power is their health)
            foreach (var b in CreatureBattery.All) if (b != null && b.NoseDrainable) Consider(b, b); // batteries dead creatures left
            return best;
        }

        private string CarryingPrompt()
        {
            var held = HeldGrabbable;
            if (held == null) return string.Empty;
            return GameText.Format("interact.carrying", held.DisplayName, held.Holders.Count, held.Weight);
        }

        // ---------- host ----------

        private void ServerTickHold()
        {
            if (serverHold == null) return;
            var ok = Alive && serverHold.IsSpawned && serverHold.ServerInRange(this, serverHold.HoldRange)
                && (serverHoldRight ? serverHold.ServerHoldRight(this, Time.deltaTime) : serverHold.ServerHold(this, Time.deltaTime));
            if (!ok) ServerForceEndHold(serverHold);
        }

        private void ServerStartHold(Interactable target, bool right)
        {
            serverHold = target;
            serverHoldRight = right;
            if (nose != null && target is INoseTarget) nose.ServerAttach(target.NetworkObject, PlayerNose.FlowNone);
        }

        private void ServerEndHold()
        {
            if (serverHold == null) return;
            var ended = serverHold;
            serverHold = null;
            if (nose != null && ended is INoseTarget) nose.ServerDetach();
            ended.ServerHoldEnded(this);
        }

        // Host ended a hold on its own (drained, full, out of range, stuck): tell the owner.
        public void ServerForceEndHold(Interactable target)
        {
            if (serverHold != target || target == null) return;
            ServerEndHold();
            HoldEndedRpc(target.NetworkObject, serverHoldSerial);
        }

        private bool TryResolve(NetworkObjectReference reference, out Interactable target)
        {
            target = null;
            if (!Alive || !reference.TryGet(out var obj)) return false;
            target = obj.GetComponent<Interactable>();
            return target != null && target.ServerInRange(this);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void BeginHoldRpc(NetworkObjectReference reference, int serial, bool right)
        {
            ServerEndHold();
            serverHoldSerial = serial;
            if (TryResolve(reference, out var target)) ServerStartHold(target, right);
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
            if (serverHold == null || serverHoldRight || !reference.TryGet(out var obj) || obj != serverHold.NetworkObject) return;
            if (serverHold.UsesSkillChecks) serverHold.ServerSkillCheck(this, result);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void TurnRpc(float yaw, float pitch)
        {
            if (serverHold is Grabbable grabbable && !serverHoldRight) grabbable.ServerTurn(this, yaw, pitch);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void HoldEndedRpc(NetworkObjectReference reference, int serial)
        {
            // Ignore a late notice about an older hold.
            if (ownerHold == null || !reference.TryGet(out var obj) || obj != ownerHold.NetworkObject) return;
            if (serial != holdSerial) return;
            var right = ownerHoldRight;
            ownerHold = null;
            BlockWhileDown(right);
        }
    }
}
