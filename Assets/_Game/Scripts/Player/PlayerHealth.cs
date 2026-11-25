using System;
using System.Collections;
using System.Collections.Generic;
using CoOpHorror.Gameplay;
using CoOpHorror.Networking;
using Unity.Netcode;
using UnityEngine;

namespace CoOpHorror.Player
{
    // Host-authoritative health. Only the server writes Health, IsDead and ReviveCharge.
    // In an ElecMole stage (a StageDirector exists) a dead player stays as a discharged body until teammates
    // beat enough current back into it; elsewhere (the development sandbox) they respawn after a delay.
    public sealed class PlayerHealth : NetworkBehaviour
    {
        public const int MaxHealth = 100;
        public const float RespawnDelay = 3f;
        public const float FallKillHeight = -20f;
        public const int ReviveThreshold = 100; // hit damage on the body needed to revive
        public const int ReviveHealth = 1;

        public static readonly List<PlayerHealth> All = new();

        public readonly NetworkVariable<int> Health = new(MaxHealth);
        public readonly NetworkVariable<bool> IsDead = new(false);
        public readonly NetworkVariable<int> ReviveCharge = new(0);

        public static PlayerHealth Local { get; private set; }
        public static bool AutoRespawn => StageDirector.Instance == null;
        public event Action<int, int> HealthChanged;

        public int RevivePercent => Mathf.Clamp(ReviveCharge.Value * 100 / ReviveThreshold, 0, 100);

        private PlayerAvatarVisual visual;
        private FirstPersonController motor;

        private void Awake()
        {
            visual = GetComponent<PlayerAvatarVisual>();
            motor = GetComponent<FirstPersonController>();
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            Health.OnValueChanged += OnHealthChanged;
            IsDead.OnValueChanged += OnDeadChanged;
            if (IsOwner) Local = this;
            if (visual != null) visual.SetDead(IsDead.Value);
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            Health.OnValueChanged -= OnHealthChanged;
            IsDead.OnValueChanged -= OnDeadChanged;
            if (Local == this) Local = null;
        }

        public override void OnDestroy()
        {
            All.Remove(this);
            base.OnDestroy();
        }

        private void OnHealthChanged(int previous, int current)
        {
            if (current < previous && visual != null) visual.Flash();
            HealthChanged?.Invoke(previous, current);
        }

        private void OnDeadChanged(bool previous, bool current)
        {
            if (visual != null) visual.SetDead(current);
            if (!current && previous) ProtoAudio.Play(ProtoSound.Zap, transform.position, 1f);
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
            if (knockback != Vector3.zero) KnockbackRpc(knockback);
            if (Health.Value == 0) Kill();
            return true;
        }

        // Server only. Slow loss without a hit reaction (left behind by the ship).
        public void ServerDrain(int amount) => ServerApplyDamage(amount, Vector3.zero);

        // Server only. Teammates hitting the discharged body; enough current brings it back at 1 health.
        public void ServerAddReviveCharge(int amount)
        {
            if (!IsServer || !IsSpawned || !IsDead.Value || amount <= 0) return;
            if (StageDirector.Instance != null && StageDirector.Instance.IsLeftBehind(this)) return;
            ReviveCharge.Value += amount;
            if (ReviveCharge.Value < ReviveThreshold) return;
            ReviveCharge.Value = 0;
            Health.Value = ReviveHealth;
            IsDead.Value = false;
        }

        private void Kill()
        {
            if (IsDead.Value) return;
            IsDead.Value = true;
            ReviveCharge.Value = 0;
            if (AutoRespawn) StartCoroutine(RespawnAfterDelay());
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
