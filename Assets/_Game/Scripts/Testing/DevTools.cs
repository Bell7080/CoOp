#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using CoOpHorror.Gameplay;
using CoOpHorror.Localization;
using CoOpHorror.Networking;
using CoOpHorror.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CoOpHorror.Testing
{
    // In-game debug tools (Editor and development builds only). F1 opens the panel.
    // "Player 2" restarts Play with the DeveloperHostClient scenario (second Editor process) and both sides
    // connect on their own: the main Editor creates a room and writes its code to a small file in
    // persistentDataPath, the clone reads it and joins. Normal Play starts the main Editor alone.
    public sealed class DevTools : MonoBehaviour
    {
        public const string AutoJoinFile = "coop-dev-autojoin.txt";
        public const float AutoJoinMaxAge = 180f; // seconds; older requests are ignored

        // Set by the Editor bridge (CoOp.Editor.DevToolsEditorBridge); null in builds.
        public static Action RequestPlayer2;

        private Canvas canvas;
        private Text status;
        private bool open;

        private static string AutoJoinPath => Path.Combine(Application.persistentDataPath, AutoJoinFile);

        public static void WriteAutoJoin(string code)
        {
            try { File.WriteAllText(AutoJoinPath, DateTime.UtcNow.Ticks + "|" + code); } catch (Exception) { }
        }

        private static bool TryReadAutoJoin(out string code)
        {
            code = null;
            try
            {
                if (!File.Exists(AutoJoinPath)) return false;
                var parts = File.ReadAllText(AutoJoinPath).Split('|');
                if (parts.Length < 2 || !long.TryParse(parts[0], out var ticks)) return false;
                if ((DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalSeconds > AutoJoinMaxAge) return false;
                code = parts[1];
                return true;
            }
            catch (Exception) { return false; }
        }

        private static void ClearAutoJoin()
        {
            try { if (File.Exists(AutoJoinPath)) File.Delete(AutoJoinPath); } catch (Exception) { }
        }

        private void Start()
        {
            BuildUi();
#if UNITY_EDITOR
            if (TryReadAutoJoin(out _)) StartCoroutine(AutoConnect());
#endif
        }

        // Debug-tool sessions only: host on the main Editor, join on the clone.
        private IEnumerator AutoConnect()
        {
            var room = RoomController.Instance;
            while (room == null) { yield return null; room = RoomController.Instance; }
            yield return new WaitForSeconds(1f);
            if (Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor)
            {
                if (!room.Connected && !room.Busy) _ = room.HostAsync();
                var until = Time.realtimeSinceStartup + 60f;
                while (string.IsNullOrEmpty(room.JoinCode) && Time.realtimeSinceStartup < until) yield return null;
                if (!string.IsNullOrEmpty(room.JoinCode)) WriteAutoJoin(room.JoinCode);
                yield break;
            }
            var deadline = Time.realtimeSinceStartup + 90f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (TryReadAutoJoin(out var code) && code.Length >= 4 && code != "host")
                {
                    if (!room.Connected && !room.Busy) _ = room.JoinAsync(code);
                    var joinUntil = Time.realtimeSinceStartup + 30f;
                    while (!room.Connected && Time.realtimeSinceStartup < joinUntil) yield return null;
                    if (room.Connected) ClearAutoJoin();
                    yield break;
                }
                yield return new WaitForSeconds(0.5f);
            }
        }

        private void Update()
        {
            var k = Keyboard.current;
            if (k != null && k.f1Key.wasPressedThisFrame) SetOpen(!open);
            if (open && status != null) status.text = Status();
        }

        private void SetOpen(bool value)
        {
            open = value;
            canvas.enabled = open;
            if (PlayerInteractor.Local != null) FirstPersonController.SetCursorCaptured(!open);
        }

        private static string Status()
        {
            var nm = NetworkManager.Singleton;
            var room = RoomController.Instance;
            var role = nm == null || !nm.IsListening ? GameText.Get("dev.offline") : nm.IsHost ? GameText.Get("dev.host") : GameText.Get("dev.client");
            return GameText.Format("dev.status", role, room != null ? room.PlayerCount : 0, TempMonster.Frozen ? GameText.Get("dev.on") : GameText.Get("dev.off"));
        }

        private static bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

        private void Player2()
        {
            if (RequestPlayer2 == null) { status.text = GameText.Get("dev.editorOnly"); return; }
            WriteAutoJoin("host");
            RequestPlayer2();
        }

        private static void FreezeMonsters()
        {
            if (IsHost) TempMonster.Frozen = !TempMonster.Frozen;
        }

        // Quick restart (host): the stage starts over in the same room, without reconnecting. Every object
        // placed in the scene is despawned and spawned again (each resets itself in OnNetworkSpawn), bodies are
        // removed, and every player is put back at a spawn slot alive, full health, with an empty tank.
        private static void QuickRestart()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return;
            foreach (var body in new System.Collections.Generic.List<Corpse>(Corpse.Bodies))
                if (body != null && body.IsSpawned) body.NetworkObject.Despawn(true);
            var sceneObjects = new System.Collections.Generic.List<NetworkObject>();
            foreach (var obj in nm.SpawnManager.SpawnedObjectsList)
                if (obj != null && obj.IsSceneObject == true) sceneObjects.Add(obj);
            foreach (var p in PlayerHealth.All)
                if (p != null && p.IsSpawned && p.TryGetComponent<PlayerInteractor>(out var who)) who.ServerLetGoOfEverything();
            foreach (var obj in sceneObjects) obj.Despawn(false);
            foreach (var obj in sceneObjects) obj.Spawn();
            foreach (var p in PlayerHealth.All)
                if (p != null && p.IsSpawned) p.ServerResetForRestart(RoomController.PickRespawnPosition(p.transform));
            Debug.Log($"[ElecMole] Quick restart: respawned {sceneObjects.Count} scene objects");
        }

        private static void ChargeEverything()
        {
            if (!IsHost) return;
            foreach (var nose in UnityEngine.Object.FindObjectsByType<PlayerNose>(FindObjectsSortMode.None))
                if (nose.IsSpawned) nose.ServerAdd(PlayerNose.GaugeWatts);
        }

        // Later stages drain faster: raise the stage number to try it.
        private static void StageUp()
        {
            var stage = StageDirector.Instance;
            if (IsHost && stage != null && stage.IsSpawned) stage.Level.Value++;
        }

        private static void GoTo(Vector3 position)
        {
            var motor = FirstPersonController.Local;
            if (motor != null) motor.TeleportTo(position + Vector3.up * 1.05f, motor.transform.eulerAngles.y);
        }

        private static void GoToTransmitter()
        {
            var tx = Transmitter.Instance;
            if (tx != null) GoTo(tx.transform.position + tx.transform.forward * 2.5f);
        }

        private static void GoToShip()
        {
            var stage = StageDirector.Instance;
            if (stage != null) GoTo((stage.ShipMin + stage.ShipMax) * 0.5f);
        }

        // ---------- UI ----------

        private void BuildUi()
        {
            var go = new GameObject("DevToolsCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1440, 900);
            scaler.matchWidthOrHeight = 0.5f;

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            var rect = panel.GetComponent<RectTransform>();
            rect.SetParent(canvas.transform, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-20f, -20f);
            rect.sizeDelta = new Vector2(330f, 420f);
            panel.GetComponent<Image>().color = new Color(0.02f, 0.05f, 0.05f, 0.92f);
            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 6f;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;

            Label(rect, GameText.Get("dev.title"), 20, new Color(0.4f, 1f, 0.8f));
            status = Label(rect, string.Empty, 14, new Color(0.8f, 0.9f, 0.88f));
            Button(rect, GameText.Get("dev.player2"), Player2);
            Button(rect, GameText.Get("dev.freeze"), FreezeMonsters);
            Button(rect, GameText.Get("dev.restart"), QuickRestart);
            Button(rect, GameText.Get("dev.charge"), ChargeEverything);
            Button(rect, GameText.Get("dev.stageUp"), StageUp);
            Button(rect, GameText.Get("dev.toTransmitter"), GoToTransmitter);
            Button(rect, GameText.Get("dev.toShip"), GoToShip);
            Button(rect, GameText.Get("dev.close"), () => SetOpen(false));
            canvas.enabled = false;
        }

        private static Text Label(RectTransform parent, string value, int size, Color color)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().minHeight = size + 10;
            var text = go.GetComponent<Text>();
            text.font = GameText.UiFont;
            text.fontSize = size;
            text.color = color;
            text.text = value;
            text.raycastTarget = false;
            return text;
        }

        private static void Button(RectTransform parent, string label, Action action)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().minHeight = 36f;
            go.GetComponent<Image>().color = new Color(0.1f, 0.25f, 0.24f, 1f);
            go.GetComponent<Button>().onClick.AddListener(() => action());
            var text = Label(go.GetComponent<RectTransform>(), label, 16, Color.white);
            var t = text.rectTransform;
            t.anchorMin = Vector2.zero;
            t.anchorMax = Vector2.one;
            t.offsetMin = new Vector2(10f, 0f);
            t.offsetMax = Vector2.zero;
            text.alignment = TextAnchor.MiddleLeft;
            Destroy(text.GetComponent<LayoutElement>());
        }
    }
}
#endif
