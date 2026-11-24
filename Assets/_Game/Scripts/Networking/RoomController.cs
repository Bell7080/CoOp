using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

namespace CoOpHorror.Networking
{
    [RequireComponent(typeof(NetworkManager))]
    public sealed class RoomController : MonoBehaviour
    {
        public static RoomController Instance { get; private set; }
        public event Action Changed;
        public string Status { get; private set; } = "Create a room or enter your friend's room code.";
        public bool Busy { get; private set; }
        public string JoinCode => gateway?.JoinCode ?? string.Empty;
        public bool Connected => manager != null && manager.IsConnectedClient;
        public bool IsHost => manager != null && manager.IsHost;
        public int PlayerCount => FindObjectsByType<ConnectionAvatar>(FindObjectsSortMode.None).Length;
        private NetworkManager manager;
        private IRoomGateway gateway;
        private readonly Dictionary<ulong, int> slots = new();
        private bool leaving;

        private void Awake()
        {
            Instance = this;
            Application.runInBackground = true;
            manager = GetComponent<NetworkManager>();
            gateway = new RelayRoomGateway();
            manager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(Application.version);
            manager.ConnectionApprovalCallback = Approve;
            manager.OnClientConnectedCallback += OnConnected;
            manager.OnClientDisconnectCallback += OnDisconnected;
        }

        private void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            var sameBuild = Encoding.UTF8.GetString(request.Payload ?? Array.Empty<byte>()) == Application.version;
            var slot = -1;
            for (var i = 0; i < RoomLimits.MaxPlayers; i++)
                if (!slots.ContainsValue(i)) { slot = i; break; }
            response.Approved = sameBuild && slot >= 0;
            response.CreatePlayerObject = response.Approved;
            response.Pending = false;
            response.Reason = !sameBuild ? "Different game build. Both developers must use the same Git commit/build." : "Room is full (6 players).";
            if (!response.Approved) return;
            slots[request.ClientNetworkId] = slot;
            response.Position = new Vector3((slot % 3 - 1) * 3f, 1.05f, (slot / 3) * 3f);
            response.Rotation = Quaternion.identity;
        }

        public Task HostAsync() => RunAsync(async () =>
        {
            Status = "Creating internet room...";
            slots.Clear();
            await gateway.HostAsync();
            Status = "Hosting on this PC. Share the room code with your friend.";
        });

        public Task JoinAsync(string code) => RunAsync(async () =>
        {
            Status = "Joining internet room...";
            await gateway.JoinAsync(code);
            Status = "Connected to your friend's PC through Relay.";
        });

        public Task LeaveAsync() => RunAsync(async () =>
        {
            leaving = true;
            try { await gateway.LeaveAsync(); }
            finally
            {
                if (manager.IsListening) manager.Shutdown();
                slots.Clear();
                leaving = false;
                Status = "Disconnected. Create or join another room.";
            }
        });

        private async Task RunAsync(Func<Task> operation)
        {
            if (Busy) throw new InvalidOperationException("A room operation is already in progress.");
            Busy = true;
            Changed?.Invoke();
            try { await operation(); }
            catch (Exception error)
            {
                Status = error.Message;
                if (!gateway.HasRoom && manager.IsListening) manager.Shutdown();
                throw;
            }
            finally { Busy = false; Changed?.Invoke(); }
        }

        private void OnConnected(ulong id) => Changed?.Invoke();

        private async void OnDisconnected(ulong id)
        {
            slots.Remove(id);
            if (id == manager.LocalClientId && !leaving && !Busy)
            {
                try { await LeaveAsync(); }
                catch (Exception error) { Status = "Disconnected: " + error.Message; }
                Status = "Connection closed. The host may have left. Create or join again.";
            }
            Changed?.Invoke();
        }

        private void OnDestroy()
        {
            if (manager != null)
            {
                manager.OnClientConnectedCallback -= OnConnected;
                manager.OnClientDisconnectCallback -= OnDisconnected;
                manager.ConnectionApprovalCallback = null;
            }
            if (Instance == this) Instance = null;
        }
    }
}
