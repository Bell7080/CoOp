using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using CoOpHorror.Localization;
using Unity.Netcode;
using UnityEngine;

namespace CoOpHorror.Networking
{
    [RequireComponent(typeof(NetworkManager))]
    public sealed class RoomController : MonoBehaviour
    {
        public static RoomController Instance { get; private set; }
        public event Action Changed;
        public string Status { get; private set; } = GameText.Get("status.idle");
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
            manager.NetworkConfig.ProtocolVersion = RoomLimits.ProtocolVersion;
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
            response.Reason = !sameBuild ? GameText.Get("error.differentBuild") : GameText.Format("error.roomFull", RoomLimits.MaxPlayers);
            if (!response.Approved) return;
            slots[request.ClientNetworkId] = slot;
            response.Position = SlotPosition(slot);
            response.Rotation = Quaternion.Euler(0f, spawnYaw, 0f);
        }

        // Spawn area: the sandbox arena centre by default; a scene SpawnArea (ElecMole: inside the ship) overrides it.
        private static readonly Vector3 DefaultSpawnOrigin = new(0f, 1.05f, 0f);
        private const float DefaultSpawnSpacing = 3f;
        private static Vector3 spawnOrigin = DefaultSpawnOrigin;
        private static float spawnSpacing = DefaultSpawnSpacing;
        private static float spawnYaw;

        public static void SetSpawnArea(Vector3 origin, float yaw, float spacing)
        {
            spawnOrigin = origin;
            spawnYaw = yaw;
            spawnSpacing = spacing;
        }

        public static void ResetSpawnArea() => SetSpawnArea(DefaultSpawnOrigin, 0f, DefaultSpawnSpacing);

        public static Vector3 SlotPosition(int slot) =>
            spawnOrigin + Quaternion.Euler(0f, spawnYaw, 0f) * new Vector3((slot % 3 - 1) * spawnSpacing, 0f, (slot / 3) * spawnSpacing);

        // Server: spawn slot farthest from every other player.
        public static Vector3 PickRespawnPosition(Transform respawning)
        {
            var others = FindObjectsByType<ConnectionAvatar>(FindObjectsSortMode.None);
            var best = SlotPosition(0);
            var bestScore = float.MinValue;
            for (var i = 0; i < RoomLimits.MaxPlayers; i++)
            {
                var candidate = SlotPosition(i);
                var nearest = float.MaxValue;
                foreach (var other in others)
                    if (other.transform != respawning) nearest = Mathf.Min(nearest, (other.transform.position - candidate).sqrMagnitude);
                if (nearest > bestScore) { bestScore = nearest; best = candidate; }
            }
            return best;
        }

        public Task HostAsync() => RunAsync(async () =>
        {
            Status = GameText.Get("status.creating");
            slots.Clear();
            await gateway.HostAsync();
            Status = GameText.Get("status.hosting");
        });

        public Task JoinAsync(string code) => RunAsync(async () =>
        {
            Status = GameText.Get("status.joining");
            await gateway.JoinAsync(code);
            Status = GameText.Get("status.joined");
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
                Status = GameText.Get("status.left");
            }
        });

        private async Task RunAsync(Func<Task> operation)
        {
            if (Busy) throw new InvalidOperationException(GameText.Get("error.busy"));
            Busy = true;
            Changed?.Invoke();
            try { await operation(); }
            catch (Exception error)
            {
                Status = GameText.Format("status.error", error.Message);
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
                catch (Exception error) { Status = GameText.Format("status.disconnectError", error.Message); }
                Status = GameText.Get("status.closed");
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
