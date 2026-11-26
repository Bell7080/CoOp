using CoOpHorror.Gameplay;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoOpHorror.Player
{
    // Reviving a teammate: look at their body and hold E to pour your own energy into it. Filling the gauge
    // takes HoldSeconds; at the end it costs you Cost health and brings them back at 1 health. You need more
    // than Cost health to start. The gauge is the dead player's ReviveCharge (shown on the body prompt).
    public sealed class PlayerReviver : NetworkBehaviour
    {
        public const int Cost = 30;
        public const float HoldSeconds = 2.5f;

        // Local player is aiming at a body it could revive: E means "revive", not "radio".
        public static bool LocalLookingAtBody { get; private set; }

        // End-to-end tests: hold E without a keyboard (the body comes from PlayerInteractor.AutomationTarget).
        public bool AutomationHold { get; set; }

        private PlayerInteractor interactor;
        private PlayerHealth health;
        private float sendAccumulated;

        private void Awake()
        {
            interactor = GetComponent<PlayerInteractor>();
            health = GetComponent<PlayerHealth>();
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner) return;
            var body = interactor != null ? (interactor.AutomationTarget ?? interactor.Target) as Corpse : null;
            var dead = body != null ? body.Player : null;
            LocalLookingAtBody = dead != null && dead.IsDead.Value && !health.IsDead.Value;
            var k = Keyboard.current;
            var holding = LocalLookingAtBody && (AutomationHold || (FirstPersonController.GameplayInputReady && k != null && k.eKey.isPressed));
            if (!holding || health.Health.Value <= Cost) { sendAccumulated = 0f; return; }
            sendAccumulated += Time.deltaTime;
            if (sendAccumulated < 0.1f) return;
            ReviveTickRpc(body.NetworkObject, sendAccumulated);
            sendAccumulated = 0f;
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner) LocalLookingAtBody = false;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void ReviveTickRpc(NetworkObjectReference bodyRef, float seconds)
        {
            if (health.IsDead.Value || health.Health.Value <= Cost) return;
            if (!bodyRef.TryGet(out var obj) || !obj.TryGetComponent<Corpse>(out var body)) return;
            var dead = body.Player;
            if (dead == null || !dead.IsDead.Value || !body.ServerInRange(interactor)) return;
            var add = Mathf.CeilToInt(Mathf.Clamp(seconds, 0f, 0.5f) / HoldSeconds * PlayerHealth.ReviveThreshold);
            if (dead.ReviveCharge.Value + add >= PlayerHealth.ReviveThreshold)
            {
                health.ServerDrain(Cost); // the energy you gave
                body.ServerReviveFx();
            }
            dead.ServerAddReviveCharge(add);
        }
    }
}
