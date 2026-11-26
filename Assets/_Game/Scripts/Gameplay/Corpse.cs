using System.Collections.Generic;
using CoOpHorror.Localization;
using CoOpHorror.Player;
using Unity.Netcode;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    // The discharged body a dead player leaves behind (spawned by the host from the player prefab's
    // CorpsePrefab). A teammate holds E on it to pour in their own energy (PlayerReviver: costs 30 health)
    // and the player comes back here at 1 health. Keep the button down to carry it with telekinesis — into the ship, where
    // the ship's surge right before launch revives it. The dead player meanwhile spectates as a ghost.
    public sealed class Corpse : Grabbable
    {
        public const float HitCooldown = 0.22f; // per hitter, host side
        public const int MinHit = 10;
        public const int MaxHit = 15;
        public const ulong NoPlayer = ulong.MaxValue;

        public static readonly List<Corpse> Bodies = new();

        public Renderer BodyRenderer;
        public Renderer HeadRenderer;

        public readonly NetworkVariable<ulong> PlayerObjectId = new(NoPlayer);
        public readonly NetworkVariable<ulong> PlayerClientId = new(0);

        private MaterialPropertyBlock block;
        private float flashUntil;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        public override string DisplayName => GameText.Format("corpse.name", PlayerClientId.Value);
        protected override bool ResetToHomeOnSpawn => false;

        protected override void Awake()
        {
            base.Awake();
            Bodies.Add(this);
            block = new MaterialPropertyBlock();
        }

        public override void OnDestroy()
        {
            Bodies.Remove(this);
            base.OnDestroy();
        }

        public static Corpse Of(ulong playerObjectId)
        {
            foreach (var c in Bodies) if (c != null && c.IsSpawned && c.PlayerObjectId.Value == playerObjectId) return c;
            return null;
        }

        public PlayerHealth Player =>
            IsSpawned && NetworkManager != null && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(PlayerObjectId.Value, out var obj)
                ? obj.GetComponent<PlayerHealth>() : null;

        public override InteractKind Describe(PlayerInteractor who, out string prompt)
        {
            prompt = string.Empty;
            var player = Player;
            if (player == null || !player.IsDead.Value || HeldBy(who)) return InteractKind.None;
            var mine = who.GetComponent<PlayerHealth>();
            var canRevive = mine != null && mine.Health.Value > PlayerReviver.Cost;
            var revive = canRevive
                ? GameText.Format("interact.reviveHold", PlayerClientId.Value, PlayerReviver.Cost, player.RevivePercent)
                : GameText.Format("interact.reviveWeak", PlayerReviver.Cost);
            if (who.HandsFull)
            {
                prompt = revive;
                return InteractKind.None;
            }
            prompt = revive + " · " + GameText.Get("interact.corpseCarry");
            return InteractKind.Hold;
        }

        // Spark when a revive completes (PlayerReviver).
        public void ServerReviveFx()
        {
            if (IsSpawned) HitFxRpc();
        }

        public override bool ServerHold(PlayerInteractor who, float dt) => Player != null && ServerGrabTick(who);

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        private void HitFxRpc()
        {
            flashUntil = Time.time + 0.15f;
            ProtoAudio.Play(ProtoSound.Zap, transform.position, 0.7f);
        }

        private void Update()
        {
            if (!IsSpawned) return;
            // Soaked in blood; a spark of current now and then (and when someone pours energy in).
            var color = new Color(0.5f, 0.05f, 0.04f);
            if (Time.time < flashUntil || Random.value < 0.008f) color = new Color(0.55f, 0.9f, 1f);
            Tint(BodyRenderer, color);
            Tint(HeadRenderer, color);
        }

        private void Tint(Renderer r, Color c)
        {
            if (r == null) return;
            r.GetPropertyBlock(block);
            block.SetColor(BaseColor, c);
            r.SetPropertyBlock(block);
        }
    }
}
