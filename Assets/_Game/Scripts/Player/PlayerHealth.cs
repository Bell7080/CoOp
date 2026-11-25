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
    // Dying leaves a discharged Corpse (CorpsePrefab) and turns the player into an invisible ghost that
    // spectates the living. Teammates beat current back into the body (ReviveThreshold) to revive at 1 health,
    // and bodies brought aboard the ship revive in its surge before launch. In the development sandbox, where
    // there is no stage, everyone comes back if the whole room is down.
    public sealed class PlayerHealth : NetworkBehaviour
    {
        public const int MaxHealth = 100;
        public const float SandboxRespawnDelay = 3f;
        public const float FallKillHeight = -20f;
        public const int ReviveThreshold = 100; // hit damage on the body needed to revive
        public const int ReviveHealth = 1;

        public static readonly List<PlayerHealth> All = new();

        public GameObject CorpsePrefab;

        public readonly NetworkVariable<int> Health = new(MaxHealth);
        public readonly NetworkVariable<bool> IsDead = new(false);
        public readonly NetworkVariable<int> ReviveCharge = new(0);

        public static PlayerHealth Local { get; private set; }
        public event Action<int, int> HealthChanged;

        public int RevivePercent => Mathf.Clamp(ReviveCharge.Value * 100 / ReviveThreshold, 0, 100);
        public Corpse Body => Corpse.Of(NetworkObjectId);

        private PlayerAvatarVisual visual;
        private FirstPersonController motor;
        private CharacterController controller;
        private bool reviving;
        private static float everyoneDownSince = -1f;

        private void Awake()
        {
            visual = GetComponent<PlayerAvatarVisual>();
            motor = GetComponent<FirstPersonController>();
            controller = GetComponent<CharacterController>();
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            Health.OnValueChanged += OnHealthChanged;
            IsDead.OnValueChanged += OnDeadChanged;
            if (IsOwner) Local = this;
            ApplyDeadState(IsDead.Value);
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            Health.OnValueChanged -= OnHealthChanged;
            IsDead.OnValueChanged -= OnDeadChanged;
            if (Local == this) Local = null;
            if (IsServer)
            {
                var body = Body;
                if (body != null && body.IsSpawned) body.NetworkObject.Despawn(true);
            }
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
            ApplyDeadState(current);
            if (!current && previous) ProtoAudio.Play(ProtoSound.Zap, transform.position, 1f);
        }

        // Ghost: invisible and not solid for anyone.
        private void ApplyDeadState(bool dead)
        {
            if (visual != null) visual.SetDead(dead);
            if (controller != null) controller.enabled = !dead;
        }

        private void Update()
        {
            if (!IsServer || !IsSpawned) return;
            if (!IsDead.Value && transform.position.y < FallKillHeight)
            {
                Health.Value = 0;
                Kill(true);
            }
            if (All.Count > 0 && All[0] == this) ServerSandboxRule();
        }

        // Sandbox only (no stage): when everyone is down, bring the whole room back after a moment.
        private static void ServerSandboxRule()
        {
            if (StageDirector.Instance != null) { everyoneDownSince = -1f; return; }
            var anyAlive = false;
            foreach (var p in All) if (p != null && p.IsSpawned && !p.IsDead.Value) anyAlive = true;
            if (anyAlive || All.Count == 0) { everyoneDownSince = -1f; return; }
            if (everyoneDownSince < 0f) { everyoneDownSince = Time.time; return; }
            if (Time.time - everyoneDownSince < SandboxRespawnDelay) return;
            everyoneDownSince = -1f;
            foreach (var p in All) if (p != null && p.IsSpawned) p.ServerRevive(RoomController.PickRespawnPosition(p.transform), MaxHealth, false);
        }

        // Server only. Returns true when the damage was applied.
        public bool ServerApplyDamage(int amount, Vector3 knockback)
        {
            if (!IsServer || !IsSpawned || IsDead.Value || amount <= 0) return false;
            Health.Value = Mathf.Max(0, Health.Value - amount);
            if (knockback != Vector3.zero) KnockbackRpc(knockback);
            if (Health.Value == 0) Kill(false);
            return true;
        }

        // Server only. Slow loss without a hit reaction (left behind by the ship).
        public void ServerDrain(int amount) => ServerApplyDamage(amount, Vector3.zero);

        // Server only. Teammates hitting the discharged body; enough current brings it back at 1 health.
        public void ServerAddReviveCharge(int amount)
        {
            if (!IsServer || !IsSpawned || !IsDead.Value || reviving || amount <= 0) return;
            if (StageDirector.Instance != null && StageDirector.Instance.IsLeftBehind(this)) return;
            ReviveCharge.Value += amount;
            if (ReviveCharge.Value < ReviveThreshold) return;
            ServerRevive(transform.position, ReviveHealth, true);
        }

        // Server only. Brings the player back, at their body when it exists (atBody) or at position.
        public void ServerRevive(Vector3 position, int health, bool atBody)
        {
            if (!IsServer || !IsSpawned || !IsDead.Value || reviving) return;
            StartCoroutine(ReviveRoutine(position, health, atBody));
        }

        private IEnumerator ReviveRoutine(Vector3 position, int health, bool atBody)
        {
            reviving = true;
            var body = Body;
            var yaw = transform.eulerAngles.y;
            if (body != null && body.IsSpawned)
            {
                if (atBody) position = body.transform.position + Vector3.up * 1.05f;
                yaw = body.transform.eulerAngles.y;
                body.ServerReleaseAll();
                body.NetworkObject.Despawn(true);
            }
            RespawnRpc(position, yaw);
            // Give the owner's teleport time to replicate before the player reappears.
            yield return new WaitForSeconds(0.25f);
            reviving = false;
            if (!IsSpawned) yield break;
            ReviveCharge.Value = 0;
            Health.Value = Mathf.Clamp(health, 1, MaxHealth);
            IsDead.Value = false;
        }

        private void Kill(bool fell)
        {
            if (IsDead.Value) return;
            IsDead.Value = true;
            ReviveCharge.Value = 0;
            SpawnBody(fell);
        }

        private void SpawnBody(bool fell)
        {
            if (CorpsePrefab == null) return;
            var old = Body;
            if (old != null && old.IsSpawned) old.NetworkObject.Despawn(true);
            // A body that fell out of the world is put back at a spawn slot.
            var position = fell ? RoomController.PickRespawnPosition(transform) - Vector3.up * 0.6f : transform.position - Vector3.up * 0.55f;
            var go = Instantiate(CorpsePrefab, position, Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
            var corpse = go.GetComponent<Corpse>();
            corpse.PlayerObjectId.Value = NetworkObjectId;
            corpse.PlayerClientId.Value = OwnerClientId;
            go.GetComponent<NetworkObject>().Spawn(true);
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
