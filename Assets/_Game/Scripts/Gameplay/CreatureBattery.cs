using System.Collections.Generic;
using CoOpHorror.Localization;
using CoOpHorror.Player;
using Unity.Netcode;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    // The battery a creature leaves behind when its power is drained to nothing (TempMonster.Power): a fifth
    // of the creature's power, dropped where it fell. Noses can only drink from it (left button, the usual
    // flat rate, as loud as an appliance); nothing can be pumped back in. One per creature, placed in the
    // scene hidden; the host shows it where the creature died. The host writes everything; every peer shows it.
    public sealed class CreatureBattery : Interactable, IChargeGauge, INoseTarget
    {
        public static readonly List<CreatureBattery> All = new();

        public Renderer Indicator;
        public Light Glow;

        public readonly NetworkVariable<bool> Dropped = new(false);
        public readonly NetworkVariable<float> Power = new(0f);
        public readonly NetworkVariable<int> Capacity = new(0);
        public readonly NetworkVariable<Vector3> Spot = new(Vector3.zero);

        public bool IsEmpty => !IsSpawned || Power.Value <= 0.5f;
        public int Remaining => IsSpawned ? Mathf.CeilToInt(Power.Value - 0.01f) : 0;
        public float GaugeFraction => IsSpawned && Capacity.Value > 0 ? Mathf.Clamp01(Power.Value / Capacity.Value) : 0f;
        public bool GaugeCharging => false;
        public override bool StickyHold => true;
        public bool NoseDrainable => IsSpawned && Dropped.Value && !IsEmpty;

        private Renderer[] renderers;
        private Collider[] allColliders;
        private Collider mainCollider;
        private MaterialPropertyBlock block;
        private float nextNoise;
        private bool wasEmpty;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        protected override void Awake()
        {
            base.Awake();
            All.Add(this);
            var shown = new List<Renderer>();
            foreach (var r in GetComponentsInChildren<Renderer>(true)) if (r.enabled) shown.Add(r);
            renderers = shown.ToArray();
            allColliders = GetComponentsInChildren<Collider>(true);
            foreach (var c in allColliders) if (!c.isTrigger) { mainCollider = c; break; }
            block = new MaterialPropertyBlock();
            Show(false);
        }

        public override void OnDestroy()
        {
            All.Remove(this);
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            Dropped.Value = false;
            Power.Value = 0f;
            Capacity.Value = 0;
        }

        // Host: the creature fell here; it leaves `watts` behind.
        public void ServerDrop(Vector3 position, int watts)
        {
            if (!IsServer || watts <= 0) return;
            // rest on whatever floor is under it
            if (Physics.Raycast(position + Vector3.up * 1.5f, Vector3.down, out var hit, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                position = hit.point;
            Spot.Value = position;
            Capacity.Value = watts;
            Power.Value = watts;
            Dropped.Value = true;
        }

        public override string HoverInfo(PlayerInteractor who)
        {
            if (!IsSpawned || !Dropped.Value) return string.Empty;
            return GameText.Get("item.battery") + "\n" + (IsEmpty ? GameText.Get("hover.drained") : GameText.Format("hover.watts", Remaining, Capacity.Value));
        }

        public override InteractKind Describe(PlayerInteractor who, out string prompt)
        {
            prompt = string.Empty;
            if (!IsSpawned || !Dropped.Value) return InteractKind.None;
            if (IsEmpty)
            {
                prompt = GameText.Format("interact.drained", GameText.Get("item.battery"));
                return InteractKind.None;
            }
            prompt = GameText.Format("interact.suck", GameText.Get("item.battery"), Remaining);
            return InteractKind.Hold;
        }

        // Drink only: there is no DescribeRight, so the right button never pumps power into it.
        public override bool ServerHold(PlayerInteractor who, float dt)
        {
            if (!Dropped.Value || IsEmpty) return false;
            var nose = who.GetComponent<PlayerNose>();
            if (nose == null) return false;
            var taken = nose.ServerAdd(Mathf.Min(PlayerNose.SuckRate * dt, Power.Value));
            Power.Value = Mathf.Max(0f, Power.Value - taken);
            nose.ServerSetFlow(taken > 0f ? PlayerNose.FlowIn : PlayerNose.FlowNone);
            if (taken > 0f && Time.time >= nextNoise)
            {
                nextNoise = Time.time + Appliance.NoisePeriod;
                Noise.Emit(transform.position, Noise.DrainRadius(Mathf.Min(Capacity.Value, 1500)), NoiseKind.Drain);
            }
            if (IsEmpty) { Power.Value = 0f; return false; }
            return true;
        }

        public Vector3 NosePoint(Vector3 from)
        {
            if (mainCollider == null || !mainCollider.enabled) return transform.position + Vector3.up * 0.15f;
            return Vector3.Lerp(mainCollider.ClosestPoint(from), mainCollider.bounds.center, 0.1f);
        }

        private void Show(bool on)
        {
            foreach (var r in renderers) if (r != null) r.enabled = on;
            foreach (var c in allColliders) if (c != null) c.enabled = on;
            if (Glow != null) Glow.enabled = on;
        }

        private void Update()
        {
            var dropped = IsSpawned && Dropped.Value;
            if (dropped && (transform.position - Spot.Value).sqrMagnitude > 0.0001f) transform.position = Spot.Value;
            Show(dropped);
            if (!dropped) { wasEmpty = false; return; }
            var empty = IsEmpty;
            if (empty && !wasEmpty) ProtoAudio.Play(ProtoSound.Zap, transform.position + Vector3.up * 0.2f, 0.6f);
            wasEmpty = empty;
            if (Glow != null) { Glow.enabled = !empty; Glow.intensity = 0.4f + 0.8f * GaugeFraction; }
            if (Indicator == null) return;
            var color = empty ? new Color(0.12f, 0.03f, 0.03f)
                : Color.Lerp(new Color(0.9f, 0.55f, 0.1f), Color.Lerp(new Color(0.35f, 1f, 0.85f), Color.white, Mathf.PingPong(Time.time * 1.5f, 0.35f)), GaugeFraction);
            Indicator.GetPropertyBlock(block);
            block.SetColor(BaseColor, color);
            Indicator.SetPropertyBlock(block);
        }
    }
}
