using System.Collections.Generic;
using CoOpHorror.Localization;
using CoOpHorror.Player;
using Unity.Netcode;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    // A household appliance.
    // Uncharged: hold the left button to feed it shared power (skill checks; a miss stalls everyone on it).
    // Charged: hold the left button to lift it with telekinesis (Grabbable), or take its plug (CordPlug) and
    // pull the cord to the transmitter. Knocks and being dragged by the cord lower its value.
    // Tiny appliances fit in the inventory. Once its plug is in the transmitter, settlement swallows it.
    public sealed class Appliance : Grabbable, IChargeGauge
    {
        public const float ChargeRate = 10f;      // power per second per charging player (2-4 s per item)
        public const float FailStall = 2f;
        public const float FailNoiseRadius = 9f;
        public const float ImpactLossPerSpeed = 0.035f; // share of base value lost per m/s above the impact speed
        public const float MaxImpactLoss = 0.3f;
        public const float ScrapeLossPerSecond = 0.03f;
        public const ulong NoOwner = ulong.MaxValue;

        public static new readonly List<Appliance> All = new();

        public string NameKey = "appliance.tv";
        public float Capacity = 30f;
        public int BaseValue = 100;
        public bool Tiny;                         // fits in the inventory
        public float CordLength = 6f;
        public Transform CordAnchor;
        public CordPlug Plug;
        public Renderer Indicator;
        public Light Glow;
        public bool StartsHidden;                 // salvage from an InstalledDevice: appears when ripped off
        // Power strip (멀티탭): other appliances' plugs go into these outlets; plugged into the transmitter it
        // settles everything plugged into it. Bought at the ship's terminal (it waits hidden until delivered).
        public Transform[] Outlets;

        public readonly NetworkVariable<float> Charge = new(0f);
        public readonly NetworkVariable<int> Value = new(0);
        public readonly NetworkVariable<ulong> StoredBy = new(NoOwner);
        public readonly NetworkVariable<int> StoredSlot = new(-1);
        public readonly NetworkVariable<bool> Swallowed = new(false);
        public readonly NetworkVariable<bool> Revealed = new(false);

        public bool IsCharged => Charge.Value >= Capacity - 0.01f;
        public bool IsStored => IsSpawned && StoredBy.Value != NoOwner;
        public bool IsPlugged => Plug != null && Plug.IsSocketed;
        public bool IsMultitap => Outlets != null && Outlets.Length > 0;

        // Plugged into the transmitter, directly or through a power strip that is: settles (flies in).
        public bool InSettlement
        {
            get
            {
                if (Plug == null || !Plug.IsSocketed) return false;
                if (Plug.InTransmitter) return true;
                var host = Plug.HostTap;
                return host != null && host != this && host.Plug != null && host.Plug.InTransmitter;
            }
        }

        // What this is worth at settlement: its own value, or for a power strip what is plugged into it.
        public int SettleValue => IsMultitap ? OutletValue : Value.Value;

        public int OutletValue
        {
            get
            {
                var sum = 0;
                foreach (var p in CordPlug.All)
                    if (p.IsSocketed && p.HostTap == this && p.Appliance != null && !p.Appliance.IsMultitap) sum += p.Appliance.Value.Value;
                return sum;
            }
        }

        public int OutletsUsed
        {
            get
            {
                var used = 0;
                foreach (var p in CordPlug.All) if (p.IsSocketed && p.HostTap == this) used++;
                return used;
            }
        }

        private bool OutletUsed(int index)
        {
            foreach (var p in CordPlug.All) if (p.IsSocketed && p.HostTap == this && p.Socket.Value == index) return true;
            return false;
        }

        public int FreeOutlet()
        {
            if (!IsMultitap) return -1;
            for (var i = 0; i < Outlets.Length; i++) if (!OutletUsed(i)) return i;
            return -1;
        }

        // A plug standing in outlet i: pins down into the strip.
        public Pose OutletPose(int index)
        {
            if (!IsMultitap || index < 0 || index >= Outlets.Length || Outlets[index] == null) return new Pose(transform.position, transform.rotation);
            var up = transform.up;
            return new Pose(Outlets[index].position + up * 0.062f, Quaternion.LookRotation(-up, transform.right));
        }

        // Can this power strip take that plug right now? (Not its own, not another strip, a charged appliance.)
        public bool Accepts(CordPlug plug) =>
            IsMultitap && IsSpawned && !IsGone && plug != null && plug.Appliance != null && plug.Appliance != this
            && !plug.Appliance.IsMultitap && plug.Appliance.IsCharged && FreeOutlet() >= 0;

        // Server: the holder pushes the plug into the next free outlet.
        public bool ServerAcceptPlug(CordPlug plug)
        {
            if (!IsServer || !Accepts(plug)) return false;
            plug.ServerSocketInto(this, FreeOutlet());
            ProtoAudio.Play(ProtoSound.Click, transform.position, 1f);
            return true;
        }
        public bool IsGone => IsSpawned && (Swallowed.Value || StoredBy.Value != NoOwner || (StartsHidden && !Revealed.Value));
        public int ChargePercent => Mathf.FloorToInt(Mathf.Clamp01(Charge.Value / Capacity) * 100f);
        public float GaugeFraction => IsSpawned ? Mathf.Clamp01(Charge.Value / Capacity) : 0f;
        public bool GaugeCharging => false; // the gauge shows while the charge is changing
        public override string DisplayName => GameText.Get(NameKey);
        public override bool UsesSkillChecks => IsSpawned && !IsCharged && !IsGone;
        public override bool StickyHold => IsCharged;
        public override float HoldRange => IsCharged ? base.HoldRange : UseRange;

        private Vector3 homeScale;
        private Collider[] allColliders;
        private Renderer[] renderers;
        private MaterialPropertyBlock block;
        private bool pulling;
        private Vector3 pullStart;
        private float scrapeDebt;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly Color Empty = new(0.18f, 0.05f, 0.04f);
        private static readonly Color Full = new(0.35f, 1f, 0.85f);

        protected override void Awake()
        {
            base.Awake();
            All.Add(this);
            homeScale = transform.localScale;
            allColliders = GetComponentsInChildren<Collider>(true);
            // Only the renderers that are on in the scene: the primitive stand-ins under a dressed model are
            // switched off at build time and must stay off (they are larger than the model and would cover it).
            var shown = new List<Renderer>();
            foreach (var r in GetComponentsInChildren<Renderer>(true)) if (r.enabled) shown.Add(r);
            renderers = shown.ToArray();
            block = new MaterialPropertyBlock();
        }

        public override void OnDestroy()
        {
            All.Remove(this);
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            pulling = false;
            if (!IsServer) return;
            // Every new room starts from the scene layout.
            Charge.Value = 0f;
            Value.Value = BaseValue;
            StoredBy.Value = NoOwner;
            StoredSlot.Value = -1;
            Swallowed.Value = false;
            Revealed.Value = false;
            Body.isKinematic = StartsHidden;
        }

        // Salvage ripped off a fixture: drops out powered, at full value.
        public void ServerReveal(Pose from)
        {
            if (!IsServer || !StartsHidden || Revealed.Value) return;
            Charge.Value = Capacity;
            Value.Value = BaseValue;
            Revealed.Value = true;
            Body.isKinematic = false;
            Body.position = from.position;
            Body.rotation = from.rotation;
            transform.SetPositionAndRotation(from.position, from.rotation);
            var sync = GetComponent<Unity.Netcode.Components.NetworkTransform>();
            if (sync != null && sync.CanCommitToTransform) sync.Teleport(from.position, from.rotation, transform.localScale);
            Body.linearVelocity = from.forward * 1.2f + Vector3.up * 0.5f;
        }

        // "TV · 가치 120" / "충전 필요 · 2명이 들어야 함 · 가방에 넣을 수 있음"
        public override string HoverInfo(PlayerInteractor who)
        {
            if (!IsSpawned || IsGone || HeldBy(who)) return string.Empty;
            if (IsMultitap)
                return GameText.Format("hover.multitap", DisplayName, OutletsUsed, Outlets.Length, OutletValue)
                    + "\n" + GameText.Get(Plug != null && Plug.InTransmitter ? "hover.multitapLive" : "hover.multitapHint");
            var head = Value.Value < BaseValue
                ? GameText.Format("hover.applianceDamaged", DisplayName, Value.Value, BaseValue)
                : GameText.Format("hover.appliance", DisplayName, Value.Value);
            var tags = new List<string> { GameText.Get(IsCharged ? "hover.charged" : "hover.needsCharge") };
            if (Weight > 1) tags.Add(GameText.Format("hover.heavy", Weight));
            if (Tiny) tags.Add(GameText.Get("hover.tiny"));
            return head + "\n" + string.Join(" · ", tags);
        }

        public override InteractKind Describe(PlayerInteractor who, out string prompt)
        {
            prompt = string.Empty;
            if (IsGone || pulling || HeldBy(who)) return InteractKind.None;
            if (!IsCharged)
            {
                if (IsStalled)
                {
                    prompt = GameText.Format("interact.chargeStalled", DisplayName, ChargePercent);
                    return InteractKind.Hold;
                }
                var grid = PowerGrid.Instance;
                if (grid == null || grid.IsEmpty)
                {
                    prompt = GameText.Format("interact.noPower", DisplayName, ChargePercent);
                    return InteractKind.None;
                }
                prompt = GameText.Format("interact.charge", DisplayName, ChargePercent);
                return InteractKind.Hold;
            }
            prompt = GrabPrompt(who) + "\n" + GameText.Format("interact.value", Value.Value, BaseValue);
            return who.HandsFull ? InteractKind.None : InteractKind.Hold;
        }

        // Uncharged: charge from the shared grid (stalled = nobody progresses). Charged: telekinesis.
        public override bool ServerHold(PlayerInteractor who, float dt)
        {
            if (IsGone || pulling) return false;
            if (IsCharged) return ServerGrabTick(who);
            if (IsStalled) return true;
            var grid = PowerGrid.Instance;
            if (grid == null) return false;
            var taken = grid.ServerTake(Mathf.Min(ChargeRate * dt, Capacity - Charge.Value));
            Charge.Value = Mathf.Min(Capacity, Charge.Value + taken);
            return !IsCharged; // charging ends; grabbing needs a new press
        }

        protected override bool CanGrab(PlayerInteractor who) => IsCharged && !IsGone && !pulling;

        public override void ServerSkillCheck(PlayerInteractor who, SkillCheckResult result)
        {
            if (IsStalled || IsCharged) return;
            if (result == SkillCheckResult.Miss) ServerFail(FailStall, FailNoiseRadius);
            else if (result == SkillCheckResult.Great) Charge.Value = Mathf.Min(Capacity, Charge.Value + Capacity * 0.05f);
        }

        protected override void ServerImpact(float speed) =>
            ServerDamage(Mathf.Min(MaxImpactLoss, (speed - ImpactSpeed) * ImpactLossPerSpeed));

        // Server: lose a share of the base value (knocks, monster hits).
        public void ServerDamage(float share)
        {
            if (!IsServer || share <= 0f || Swallowed.Value) return;
            Value.Value = Mathf.Max(0, Value.Value - Mathf.Max(1, Mathf.RoundToInt(BaseValue * share)));
        }

        // Server: being dragged across the floor by its cord.
        public void ServerScrape(float dt)
        {
            if (!IsServer || Swallowed.Value) return;
            scrapeDebt += BaseValue * ScrapeLossPerSecond * dt;
            if (scrapeDebt < 1f) return;
            var loss = Mathf.FloorToInt(scrapeDebt);
            scrapeDebt -= loss;
            Value.Value = Mathf.Max(0, Value.Value - loss);
        }

        public void ServerAccelerate(Vector3 acceleration)
        {
            if (IsServer && !Body.isKinematic) Body.AddForce(acceleration, ForceMode.Acceleration);
        }

        public float Speed => Body != null ? Body.linearVelocity.magnitude : 0f;

        protected override Vector3 BeamTargetLocal => CordAnchor != null ? new Vector3(0f, CordAnchor.localPosition.y, 0f) : base.BeamTargetLocal;

        // Blackout: everything left on the map loses its power and drops.
        public void ServerDischarge()
        {
            if (!IsServer || IsPlugged) return;
            Charge.Value = 0f;
            ServerReleaseAll();
        }

        // Inventory. Only tiny appliances with their plug stowed; the holder must be the only one.
        public bool CanStore => Tiny && !IsGone && !pulling && (Plug == null || Plug.State.Value == PlugState.Stowed) && (!IsMultitap || OutletsUsed == 0);

        public void ServerStore(PlayerInteractor who, int slot)
        {
            if (!IsServer || !CanStore) return;
            ServerReleaseAll();
            Body.isKinematic = true;
            StoredSlot.Value = slot;
            StoredBy.Value = who.NetworkObjectId;
        }

        public void ServerTakeOut(PlayerInteractor who)
        {
            if (!IsServer || StoredBy.Value == NoOwner) return;
            StoredBy.Value = NoOwner;
            StoredSlot.Value = -1;
            Body.isKinematic = false;
            ServerPlaceAtHold(who);
        }

        protected override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!IsServer || !IsSpawned) return;

            if (StoredBy.Value != NoOwner)
            {
                // Rides along with the owner, invisible.
                if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(StoredBy.Value, out var owner)) Body.position = owner.transform.position;
                return;
            }

            var transmitter = Transmitter.Instance;
            // once the yank has started it finishes, whatever its cord does on the way
            var t = (InSettlement || pulling) && transmitter != null ? transmitter.PullProgress : 0f;
            if (t <= 0f || Swallowed.Value) return;
            if (!pulling)
            {
                pulling = true;
                ServerReleaseAll();
                pullStart = Body.position;
                Body.isKinematic = true;
            }
            // Yanked along its cord into the transmitter's mouth, accelerating and spinning.
            var eased = t * t;
            Body.MovePosition(Vector3.Lerp(pullStart, transmitter.MouthPosition, eased));
            Body.MoveRotation(Body.rotation * Quaternion.Euler(0f, 540f * Time.fixedDeltaTime * (0.3f + eased), 200f * Time.fixedDeltaTime * eased));
            if (t >= 1f) Swallowed.Value = true;
        }

        private void Update()
        {
            if (!IsSpawned) return; // outside a room it stays where it was

            var gone = IsGone;
            var transmitter = Transmitter.Instance;
            var t = InSettlement && transmitter != null ? transmitter.PullProgress : 0f;
            transform.localScale = homeScale * Mathf.Lerp(1f, 0.15f, t * t);
            foreach (var r in renderers) if (r != null) r.enabled = !gone;
            foreach (var c in allColliders) if (c != null) c.enabled = !gone;

            if (Indicator != null)
            {
                var charge = Mathf.Clamp01(Charge.Value / Capacity);
                Color color;
                if (IsStalled) color = Random.value < 0.4f ? new Color(1f, 0.3f, 0.1f) : Empty;
                else if (IsCharged) color = Color.Lerp(Full, Color.white, Mathf.PingPong(Time.time * 1.5f, 0.35f));
                else color = Color.Lerp(Empty, new Color(0.9f, 0.75f, 0.1f), charge);
                Indicator.GetPropertyBlock(block);
                block.SetColor(BaseColor, color);
                Indicator.SetPropertyBlock(block);
            }
            if (Glow != null) Glow.enabled = IsCharged && !gone;
        }
    }
}
