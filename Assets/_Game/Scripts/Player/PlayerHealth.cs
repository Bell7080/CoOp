using System;
using System.Collections;
using CoOpHorror.Networking;
using Unity.Netcode;
using UnityEngine;

namespace CoOpHorror.Player
{
    // Host-authoritative health. Only the server writes Health and IsDead.
    public sealed class PlayerHealth : NetworkBehaviour
    {
        public const int MaxHealth = 100;
        public const float RespawnDelay = 3f;
        public const float FallKillHeight = -20f;

        public readonly NetworkVariable<int> Health = new(MaxHealth);
        public readonly NetworkVariable<bool> IsDead = new(false);

        public static PlayerHealth Local { get; private set; }
        public event Action<int, int> HealthChanged;

        private PlayerAvatarVisual visual;
        private FirstPersonController motor;

        private void Awake()
        {
            visual = GetComponent<PlayerAvatarVisual>();
            motor = GetComponent<FirstPersonController>();
        }

        public override void OnNetworkSpawn()
        {
            Health.OnValueChanged += OnHealthChanged;
            IsDead.OnValueChanged += OnDeadChanged;
            if (IsOwner) Local = this;
            if (visual != null) visual.SetDead(IsDead.Value);
        }

        public override void OnNetworkDespawn()
        {
            Health.OnValueChanged -= OnHealthChanged;
            IsDead.OnValueChanged -= OnDeadChanged;
            if (Local == this) Local = null;
        }

        private void OnHealthChanged(int previous, int current)
        {
            if (current < previous && visual != null) visual.Flash();
            HealthChanged?.Invoke(previous, current);
        }

        private void OnDeadChanged(bool previous, bool current)
        {
            if (visual != null) visual.SetDead(current);
        }

        private void Update()
        {
            if (IsServer && IsSpawned && !IsDead.Value && transform.position.y < FallKillHeight)
            {
                Health.Value = 0;
                Kill();
            }
        }

        // Server only. Returns true when the damage was applied.
        public bool ServerApplyDamage(int amount, Vector3 knockback)
        {
            if (!IsServer || !IsSpawned || IsDead.Value || amount <= 0) return false;
            Health.Value = Mathf.Max(0, Health.Value - amount);
            KnockbackRpc(knockback);
            if (Health.Value == 0) Kill();
            return true;
        }

        private void Kill()
        {
            if (IsDead.Value) return;
            IsDead.Value = true;
            StartCoroutine(RespawnAfterDelay());
        }

        private IEnumerator RespawnAfterDelay()
        {
            yield return new WaitForSeconds(RespawnDelay);
            if (!IsSpawned) yield break;
            var position = RoomController.PickRespawnPosition(transform);
            // Face the arena centre.
            var yaw = Mathf.Atan2(-position.x, 1.5f - position.z) * Mathf.Rad2Deg;
            RespawnRpc(position, yaw);
            // Give the owner's teleport time to replicate before the body reappears.
            yield return new WaitForSeconds(0.25f);
            if (!IsSpawned) yield break;
            Health.Value = MaxHealth;
            IsDead.Value = false;
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void KnockbackRpc(Vector3 velocity)
        {
            if (motor != null) motor.AddImpulse(velocity);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void RespawnRpc(Vector3 position, float yaw)
        {
            if (motor != null) motor.TeleportTo(position, yaw);
        }
    }
}
