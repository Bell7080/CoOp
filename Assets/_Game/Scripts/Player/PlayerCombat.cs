using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoOpHorror.Player
{
    // Owner requests punches; the host validates cooldown/aim and applies damage.
    public sealed class PlayerCombat : NetworkBehaviour
    {
        public const float Cooldown = 0.4f;
        public const float ServerCooldown = 0.3f; // tolerance for network jitter
        public const float Range = 2.2f;
        public const float Radius = 0.3f;
        public const int MinDamage = 10;
        public const int MaxDamage = 15;
        public const float KnockbackSpeed = 5f;
        public const float KnockbackLift = 2.5f;
        public const float MaxAimOffsetDegrees = 89f;

        // Raised on the attacker's own client when the host confirms a hit.
        public static event Action LocalHitConfirmed;

        private readonly float[] nextLocal = new float[2];
        private readonly float[] nextServer = new float[2];
        private static readonly RaycastHit[] Hits = new RaycastHit[16];
        private PlayerAvatarVisual visual;
        private PlayerHealth health;
        private FirstPersonController motor;

        private void Awake()
        {
            visual = GetComponent<PlayerAvatarVisual>();
            health = GetComponent<PlayerHealth>();
            motor = GetComponent<FirstPersonController>();
        }

        private void Update()
        {
            if (!IsOwner || !IsSpawned || Mouse.current == null) return;
            if (!FirstPersonController.GameplayInputReady || (health != null && health.IsDead.Value)) return;
            if (Mouse.current.leftButton.wasPressedThisFrame) TryPunch(PunchHand.Left);
            if (Mouse.current.rightButton.wasPressedThisFrame) TryPunch(PunchHand.Right);
        }

        // Owner entry point (also used by automated tests).
        public bool TryPunch(PunchHand hand)
        {
            if (!IsOwner || !IsSpawned) return false;
            var i = (int)hand;
            if (Time.time < nextLocal[i]) return false;
            nextLocal[i] = Time.time + Cooldown;
            if (visual != null) visual.PlayPunch(hand);
            PunchRpc(hand, AimDirection());
            return true;
        }

        private Vector3 AimDirection()
        {
            var cam = Camera.main;
            if (cam != null && motor != null && cam.transform.IsChildOf(transform))
                return cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)).direction;
            return motor != null && motor.AimPivot != null ? motor.AimPivot.forward : transform.forward;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void PunchRpc(PunchHand hand, Vector3 aim)
        {
            var i = (int)hand;
            if (i < 0 || i > 1) return;
            if (health != null && health.IsDead.Value) return;
            if (Time.time < nextServer[i]) return;
            nextServer[i] = Time.time + ServerCooldown;
            PunchFxRpc(hand);

            // Clamp the requested aim to the attacker's body facing.
            var facing = transform.forward;
            if (float.IsNaN(aim.x) || float.IsNaN(aim.y) || float.IsNaN(aim.z) || aim.sqrMagnitude < 1e-4f) aim = facing;
            var direction = Vector3.RotateTowards(facing, aim.normalized, MaxAimOffsetDegrees * Mathf.Deg2Rad, 0f).normalized;
            var origin = motor != null && motor.AimPivot != null ? motor.AimPivot.position : transform.position + Vector3.up * 0.7f;

            Physics.SyncTransforms();
            var count = Physics.SphereCastNonAlloc(origin, Radius, direction, Hits, Range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            PlayerHealth target = null;
            var best = float.MaxValue;
            for (var h = 0; h < count; h++)
            {
                var victim = Hits[h].collider.GetComponentInParent<PlayerHealth>();
                if (victim == null || victim == health || victim.IsDead.Value) continue;
                // Overlaps at the cast start report distance 0; those count as point-blank hits.
                if (Hits[h].distance < best) { best = Hits[h].distance; target = victim; }
            }
            if (target == null) return;
            var push = new Vector3(direction.x, 0f, direction.z).normalized * KnockbackSpeed + Vector3.up * KnockbackLift;
            if (target.ServerApplyDamage(UnityEngine.Random.Range(MinDamage, MaxDamage + 1), push)) HitConfirmedRpc();
        }

        [Rpc(SendTo.NotOwner, InvokePermission = RpcInvokePermission.Server)]
        private void PunchFxRpc(PunchHand hand)
        {
            if (visual != null) visual.PlayPunch(hand);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void HitConfirmedRpc() => LocalHitConfirmed?.Invoke();
    }
}
