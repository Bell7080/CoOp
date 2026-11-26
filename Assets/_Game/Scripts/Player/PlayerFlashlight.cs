using CoOpHorror.Gameplay;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoOpHorror.Player
{
    // F toggles the flashlight (free: there is no shared power any more). Players left behind by the ship
    // lose it. On/off is host-written.
    public sealed class PlayerFlashlight : NetworkBehaviour
    {
        public const float BeamIntensity = 6.5f;   // weak on purpose: upgrades will bring it back up later

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
            beam.range = 15f;
            beam.spotAngle = 62f;
            beam.innerSpotAngle = 30f;
            beam.intensity = BeamIntensity;
            beam.shadows = LightShadows.None;
            beam.enabled = false;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer) On.Value = false;
            On.OnValueChanged += SwitchSound;
            // Only your own torch casts shadows (cost); it is what carves the dark.
            beam.shadows = IsOwner ? LightShadows.Soft : LightShadows.None;
            beam.shadowStrength = 0.9f;
        }

        public override void OnNetworkDespawn()
        {
            On.OnValueChanged -= SwitchSound;
            if (IsOwner) { Shader.SetGlobalFloat("_ElecTorch", 0f); Shader.SetGlobalFloat("_ElecVisionActive", 0f); }
            base.OnNetworkDespawn();
        }

        private void SwitchSound(bool previous,bool current)
        {
            if(previous!=current)ProtoAudio.Play(ProtoSound.FlashSwitch,beam.transform.position,0.4f);
        }

        private bool Dead => health != null && health.IsDead.Value;

        private void Update()
        {
            if (!IsSpawned) { beam.enabled = false; return; }

            if (IsOwner && FirstPersonController.GameplayInputReady && !Dead && Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
                ToggleRpc();

            if (IsServer && On.Value)
            {
                var stage = StageDirector.Instance;
                // Only death or being left behind turns it off.
                if (Dead || (stage != null && stage.IsLeftBehind(health))) On.Value = false;
            }

            if (IsOwner)
            {
                Shader.SetGlobalFloat("_ElecTorch", On.Value && !Dead ? 1f : 0f);
                Shader.SetGlobalFloat("_ElecVisionActive", 1f);
                var camera = Camera.main;
                if (camera != null)
                {
                    Shader.SetGlobalVector("_ElecEye", camera.transform.position);
                    Shader.SetGlobalVector("_ElecForward", camera.transform.forward);
                }
            }
            beam.enabled = On.Value;
            if (!beam.enabled) return;
            beam.intensity = BeamIntensity;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void ToggleRpc()
        {
            if (Dead) return;
            var stage = StageDirector.Instance;
            if (!On.Value && stage != null && stage.IsLeftBehind(health)) return;
            On.Value = !On.Value;
        }
    }
}
