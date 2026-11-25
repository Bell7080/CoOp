using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CoOpHorror.Gameplay;
using CoOpHorror.Networking;
using CoOpHorror.Player;
using CoOpHorror.Testing;
using CoOpHorror.UI;
using Unity.AI.Navigation;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CoOpHorror.Editor
{
    // ElecMole stage prototype builder (main flow, prefabs and gameplay objects). The level layout and
    // props are in ElecMoleMap.cs, the creatures in ElecMoleCreatures.cs, the player model in
    // ElecMolePlayerModel.cs. Re-runnable: rebuilds every "Proto_" root of the prototype scene in place.
    // Run from the Editor menu (or Unity CLI script eval), never by editing scene YAML.
    public static partial class ElecMolePrototypeSetup
    {
        public const string ScenePath = "Assets/_Game/Scenes/ElecMolePrototype.unity";
        private const string NavMeshPath = "Assets/_Game/Scenes/ElecMolePrototype_NavMesh.asset";
        private const string PlayerPrefabPath = "Assets/_Game/Prefabs/ConnectionAvatar.prefab";
        private const string CorpsePrefabPath = "Assets/_Game/Prefabs/PlayerCorpse.prefab";
        private const string NetworkPrefabsPath = "Assets/DefaultNetworkPrefabs.asset";
        private const string MaterialDir = "Assets/_Game/Materials/Prototype";
        private const string Prefix = "Proto_";

        private static readonly Dictionary<string, Material> Materials = new();

        [MenuItem("CoOp/Setup/Build ElecMole prototype scene")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
            Materials.Clear();
            Directory.CreateDirectory(MaterialDir);
            AssetDatabase.Refresh();
            ElecMoleLookSetup.Apply();
            PrepareCharacters();
            var corpse = BuildCorpsePrefab();
            ConfigurePlayerPrefab(corpse);
            var prefabs = RegisterNetworkPrefab(corpse);
            PatchSandboxScene(prefabs);

            if (!File.Exists(ScenePath) && !AssetDatabase.CopyAsset(DevelopmentProjectSetup.ScenePath, ScenePath))
                throw new InvalidOperationException("Could not copy the sandbox scene.");
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == "DevelopmentFloor" || root.name.StartsWith(Prefix, StringComparison.Ordinal)) Object.DestroyImmediate(root);

            var sun = ConfigureLightingAndCamera();
            var map = BuildMap(out var emergencyLights);
            BakeNavMesh(map);
            BuildGameplay(sun, emergencyLights);
            Ensure<PrototypeHud>("PrototypeHud");
            Ensure<CctvView>("CctvView").FeedMaterial = CctvFeedMaterial();
            Ensure<DevTools>("DevTools");
            ConfigureNetworkManager(prefabs);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            // In-scene NetworkObjects need their GlobalObjectIdHash from the saved scene; regenerate and save again.
            var validate = typeof(NetworkObject).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            foreach (var no in Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                validate?.Invoke(no, null);
                EditorUtility.SetDirty(no);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true),
                new EditorBuildSettingsScene(DevelopmentProjectSetup.ScenePath, true),
            };
            AssetDatabase.SaveAssets();
            Debug.Log("[ElecMole] Prototype scene built: " + ScenePath);
        }

        private static T Ensure<T>(string name) where T : Component
        {
            var existing = Object.FindFirstObjectByType<T>();
            var component = existing != null ? existing : new GameObject(name).AddComponent<T>();
            EditorUtility.SetDirty(component);
            return component;
        }

        // Player prefab: the squishy mole alien, left-click interaction and telekinesis, flashlight,
        // inventory, radio, and the body it leaves when it dies.
        private static void ConfigurePlayerPrefab(GameObject corpse)
        {
            var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root); // removed PlayerCombat / PlayerCorpse
                if (root.GetComponent<PlayerInteractor>() == null) root.AddComponent<PlayerInteractor>();
                if (root.GetComponent<PlayerFlashlight>() == null) root.AddComponent<PlayerFlashlight>();
                if (root.GetComponent<PlayerInventory>() == null) root.AddComponent<PlayerInventory>();
                if (root.GetComponent<PlayerRadio>() == null) root.AddComponent<PlayerRadio>();
                root.GetComponent<PlayerHealth>().CorpsePrefab = corpse;
                BuildMoleModel(root);
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // The discharged body: a server-simulated physics object that can be hit (revive) or carried.
        private static GameObject BuildCorpsePrefab()
        {
            var root = new GameObject("PlayerCorpse");
            try
            {
                root.AddComponent<NetworkObject>();
                var body = root.AddComponent<Rigidbody>();
                body.mass = 25f;
                body.linearDamping = 0.3f;
                body.angularDamping = 1.5f;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                var capsule = root.AddComponent<CapsuleCollider>();
                capsule.direction = 2;
                capsule.height = 1.3f;
                capsule.radius = 0.38f;
                capsule.center = new Vector3(0f, 0.38f, 0f);
                AddPhysicsSync(root);
                var skin = Mat("CorpseSkin", new Color(0.2f, 0.21f, 0.23f));
                // A deflated, discharged mole lying on its side.
                var torso = Shape(root.transform, "Body", PrimitiveType.Sphere, new Vector3(0f, 0.34f, -0.05f), new Vector3(0f, 0f, 80f), new Vector3(0.75f, 0.95f, 1.15f), skin);
                var head = Shape(root.transform, "Snout", PrimitiveType.Sphere, new Vector3(0.05f, 0.3f, 0.62f), Vector3.zero, new Vector3(0.3f, 0.22f, 0.28f), skin);
                var corpse = root.AddComponent<Corpse>();
                corpse.Weight = 1;
                corpse.BodyRenderer = torso.GetComponent<Renderer>();
                corpse.HeadRenderer = head.GetComponent<Renderer>();
                Directory.CreateDirectory(Path.GetDirectoryName(CorpsePrefabPath));
                return PrefabUtility.SaveAsPrefabAsset(root, CorpsePrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static NetworkPrefabsList RegisterNetworkPrefab(GameObject prefab)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
            if (list == null) throw new InvalidOperationException("Missing " + NetworkPrefabsPath);
            if (!list.Contains(prefab))
            {
                list.Add(new NetworkPrefab { Prefab = prefab });
                EditorUtility.SetDirty(list);
                AssetDatabase.SaveAssets();
            }
            return list;
        }

        private static void ConfigureNetworkManager(NetworkPrefabsList prefabs)
        {
            var manager = Object.FindFirstObjectByType<NetworkManager>();
            if (manager == null) return;
            manager.NetworkConfig.ProtocolVersion = RoomLimits.ProtocolVersion;
            if (!manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Contains(prefabs)) manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(prefabs);
            EditorUtility.SetDirty(manager);
        }

        // The development sandbox spawns bodies too, so its NetworkManager needs the prefab list.
        private static void PatchSandboxScene(NetworkPrefabsList prefabs)
        {
            var sandbox = EditorSceneManager.OpenScene(DevelopmentProjectSetup.ScenePath, OpenSceneMode.Single);
            ConfigureNetworkManager(prefabs);
            EditorSceneManager.MarkSceneDirty(sandbox);
            EditorSceneManager.SaveScene(sandbox);
        }

        // Server-simulated rigidbody replicated by a server-authority NetworkTransform.
        private static void AddPhysicsSync(GameObject root)
        {
            var sync = root.AddComponent<NetworkTransform>();
            sync.AuthorityMode = NetworkTransform.AuthorityModes.Server;
            sync.SyncScaleX = sync.SyncScaleY = sync.SyncScaleZ = false;
            root.AddComponent<NetworkRigidbody>();
        }

        // Pitch dark: no sun, almost no ambient light and thick black fog. The flashlight and charged lamps
        // are what you see by.
        private static Light ConfigureLightingAndCamera()
        {
            Light light = null;
            var sun = GameObject.Find("DevelopmentLight");
            if (sun != null && sun.TryGetComponent(out light))
            {
                light.intensity = 0.02f;
                light.color = new Color(0.6f, 0.7f, 1f);
                light.shadows = LightShadows.None;
            }
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.06f, 0.062f, 0.072f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.04f;
            RenderSettings.fogColor = new Color(0.012f, 0.012f, 0.016f);
            var cam = GameObject.Find("DevelopmentCamera");
            if (cam != null)
            {
                cam.transform.position = new Vector3(0f, 60f, -48f);
                cam.transform.LookAt(new Vector3(0f, 0f, 0f));
                if (cam.TryGetComponent<Camera>(out var c)) c.farClipPlane = 140f;
            }
            return light;
        }

        private static void BakeNavMesh(Transform map)
        {
            var surface = map.gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
            AssetDatabase.DeleteAsset(NavMeshPath);
            AssetDatabase.CreateAsset(surface.navMeshData, NavMeshPath);
        }

        // ---------------- gameplay objects ----------------

        private static void BuildGameplay(Light sun, Light[] emergencyLights)
        {
            NetRoot("PowerGrid", Vector3.zero, 0f).AddComponent<PowerGrid>();
            BuildGenerator(Ship.Generator, 0f);
            var door = BuildPoweredDoor("ShipDoor", Ship.Door, 0f, 8f);
            BuildLaunchButton(Ship.Launch, 135f);
            BuildCctvPanel(Ship.Panel, 0f);
            BuildShipMic(Ship.Panel + new Vector3(0.55f, 0.9f, 0.15f));
            BuildTransmitter(TransmitterPosition, -135f);

            // name, text key, power, value, weight (holders to lift), tiny (inventory), cord length.
            BuildAppliance("TV", "appliance.tv", 40f, 400, 2, false, 6f, new Vector3(-30f, 0f, 6f), 90f, Mat("ApplianceDark", new Color(0.12f, 0.12f, 0.13f)),
                new Vector3(0f, 0.3f, 0f), new Vector3(0.75f, 0.55f, 0.45f), new Vector3(0f, 0.31f, 0.231f), new Vector3(0.6f, 0.42f, 0.02f));
            BuildAppliance("Microwave", "appliance.microwave", 30f, 300, 2, false, 6f, new Vector3(14.2f, 0.95f, 9.15f), 180f, Mat("ApplianceLight", new Color(0.7f, 0.7f, 0.68f)),
                new Vector3(0f, 0.17f, 0f), new Vector3(0.55f, 0.34f, 0.4f), new Vector3(-0.06f, 0.17f, 0.205f), new Vector3(0.32f, 0.22f, 0.02f));
            BuildAppliance("Radio", "appliance.radio", 20f, 150, 1, true, 5f, new Vector3(-22f, 0f, 25f), 180f, Mat("ApplianceWood", new Color(0.42f, 0.27f, 0.15f)),
                new Vector3(0f, 0.13f, 0f), new Vector3(0.45f, 0.26f, 0.15f), new Vector3(0f, 0.2f, 0.08f), new Vector3(0.35f, 0.06f, 0.02f));
            BuildFan(new Vector3(-4f, 0f, 7f), 180f);
            BuildAppliance("Toaster", "appliance.toaster", 15f, 120, 1, true, 4.5f, new Vector3(12.2f, 0.95f, 9.15f), 180f, Mat("ApplianceMetal", new Color(0.6f, 0.62f, 0.66f)),
                new Vector3(0f, 0.11f, 0f), new Vector3(0.3f, 0.22f, 0.18f), new Vector3(0f, 0.11f, 0.095f), new Vector3(0.18f, 0.08f, 0.01f));
            BuildAppliance("Kettle", "appliance.kettle", 15f, 160, 1, true, 4f, new Vector3(16.2f, 0.95f, 9.15f), 180f, Mat("ApplianceRed", new Color(0.55f, 0.12f, 0.1f)),
                new Vector3(0f, 0.14f, 0f), new Vector3(0.22f, 0.28f, 0.22f), new Vector3(0f, 0.2f, 0.115f), new Vector3(0.08f, 0.05f, 0.01f));
            BuildAppliance("RiceCooker", "appliance.ricecooker", 25f, 220, 1, false, 5f, new Vector3(12f, 0f, -22f), 0f, Mat("ApplianceLight", new Color(0.7f, 0.7f, 0.68f)),
                new Vector3(0f, 0.17f, 0f), new Vector3(0.38f, 0.34f, 0.38f), new Vector3(0f, 0.2f, 0.195f), new Vector3(0.16f, 0.06f, 0.01f));

            // CCTV cameras on room walls (root on the wall face, facing into the room) and ceiling lamps.
            foreach (var cam in Cameras) BuildCctvCamera(cam.Number, cam.Position, cam.Yaw, IntoRoom(cam.Position, cam.Yaw, cam.DefaultYaw));
            var lampIndex = 0;
            foreach (var room in Rooms)
                if (room.Kind != RoomKind.Ship) BuildLamp(++lampIndex, room.Center + new Vector3(room.LampOffset.x, 3.1f, room.LampOffset.y));

            BuildCreatures();

            var spawn = new GameObject(Prefix + "SpawnArea");
            spawn.transform.SetPositionAndRotation(Ship.Spawn, Quaternion.Euler(0f, 90f, 0f));
            spawn.AddComponent<SpawnArea>().Spacing = 2f;

            var director = NetRoot("StageDirector", Vector3.zero, 0f).AddComponent<StageDirector>();
            director.RoomLights = Array.Empty<Light>();
            director.EmergencyLights = emergencyLights;
            director.Sun = sun;
            director.ShipMin = Ship.Min;
            director.ShipMax = Ship.Max;
            director.ShipDoor = door;
        }

        private static void BuildGenerator(Vector3 position, float yaw)
        {
            var root = NetRoot("Generator", position, yaw);
            var body = Mat("Generator", new Color(0.45f, 0.3f, 0.12f), tex: Texture("Rust"), tiling: 1f);
            var dark = Mat("GeneratorDark", new Color(0.1f, 0.1f, 0.1f));
            Box(root.transform, "Base", new Vector3(0f, 0.6f, 0f), new Vector3(1.6f, 1.2f, 1.1f), body);
            Shape(root.transform, "Exhaust", PrimitiveType.Cylinder, new Vector3(-0.5f, 1.4f, 0.3f), Vector3.zero, new Vector3(0.15f, 0.4f, 0.15f), dark);
            Shape(root.transform, "Stripe", PrimitiveType.Cube, new Vector3(0f, 1.05f, 0.56f), Vector3.zero, new Vector3(1.5f, 0.12f, 0.01f), Mat("Hazard", Color.white, tex: Texture("Hazard"), tiling: 4f));
            var rotor = new GameObject("Rotor").transform;
            rotor.SetParent(root.transform, false);
            rotor.localPosition = new Vector3(0.86f, 0.7f, 0f);
            Shape(rotor, "Wheel", PrimitiveType.Cylinder, Vector3.zero, new Vector3(0f, 0f, 90f), new Vector3(0.8f, 0.05f, 0.8f), dark);
            Shape(rotor, "Handle", PrimitiveType.Cube, new Vector3(0.08f, 0.3f, 0f), Vector3.zero, new Vector3(0.08f, 0.22f, 0.08f), body);
            var lamp = PointLight(root.transform, "Lamp", new Vector3(0f, 1.9f, 0.4f), new Color(1f, 0.65f, 0.25f), 2.5f, 6f);
            Obstacle(root, new Vector3(0f, 0.6f, 0f), new Vector3(1.6f, 1.2f, 1.1f));
            var generator = root.AddComponent<Generator>();
            generator.Rotor = rotor;
            generator.Lamp = lamp;
        }

        // Door along the Z axis (the wall runs north-south); the panel slides up when powered.
        private static PoweredDoor BuildPoweredDoor(string name, Vector3 position, float yaw, float capacity)
        {
            var root = NetRoot(name, position, yaw);
            var panelMat = Mat("DoorPanel", new Color(0.42f, 0.44f, 0.42f), tex: Texture("Metal"), tiling: 1f);
            var lampMat = Mat("Indicator", Color.white, unlit: true);
            var panel = Box(root.transform, "Panel", new Vector3(0f, WallHeight / 2f, 0f), new Vector3(0.3f, WallHeight, DoorWidth), panelMat);
            Shape(panel.transform, "Stripe", PrimitiveType.Cube, new Vector3(0f, -0.3f, 0f), Vector3.zero, new Vector3(1.05f, 0.06f, 1f), Mat("Hazard", Color.white, tex: Texture("Hazard"), tiling: 4f));
            var obstacle = panel.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = Vector3.zero;
            obstacle.size = Vector3.one;
            obstacle.carving = true;
            var inside = Shape(root.transform, "IndicatorInside", PrimitiveType.Cube, new Vector3(-0.25f, 1.4f, DoorWidth / 2f + 0.25f), Vector3.zero, new Vector3(0.08f, 0.25f, 0.25f), lampMat);
            Shape(root.transform, "IndicatorOutside", PrimitiveType.Cube, new Vector3(0.25f, 1.4f, DoorWidth / 2f + 0.25f), Vector3.zero, new Vector3(0.08f, 0.25f, 0.25f), lampMat);
            var door = root.AddComponent<PoweredDoor>();
            door.Capacity = capacity;
            door.Panel = panel.transform;
            door.Indicator = inside.GetComponent<Renderer>();
            return door;
        }

        private static void BuildLaunchButton(Vector3 position, float yaw)
        {
            var root = NetRoot("LaunchButton", position, yaw);
            var console = Mat("Console", new Color(0.18f, 0.2f, 0.24f), tex: Texture("Metal"), tiling: 1f);
            Box(root.transform, "Console", new Vector3(0f, 0.5f, 0f), new Vector3(0.9f, 1f, 0.6f), console);
            Shape(root.transform, "Screen", PrimitiveType.Cube, new Vector3(0f, 1.35f, -0.2f), new Vector3(-20f, 0f, 0f), new Vector3(0.8f, 0.5f, 0.05f), Mat("Screen", new Color(0.05f, 0.25f, 0.2f), unlit: true));
            var lamp = Shape(root.transform, "Button", PrimitiveType.Cylinder, new Vector3(0f, 1.03f, 0.1f), Vector3.zero, new Vector3(0.3f, 0.04f, 0.3f), Mat("Indicator", Color.white, unlit: true));
            root.AddComponent<LaunchButton>().Lamp = lamp.GetComponent<Renderer>();
        }

        private static void BuildCctvPanel(Vector3 position, float yaw)
        {
            var root = NetRoot("CctvPanel", position, yaw);
            var console = Mat("Console", new Color(0.18f, 0.2f, 0.24f), tex: Texture("Metal"), tiling: 1f);
            var dark = Mat("GeneratorDark", new Color(0.1f, 0.1f, 0.1f));
            Box(root.transform, "Desk", new Vector3(0f, 0.45f, 0f), new Vector3(1.6f, 0.9f, 0.7f), console);
            Box(root.transform, "MonitorFrame", new Vector3(0f, 1.45f, -0.2f), new Vector3(1.3f, 1f, 0.12f), dark);
            Shape(root.transform, "Keyboard", PrimitiveType.Cube, new Vector3(0f, 0.92f, 0.1f), Vector3.zero, new Vector3(0.6f, 0.03f, 0.2f), dark);
            var screen = Shape(root.transform, "Screen", PrimitiveType.Quad, new Vector3(0f, 1.45f, -0.135f), new Vector3(0f, 180f, 0f), new Vector3(1.2f, 0.9f, 1f), CctvFeedMaterial());
            PointLight(root.transform, "ScreenGlow", new Vector3(0f, 1.5f, 0.5f), new Color(0.55f, 1f, 0.7f), 0.5f, 3.5f);
            root.AddComponent<CctvPanel>().Screen = screen.GetComponent<Renderer>();
        }

        // A wall-mounted camera with its own lamp, plus the salvage it drops when ripped off.
        // Turn the camera's resting angle towards the middle of its room, not along the nearest wall.
        private static float IntoRoom(Vector3 position, float yaw, float defaultYaw)
        {
            var mount = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            var probe = position + mount * 2f;
            var center = probe;
            foreach (var room in Rooms)
            {
                var c = room.Center;
                if (probe.x >= XCols[room.Col] && probe.x <= XCols[room.Col + 1] && probe.z >= ZRows[room.Row] && probe.z <= ZRows[room.Row + 1]) center = c;
            }
            var to = new Vector3(center.x - position.x, 0f, center.z - position.z);
            float Score(float d) => Vector3.Dot(Quaternion.Euler(0f, yaw + d, 0f) * Vector3.forward, to.normalized);
            var a = Mathf.Abs(defaultYaw);
            return Score(a) >= Score(-a) ? a : -a;
        }

        private static void BuildCctvCamera(int number, Vector3 position, float yaw, float defaultYaw)
        {
            var root = NetRoot($"Cctv_{number:00}", position, yaw);
            var housingMat = Mat("CctvHousing", new Color(0.55f, 0.56f, 0.52f));
            var dark = Mat("GeneratorDark", new Color(0.1f, 0.1f, 0.1f));
            Box(root.transform, "JunctionBox", new Vector3(0f, 1.3f, 0.1f), new Vector3(0.35f, 0.45f, 0.2f), housingMat);
            Shape(root.transform, "Conduit", PrimitiveType.Cube, new Vector3(0f, 2.4f, 0.04f), Vector3.zero, new Vector3(0.06f, 1.8f, 0.06f), dark);
            Shape(root.transform, "Bracket", PrimitiveType.Cube, new Vector3(0f, 3.3f, 0.15f), Vector3.zero, new Vector3(0.08f, 0.08f, 0.3f), dark);
            var head = new GameObject("Head").transform;
            head.SetParent(root.transform, false);
            head.localPosition = new Vector3(0f, 3.3f, 0.3f);
            Box(head, "Housing", new Vector3(0f, 0f, 0.15f), new Vector3(0.22f, 0.18f, 0.4f), housingMat);
            Shape(head, "LensGlass", PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.36f), new Vector3(90f, 0f, 0f), new Vector3(0.12f, 0.02f, 0.12f), dark);
            var led = Shape(head, "Led", PrimitiveType.Sphere, new Vector3(0.08f, 0.07f, 0.3f), Vector3.zero, Vector3.one * 0.04f, Mat("Indicator", Color.white, unlit: true));
            var lens = new GameObject("Lens").transform;
            lens.SetParent(head, false);
            lens.localPosition = new Vector3(0f, 0f, 0.4f);
            var spot = new GameObject("Lamp").AddComponent<Light>();
            spot.transform.SetParent(head, false);
            spot.transform.localPosition = new Vector3(0f, 0.12f, 0.35f);
            spot.type = LightType.Spot;
            spot.color = new Color(1f, 0.93f, 0.8f);
            spot.range = 35f;
            spot.spotAngle = 110f;
            spot.innerSpotAngle = 40f;
            spot.intensity = 11f;
            spot.shadows = LightShadows.None;
            spot.enabled = false;
            // Night-vision style fill so the whole room reads on the monitor, not just the lamp's circle.
            var fill = new GameObject("NightFill").AddComponent<Light>();
            fill.transform.SetParent(head, false);
            fill.transform.localPosition = new Vector3(0f, -0.5f, 7f);
            fill.type = LightType.Point;
            fill.color = new Color(0.8f, 0.95f, 0.85f);
            fill.range = 26f;
            fill.intensity = 4.5f;
            fill.shadows = LightShadows.None;
            fill.enabled = false;
            var cctv = root.AddComponent<CctvCamera>();
            cctv.NameKey = "cctv.cam" + number;
            cctv.Order = number;
            cctv.DefaultYaw = defaultYaw;
            cctv.DefaultPitch = 25f;
            cctv.Head = head;
            cctv.Lens = lens;
            cctv.Led = led.GetComponent<Renderer>();
            cctv.Spot = spot;
            cctv.Fill = fill;
            cctv.Capacity = 6f;
            cctv.DetachSeconds = 4f;
            cctv.SalvageSpawn = head;
            head.localRotation = Quaternion.Euler(cctv.DefaultPitch, defaultYaw, 0f);
            // Salvage: the camera unit with a battery cable, hidden until ripped off.
            cctv.Salvage = BuildSalvage($"Cctv_{number:00}", "salvage.cctv", 180, 3f, root.transform.position + root.transform.forward * 0.6f, housingMat,
                new Vector3(0f, 0.1f, 0f), new Vector3(0.22f, 0.18f, 0.4f));
        }

        // A ceiling lamp on a short cord, plus the salvage it drops when ripped off.
        private static void BuildLamp(int number, Vector3 position)
        {
            var root = NetRoot($"Lamp_{number:00}", position, 0f);
            var metal = Mat("LampShade", new Color(0.3f, 0.32f, 0.28f));
            var fixture = new GameObject("Fixture");
            fixture.transform.SetParent(root.transform, false);
            Shape(fixture.transform, "Cord", PrimitiveType.Cylinder, new Vector3(0f, 0.55f, 0f), Vector3.zero, new Vector3(0.02f, 0.45f, 0.02f), Mat("GeneratorDark", new Color(0.1f, 0.1f, 0.1f)));
            Shape(fixture.transform, "Shade", PrimitiveType.Cylinder, new Vector3(0f, 0.08f, 0f), Vector3.zero, new Vector3(0.55f, 0.06f, 0.55f), metal);
            var glass = Shape(fixture.transform, "Bulb", PrimitiveType.Sphere, new Vector3(0f, -0.02f, 0f), Vector3.zero, new Vector3(0.22f, 0.16f, 0.22f), Mat("Indicator", Color.white, unlit: true));
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.05f, 0f);
            collider.size = new Vector3(0.7f, 0.4f, 0.7f);
            var bulb = PointLight(root.transform, "Light", new Vector3(0f, -0.25f, 0f), new Color(1f, 0.82f, 0.55f), 6f, 15f);
            bulb.enabled = false;
            var lamp = root.AddComponent<PowerLamp>();
            lamp.NameKey = "lamp.ceiling";
            lamp.Capacity = 5f;
            lamp.DetachSeconds = 3f;
            lamp.Bulb = bulb;
            lamp.Glass = glass.GetComponent<Renderer>();
            lamp.Fixture = fixture;
            lamp.SalvageSpawn = root.transform;
            lamp.Salvage = BuildSalvage($"Lamp_{number:00}", "salvage.lamp", 90, 3f, new Vector3(position.x, 0f, position.z), metal,
                new Vector3(0f, 0.06f, 0f), new Vector3(0.5f, 0.12f, 0.5f));
        }

        // Hidden appliance that appears when its fixture is ripped off: charged, tiny, with a battery cable.
        private static Appliance BuildSalvage(string name, string nameKey, int value, float cord, Vector3 position, Material body, Vector3 center, Vector3 size)
        {
            var root = NetRoot("Salvage_" + name, position, 0f);
            Shape(root.transform, "Body", PrimitiveType.Cube, center, Vector3.zero, size, body);
            var indicator = Shape(root.transform, "Indicator", PrimitiveType.Cube, center + new Vector3(0f, size.y / 2f, 0f), Vector3.zero, new Vector3(0.06f, 0.02f, 0.06f), Mat("Indicator", Color.white, unlit: true));
            var collider = root.AddComponent<BoxCollider>();
            collider.center = center;
            collider.size = size;
            var appliance = FinishAppliance(root, name, nameKey, 6f, value, 1, true, cord, new Vector3(0f, center.y, -size.z / 2f - 0.01f), indicator.GetComponent<Renderer>(), size.y);
            appliance.StartsHidden = true;
            return appliance;
        }

        private static Material CctvFeedMaterial()
        {
            if (Materials.TryGetValue("CctvFeed", out var cached)) return cached;
            var path = $"{MaterialDir}/CctvFeed.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("CoOp/CctvFisheye");
                if (shader == null) throw new InvalidOperationException("CoOp/CctvFisheye shader is missing.");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            // Explicit look values: a material keeps old values when the shader changes.
            material.SetColor("_Color", new Color(0.92f, 0.95f, 0.9f));
            material.SetFloat("_Gain", 1.4f);
            material.SetFloat("_Lift", 0.55f);
            material.SetFloat("_Grain", 0.07f);
            material.SetFloat("_Lines", 260f);
            material.SetFloat("_Curve", 0.16f);
            material.SetFloat("_Corner", 0.14f);
            material.SetFloat("_ZTest", 4f);
            material.SetFloat("_Signal", 0f);
            material.SetFloat("_Static", 0f);
            EditorUtility.SetDirty(material);
            Materials["CctvFeed"] = material;
            return material;
        }

        private static void BuildTransmitter(Vector3 position, float yaw)
        {
            var root = NetRoot("Transmitter", position, yaw);
            var body = Mat("Transmitter", new Color(0.3f, 0.36f, 0.45f), tex: Texture("Metal"), tiling: 2f);
            var lampMat = Mat("Indicator", Color.white, unlit: true);
            var holeMat = Mat("Hole", new Color(0.01f, 0.01f, 0.01f), unlit: true);

            // Everything that sinks lives under Body; the hole it leaves stays at the root.
            var bodyRoot = new GameObject("Body").transform;
            bodyRoot.SetParent(root.transform, false);
            Box(bodyRoot, "Cabinet", new Vector3(0f, 1.25f, 0f), new Vector3(2.6f, 2.5f, 0.8f), body);
            Box(bodyRoot, "Shelf", new Vector3(0f, 0.25f, 0.75f), new Vector3(2.6f, 0.5f, 0.7f), body);
            Shape(bodyRoot, "Mast", PrimitiveType.Cylinder, new Vector3(0f, 3.4f, 0f), Vector3.zero, new Vector3(0.12f, 0.9f, 0.12f), body);
            Shape(bodyRoot, "Dish", PrimitiveType.Sphere, new Vector3(0f, 3.8f, 0f), Vector3.zero, Vector3.one * 0.4f, lampMat);
            Shape(bodyRoot, "MouthRim", PrimitiveType.Cylinder, new Vector3(0f, 2.52f, 0f), Vector3.zero, new Vector3(1.2f, 0.03f, 0.6f), holeMat);
            var mouth = new GameObject("Mouth").transform;
            mouth.SetParent(bodyRoot, false);
            mouth.localPosition = new Vector3(0f, 2.55f, 0f);
            var power = Shape(bodyRoot, "PowerLamp", PrimitiveType.Cube, new Vector3(0f, 1.9f, 0.41f), Vector3.zero, new Vector3(1.6f, 0.12f, 0.02f), lampMat);
            var sockets = new Transform[3];
            var lamps = new Renderer[3];
            for (var i = 0; i < 3; i++)
            {
                var x = (i - 1) * 0.85f;
                sockets[i] = new GameObject("Socket_" + i).transform;
                sockets[i].SetParent(bodyRoot, false);
                sockets[i].localPosition = new Vector3(x, 0.5f, 0.75f);
                lamps[i] = Shape(bodyRoot, "SocketLamp_" + i, PrimitiveType.Sphere, new Vector3(x, 1.0f, 0.42f), Vector3.zero, Vector3.one * 0.14f, lampMat).GetComponent<Renderer>();
            }
            var beacon = PointLight(bodyRoot, "Beacon", new Vector3(0f, 3.7f, 0.6f), new Color(0.8f, 0.2f, 0.15f), 0.6f, 10f);
            Obstacle(bodyRoot.gameObject, new Vector3(0f, 1.25f, 0.2f), new Vector3(2.6f, 2.5f, 1.4f));

            var hole = Shape(root.transform, "Hole", PrimitiveType.Cylinder, new Vector3(0f, 0.015f, 0.2f), Vector3.zero, new Vector3(2.8f, 0.01f, 1.6f), holeMat);
            hole.SetActive(false);

            var transmitter = root.AddComponent<Transmitter>();
            transmitter.Capacity = 30f;
            transmitter.Body = bodyRoot;
            transmitter.Mouth = mouth;
            transmitter.Hole = hole;
            transmitter.Sockets = sockets;
            transmitter.SocketLamps = lamps;
            transmitter.PowerLamp = power.GetComponent<Renderer>();
            transmitter.Beacon = beacon;
        }

        // Appliance root pivot is at its bottom so it rests on the floor or a counter.
        private static Appliance BuildAppliance(string name, string nameKey, float capacity, int value, int weight, bool tiny, float cord,
            Vector3 position, float yaw, Material body, Vector3 bodyCenter, Vector3 bodySize, Vector3 indicatorCenter, Vector3 indicatorSize)
        {
            var root = NetRoot("Appliance_" + name, position, yaw);
            Shape(root.transform, "Body", PrimitiveType.Cube, bodyCenter, Vector3.zero, bodySize, body);
            var indicator = Shape(root.transform, "Indicator", PrimitiveType.Cube, indicatorCenter, Vector3.zero, indicatorSize, Mat("Indicator", Color.white, unlit: true));
            var collider = root.AddComponent<BoxCollider>();
            collider.center = bodyCenter;
            collider.size = bodySize;
            var anchor = new Vector3(0f, bodyCenter.y * 0.7f, bodyCenter.z - bodySize.z / 2f - 0.01f);
            return FinishAppliance(root, name, nameKey, capacity, value, weight, tiny, cord, anchor, indicator.GetComponent<Renderer>(), bodyCenter.y * 2f);
        }

        private static void BuildFan(Vector3 position, float yaw)
        {
            var root = NetRoot("Appliance_Fan", position, yaw);
            var body = Mat("ApplianceLight", new Color(0.7f, 0.7f, 0.68f));
            Shape(root.transform, "Base", PrimitiveType.Cube, new Vector3(0f, 0.03f, 0f), Vector3.zero, new Vector3(0.35f, 0.06f, 0.3f), body);
            Shape(root.transform, "Pole", PrimitiveType.Cube, new Vector3(0f, 0.27f, 0f), Vector3.zero, new Vector3(0.06f, 0.45f, 0.06f), body);
            Shape(root.transform, "Head", PrimitiveType.Cylinder, new Vector3(0f, 0.52f, 0f), new Vector3(90f, 0f, 0f), new Vector3(0.4f, 0.04f, 0.4f), body);
            var hub = Shape(root.transform, "Indicator", PrimitiveType.Sphere, new Vector3(0f, 0.52f, 0.05f), Vector3.zero, Vector3.one * 0.12f, Mat("Indicator", Color.white, unlit: true));
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.36f, 0f);
            collider.size = new Vector3(0.42f, 0.72f, 0.3f);
            FinishAppliance(root, "Fan", "appliance.fan", 25f, 200, 1, false, 6f, new Vector3(0f, 0.04f, -0.16f), hub.GetComponent<Renderer>(), 0.72f);
        }

        private static Appliance FinishAppliance(GameObject root, string name, string nameKey, float capacity, int value, int weight, bool tiny, float cord,
            Vector3 anchorLocal, Renderer indicator, float height)
        {
            var glow = PointLight(root.transform, "Glow", new Vector3(0f, height + 0.25f, 0.3f), new Color(0.35f, 1f, 0.85f), 1.6f, 3f);
            glow.enabled = false;
            var anchor = new GameObject("CordAnchor").transform;
            anchor.SetParent(root.transform, false);
            anchor.localPosition = anchorLocal;
            var body = root.AddComponent<Rigidbody>();
            body.mass = 8f * weight;
            body.linearDamping = 0.2f;
            body.angularDamping = 1f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            AddPhysicsSync(root);
            var appliance = root.AddComponent<Appliance>();
            appliance.NameKey = nameKey;
            appliance.Capacity = capacity;
            appliance.BaseValue = value;
            appliance.Weight = weight;
            appliance.Tiny = tiny;
            appliance.CordLength = cord;
            appliance.CordAnchor = anchor;
            appliance.Indicator = indicator;
            appliance.Glow = glow;
            appliance.Plug = BuildPlug(name, appliance, anchor.position);
            return appliance;
        }

        // The plug is its own scene-root NetworkObject (a moving trigger), drawn with a cord to its appliance.
        private static CordPlug BuildPlug(string name, Appliance appliance, Vector3 position)
        {
            var root = NetRoot("Plug_" + name, position, 0f);
            var dark = Mat("GeneratorDark", new Color(0.1f, 0.1f, 0.1f));
            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            Shape(visual, "Head", PrimitiveType.Cube, Vector3.zero, Vector3.zero, new Vector3(0.07f, 0.05f, 0.09f), dark);
            var metal = Mat("ApplianceMetal", new Color(0.6f, 0.62f, 0.66f));
            Shape(visual, "PinL", PrimitiveType.Cube, new Vector3(-0.015f, 0f, 0.06f), Vector3.zero, new Vector3(0.008f, 0.008f, 0.04f), metal);
            Shape(visual, "PinR", PrimitiveType.Cube, new Vector3(0.015f, 0f, 0.06f), Vector3.zero, new Vector3(0.008f, 0.008f, 0.04f), metal);
            var trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(0.28f, 0.28f, 0.28f);
            var line = root.AddComponent<LineRenderer>();
            line.sharedMaterial = Mat("Cord", new Color(0.06f, 0.06f, 0.06f), unlit: true);
            line.widthMultiplier = 0.022f;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.positionCount = CordPlug.CordPoints;
            var plug = root.AddComponent<CordPlug>();
            plug.Appliance = appliance;
            plug.Cord = line;
            plug.Visual = visual;
            return plug;
        }

        // The ship's broadcast microphone on the CCTV desk.
        private static void BuildShipMic(Vector3 position)
        {
            var root = NetRoot("ShipMic", position, 180f);
            var dark = Mat("GeneratorDark", new Color(0.1f, 0.1f, 0.1f));
            Shape(root.transform, "Base", PrimitiveType.Cylinder, new Vector3(0f, 0.01f, 0f), Vector3.zero, new Vector3(0.12f, 0.01f, 0.12f), dark);
            Shape(root.transform, "Stem", PrimitiveType.Cylinder, new Vector3(0f, 0.12f, 0f), new Vector3(-15f, 0f, 0f), new Vector3(0.02f, 0.11f, 0.02f), dark);
            Shape(root.transform, "Head", PrimitiveType.Sphere, new Vector3(0f, 0.24f, 0.04f), Vector3.zero, new Vector3(0.07f, 0.09f, 0.07f), Mat("ApplianceMetal", new Color(0.6f, 0.62f, 0.66f)));
            var lamp = Shape(root.transform, "Lamp", PrimitiveType.Sphere, new Vector3(0.05f, 0.03f, 0.04f), Vector3.zero, Vector3.one * 0.025f, Mat("Indicator", Color.white, unlit: true));
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.15f, 0f);
            collider.size = new Vector3(0.32f, 0.34f, 0.32f);
            root.AddComponent<ShipMic>().Lamp = lamp.GetComponent<Renderer>();
        }

        // ---------------- helpers ----------------

        // In-scene NetworkObjects stay at the scene root (no plain-GameObject parent).
        private static GameObject NetRoot(string name, Vector3 position, float yaw)
        {
            var go = new GameObject(Prefix + name);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            go.AddComponent<NetworkObject>();
            return go;
        }

        // Keeps the monster's path around props that are not part of the baked map.
        private static void Obstacle(GameObject go, Vector3 center, Vector3 size)
        {
            var obstacle = go.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = center;
            obstacle.size = size;
            obstacle.carving = true;
        }

        private static GameObject Box(Transform parent, string name, Vector3 localPosition, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        // Visual-only primitive (collider removed).
        private static GameObject Shape(Transform parent, string name, PrimitiveType type, Vector3 localPosition, Vector3 localEuler, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(localEuler);
            go.transform.localScale = scale;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        private static Light PointLight(Transform parent, string name, Vector3 localPosition, Color color, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            return light;
        }

        private static Material Mat(string name, Color color, bool unlit = false, Texture2D tex = null, float tiling = 1f, float tilingY = -1f)
        {
            if (Materials.TryGetValue(name, out var cached)) return cached;
            var path = $"{MaterialDir}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetTexture("_BaseMap", tex);
            material.SetTextureScale("_BaseMap", new Vector2(tiling, tilingY < 0f ? tiling : tilingY));
            if (!unlit)
            {
                material.SetFloat("_Smoothness", 0.08f);
                material.SetFloat("_Metallic", 0f);
            }
            EditorUtility.SetDirty(material);
            Materials[name] = material;
            return material;
        }
    }
}
