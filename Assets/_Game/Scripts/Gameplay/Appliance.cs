using System.Collections.Generic;
using CoOpHorror.Localization;
using CoOpHorror.Player;
using Unity.Netcode;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    // A household appliance holding electricity (Watts). Hold the left button on it and your nose latches on
    // and drinks it into your energy tank (PlayerNose) at a flat, slow rate (PlayerNose.SuckRate, faster on
    // later stages), so bigger appliances simply take longer. No minigame. Drained, it goes dark and stays
    // where it is; nothing is carried. The host writes the remaining power; every peer shows it.
    public sealed class Appliance : Interactable, IChargeGauge, INoseTarget
    {
        public static readonly List<Appliance> All = new();

        public string NameKey = "appliance.tv";
        public int Watts = 100;                   // how much power it holds (its worth)
        public Renderer Indicator;
        public Light Glow;

        public readonly NetworkVariable<float> Power = new(0f);

        public bool IsEmpty => IsSpawned && Power.Value <= 0.5f;
        public bool IsGone => false;              // (kept for the HUD / labels: nothing hides appliances now)
        public int Remaining => IsSpawned ? Mathf.CeilToInt(Power.Value - 0.01f) : Watts;
        public float GaugeFraction => IsSpawned && Watts > 0 ? Mathf.Clamp01(Power.Value / Watts) : 0f;
        public bool GaugeCharging => false;
        public string DisplayName => GameText.Get(NameKey);
        public override bool StickyHold => true;   // the nose stays latched while you look around

        private Rigidbody body;
        private Collider[] allColliders;
        private Collider mainCollider;
        private Renderer[] renderers;
        private MaterialPropertyBlock block;
        private float glowIntensity;
        private bool wasEmpty;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly Color Dead = new(0.12f, 0.03f, 0.03f);
        private static readonly Color Full = new(0.35f, 1f, 0.85f);

        protected override void Awake()
        {
            base.Awake();
            All.Add(this);
            body = GetComponent<Rigidbody>();
            allColliders = GetComponentsInChildren<Collider>(true);
            foreach (var c in allColliders) if (!c.isTrigger) { mainCollider = c; break; }
            // Only the renderers that are on in the scene: the primitive stand-ins under a dressed model are
            // switched off at build time and must stay off (they are larger than the model and would cover it).
            var shown = new List<Renderer>();
            foreach (var r in GetComponentsInChildren<Renderer>(true)) if (r.enabled) shown.Add(r);
            renderers = shown.ToArray();
            block = new MaterialPropertyBlock();
            if (Glow != null) glowIntensity = Glow.intensity;
            CoOpHorror.UI.ItemFeedback.Ensure();
        }

        public override void OnDestroy()
        {
            All.Remove(this);
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            wasEmpty = false;
            if (!IsServer) return;
            // Every new room starts full.
            Power.Value = Watts;
            if (body != null) body.isKinematic = true;
        }

        // "커피포트\n1500W 남음" / "방전됨"
        public override string HoverInfo(PlayerInteractor who)
        {
            if (!IsSpawned || IsGone) return string.Empty;
            return DisplayName + "\n" + (IsEmpty ? GameText.Get("hover.drained") : GameText.Format("hover.watts", Remaining, Watts));
        }

        public override InteractKind Describe(PlayerInteractor who, out string prompt)
        {
            prompt = string.Empty;
            if (IsGone) return InteractKind.None;
            if (IsEmpty)
            {
                prompt = GameText.Format("interact.drained", DisplayName);
                return InteractKind.None;
            }
            prompt = GameText.Format("interact.suck", DisplayName, Remaining);
            return InteractKind.Hold;
        }

        // The nose drinks: power moves from the appliance into the holder's tank.
        public override bool ServerHold(PlayerInteractor who, float dt)
        {
            if (IsGone || IsEmpty) return false;
            var nose = who.GetComponent<PlayerNose>();
            if (nose == null) return false;
            var taken = nose.ServerAdd(Mathf.Min(PlayerNose.SuckRate * dt, Power.Value));
            Power.Value = Mathf.Max(0f, Power.Value - taken);
            nose.ServerSetFlow(taken > 0f ? PlayerNose.FlowIn : PlayerNose.FlowNone);
            if (IsEmpty) { Power.Value = 0f; return false; }
            return true;
        }

        // Right button: the nose pumps power back in from the tank (switches it on again).
        public override InteractKind DescribeRight(PlayerInteractor who, out string prompt)
        {
            prompt = string.Empty;
            if (!IsSpawned || Power.Value >= Watts - 0.5f) return InteractKind.None;
            var nose = who.GetComponent<PlayerNose>();
            if (nose == null || nose.Energy.Value < 0.5f) return InteractKind.None;
            prompt = GameText.Format("interact.giveBack", DisplayName, Remaining, Watts);
            return InteractKind.Hold;
        }

        public override bool ServerHoldRight(PlayerInteractor who, float dt)
        {
            var nose = who.GetComponent<PlayerNose>();
            if (nose == null || Power.Value >= Watts - 0.01f) return false;
            var given = nose.ServerTake(Mathf.Min(PlayerNose.SuckRate * dt, Watts - Power.Value));
            Power.Value = Mathf.Min(Watts, Power.Value + given);
            nose.ServerSetFlow(given > 0f ? PlayerNose.FlowOut : PlayerNose.FlowNone);
            return Power.Value < Watts - 0.01f && nose.Energy.Value > 0.01f;
        }

        // Blackout (the transmitter fired): whatever is left on the map loses its power.
        public void ServerDischarge()
        {
            if (IsServer) Power.Value = 0f;
        }

        public Vector3 NosePoint(Vector3 from)
        {
            if (mainCollider == null || !mainCollider.enabled) return transform.position + Vector3.up * 0.2f;
            var center = mainCollider.bounds.center;
            var surface = mainCollider.ClosestPoint(from);
            return Vector3.Lerp(surface, center, 0.08f);
        }

        private void Update()
        {
            if (!IsSpawned) return; // outside a room it stays as the scene placed it

            var gone = IsGone;
            foreach (var r in renderers) if (r != null) r.enabled = !gone;
            foreach (var c in allColliders) if (c != null) c.enabled = !gone;

            var empty = IsEmpty;
            if (empty && !wasEmpty && !gone) ProtoAudio.Play(ProtoSound.Zap, transform.position + Vector3.up * 0.3f, 0.7f);
            wasEmpty = empty;

            var fraction = GaugeFraction;
            if (Indicator != null)
            {
                Color color;
                if (empty) color = Dead;
                else color = Color.Lerp(new Color(0.9f, 0.55f, 0.1f), Color.Lerp(Full, Color.white, Mathf.PingPong(Time.time * 1.5f, 0.35f)), fraction);
                Indicator.GetPropertyBlock(block);
                block.SetColor(BaseColor, color);
                Indicator.SetPropertyBlock(block);
            }
            if (Glow != null)
            {
                Glow.enabled = !gone && !empty;
                Glow.intensity = glowIntensity * (0.25f + 0.75f * fraction);
            }
        }
    }
}
