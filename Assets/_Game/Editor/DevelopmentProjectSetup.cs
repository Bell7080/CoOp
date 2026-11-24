using System;
using System.IO;
using CoOpHorror.Networking;
using CoOpHorror.UI;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace CoOpHorror.Editor
{
    public static class DevelopmentProjectSetup
    {
        public const string ScenePath = "Assets/_Game/Scenes/NetworkSandbox.unity";
        private const string PrefabPath = "Assets/_Game/Prefabs/ConnectionAvatar.prefab";
        private static Font font;

        [MenuItem("CoOp/Setup/Create network development scene")]
        public static void Create()
        {
            if (File.Exists(ScenePath)) throw new InvalidOperationException("Development scene already exists. Open it instead of replacing it.");
            Directory.CreateDirectory("Assets/_Game/Scenes");
            Directory.CreateDirectory("Assets/_Game/Prefabs");
            Directory.CreateDirectory("Assets/_Game/Materials");
            AssetDatabase.Refresh();
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", new Color(0.36f, 0.8f, 0.65f));
            AssetDatabase.CreateAsset(material, "Assets/_Game/Materials/DeveloperAvatar.mat");
            var avatar = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            avatar.name = "ConnectionAvatar";
            avatar.AddComponent<NetworkObject>();
            var transformSync = avatar.AddComponent<NetworkTransform>();
            transformSync.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            transformSync.SyncScaleX = transformSync.SyncScaleY = transformSync.SyncScaleZ = false;
            transformSync.SyncRotAngleX = transformSync.SyncRotAngleZ = false;
            avatar.AddComponent<ConnectionAvatar>();
            avatar.AddComponent<SandboxAvatarMotor>();
            avatar.GetComponent<Renderer>().sharedMaterial = material;
            var playerPrefab = PrefabUtility.SaveAsPrefabAsset(avatar, PrefabPath);
            UnityEngine.Object.DestroyImmediate(avatar);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "DevelopmentFloor";
            floor.transform.position = new Vector3(0, -0.25f, 0);
            floor.transform.localScale = new Vector3(22, 0.5f, 22);
            var floorMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            floorMat.SetColor("_BaseColor", new Color(0.055f, 0.07f, 0.08f));
            AssetDatabase.CreateAsset(floorMat, "Assets/_Game/Materials/DevelopmentFloor.mat");
            floor.GetComponent<Renderer>().sharedMaterial = floorMat;
            var light = new GameObject("DevelopmentLight").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.4f;
            light.transform.rotation = Quaternion.Euler(55, -35, 0);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.22f, 0.26f, 0.3f);
            var camera = new GameObject("DevelopmentCamera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(1, 18, -18);
            camera.transform.LookAt(Vector3.zero);
            camera.backgroundColor = new Color(0.015f, 0.025f, 0.035f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.gameObject.AddComponent<AudioListener>();

            var networkRoot = new GameObject("Networking");
            var transport = networkRoot.AddComponent<UnityTransport>();
            var manager = networkRoot.AddComponent<NetworkManager>();
            manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                PlayerPrefab = playerPrefab,
                TickRate = RoomLimits.TickRate,
                ProtocolVersion = RoomLimits.ProtocolVersion,
                ConnectionApproval = true,
                ForceSamePrefabs = true,
                EnableSceneManagement = true
            };
            networkRoot.AddComponent<RoomController>();
            CreatePanel();
            var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            PlayerSettings.productName = "CoOpHorror";
            PlayerSettings.companyName = "CoOp";
            PlayerSettings.bundleVersion = ProjectVersionSync.Current;
            PlayerSettings.runInBackground = true;
            EditorSettings.serializationMode = SerializationMode.ForceText;
            UnityEditor.VersionControlSettings.mode = "Visible Meta Files";
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
        }

        private static void CreatePanel()
        {
            var canvas = new GameObject("DevelopmentUI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1440, 900);
            scaler.matchWidthOrHeight = 0.5f;
            var panel = new GameObject("RoomPanel", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(DevelopmentRoomPanel));
            panel.transform.SetParent(canvas.transform, false);
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(24, -24);
            rect.sizeDelta = new Vector2(470, 660);
            panel.GetComponent<UnityEngine.UI.Image>().color = new Color(0.02f, 0.035f, 0.05f, 0.96f);
            var ui = panel.GetComponent<DevelopmentRoomPanel>();
            Label(panel.transform, "COOP / NETWORK SANDBOX", 22, 22, 25);
            Label(panel.transform, "WASD: move | E: send sync signal", 22, 58, 16);
            ui.HostButton = Button(panel.transform, "CREATE INTERNET ROOM", 22, 100, 426);
            ui.CodeInput = Input(panel.transform, 22, 160, 268);
            ui.JoinButton = Button(panel.transform, "JOIN", 304, 160, 144);
            ui.RoomText = Label(panel.transform, "ROOM CODE  ---", 22, 228, 28);
            ui.CopyButton = Button(panel.transform, "COPY CODE", 22, 280, 204);
            ui.LeaveButton = Button(panel.transform, "LEAVE ROOM", 240, 280, 208);
            ui.StatusText = Label(panel.transform, "Ready", 22, 346, 18, 78);
            ui.PlayersText = Label(panel.transform, "PLAYERS 0/6", 22, 430, 18, 140);
            ui.SignalButton = Button(panel.transform, "SEND SYNC SIGNAL", 22, 590, 426);
        }

        private static RectTransform Place(GameObject go, Transform parent, float x, float y, float width, float height)
        {
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        private static Text Label(Transform parent, string value, float x, float y, int size, float height = 42)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            Place(go, parent, x, y, 426, height);
            var text = go.GetComponent<Text>();
            text.font = font;
            text.text = value;
            text.fontSize = size;
            text.color = new Color(0.88f, 0.94f, 0.95f);
            text.raycastTarget = false;
            return text;
        }

        private static Button Button(Transform parent, string value, float x, float y, float width)
        {
            var go = new GameObject(value, typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
            Place(go, parent, x, y, width, 46);
            go.GetComponent<UnityEngine.UI.Image>().color = new Color(0.09f, 0.28f, 0.29f);
            var label = Label(go.transform, value, 4, 0, 17, 46);
            label.rectTransform.sizeDelta = new Vector2(width - 8, 46);
            label.alignment = TextAnchor.MiddleCenter;
            return go.GetComponent<UnityEngine.UI.Button>();
        }

        private static InputField Input(Transform parent, float x, float y, float width)
        {
            var go = new GameObject("RoomCodeInput", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(InputField));
            Place(go, parent, x, y, width, 46);
            go.GetComponent<UnityEngine.UI.Image>().color = new Color(0.11f, 0.14f, 0.17f);
            var input = go.GetComponent<InputField>();
            var text = Label(go.transform, "", 10, 0, 22, 46);
            text.rectTransform.sizeDelta = new Vector2(width - 20, 46);
            text.alignment = TextAnchor.MiddleLeft;
            input.textComponent = text;
            var placeholder = Label(go.transform, "ROOM CODE", 10, 0, 18, 46);
            placeholder.rectTransform.sizeDelta = new Vector2(width - 20, 46);
            placeholder.alignment = TextAnchor.MiddleLeft;
            placeholder.color = new Color(0.55f, 0.65f, 0.68f);
            input.placeholder = placeholder;
            input.characterLimit = 12;
            input.contentType = InputField.ContentType.Alphanumeric;
            return input;
        }
    }
}
