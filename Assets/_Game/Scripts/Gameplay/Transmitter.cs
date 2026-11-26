using CoOpHorror.Localization;
using CoOpHorror.Player;
using Unity.Netcode;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    // Charging: collecting power. Transmitting: the launch was pressed, it sends everything home (beam, the
    // facility's power dies). Done: the ship has left.
    public enum TransmitterPhase : byte { Charging, Transmitting, Done }

    // The ship's final power transmitter (the power store). It starts with a little power in it. Every round
    // needs RequiredWatts in it before the ship may leave.
    // RIGHT mouse held: your nose latches onto its intake and pumps your tank in; beyond the cap the surplus is
    // exchanged for the team's money (StageDirector.Money).
    // LEFT mouse held: your nose drinks power back out of it into your tank (it is a power source like any other).
    // Once it holds RequiredWatts the launch button works; players can keep exchanging surplus and leave when
    // they like. Pressing launch fires it (StageDirector.ServerArmLaunch): blackout and countdown.
    // The host writes the stored power and phase; every peer animates from them.
    public sealed class Transmitter : Interactable, INoseTarget
    {
        public const float InjectRate = 200f;      // W per second one nose pumps in

        public static Transmitter Instance { get; private set; }

        public int RequiredWatts = 2500;
        public int StartWatts = 300;               // what is already in it when the stage starts
        public Transform Intake;                   // where noses latch on
        public Transform Fill;                     // a glowing column scaled 0..1 on local Y with the stored power
        public Renderer FillRenderer;
        public Renderer[] Lamps;
        public Light Glow;

        public readonly NetworkVariable<float> Stored = new(0f);
        public readonly NetworkVariable<TransmitterPhase> Phase = new(TransmitterPhase.Charging);
        public readonly NetworkVariable<double> PhaseStart = new(0d);

        public float Fraction => IsSpawned && RequiredWatts > 0 ? Mathf.Clamp01(Stored.Value / RequiredWatts) : 0f;
        public int StoredWatts => IsSpawned ? Mathf.FloorToInt(Stored.Value + 0.01f) : 0;
        public bool IsFull => IsSpawned && Stored.Value >= RequiredWatts - 0.01f;
        public bool IsCharging => IsSpawned && Phase.Value == TransmitterPhase.Charging;
        public float PhaseElapsed => IsSpawned ? (float)(NetTime - PhaseStart.Value) : 0f;
        public override bool StickyHold => true;

        private MaterialPropertyBlock block;
        private Vector3 fillScale;
        private float glowIntensity;
        private bool firedFx, celebrated;  // celebrated: the "quest complete" chime already played this stage
        private float exchangeDebt;          // host: watts exchanged but not yet a whole dollar
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        protected override void Awake()
        {
            base.Awake();
            Instance = this;
            block = new MaterialPropertyBlock();
            if (Fill != null) fillScale = Fill.localScale;
            if (Glow != null) glowIntensity = Glow.intensity;
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            celebrated = IsFull;   // a late joiner who arrives with it already full hears nothing
            if (!IsServer) return;
            Stored.Value = Mathf.Min(StartWatts, RequiredWatts);
            Phase.Value = TransmitterPhase.Charging;
            PhaseStart.Value = NetTime;
            exchangeDebt = 0f;
        }

        public override string HoverInfo(PlayerInteractor who)
        {
            if (!IsSpawned) return string.Empty;
            return GameText.Get("transmitter.name") + "\n" + GameText.Format("transmitter.progress", StoredWatts, RequiredWatts)
                + (IsFull && IsCharging ? "\n" + GameText.Format("transmitter.exchangeRate", StageDirector.WattsPerDollar) : string.Empty);
        }

        // Left button: drink power back out of it.
        public override InteractKind Describe(PlayerInteractor who, out string prompt)
        {
            prompt = string.Empty;
            if (!IsCharging || Stored.Value < 0.5f) return InteractKind.None;
            prompt = GameText.Format("interact.drainTransmitter", StoredWatts);
            return InteractKind.Hold;
        }

        public override bool ServerHold(PlayerInteractor who, float dt)
        {
            if (!IsCharging) return false;
            var nose = who.GetComponent<PlayerNose>();
            if (nose == null) return false;
            var taken = nose.ServerAdd(Mathf.Min(PlayerNose.SuckRate * dt, Stored.Value));
            Stored.Value = Mathf.Max(0f, Stored.Value - taken);
            nose.ServerSetFlow(taken > 0f ? PlayerNose.FlowIn : PlayerNose.FlowNone);
            return Stored.Value > 0.01f;
        }

        // Right button: pump the tank in (surplus beyond the cap becomes money).
        public override InteractKind DescribeRight(PlayerInteractor who, out string prompt)
        {
            prompt = string.Empty;
            if (!IsCharging) return InteractKind.None;
            var nose = who.GetComponent<PlayerNose>();
            if (nose == null || nose.Energy.Value < 0.5f)
            {
                prompt = GameText.Get("interact.tankEmpty");
                return InteractKind.None;
            }
            prompt = GameText.Format(IsFull ? "interact.exchange" : "interact.inject", Mathf.FloorToInt(nose.Energy.Value));
            return InteractKind.Hold;
        }

        public override bool ServerHoldRight(PlayerInteractor who, float dt)
        {
            if (!IsCharging) return false;
            var nose = who.GetComponent<PlayerNose>();
            if (nose == null) return false;
            var given = nose.ServerTake(InjectRate * dt);
            nose.ServerSetFlow(given > 0f ? PlayerNose.FlowOut : PlayerNose.FlowNone);
            var fill = Mathf.Min(given, Mathf.Max(0f, RequiredWatts - Stored.Value));
            Stored.Value += fill;
            ServerExchange(given - fill);
            return nose.Energy.Value > 0.01f;
        }

        private void ServerExchange(float watts)
        {
            var stage = StageDirector.Instance;
            if (watts <= 0f || stage == null) return;
            exchangeDebt += watts;
            var dollars = Mathf.FloorToInt(exchangeDebt / StageDirector.WattsPerDollar);
            if (dollars <= 0) return;
            exchangeDebt -= dollars * StageDirector.WattsPerDollar;
            stage.ServerAddMoney(dollars);
        }

        // Server (launch pressed): send it all home.
        public void ServerFire()
        {
            if (!IsServer || !IsCharging) return;
            Phase.Value = TransmitterPhase.Transmitting;
            PhaseStart.Value = NetTime;
            Debug.Log("[ElecMole] Transmitter fired at launch");
        }

        public void ServerFinish()
        {
            if (!IsServer || Phase.Value == TransmitterPhase.Done) return;
            Phase.Value = TransmitterPhase.Done;
            PhaseStart.Value = NetTime;
        }

        public Vector3 NosePoint(Vector3 from) => Intake != null ? Intake.position : transform.position + Vector3.up * 1.1f;

        private void Update()
        {
            var phase = IsSpawned ? Phase.Value : TransmitterPhase.Charging;
            var fraction = Fraction;
            var full = IsFull;
            var firing = phase == TransmitterPhase.Transmitting;
            if (Fill != null)
            {
                var shown = firing ? Mathf.Clamp01(1f - PhaseElapsed / 2.5f) : phase == TransmitterPhase.Done ? 0f : fraction;
                Fill.localScale = new Vector3(fillScale.x, Mathf.Max(0.001f, fillScale.y * shown), fillScale.z);
            }
            var color = firing ? Color.Lerp(new Color(0.6f, 0.9f, 1f), Color.white, Mathf.PingPong(Time.time * 6f, 1f))
                : phase == TransmitterPhase.Done ? new Color(0.15f, 0.2f, 0.25f)
                : full ? Color.Lerp(new Color(1f, 0.8f, 0.3f), new Color(0.45f, 1f, 0.6f), Mathf.PingPong(Time.time * 1.5f, 1f)) // ready: launch, or exchange
                : Color.Lerp(new Color(0.2f, 0.45f, 0.9f), new Color(0.45f, 1f, 0.85f), fraction);
            if (FillRenderer != null) Tint(FillRenderer, color);
            if (Lamps != null)
                for (var i = 0; i < Lamps.Length; i++)
                    if (Lamps[i] != null)
                        Tint(Lamps[i], (i + 0.5f) / Lamps.Length <= fraction || firing ? color : new Color(0.08f, 0.1f, 0.14f));
            if (Glow != null)
            {
                Glow.enabled = phase != TransmitterPhase.Done;
                Glow.color = color;
                Glow.intensity = firing ? glowIntensity * (2f + Mathf.PingPong(Time.time * 10f, 2f)) : glowIntensity * (0.3f + fraction);
            }
            // Only the first time it fills up: a celebratory chime (draining and refilling it stays quiet).
            if (full && !celebrated && IsCharging)
            {
                celebrated = true;
                ProtoAudio.Play(ProtoSound.TransmitterFull, transform.position + Vector3.up * 2f, 1f);
            }
            if (firing && !firedFx)
            {
                firedFx = true;
                ProtoAudio.Play(ProtoSound.Rumble, transform.position, 1f);
                ProtoAudio.Play(ProtoSound.Zap, transform.position + Vector3.up, 1f);
            }
            if (phase == TransmitterPhase.Charging) firedFx = false;
        }

        private void Tint(Renderer r, Color color)
        {
            r.GetPropertyBlock(block);
            block.SetColor(BaseColor, color);
            r.SetPropertyBlock(block);
        }
    }
}
