using CoOpHorror.Localization;
using CoOpHorror.Player;
using Unity.Netcode;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    public enum SupplyPhase : byte { Idle, Incoming, Open }
    public enum SupplyItem : byte { Medkit, Adrenaline }
    public enum SupplyCategory : byte { Medical }

    // Item support station: a drop box out in the facility. Someone in the ship buys an item on the ship's
    // supply terminal (paid with the team's money); a few seconds later the box's beacon spins, its hatch flips up
    // and a tray slides out with that item. Whoever is out there clicks to use it: a medkit heals, an
    // adrenaline shot gives a stretch of tireless running. Then the hatch closes and the next order can come.
    // Host writes the phase; every peer animates from it.
    public sealed class SupplyStation : Interactable
    {
        public const float IncomingSeconds = 5f;
        public const int MedkitHeal = 40;
        public const float AdrenalineSeconds = 20f;

        // What the ship's supply terminal sells, in list order, and the prices in dollars (the team's money,
        // exchanged from surplus power at the transmitter).
        public static readonly SupplyItem[] Catalog = { SupplyItem.Medkit, SupplyItem.Adrenaline };

        public static int Price(SupplyItem item) => item switch
        {
            SupplyItem.Medkit => 40,
            _ => 25,
        };

        public static string NameKey(SupplyItem item) => item switch
        {
            SupplyItem.Medkit => "supply.medkit",
            _ => "supply.adrenaline",
        };

        public static string EffectKey(SupplyItem item) => item switch
        {
            SupplyItem.Medkit => "hover.medkitEffect",
            _ => "hover.adrenalineEffect",
        };

        // The terminal's tabs, in order, and which tab each item is listed under.
        public static readonly SupplyCategory[] Categories = { SupplyCategory.Medical };
        public static SupplyCategory Category(SupplyItem item) => SupplyCategory.Medical;
        public static string CategoryKey(SupplyCategory category) => "shop.tabMedical";

        // Can this be ordered now?
        public bool Available(SupplyItem item) => true;

        public static SupplyStation Instance { get; private set; }

        public Transform Hatch;        // pivot on the top edge, opens about local X
        public Transform Tray;         // slides out along local Z
        public GameObject MedkitModel, AdrenalineModel;
        public Renderer Lamp;
        public Light Beacon;

        public readonly NetworkVariable<SupplyPhase> Phase = new(SupplyPhase.Idle);
        public readonly NetworkVariable<double> PhaseStart = new(0d);
        public readonly NetworkVariable<SupplyItem> Item = new(SupplyItem.Medkit);

        private float open;
        private Vector3 trayHome;
        private float nextBeep;
        private MaterialPropertyBlock block;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        public bool IsBusy => IsSpawned && Phase.Value != SupplyPhase.Idle;

        protected override void Awake()
        {
            base.Awake();
            Instance = this;
            block = new MaterialPropertyBlock();
            if (Tray != null) trayHome = Tray.localPosition;
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            Phase.Value = SupplyPhase.Idle;
            Item.Value = SupplyItem.Medkit;
        }

        // Server: the console paid for a drop.
        public bool ServerRequest(SupplyItem item)
        {
            if (!IsServer || Phase.Value != SupplyPhase.Idle) return false;
            Item.Value = item;
            Phase.Value = SupplyPhase.Incoming;
            PhaseStart.Value = NetTime;
            return true;
        }

        public override InteractKind Describe(PlayerInteractor who, out string prompt)
        {
            if (IsSpawned && Phase.Value == SupplyPhase.Open && open > 0.8f)
            {
                prompt = GameText.Get(NameKey(Item.Value));
                return InteractKind.Press;
            }
            prompt = string.Empty;
            return InteractKind.None;
        }

        public override string HoverInfo(PlayerInteractor who)
        {
            if (!IsSpawned) return string.Empty;
            return Phase.Value switch
            {
                SupplyPhase.Open => GameText.Get(NameKey(Item.Value)) + "\n" + GameText.Get(EffectKey(Item.Value)),
                SupplyPhase.Incoming => GameText.Get("supply.sending"),
                _ => GameText.Get("hover.supplyIdle"),
            };
        }

        public override void ServerUse(PlayerInteractor who)
        {
            if (Phase.Value != SupplyPhase.Open) return;
            if (Item.Value == SupplyItem.Medkit)
            {
                var health = who.GetComponent<PlayerHealth>();
                if (health == null || health.IsDead.Value || health.Health.Value >= PlayerHealth.MaxHealth) return;
                health.Health.Value = Mathf.Min(PlayerHealth.MaxHealth, health.Health.Value + MedkitHeal);
            }
            else
            {
                var motor = who.GetComponent<FirstPersonController>();
                if (motor == null) return;
                motor.BoostRpc(AdrenalineSeconds);
            }
            Phase.Value = SupplyPhase.Idle;
            PhaseStart.Value = NetTime;
        }

        private void Update()
        {
            if (!IsSpawned) return;
            var phase = Phase.Value;
            var elapsed = (float)(NetTime - PhaseStart.Value);
            if (IsServer && phase == SupplyPhase.Incoming && elapsed >= IncomingSeconds)
            {
                Phase.Value = SupplyPhase.Open;
                PhaseStart.Value = NetTime;
                ProtoAudio.Play(ProtoSound.Delivery, transform.position + Vector3.up, 0.8f);
            }

            open = Mathf.MoveTowards(open, phase == SupplyPhase.Open ? 1f : 0f, Time.deltaTime * 1.4f);
            var o = Mathf.SmoothStep(0f, 1f, open);
            if (Hatch != null) Hatch.localRotation = Quaternion.Euler(-100f * Mathf.Clamp01(o * 1.6f), 0f, 0f);
            if (Tray != null) Tray.localPosition = trayHome + Vector3.forward * (0.42f * Mathf.Clamp01(o * 1.6f - 0.6f));
            var showItem = phase == SupplyPhase.Open || open > 0.01f && phase != SupplyPhase.Idle;
            if (MedkitModel != null) MedkitModel.SetActive(showItem && Item.Value == SupplyItem.Medkit);
            if (AdrenalineModel != null) AdrenalineModel.SetActive(showItem && Item.Value == SupplyItem.Adrenaline);

            // beeping and a spinning amber beacon while the drop is on its way
            if (phase == SupplyPhase.Incoming && Time.time >= nextBeep)
            {
                nextBeep = Time.time + 0.8f;
                ProtoAudio.PlayPitched(ProtoSound.Beep, transform.position + Vector3.up * 1.5f, 0.7f, 1.4f);
            }
            if (Beacon != null)
            {
                Beacon.enabled = phase != SupplyPhase.Idle;
                Beacon.transform.localRotation = Quaternion.Euler(0f, Time.time * 360f, 0f);
            }
            if (Lamp != null)
            {
                var color = phase switch
                {
                    SupplyPhase.Incoming => Mathf.PingPong(Time.time * 3f, 1f) > 0.5f ? new Color(1f, 0.65f, 0.1f) : new Color(0.3f, 0.15f, 0.02f),
                    SupplyPhase.Open => new Color(0.3f, 1f, 0.4f),
                    _ => new Color(0.12f, 0.2f, 0.12f),
                };
                Lamp.GetPropertyBlock(block);
                block.SetColor(BaseColor, color);
                Lamp.SetPropertyBlock(block);
            }
        }
    }
}
