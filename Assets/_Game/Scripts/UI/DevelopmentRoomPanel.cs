using System;
using System.Linq;
using System.Threading.Tasks;
using CoOpHorror.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CoOpHorror.UI
{
    public sealed class DevelopmentRoomPanel : MonoBehaviour
    {
        public Text StatusText;
        public Text RoomText;
        public Text PlayersText;
        public InputField CodeInput;
        public Button HostButton;
        public Button JoinButton;
        public Button LeaveButton;
        public Button CopyButton;
        public Button SignalButton;
        private RoomController room;
        private float nextRefresh;

        private void Start()
        {
            room = RoomController.Instance;
            HostButton.onClick.AddListener(() => Execute(() => room.HostAsync()));
            JoinButton.onClick.AddListener(() => Execute(() => room.JoinAsync(CodeInput.text)));
            LeaveButton.onClick.AddListener(() => Execute(() => room.LeaveAsync()));
            CopyButton.onClick.AddListener(() => GUIUtility.systemCopyBuffer = room.JoinCode);
            SignalButton.onClick.AddListener(() =>
            {
                var avatar = FindObjectsByType<ConnectionAvatar>(FindObjectsSortMode.None).FirstOrDefault(x => x.IsOwner);
                if (avatar != null) avatar.SendSignal();
            });
            Refresh();
        }

        private async void Execute(Func<Task> operation)
        {
            try { await operation(); }
            catch (Exception) { /* RoomController exposes the actionable error in Status. */ }
            Refresh();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.2f;
            Refresh();
        }

        private void Refresh()
        {
            if (room == null) return;
            StatusText.text = room.Status;
            RoomText.text = string.IsNullOrEmpty(room.JoinCode) ? "ROOM CODE  ---" : "ROOM CODE  " + room.JoinCode;
            var avatars = FindObjectsByType<ConnectionAvatar>(FindObjectsSortMode.None).OrderBy(x => x.OwnerClientId).ToArray();
            PlayersText.text = $"v{Application.version} | {(room.IsHost ? "HOST PC" : "CLIENT")} | PLAYERS {avatars.Length}/{RoomLimits.MaxPlayers}\n" +
                string.Join("\n", avatars.Select(x => $"Player {x.OwnerClientId}   signals: {x.ConfirmedSignals.Value}"));
            HostButton.interactable = JoinButton.interactable = !room.Busy && !room.Connected;
            LeaveButton.interactable = !room.Busy && room.Connected;
            SignalButton.interactable = room.Connected && !room.Busy;
            CopyButton.interactable = !string.IsNullOrEmpty(room.JoinCode);
            CodeInput.interactable = !room.Busy && !room.Connected;
        }
    }
}
