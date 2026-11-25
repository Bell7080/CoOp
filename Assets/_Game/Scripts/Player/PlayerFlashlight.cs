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
            beam.range = 18f;
            beam.spotAngle = 52f;
            beam.innerSpotAngle = 28f;
            beam.intensity = 3f;
            beam.shadows = LightShadows.None;
            beam.enabled = false;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer) On.Value = false;
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
                if (Dead || (grid != null && grid.IsEmpty) || (stage != null && stage.IsLeftBehind(health))) On.Value = false;
                else if (grid != null) grid.ServerTake(DrainPerSecond * Time.deltaTime);
            }

            beam.enabled = On.Value;
            if (!beam.enabled) return;
            // Weak, flickering beam when the shared grid is nearly empty.
            var low = grid != null && grid.IsSpawned && grid.Fraction < LowPowerFraction;
            beam.intensity = low ? (Random.value < 0.08f ? 0.3f : 1.6f) : 3f;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void ToggleRpc()
        {
            if (Dead) return;
            var grid = PowerGrid.Instance;
            var stage = StageDirector.Instance;
            if (!On.Value && ((grid != null && grid.IsEmpty) || (stage != null && stage.IsLeftBehind(health)))) return;
            On.Value = !On.Value;
        }
    }
}
