using System.Collections.Generic;
using CoOpHorror.Gameplay;
using CoOpHorror.Localization;
using Unity.Netcode;
using UnityEngine;

namespace CoOpHorror.Player
{
    // A discharged teammate's body. Left-click it repeatedly: the hit damage turns into current, and once
    // PlayerHealth.ReviveThreshold is reached the player comes back at 1 health. Host validates and applies.
    public sealed class PlayerCorpse : Interactable
    {
        public const float HitCooldown = 0.22f; // per hitter, host side
        public const int MinHit = 10;
        public const int MaxHit = 15;

        private PlayerHealth health;
        private PlayerAvatarVisual visual;
        private readonly Dictionary<ulong, float> nextHit = new();

        protected override void Awake()
        {
            base.Awake();
            health = GetComponent<PlayerHealth>();
            visual = GetComponent<PlayerAvatarVisual>();
        }

        public override InteractKind Describe(PlayerInteractor who, out string prompt)
        {
            if (health == null || !health.IsDead.Value || who.gameObject == gameObject || PlayerHealth.AutoRespawn)
            {
                prompt = string.Empty;
                return InteractKind.None;
            }
            prompt = GameText.Format("interact.revive", OwnerClientId, health.RevivePercent);
            return InteractKind.Press;
        }

        public override void ServerUse(PlayerInteractor who)
        {
            if (health == null || !health.IsDead.Value || who.gameObject == gameObject || PlayerHealth.AutoRespawn) return;
            var now = Time.time;
            if (nextHit.TryGetValue(who.NetworkObjectId, out var next) && now < next) return;
            nextHit[who.NetworkObjectId] = now + HitCooldown;
            health.ServerAddReviveCharge(Random.Range(MinHit, MaxHit + 1));
            HitFxRpc();
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        private void HitFxRpc()
        {
            if (visual != null) visual.Flash();
            ProtoAudio.Play(ProtoSound.Zap, transform.position, 0.7f);
        }
    }
}
