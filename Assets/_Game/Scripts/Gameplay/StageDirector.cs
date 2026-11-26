using CoOpHorror.Player;
using Unity.Netcode;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    public enum StagePhase : byte { Exploring, Blackout, Launching, Escaped, Defeat }

    // Host-owned stage flow: exploration (fill the transmitter; once it is full the launch button works and the
    // surplus can be exchanged for money) -> launch pressed: the transmitter fires, the facility blacks out and
    // the countdown runs -> escape. (StagePhase.Blackout is no longer entered; kept for the enum's order.)
    // Everyone dead at any point is a defeat. Players left outside when the ship leaves lose their link to the
    // ship's power and slowly die. Every peer applies the matching lighting from the replicated phase.
    public sealed class StageDirector : NetworkBehaviour
    {
        public const float LaunchWait = 10f;           // countdown when pressed before everyone is aboard
        public const float LaunchWhenAllAboard = 3f;   // shortened to this once every living player is aboard
        public const int LaunchReviveHealth = 50;      // bodies aboard revive in the ship's surge
        public const int LeftBehindDrainPerTick = 2;   // health lost every DrainInterval outside the departed ship
        public const float DrainInterval = 0.5f;

        public static StageDirector Instance { get; private set; }

        public Light[] RoomLights;
        public Light[] EmergencyLights;
        public Light Sun;
        public Vector3 ShipMin;
        public Vector3 ShipMax;
        public PoweredDoor ShipDoor;

        public readonly NetworkVariable<StagePhase> Phase = new(StagePhase.Exploring);
        public readonly NetworkVariable<double> LaunchAt = new(0d);
        public readonly NetworkVariable<double> IntroStart = new(0d);   // crash-landing opening (CrashIntro)
        public readonly NetworkVariable<int> Earned = new(0);          // watts sent by the transmitter this stage
        // Stage number of the run (1 = first). Later stages drain faster (PlayerNose.SuckRate). There is no
        // stage-to-stage flow yet: a new room starts at 1 and the dev tools can raise it for testing.
        public readonly NetworkVariable<int> Level = new(1);
        // The team's money: power pumped into the transmitter beyond its cap, and whatever is left in the tanks
        // of the players aboard when the ship leaves, exchanged at WattsPerDollar. Spent at the supply terminal.
        public const int WattsPerDollar = 10;
        public readonly NetworkVariable<int> Money = new(0);

        public void ServerAddMoney(int dollars)
        {
            if (IsServer && dollars > 0) Money.Value += dollars;
        }

        public bool ServerSpend(int dollars)
        {
            if (!IsServer || dollars < 0 || Money.Value < dollars) return false;
            Money.Value -= dollars;
            return true;
        }
        public NetworkList<ulong> Aboard; // OwnerClientIds inside the ship when it left

        private float[] roomIntensity;
        private float sunIntensity;
        private Color ambient;
        private bool initialized;
        private bool? appliedDark;
        private float nextDrain;

        public double NetTime => NetworkManager != null ? NetworkManager.ServerTime.Time : 0d;
        public float LaunchRemaining => Mathf.Max(0f, (float)(LaunchAt.Value - NetTime));

        private void Awake()
        {
            Instance = this;
            Aboard = new NetworkList<ulong>();
            roomIntensity = new float[RoomLights != null ? RoomLights.Length : 0];
            for (var i = 0; i < roomIntensity.Length; i++) roomIntensity[i] = RoomLights[i] != null ? RoomLights[i].intensity : 0f;
            sunIntensity = Sun != null ? Sun.intensity : 0f;
            ambient = RenderSettings.ambientLight;
            initialized = true;
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            ApplyLighting(false);
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            Phase.Value = StagePhase.Exploring;
            IntroStart.Value = NetworkManager.ServerTime.Time + 0.5d;
            LaunchAt.Value = 0d;
            Earned.Value = 0;
            Level.Value = 1;
            Money.Value = 0;
            Aboard.Clear();
        }

        public override void OnNetworkDespawn()
        {
            appliedDark = null;
            ApplyLighting(false);
        }

        public bool InShip(Vector3 position) =>
            position.x >= ShipMin.x && position.x <= ShipMax.x && position.z >= ShipMin.z && position.z <= ShipMax.z;

        public bool IsAboard(ulong clientId)
        {
            foreach (var id in Aboard) if (id == clientId) return true;
            return false;
        }

        // A player outside the ship after it left.
        public bool IsLeftBehind(PlayerHealth player) =>
            IsSpawned && Phase.Value == StagePhase.Escaped && player != null && !IsAboard(player.OwnerClientId);

        // The transmitter fired: every appliance and fixture (CCTV, lamps) loses its power and the map goes dark.
        private void ServerDischargeAll()
        {
            foreach (var appliance in Appliance.All) if (appliance.IsSpawned) appliance.ServerDischarge();
            foreach (var device in InstalledDevice.Devices) if (device.IsSpawned) device.ServerDischarge();
        }

        // The launch button works once the transmitter holds its required power.
        public bool LaunchReady => IsSpawned && Phase.Value == StagePhase.Exploring && Transmitter.Instance != null && Transmitter.Instance.IsFull;

        // Launch pressed: the transmitter fires, the facility blacks out, the countdown starts.
        public bool ServerArmLaunch()
        {
            if (!IsServer || !LaunchReady) return false;
            Transmitter.Instance.ServerFire();
            ServerDischargeAll();
            LaunchAt.Value = NetTime + LaunchWait;
            Phase.Value = StagePhase.Launching;
            return true;
        }

        private void Update()
        {
            if (IsSpawned && IsServer) ServerTick();
            var phase = IsSpawned ? Phase.Value : StagePhase.Exploring;
            var dark = phase == StagePhase.Blackout || phase == StagePhase.Launching || phase == StagePhase.Escaped;
            if (appliedDark != dark) ApplyLighting(dark);
            if (dark && EmergencyLights != null)
                foreach (var light in EmergencyLights)
                    if (light != null) light.intensity = 0.35f + Mathf.PingPong(Time.time * 0.8f + light.transform.position.x, 0.25f);
        }

        private void ServerTick()
        {
            var phase = Phase.Value;
            if (phase == StagePhase.Defeat) return;

            var anyPlayer = false;
            var anyAlive = false;
            var allAliveAboard = true;
            foreach (var p in PlayerHealth.All)
            {
                if (p == null || !p.IsSpawned) continue;
                anyPlayer = true;
                if (p.IsDead.Value) continue;
                anyAlive = true;
                if (!InShip(p.transform.position)) allAliveAboard = false;
            }

            if (phase == StagePhase.Escaped)
            {
                if (Time.time < nextDrain) return;
                nextDrain = Time.time + DrainInterval;
                foreach (var p in PlayerHealth.All)
                    if (p != null && p.IsSpawned && !p.IsDead.Value && IsLeftBehind(p)) p.ServerDrain(LeftBehindDrainPerTick);
                return;
            }

            if (anyPlayer && !anyAlive)
            {
                Phase.Value = StagePhase.Defeat;
                Debug.Log("[ElecMole] Defeat: everyone is discharged");
                return;
            }

            if (phase != StagePhase.Launching) return;
            if (allAliveAboard && LaunchAt.Value > NetTime + LaunchWhenAllAboard) LaunchAt.Value = NetTime + LaunchWhenAllAboard;
            if (NetTime < LaunchAt.Value) return;

            Aboard.Clear();
            var aliveAboard = 0;
            foreach (var p in PlayerHealth.All)
            {
                if (p == null || !p.IsSpawned || p.IsDead.Value || !InShip(p.transform.position)) continue;
                Aboard.Add(p.OwnerClientId);
                aliveAboard++;
            }
            // Bodies carried aboard come back in the ship's surge right before launch.
            foreach (var body in Corpse.Bodies)
            {
                if (body == null || !body.IsSpawned || !InShip(body.transform.position)) continue;
                var player = body.Player;
                if (player == null || !player.IsDead.Value || IsAboard(player.OwnerClientId)) continue;
                player.ServerRevive(body.transform.position + Vector3.up * 1.05f, LaunchReviveHealth, true);
                Aboard.Add(player.OwnerClientId);
                aliveAboard++;
            }
            if (ShipDoor != null) ShipDoor.ServerClose();
            // Settlement: what the transmitter sent plus the power the survivors carry in their tanks; the
            // tanks are also exchanged for money.
            var earned = Transmitter.Instance != null ? Transmitter.Instance.StoredWatts : 0;
            foreach (var p in PlayerHealth.All)
                if (p != null && p.IsSpawned && IsAboard(p.OwnerClientId) && p.TryGetComponent<PlayerNose>(out var tank))
                {
                    earned += Mathf.FloorToInt(tank.Energy.Value);
                    ServerAddMoney(Mathf.FloorToInt(tank.Energy.Value / WattsPerDollar));
                    tank.ServerTake(tank.Energy.Value);
                }
            Earned.Value = earned;
            if (Transmitter.Instance != null) Transmitter.Instance.ServerFinish();
            Phase.Value = aliveAboard > 0 ? StagePhase.Escaped : StagePhase.Defeat;
            Debug.Log($"[ElecMole] Ship left with {aliveAboard} living player(s) aboard");
        }

        private void ApplyLighting(bool dark)
        {
            if (!initialized) return;
            appliedDark = dark;
            if (RoomLights != null)
                for (var i = 0; i < RoomLights.Length && i < roomIntensity.Length; i++)
                    if (RoomLights[i] != null) RoomLights[i].enabled = !dark;
            if (EmergencyLights != null)
                foreach (var light in EmergencyLights) if (light != null) light.enabled = dark;
            if (Sun != null) Sun.intensity = dark ? sunIntensity * 0.15f : sunIntensity;
            RenderSettings.ambientLight = dark ? ambient * 0.3f : ambient;
        }
    }
}
