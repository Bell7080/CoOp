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
        public Transform Model;          // the slumped mole (presentation; flops around)
        public Transform Blood;          // the pool it left: stays on the floor where it died

        // Floppy corpse (presentation only): lying, the lump sags flat and its limbs splay; lifted, it hangs
        // and stretches under its own weight, drags behind its motion, and its head, arms, legs and nose droop
        // toward the ground; hitting the floor squashes it. Thick, slow springs: a heavy, viscous body.
        private static readonly Vector3 LyingScale = new(1.25f, 0.42f, 1.2f);
        private static readonly Vector3 HangingScale = new(0.82f, 1.15f, 0.82f);
        private static readonly string[] DroopBones = { "Head", "Nose1", "ArmL1", "ArmL2", "ArmR1", "ArmR2", "LegL", "LegR" };
        private Transform[] droop;
        private Quaternion[] droopBind;
        private Vector3 scale, scaleVelocity, lastPosition, lastVelocity, lean, leanVelocity;
        private float hang, droopAmount;
        private Vector3 bloodHome;
        private Quaternion bloodRotation;
        private Quaternion modelBaseRotation;
        private Vector3 modelBasePosition;

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
            scale = LyingScale;
            if (Model != null)
            {
                modelBaseRotation = Model.localRotation;
                modelBasePosition = Model.localPosition;
                var list = new List<Transform>();
                foreach (var name in DroopBones)
                    foreach (var t in Model.GetComponentsInChildren<Transform>(true))
                        if (t.name == name) { list.Add(t); break; }
                droop = list.ToArray();
                droopBind = new Quaternion[droop.Length];
                for (var i = 0; i < droop.Length; i++) droopBind[i] = droop[i].localRotation;
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            // the pool stays where the player fell
            if (Blood != null) { bloodHome = Blood.position; bloodRotation = Blood.rotation; }
            lastPosition = transform.position;
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

        protected override void LateUpdate()
        {
            base.LateUpdate();
            if (!IsSpawned) return;
            if (Blood != null) Blood.SetPositionAndRotation(bloodHome, bloodRotation);
            if (Model == null) return;
            var dt = Mathf.Max(Time.deltaTime, 0.0001f);
            var velocity = (transform.position - lastPosition) / dt;
            lastPosition = transform.position;
            if (velocity.sqrMagnitude > 400f) velocity = Vector3.zero; // teleports
            var accel = (velocity - lastVelocity) / dt;
            lastVelocity = velocity;

            hang = Mathf.MoveTowards(hang, IsHeld ? 1f : 0f, dt * 2.5f);
            var target = Vector3.Lerp(LyingScale, HangingScale, hang);
            // landing hard: a squash that oozes back
            if (accel.y > 25f) scaleVelocity += new Vector3(1.2f, -2.5f, 1.2f) * Mathf.Clamp01(accel.y / 120f);
            // viscous spring (over-damped)
            scaleVelocity += (target - scale) * (60f * dt) - scaleVelocity * Mathf.Min(1f, 12f * dt);
            scale += scaleVelocity * dt;
            Model.localScale = scale;
            // hanging: it dangles below the hold point and trails behind its motion
            var flat = new Vector3(velocity.x, 0f, velocity.z);
            var leanTarget = new Vector3(Mathf.Clamp(flat.z * 6f, -25f, 25f), 0f, Mathf.Clamp(-flat.x * 6f, -25f, 25f)) * hang;
            lean = Vector3.SmoothDamp(lean, leanTarget, ref leanVelocity, 0.35f);
            Model.localRotation = Quaternion.Inverse(transform.rotation) * Quaternion.Euler(lean.x, 0f, lean.z) * transform.rotation * modelBaseRotation;
            Model.localPosition = modelBasePosition + Vector3.down * (0.35f * hang);

            // head, arms, legs and nose sag toward the ground (more when lifted)
            droopAmount = Mathf.MoveTowards(droopAmount, Mathf.Lerp(0.45f, 0.85f, hang), dt * 1.5f);
            if (droop == null) return;
            for (var i = 0; i < droop.Length; i++)
            {
                var bone = droop[i];
                if (bone == null) continue;
                bone.localRotation = droopBind[i];
                var tip = bone.childCount > 0 ? bone.GetChild(0).position - bone.position : bone.up;
                if (tip.sqrMagnitude < 1e-6f) continue;
                var toDown = Quaternion.FromToRotation(tip, Vector3.down);
                bone.rotation = Quaternion.Slerp(Quaternion.identity, toDown, droopAmount) * bone.rotation;
            }
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
