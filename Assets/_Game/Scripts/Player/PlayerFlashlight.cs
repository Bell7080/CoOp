using CoOpHorror.Gameplay;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoOpHorror.Player
{
    // F toggles the flashlight. It draws a little from the shared grid while on and switches off at 0%.
    // Players left behind by the ship lose the link to its power. On/off is host-written.
    public sealed class PlayerFlashlight : NetworkBehaviour
    {
        public const float DrainPerSecond = 0.15f;
        public const float LowPowerFraction = 0.1f;
        public const float BeamIntensity = 24f;

        public readonly NetworkVariable<bool> On = new(false);

        private PlayerHealth health;
        private FirstPersonController motor;
        private Light beam;

        private void Awake()
        {
            health = GetComponent<PlayerHealth>();
            motor = GetComponent<FirstPersonController>();
            var pivot = motor != null && motor.AimPivot != null ? motor.AimPivot : transform;
            var go = new GameObject("FlashlightBeam");
            go.transform.SetParent(pivot, false);
            go.transform.localPosition = new Vector3(0.18f, -0.12f, 0.15f);
            beam = go.AddComponent<Light>();
            beam.type = LightType.Spot;
            beam.color = new Color(1f, 0.95f, 0.82f);
            beam.range = 45f;
            beam.spotAngle = 78f;
            beam.innerSpotAngle = 40f;
            beam.intensity = BeamIntensity;
            beam.shadows = LightShadows.None;
            beam.enabled = false;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer) On.Value = false;
            // Only your own torch casts shadows (cost); it is what carves the dark.
            beam.shadows = IsOwner ? LightShadows.Soft : LightShadows.None;
            beam.shadowStrength = 0.9f;
        }

        private bool Dead => health != null && health.IsDead.Value;

        private void Update()
        {
            if (!IsSpawned) { beam.enabled = false; return; }

            if (IsOwner && FirstPersonController.GameplayInputReady && !Dead && Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
                ToggleRpc();

            var grid = PowerGrid.Instance;
            if (IsServer && On.Value)
            {
                var stage = StageDirector.Instance;
                // Stays on even when the grid runs dry (just dimmer); only death or being left behind turns it off.
                if (Dead || (stage != null && stage.IsLeftBehind(health))) On.Value = false;
                else if (grid != null) grid.ServerTake(DrainPerSecond * Time.deltaTime);
            }

            beam.enabled = On.Value;
            if (!beam.enabled) return;
            // Steady beam; a little dimmer when the shared grid is (nearly) empty. No flicker.
            var low = grid != null && grid.IsSpawned && grid.Fraction < LowPowerFraction;
            beam.intensity = low ? BeamIntensity * 0.65f : BeamIntensity;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void ToggleRpc()
        {
            if (Dead) return;
            var grid = PowerGrid.Instance;
            var stage = StageDirector.Instance;
            if (!On.Value && stage != null && stage.IsLeftBehind(health)) return;
            On.Value = !On.Value;
        }
    }
}
