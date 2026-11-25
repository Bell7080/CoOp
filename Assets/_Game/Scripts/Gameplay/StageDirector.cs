using CoOpHorror.Player;
using Unity.Netcode;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    public enum StagePhase : byte { Exploring, Blackout, Launching, Escaped, Defeat }

    // Host-owned stage flow: exploration -> blackout (transmitter gone) -> launch countdown -> escape.
    // Everyone dead at any point is a defeat. Players left outside when the ship leaves lose their link to the
    // ship's power and slowly die. Every peer applies the matching lighting from the replicated phase.
    public sealed class StageDirector : NetworkBehaviour
    {
        public const float LaunchWait = 30f;           // countdown after the launch button is pressed
        public const float LaunchWhenAllAboard = 5f;   // shortened to this once every living player is aboard
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
            LaunchAt.Value = 0d;
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

        // Transmitter is gone: every appliance and CCTV camera loses its power and the map goes dark.
        public void ServerBlackout()
        {
            if (!IsServer || Phase.Value != StagePhase.Exploring) return;
            foreach (var appliance in Appliance.All) if (appliance.IsSpawned) appliance.ServerDischarge();
            foreach (var cctv in CctvCamera.All) if (cctv.IsSpawned) cctv.ServerDischarge();
            Phase.Value = StagePhase.Blackout;
        }

        public bool ServerArmLaunch()
        {
            if (!IsServer || Phase.Value != StagePhase.Blackout) return false;
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
                if (p == null || !p.IsSpawned || !InShip(p.transform.position)) continue;
                Aboard.Add(p.OwnerClientId);
                if (!p.IsDead.Value) aliveAboard++;
            }
            if (ShipDoor != null) ShipDoor.ServerClose();
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
