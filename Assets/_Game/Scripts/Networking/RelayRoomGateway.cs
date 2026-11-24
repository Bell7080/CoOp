using System;
using System.Threading.Tasks;
using CoOpHorror.Localization;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;

namespace CoOpHorror.Networking
{
    public sealed class RelayRoomGateway : IRoomGateway
    {
        private ISession session;
        public string JoinCode => session?.Code ?? string.Empty;
        public bool HasRoom => session != null;

        private static async Task InitializeAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                var options = new InitializationOptions();
#if UNITY_EDITOR
                // Editor and virtual players can run concurrently under one OS account.
                options.SetProfile("dev_" + System.Diagnostics.Process.GetCurrentProcess().Id);
#endif
                await UnityServices.InitializeAsync(options);
            }
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        public async Task HostAsync()
        {
            if (HasRoom) throw new InvalidOperationException(GameText.Get("error.leaveFirst"));
            await InitializeAsync();
            var options = new SessionOptions
            {
                Name = "CoOpHorror Dev",
                Type = "coop-development-v1",
                MaxPlayers = RoomLimits.MaxPlayers,
                IsPrivate = true
            }.WithRelayNetwork();
            // The MPS NGO handler starts the host/client. Never also call StartHost here.
            session = await MultiplayerService.Instance.CreateSessionAsync(options);
        }

        public async Task JoinAsync(string code)
        {
            if (HasRoom) throw new InvalidOperationException(GameText.Get("error.leaveFirst"));
            if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException(GameText.Get("error.enterCode"));
            await InitializeAsync();
            session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code.Trim().ToUpperInvariant());
        }

        public async Task LeaveAsync()
        {
            var previous = session;
            session = null;
            if (previous == null) return;
            // Explicit host exit closes the room; there is no automatic host migration.
            if (previous.IsHost && previous is IHostSession host) await host.DeleteAsync();
            else await previous.LeaveAsync();
        }
    }
}
