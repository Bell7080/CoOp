using System;
using System.Linq;
using System.Threading.Tasks;
using CoOpHorror.Localization;
using CoOpHorror.Networking;
using CoOpHorror.Player;
using UnityEngine;
using UnityEngine.UI;

namespace CoOpHorror.UI
{
    public sealed class DevelopmentRoomPanel : MonoBehaviour
    {
        public Text TitleText;
        public Text HintText;
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
        private CanvasGroup group;
        private float nextRefresh;

        private void Start()
        {
            room = RoomController.Instance;
            group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
            ApplyStaticText();
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

        private void ApplyStaticText()
        {
            if (TitleText == null && transform.childCount > 0) TitleText = transform.GetChild(0).GetComponent<Text>();
            if (HintText == null && transform.childCount > 1) HintText = transform.GetChild(1).GetComponent<Text>();
            foreach (var text in GetComponentsInChildren<Text>(true)) text.font = GameText.UiFont;
            if (TitleText != null) TitleText.text = GameText.Get("room.title");
            if (HintText != null) { HintText.text = GameText.Get("room.hint"); HintText.fontSize = 14; }
            SetLabel(HostButton, "room.host");
            SetLabel(JoinButton, "room.join");
            SetLabel(CopyButton, "room.copy");
            SetLabel(LeaveButton, "room.leave");
            SetLabel(SignalButton, "room.signal");
            if (CodeInput != null && CodeInput.placeholder is Text placeholder) placeholder.text = GameText.Get("room.codePlaceholder");
        }

        private static void SetLabel(Button button, string key)
        {
            var label = button != null ? button.GetComponentInChildren<Text>(true) : null;
            if (label != null) label.text = GameText.Get(key);
        }

        private async void Execute(Func<Task> operation)
        {
            try { await operation(); }
            catch (Exception) { /* RoomController exposes the actionable error in Status. */ }
            Refresh();
        }

        private void Update()
        {
            // While playing with a captured cursor the panel stays out of the way; Esc brings it back.
            var hidden = room != null && room.Connected && FirstPersonController.CursorCaptured;
            group.alpha = hidden ? 0f : 1f;
            group.blocksRaycasts = group.interactable = !hidden;
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.2f;
            Refresh();
        }

        private void Refresh()
        {
            if (room == null) return;
            StatusText.text = room.Status;
            RoomText.text = string.IsNullOrEmpty(room.JoinCode) ? GameText.Get("room.codeEmpty") : GameText.Format("room.codeLabel", room.JoinCode);
            var avatars = FindObjectsByType<ConnectionAvatar>(FindObjectsSortMode.None).OrderBy(x => x.OwnerClientId).ToArray();
            var role = GameText.Get(room.IsHost ? "room.roleHost" : "room.roleClient");
            PlayersText.text = GameText.Format("room.summary", Application.version, role, avatars.Length, RoomLimits.MaxPlayers) + "\n" +
                string.Join("\n", avatars.Select(x =>
                {
                    var health = x.GetComponent<PlayerHealth>();
                    return GameText.Format("room.playerLine", x.OwnerClientId, health != null ? health.Health.Value : 0, x.ConfirmedSignals.Value);
                }));
            HostButton.interactable = JoinButton.interactable = !room.Busy && !room.Connected;
            LeaveButton.interactable = !room.Busy && room.Connected;
            SignalButton.interactable = room.Connected && !room.Busy;
            CopyButton.interactable = !string.IsNullOrEmpty(room.JoinCode);
            CodeInput.interactable = !room.Busy && !room.Connected;
        }
    }
}
