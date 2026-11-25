using System.Collections.Generic;
using CoOpHorror.Networking;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace CoOpHorror.Player
{
    // First-person movement for the squishy mole alien. Ctrl squeezes the body down (crouch) to crawl under
    // desks and beds; tucked under something, monsters only notice you up close.
    // Movement stays owner-authoritative through the Owner NetworkTransform (prototype, not anti-cheat).
    [RequireComponent(typeof(CharacterController))]
    public sealed class FirstPersonController : NetworkBehaviour
    {
        public const float WalkSpeed = 4f;
        public const float RunSpeed = 7f;
        public const float JumpHeight = 1.1f;
        public const float Gravity = -20f;
        public const float MaxPitch = 85f;
        public const float LookSensitivity = 0.08f;
        public const float StaminaDrain = 0.2f;     // per second while running (5 s from full)
        public const float StaminaRegen = 0.16f;    // per second after the delay
        public const float StaminaRegenDelay = 0.8f;
        public const float StaminaResume = 0.3f;    // after running dry, wait until this much is back
        public const float CrouchSpeed = 1.8f;
        public const float StandHeight = 1.5f;      // CharacterController; the root sits 1 m above the feet
        public const float StandCenterY = -0.25f;
        public const float CrouchHeight = 0.8f;
        public const float CrouchCenterY = -0.6f;
        public const float StandEyeY = 0.25f;
        public const float CrouchEyeY = -0.32f;
        public const float TuckedCheck = 1.3f;      // something solid this close above a crouched player hides them

        public Transform AimPivot;

        // Owner-local stamina 0..1 (presentation and movement only; not replicated).
        public float Stamina { get; private set; } = 1f;
        public bool Exhausted { get; private set; }

        // Owner-written so remote players see roughly where the attacker is looking.
        public readonly NetworkVariable<float> Pitch = new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<bool> Crouched = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public bool AutomationCrouch { get; set; }

        public static FirstPersonController Local { get; private set; }
        public static bool CursorCaptured { get; private set; }
        // A full-screen view (CCTV) owns the mouse and keys: no walking, looking or interacting.
        public static bool ViewLocked { get; set; }
        // True when gameplay mouse buttons belong to the game (not the frame that re-captured the cursor).
        public static bool GameplayInputReady => CursorCaptured && !ViewLocked && Time.frameCount != captureFrame && !IsTyping();

        private static int captureFrame = -1;
        private static float captureTime = -10f;
        private static Transform cameraHomeParent;
        private static Vector3 cameraHomePosition;
        private static Quaternion cameraHomeRotation;
        private static bool cameraHomeSaved;

        private CharacterController controller;
        private PlayerHealth health;
        private PlayerInteractor interactor;
        private ConnectionAvatar signal;
        private float staminaRestAt;
        private float verticalVelocity;
        private Vector3 impulse;
        private float pitch;
        private Camera attachedCamera;

        // Automation hook for CLI/Play Mode verification (clone editors drop synthetic keyboard input
        // while unfocused). Owner only; real input is unaffected.
        public Vector2 AutomationMove { get; set; }
        public bool AutomationRun { get; set; }
        private bool automationJump;
        public void RequestJump() => automationJump = true;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            health = GetComponent<PlayerHealth>();
            interactor = GetComponent<PlayerInteractor>();
            signal = GetComponent<ConnectionAvatar>();
            if (AimPivot == null) AimPivot = transform.Find("AimPivot");
        }

        public override void OnNetworkSpawn()
        {
            var visual = GetComponent<PlayerAvatarVisual>();
            if (visual != null) visual.Configure(IsOwner, OwnerClientId);
            if (!IsOwner) return;
            // The approved spawn pose is applied to the transform after the CharacterController was
            // created; re-enable it so its capsule starts from that pose instead of falling through the floor.
            controller.enabled = false;
            controller.enabled = true;
            Physics.SyncTransforms();
            Local = this;
            AttachCamera();
            SetCaptured(true);
        }

        public override void OnNetworkDespawn()
        {
            if (!IsOwner) return;
            DetachCamera();
            if (Local == this)
            {
                Local = null;
                ViewLocked = false;
                SetCaptured(false);
            }
        }

        public override void OnDestroy()
        {
            DetachCamera();
            if (Local == this) { Local = null; SetCaptured(false); }
            base.OnDestroy();
        }

        private void Update()
        {
            if (!IsSpawned) return;
            ApplyStance();
            if (!IsOwner)
            {
                if (AimPivot != null)
                    AimPivot.localRotation = Quaternion.Slerp(AimPivot.localRotation, Quaternion.Euler(Pitch.Value, 0, 0), 1f - Mathf.Exp(-20f * Time.deltaTime));
                return;
            }

            UpdateCursor();
            var alive = health == null || !health.IsDead.Value;
            var typing = IsTyping();
            var k = Keyboard.current;
            var dt = Mathf.Clamp(Time.deltaTime, 0f, 0.05f);

            // Dead: an invisible ghost spectating the living.
            if (!alive) { UpdateSpectator(dt); return; }
            if (spectating) StopSpectating();

            // Locking the cursor produces one large delta; ignore the first frames after capture.
            var turningHeld = interactor != null && interactor.RotatingHeld;
            if (CursorCaptured && !ViewLocked && !turningHeld && Mouse.current != null && Time.frameCount > captureFrame + 2 && Time.unscaledTime > captureTime + 0.2f)
            {
                // Clamp per-frame jumps (cursor warp on lock/focus) so the view never snaps.
                var delta = Vector2.ClampMagnitude(Mouse.current.delta.ReadValue(), 250f) * LookSensitivity;
                transform.Rotate(0f, delta.x, 0f, Space.Self);
                pitch = Mathf.Clamp(pitch - delta.y, -MaxPitch, MaxPitch);
                if (AimPivot != null) AimPivot.localRotation = Quaternion.Euler(pitch, 0, 0);
                if (Mathf.Abs(Pitch.Value - pitch) > 0.5f) Pitch.Value = pitch;
            }

            var input = Vector2.zero;
            var run = false;
            var jump = false;
            var crouch = AutomationCrouch;
            if (k != null && !typing && alive && !ViewLocked)
            {
                crouch |= k.leftCtrlKey.isPressed || k.rightCtrlKey.isPressed;
                input = new Vector2((k.dKey.isPressed ? 1 : 0) - (k.aKey.isPressed ? 1 : 0), (k.wKey.isPressed ? 1 : 0) - (k.sKey.isPressed ? 1 : 0));
                run = k.leftShiftKey.isPressed || k.rightShiftKey.isPressed;
                // Space is the skill-check key while holding the use button on something.
                jump = k.spaceKey.wasPressedThisFrame && (interactor == null || !interactor.HoldingUse);
                // E is reserved for the radio (planned); the dev sync signal is on G.
                if (k.gKey.wasPressedThisFrame && signal != null) signal.SendSignal();
            }

            if (alive && AutomationMove != Vector2.zero) { input = AutomationMove; run |= AutomationRun; }
            jump |= alive && automationJump;
            automationJump = false;

            // Stay squeezed while something is overhead.
            if (!crouch && Crouched.Value && !CanStandUp()) crouch = true;
            if (Crouched.Value != crouch) Crouched.Value = crouch;
            if (crouch) { run = false; jump = false; }

            var running = run && !Exhausted && input != Vector2.zero;
            if (running)
            {
                Stamina = Mathf.Max(0f, Stamina - StaminaDrain * dt);
                staminaRestAt = Time.time + StaminaRegenDelay;
                if (Stamina <= 0f) Exhausted = true;
            }
            else if (Time.time >= staminaRestAt)
            {
                Stamina = Mathf.Min(1f, Stamina + StaminaRegen * dt);
                if (Exhausted && Stamina >= StaminaResume) Exhausted = false;
            }

            var move = (transform.right * input.x + transform.forward * input.y);
            move = Vector3.ClampMagnitude(move, 1f) * (crouch ? CrouchSpeed : running ? RunSpeed : WalkSpeed);
            var grounded = controller.isGrounded;
            if (grounded && verticalVelocity < 0f) verticalVelocity = -2f;
            if (grounded && jump) verticalVelocity = Mathf.Sqrt(2f * JumpHeight * -Gravity);
            verticalVelocity += Gravity * dt;
            impulse = Vector3.MoveTowards(impulse, Vector3.zero, (grounded ? 14f : 4f) * dt);
            if (controller.enabled) controller.Move((move + impulse + Vector3.up * verticalVelocity) * dt);
        }

        // ---------- crouch ----------

        // Every peer sizes the capsule from the replicated stance so host-side sight checks match.
        private void ApplyStance()
        {
            var crouched = Crouched.Value;
            var height = crouched ? CrouchHeight : StandHeight;
            if (!Mathf.Approximately(controller.height, height))
            {
                controller.height = height;
                controller.center = new Vector3(0f, crouched ? CrouchCenterY : StandCenterY, 0f);
            }
            if (AimPivot != null)
            {
                var eye = new Vector3(0f, crouched ? CrouchEyeY : StandEyeY, 0f);
                AimPivot.localPosition = Vector3.MoveTowards(AimPivot.localPosition, eye, 4f * Time.deltaTime);
            }
        }

        private bool CanStandUp()
        {
            var radius = controller.radius * 0.9f;
            var top = transform.position + Vector3.up * (CrouchCenterY + CrouchHeight / 2f - radius);
            return !Physics.SphereCast(top, radius, Vector3.up, out _, StandHeight - CrouchHeight + 0.05f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        // Host: crouched under furniture (something solid just overhead) — hidden from monsters at range.
        public bool IsTucked()
        {
            if (!Crouched.Value) return false;
            var head = transform.position + Vector3.up * (CrouchCenterY + CrouchHeight / 2f - 0.05f);
            var hits = Physics.RaycastAll(head, Vector3.up, TuckedCheck, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            foreach (var h in hits) if (!h.collider.transform.IsChildOf(transform)) return true;
            return false;
        }

        // ---------- ghost spectating ----------

        public static ulong? SpectatingClient { get; private set; }
        private bool spectating;
        private int spectateIndex;
        private Vector3 spectateVelocity;
        private static readonly List<PlayerHealth> Living = new();

        private void UpdateSpectator(float dt)
        {
            var cam = attachedCamera;
            if (cam == null) return;
            if (!spectating)
            {
                spectating = true;
                cam.transform.SetParent(null, true);
            }
            Living.Clear();
            foreach (var p in PlayerHealth.All)
                if (p != null && p.IsSpawned && !p.IsDead.Value && p.gameObject != gameObject) Living.Add(p);
            Living.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));
            var m = Mouse.current;
            if (CursorCaptured && !ViewLocked && m != null && Living.Count > 0)
            {
                if (m.leftButton.wasPressedThisFrame) spectateIndex++;
                if (m.rightButton.wasPressedThisFrame) spectateIndex--;
            }

            Vector3 focus;
            Vector3 back;
            if (Living.Count > 0)
            {
                var target = Living[((spectateIndex % Living.Count) + Living.Count) % Living.Count];
                SpectatingClient = target.OwnerClientId;
                focus = target.transform.position + Vector3.up * 0.7f;
                back = -target.transform.forward;
            }
            else
            {
                // Nobody left: hover over our own body.
                SpectatingClient = null;
                var body = health != null ? health.Body : null;
                focus = (body != null ? body.transform.position : transform.position) + Vector3.up * 0.3f;
                back = Quaternion.Euler(0f, Time.time * 12f, 0f) * Vector3.back;
            }
            var desired = focus + back * 3.2f + Vector3.up * 1.3f;
            var toDesired = desired - focus;
            if (Physics.SphereCast(focus, 0.2f, toDesired.normalized, out var hit, toDesired.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                desired = focus + toDesired.normalized * Mathf.Max(0.4f, hit.distance - 0.1f);
            cam.transform.position = Vector3.SmoothDamp(cam.transform.position, desired, ref spectateVelocity, 0.15f, 50f, dt);
            cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, Quaternion.LookRotation(focus - cam.transform.position), 1f - Mathf.Exp(-10f * dt));
        }

        private void StopSpectating()
        {
            spectating = false;
            SpectatingClient = null;
            if (attachedCamera == null || AimPivot == null) return;
            attachedCamera.transform.SetParent(AimPivot, false);
            attachedCamera.transform.localPosition = Vector3.zero;
            attachedCamera.transform.localRotation = Quaternion.identity;
        }

        // End-to-end tests (owner): face a direction.
        public void AutomationLook(float yaw, float lookPitch)
        {
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            pitch = Mathf.Clamp(lookPitch, -MaxPitch, MaxPitch);
            if (AimPivot != null) AimPivot.localRotation = Quaternion.Euler(pitch, 0, 0);
            Pitch.Value = pitch;
        }

        // Called on the owner by the server through PlayerHealth.
        public void AddImpulse(Vector3 velocity)
        {
            impulse += new Vector3(velocity.x, 0f, velocity.z);
            if (velocity.y > 0f) verticalVelocity = Mathf.Max(verticalVelocity, velocity.y);
        }

        public void TeleportTo(Vector3 position, float yaw)
        {
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            var sync = GetComponent<NetworkTransform>();
            if (sync != null && sync.CanCommitToTransform) sync.Teleport(position, rotation, transform.localScale);
            controller.enabled = true;
            verticalVelocity = 0f;
            impulse = Vector3.zero;
        }

        // A full-screen view (CCTV) frees the cursor and takes it back when it closes.
        public static void SetCursorCaptured(bool captured) => SetCaptured(captured);

        private void UpdateCursor()
        {
            if (ViewLocked) return;
            var k = Keyboard.current;
            var m = Mouse.current;
            if (k != null && k.escapeKey.wasPressedThisFrame) { SetCaptured(false); return; }
            if (CursorCaptured || m == null || !m.leftButton.wasPressedThisFrame || IsTyping()) return;
            var overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (overUi) return;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            SetCaptured(true);
        }

        private void OnApplicationFocus(bool focus)
        {
            if (!focus && IsOwner) SetCaptured(false);
        }

        private static void SetCaptured(bool captured)
        {
            if (captured && !CursorCaptured) { captureFrame = Time.frameCount; captureTime = Time.unscaledTime; }
            CursorCaptured = captured;
            Cursor.lockState = captured ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !captured;
        }

        public static bool IsTyping()
        {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selected == null) return false;
            var field = selected.GetComponent<UnityEngine.UI.InputField>();
            return field != null && field.isFocused;
        }

        private void AttachCamera()
        {
            var cam = Camera.main;
            if (cam == null || AimPivot == null) return;
            if (!cameraHomeSaved)
            {
                cameraHomeParent = cam.transform.parent;
                cameraHomePosition = cam.transform.position;
                cameraHomeRotation = cam.transform.rotation;
                cameraHomeSaved = true;
            }
            cam.transform.SetParent(AimPivot, false);
            cam.transform.localPosition = Vector3.zero;
            cam.transform.localRotation = Quaternion.identity;
            cam.nearClipPlane = 0.03f;
            cam.fieldOfView = 75f;
            attachedCamera = cam;
        }

        private void DetachCamera()
        {
            if (attachedCamera == null || !cameraHomeSaved) return;
            if (attachedCamera.transform.parent == AimPivot || spectating)
            {
                spectating = false;
                SpectatingClient = null;
                attachedCamera.transform.SetParent(cameraHomeParent, false);
                attachedCamera.transform.SetPositionAndRotation(cameraHomePosition, cameraHomeRotation);
            }
            attachedCamera = null;
        }
    }
}
