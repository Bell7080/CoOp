using CoOpHorror.Networking;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace CoOpHorror.Player
{
    // First-person movement for the development fight room.
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

        public Transform AimPivot;

        // Owner-written so remote players see roughly where the attacker is looking.
        public readonly NetworkVariable<float> Pitch = new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public static FirstPersonController Local { get; private set; }
        public static bool CursorCaptured { get; private set; }
        // True when gameplay mouse buttons belong to the game (not the frame that re-captured the cursor).
        public static bool GameplayInputReady => CursorCaptured && Time.frameCount != captureFrame && !IsTyping();

        private static int captureFrame = -1;
        private static float captureTime = -10f;
        private static Transform cameraHomeParent;
        private static Vector3 cameraHomePosition;
        private static Quaternion cameraHomeRotation;
        private static bool cameraHomeSaved;

        private CharacterController controller;
        private PlayerHealth health;
        private ConnectionAvatar signal;
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

            // Locking the cursor produces one large delta; ignore the first frames after capture.
            if (CursorCaptured && alive && Mouse.current != null && Time.frameCount > captureFrame + 2 && Time.unscaledTime > captureTime + 0.2f)
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
            if (k != null && !typing && alive)
            {
                input = new Vector2((k.dKey.isPressed ? 1 : 0) - (k.aKey.isPressed ? 1 : 0), (k.wKey.isPressed ? 1 : 0) - (k.sKey.isPressed ? 1 : 0));
                run = k.leftShiftKey.isPressed || k.rightShiftKey.isPressed;
                jump = k.spaceKey.wasPressedThisFrame;
                if (k.eKey.wasPressedThisFrame && signal != null) signal.SendSignal();
            }

            if (alive && AutomationMove != Vector2.zero) { input = AutomationMove; run |= AutomationRun; }
            jump |= alive && automationJump;
            automationJump = false;

            var move = (transform.right * input.x + transform.forward * input.y);
            move = Vector3.ClampMagnitude(move, 1f) * (run ? RunSpeed : WalkSpeed);
            var grounded = controller.isGrounded;
            if (grounded && verticalVelocity < 0f) verticalVelocity = -2f;
            if (grounded && jump) verticalVelocity = Mathf.Sqrt(2f * JumpHeight * -Gravity);
            verticalVelocity += Gravity * dt;
            impulse = Vector3.MoveTowards(impulse, Vector3.zero, (grounded ? 14f : 4f) * dt);
            if (controller.enabled) controller.Move((move + impulse + Vector3.up * verticalVelocity) * dt);
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

        private void UpdateCursor()
        {
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
            if (attachedCamera.transform.parent == AimPivot)
            {
                attachedCamera.transform.SetParent(cameraHomeParent, false);
                attachedCamera.transform.SetPositionAndRotation(cameraHomePosition, cameraHomeRotation);
            }
            attachedCamera = null;
        }
    }
}
