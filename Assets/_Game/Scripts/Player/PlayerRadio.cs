using CoOpHorror.Gameplay;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoOpHorror.Player
{
    public enum RadioMode : byte { Off, Walkie, ShipMic }

    // E (hold) = talk on the radio. Outside: raise the walkie-talkie; the voice is broadcast inside the ship.
    // Inside, looking at the ship microphone: broadcast to every player like an earpiece.
    // This is the game-state side only (who transmits, on which path). Microphone capture/transport is a
    // separate voice provider that has not been added yet (see Docs/VOICE.md); VoiceRouting holds the rules.
    public sealed class PlayerRadio : NetworkBehaviour
    {
        public readonly NetworkVariable<RadioMode> Mode = new(RadioMode.Off);

        private PlayerInteractor interactor;
        private PlayerHealth health;
        private FirstPersonController motor;
        private GameObject walkie;
        private RadioMode sent;

        private void Awake()
        {
            interactor = GetComponent<PlayerInteractor>();
            health = GetComponent<PlayerHealth>();
            motor = GetComponent<FirstPersonController>();
            // Placeholder walkie-talkie in the left hand.
            var pivot = motor != null && motor.AimPivot != null ? motor.AimPivot : transform;
            walkie = GameObject.CreatePrimitive(PrimitiveType.Cube);
            walkie.name = "WalkieTalkie";
            Destroy(walkie.GetComponent<Collider>());
            walkie.transform.SetParent(pivot, false);
            walkie.transform.localPosition = new Vector3(-0.22f, -0.18f, 0.38f);
            walkie.transform.localRotation = Quaternion.Euler(-15f, 20f, 0f);
            walkie.transform.localScale = new Vector3(0.07f, 0.16f, 0.04f);
            var antenna = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(antenna.GetComponent<Collider>());
            antenna.transform.SetParent(walkie.transform, false);
            antenna.transform.localPosition = new Vector3(0.3f, 0.75f, 0f);
            antenna.transform.localScale = new Vector3(0.15f, 0.35f, 0.15f);
            foreach (var r in walkie.GetComponentsInChildren<Renderer>()) r.material.color = new Color(0.12f, 0.12f, 0.12f);
            walkie.SetActive(false);
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer) Mode.Value = RadioMode.Off;
            Mode.OnValueChanged += OnModeChanged;
            sent = RadioMode.Off;
        }

        public override void OnNetworkDespawn() => Mode.OnValueChanged -= OnModeChanged;

        private bool Dead => health != null && health.IsDead.Value;

        private void OnModeChanged(RadioMode previous, RadioMode current)
        {
            // Squelch click for everyone who would hear this transmission (and the speaker).
            var local = PlayerRadio.LocalRadio;
            if (local == null) return;
            var mode = current != RadioMode.Off ? current : previous;
            if (local == this || VoiceRouting.HearsOverRadio(local, this, mode))
                ProtoAudio.Play(ProtoSound.Radio, Camera.main != null ? Camera.main.transform.position : transform.position, 0.35f);
        }

        public static PlayerRadio LocalRadio => PlayerInteractor.Local != null ? PlayerInteractor.Local.GetComponent<PlayerRadio>() : null;

        // Automation (owner): force transmitting on/off.
        public bool? AutomationTalk { get; set; }

        private void Update()
        {
            if (!IsSpawned) { walkie.SetActive(false); return; }
            if (IsOwner)
            {
                var k = Keyboard.current;
                // Push-to-talk is V (E is for plugs and reviving).
                var talk = AutomationTalk ?? (FirstPersonController.GameplayInputReady && k != null && k.vKey.isPressed);
                var desired = !talk || Dead ? RadioMode.Off : interactor != null && interactor.Target is ShipMic ? RadioMode.ShipMic : RadioMode.Walkie;
                if (desired != sent)
                {
                    sent = desired;
                    SetModeRpc(desired);
                }
            }
            if (IsServer && Mode.Value != RadioMode.Off && Dead) Mode.Value = RadioMode.Off;
            walkie.SetActive(Mode.Value == RadioMode.Walkie && !Dead);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SetModeRpc(RadioMode mode)
        {
            if (Dead) mode = RadioMode.Off;
            if (mode == RadioMode.ShipMic)
            {
                var mic = ShipMic.Instance;
                if (mic == null || !mic.ServerInRange(interactor)) mode = RadioMode.Walkie;
            }
            var previous = Mode.Value;
            Mode.Value = mode;
            if (mode == previous) return;
            // Keying a radio is not silent: the walkie clicks where you stand, and the ship's broadcast
            // crackles out of every radio outside (the fan crawler hears both).
            var stage = StageDirector.Instance;
            if (mode == RadioMode.Walkie) Noise.Emit(transform.position, Noise.RadioRadius, NoiseKind.Radio);
            else if (mode == RadioMode.ShipMic)
                foreach (var p in PlayerHealth.All)
                    if (p != null && p.IsSpawned && !p.IsDead.Value && p.gameObject != gameObject && (stage == null || !stage.InShip(p.transform.position)))
                        Noise.Emit(p.transform.position, Noise.ShipCallRadius, NoiseKind.Radio);
        }
    }
}
