using System.Collections.Generic;
using CoOpHorror.Localization;
using CoOpHorror.Player;
using Unity.Netcode;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    // A fixture mounted in the level (CCTV camera, ceiling lamp). Hold the left button to feed it shared power;
    // once it works, holding the left button on it again rips it off its mount. That takes a while (several
    // players are faster), makes some noise, switches it off for good and drops its salvage: a charged,
    // sellable item with a battery cable (a hidden Appliance placed with it) that plugs into the transmitter.
    public abstract class InstalledDevice : Interactable, IChargeGauge
    {
        public const float ChargeRate = 6f;
        public const float DetachNoiseRadius = 7f;

        public static readonly List<InstalledDevice> Devices = new();

        public float Capacity = 6f;
        public float DetachSeconds = 4f;
        public Appliance Salvage;         // revealed when ripped off
        public Transform SalvageSpawn;    // where the salvage drops from

        public readonly NetworkVariable<float> Charge = new(0f);
        public readonly NetworkVariable<bool> Detached = new(false);
        public readonly NetworkVariable<float> DetachProgress = new(0f);

        public bool Online => IsSpawned && !Detached.Value && Charge.Value >= Capacity - 0.01f;
        public int ChargePercent => Mathf.FloorToInt(Mathf.Clamp01(Charge.Value / Capacity) * 100f);
        public float GaugeFraction => IsSpawned ? Mathf.Clamp01(Charge.Value / Capacity) : 0f;
        public bool GaugeCharging => false;
        public int DetachPercent => Mathf.FloorToInt(Mathf.Clamp01(DetachProgress.Value) * 100f);
        public abstract string DisplayName { get; }

        // Extra line while it works (e.g. "linked to the ship's panel").
        protected virtual string OnlineLabel => GameText.Format("interact.deviceOnline", DisplayName);

        protected override void Awake()
        {
            base.Awake();
            Devices.Add(this);
        }

        public override void OnDestroy()
        {
            Devices.Remove(this);
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            Charge.Value = 0f;
            Detached.Value = false;
            DetachProgress.Value = 0f;
        }

        public override InteractKind Describe(PlayerInteractor who, out string prompt)
        {
            if (Detached.Value)
            {
                prompt = GameText.Format("interact.deviceDetached", DisplayName);
                return InteractKind.None;
            }
            if (Online)
            {
                prompt = OnlineLabel + "\n" + GameText.Format("interact.deviceDetach", DisplayName, DetachPercent);
                return who.HandsFull ? InteractKind.None : InteractKind.Hold;
            }
            var grid = PowerGrid.Instance;
            if (grid == null || grid.IsEmpty)
            {
                prompt = GameText.Format("interact.deviceNoPower", DisplayName, ChargePercent);
                return InteractKind.None;
            }
            prompt = GameText.Format("interact.deviceCharge", DisplayName, ChargePercent);
            return InteractKind.Hold;
        }

        public override bool ServerHold(PlayerInteractor who, float dt)
        {
            if (Detached.Value) return false;
            if (Online)
            {
                // Ripping it off; every player on it adds progress.
                DetachProgress.Value = Mathf.Min(1f, DetachProgress.Value + dt / DetachSeconds);
                if (DetachProgress.Value < 1f) return true;
                ServerDetach();
                return false;
            }
            var grid = PowerGrid.Instance;
            if (grid == null) return false;
            var taken = grid.ServerTake(Mathf.Min(ChargeRate * dt, Capacity - Charge.Value));
            Charge.Value = Mathf.Min(Capacity, Charge.Value + taken);
            return !Online; // powering ends; ripping off needs a new press
        }

        private void ServerDetach()
        {
            Detached.Value = true;
            Charge.Value = 0f;
            Noise.Emit(transform.position, DetachNoiseRadius);
            if (Salvage == null) return;
            var from = SalvageSpawn != null ? SalvageSpawn : transform;
            Salvage.ServerReveal(new Pose(from.position, from.rotation));
        }

        // Blackout: everything still mounted loses its power.
        public void ServerDischarge()
        {
            if (IsServer && !Detached.Value) Charge.Value = 0f;
        }
    }
}
