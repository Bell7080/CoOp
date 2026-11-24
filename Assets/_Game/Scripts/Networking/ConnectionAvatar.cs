using Unity.Netcode;
using UnityEngine;

namespace CoOpHorror.Networking
{
    // Server-confirmed signal state for the multiplayer development room.
    public sealed class ConnectionAvatar : NetworkBehaviour
    {
        public NetworkVariable<int> ConfirmedSignals = new(0);
        private float nextSignal;

        public void SendSignal()
        {
            if (IsSpawned && IsOwner) SignalRpc();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SignalRpc()
        {
            if (Time.unscaledTime < nextSignal) return;
            nextSignal = Time.unscaledTime + 0.25f;
            ConfirmedSignals.Value++;
        }
    }
}
