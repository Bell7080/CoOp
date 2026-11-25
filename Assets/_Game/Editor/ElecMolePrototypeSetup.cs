using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CoOpHorror.Gameplay;
using CoOpHorror.Networking;
using CoOpHorror.Player;
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
    // v0.3 ElecMole stage prototype: temporary map with the crashed ship (south-west zone), generator ->
    // shared power -> ship door / appliances / transmitter -> settlement -> blackout -> launch, and a
    // placeholder NavMesh monster. Re-runnable: rebuilds every "Proto_" root of the prototype scene in place.
    // Run from the Editor menu (or Unity CLI script eval), never by editing scene YAML.
    public static class ElecMolePrototypeSetup
    {
        public const string ScenePath = "Assets/_Game/Scenes/ElecMolePrototype.unity";
        private const string NavMeshPath = "Assets/_Game/Scenes/ElecMolePrototype_NavMesh.asset";
        private const string PlayerPrefabPath = "Assets/_Game/Prefabs/ConnectionAvatar.prefab";
        private const string MaterialDir = "Assets/_Game/Materials/Prototype";
        private const string Prefix = "Proto_";
        private const float WallHeight = 4f;
        private const float WallThickness = 0.4f;
        private const float DoorWidth = 3f;

        // 3 x 3 zones. Zone (0,0), the south-west corner, is the crashed ship.
        private static readonly (float min, float max)[] XZones = { (-22f, -8f), (-8f, 8f), (8f, 22f) };
        private static readonly (float min, float max)[] ZZones = { (-20f, -6f), (-6f, 10f), (10f, 24f) };
        private static readonly Vector3 ShipMin = new(-22f, 0f, -20f);
        private static readonly Vector3 ShipMax = new(-8f, 0f, -6f);
        private static readonly Vector3 ShipDoorPosition = new(-8f, 0f, -13f);

        private static readonly Dictionary<string, Material> Materials = new();

        [MenuItem("CoOp/Setup/Build ElecMole prototype scene (v0.3)")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
            ConfigurePlayerPrefab();
            Materials.Clear();
            Directory.CreateDirectory(MaterialDir);
            AssetDatabase.Refresh();

            if (!File.Exists(ScenePath) && !AssetDatabase.CopyAsset(DevelopmentProjectSetup.ScenePath, ScenePath))
                throw new InvalidOperationException("Could not copy the sandbox scene.");
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == "DevelopmentFloor" || root.name.StartsWith(Prefix, StringComparison.Ordinal)) Object.DestroyImmediate(root);

            var sun = ConfigureLightingAndCamera();
            var map = BuildMap(out var roomLights, out var emergencyLights);
            BakeNavMesh(map);
            BuildGameplay(sun, roomLights, emergencyLights);
            if (Object.FindFirstObjectByType<PrototypeHud>() == null) new GameObject("PrototypeHud", typeof(PrototypeHud));
            var view = Object.FindFirstObjectByType<CctvView>();
            if (view == null) view = new GameObject("CctvView").AddComponent<CctvView>();
            view.FeedMaterial = CctvFeedMaterial();
            EditorUtility.SetDirty(view);
            var manager = Object.FindFirstObjectByType<NetworkManager>();
            if (manager != null)
            {
                manager.NetworkConfig.ProtocolVersion = RoomLimits.ProtocolVersion;
                EditorUtility.SetDirty(manager);
            }

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

        // Player prefab: no punching any more; left-click interaction, flashlight and revivable body.
        private static void ConfigurePlayerPrefab()
        {
            var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root); // the removed PlayerCombat
                if (root.GetComponent<PlayerInteractor>() == null) root.AddComponent<PlayerInteractor>();
                if (root.GetComponent<PlayerFlashlight>() == null) root.AddComponent<PlayerFlashlight>();
                if (root.GetComponent<PlayerCorpse>() == null) root.AddComponent<PlayerCorpse>();
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static Light ConfigureLightingAndCamera()
        {
            Light light = null;
            var sun = GameObject.Find("DevelopmentLight");
            if (sun != null && sun.TryGetComponent(out light))
            {
                light.intensity = 0.35f;
                light.color = new Color(0.72f, 0.8f, 1f);
            }
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.12f, 0.13f, 0.15f);
            var cam = GameObject.Find("DevelopmentCamera");
            if (cam != null)
            {
                cam.transform.position = new Vector3(0f, 38f, -26f);
                cam.transform.LookAt(new Vector3(0f, 0f, 2f));
            }
            return light;
        }

        // ---------------- map ----------------

        private static Transform BuildMap(out Light[] roomLights, out Light[] emergencyLights)
        {
            var map = new GameObject(Prefix + "Map").transform;
            var floorA = Mat("FloorA", new Color(0.075f, 0.085f, 0.09f));
            var floorB = Mat("FloorB", new Color(0.095f, 0.09f, 0.08f));
            var shipFloor = Mat("ShipFloor", new Color(0.1f, 0.13f, 0.16f));
            var wall = Mat("Wall", new Color(0.23f, 0.24f, 0.25f));
            var crate = Mat("Crate", new Color(0.33f, 0.24f, 0.14f));
            var stripe = Mat("Stripe", new Color(0.85f, 0.7f, 0.1f));

            for (var i = 0; i < 3; i++)
            for (var j = 0; j < 3; j++)
            {
                var (x0, x1) = XZones[i];
                var (z0, z1) = ZZones[j];
                var material = i == 0 && j == 0 ? shipFloor : (i + j) % 2 == 0 ? floorA : floorB;
                Box(map, $"Floor_{i}{j}", new Vector3((x0 + x1) / 2f, -0.25f, (z0 + z1) / 2f), new Vector3(x1 - x0, 0.5f, z1 - z0), material);
            }

            // Outer walls.
            Box(map, "Wall_South", new Vector3(0f, WallHeight / 2f, -20f), new Vector3(44f + WallThickness, WallHeight, WallThickness), wall);
            Box(map, "Wall_North", new Vector3(0f, WallHeight / 2f, 24f), new Vector3(44f + WallThickness, WallHeight, WallThickness), wall);
            Box(map, "Wall_West", new Vector3(-22f, WallHeight / 2f, 2f), new Vector3(WallThickness, WallHeight, 44f + WallThickness), wall);
            Box(map, "Wall_East", new Vector3(22f, WallHeight / 2f, 2f), new Vector3(WallThickness, WallHeight, 44f + WallThickness), wall);

            // Inner walls with a 3 m doorway in the middle of every zone edge. The ship keeps only its east door.
            foreach (var x in new[] { -8f, 8f })
                foreach (var (z0, z1) in ZZones) WallWithDoorAlongZ(map, wall, x, z0, z1);
            foreach (var z in new[] { -6f, 10f })
                foreach (var (x0, x1) in XZones)
                {
                    if (Mathf.Approximately(z, -6f) && Mathf.Approximately(x0, -22f)) WallSegment(map, wall, new Vector3(x0, 0f, z), new Vector3(x1, 0f, z));
                    else WallWithDoorAlongX(map, wall, z, x0, x1);
                }
            // Hazard stripe at the ship's threshold.
            Shape(map, "ShipThreshold", PrimitiveType.Cube, ShipDoorPosition + new Vector3(-0.6f, 0.01f, 0f), Vector3.zero, new Vector3(0.35f, 0.02f, DoorWidth), stripe);

            // A few crates, kept off the doorway lines the monster patrols along.
            var crates = new[]
            {
                new Vector3(-20f, 0.5f, 7f), new Vector3(-10.5f, 0.5f, -3f),
                new Vector3(20f, 0.5f, -3f), new Vector3(10.5f, 0.5f, 7f),
                new Vector3(-4f, 0.5f, 22f), new Vector3(4f, 0.5f, 12.5f),
                new Vector3(4f, 0.5f, -18f), new Vector3(-4f, 0.5f, -8.5f),
            };
            for (var c = 0; c < crates.Length; c++) Box(map, "Crate_" + c, crates[c], new Vector3(1.2f, 1f, 1.2f), crate);

            // Room lights go out at the blackout; red emergency lights replace them. The ship keeps its own light.
            var rooms = new List<Light>();
            var emergency = new List<Light>();
            for (var i = 0; i < 3; i++)
            for (var j = 0; j < 3; j++)
            {
                var center = new Vector3((XZones[i].min + XZones[i].max) / 2f, 3.6f, (ZZones[j].min + ZZones[j].max) / 2f);
                if (i == 0 && j == 0)
                {
                    PointLight(map, "ShipLight", center, new Color(0.75f, 0.88f, 1f), 1.1f, 13f);
                    continue;
                }
                var lair = i == 2 && j == 0;
                rooms.Add(PointLight(map, $"RoomLight_{i}{j}", center, lair ? new Color(0.9f, 0.35f, 0.3f) : new Color(1f, 0.83f, 0.62f), lair ? 0.5f : 1.3f, 12f));
                var red = PointLight(map, $"EmergencyLight_{i}{j}", center + Vector3.down * 0.6f, new Color(1f, 0.12f, 0.08f), 0.45f, 10f);
                red.enabled = false;
                emergency.Add(red);
            }
            roomLights = rooms.ToArray();
            emergencyLights = emergency.ToArray();
            return map;
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

        private static void WallWithDoorAlongZ(Transform parent, Material material, float x, float z0, float z1)
        {
            var mid = (z0 + z1) / 2f;
            WallSegment(parent, material, new Vector3(x, 0f, z0), new Vector3(x, 0f, mid - DoorWidth / 2f));
            WallSegment(parent, material, new Vector3(x, 0f, mid + DoorWidth / 2f), new Vector3(x, 0f, z1));
        }

        private static void WallWithDoorAlongX(Transform parent, Material material, float z, float x0, float x1)
        {
            var mid = (x0 + x1) / 2f;
            WallSegment(parent, material, new Vector3(x0, 0f, z), new Vector3(mid - DoorWidth / 2f, 0f, z));
            WallSegment(parent, material, new Vector3(mid + DoorWidth / 2f, 0f, z), new Vector3(x1, 0f, z));
        }

        private static void WallSegment(Transform parent, Material material, Vector3 a, Vector3 b)
        {
            var center = (a + b) / 2f + Vector3.up * (WallHeight / 2f);
            var alongX = Mathf.Abs(b.x - a.x) > Mathf.Abs(b.z - a.z);
            var length = alongX ? Mathf.Abs(b.x - a.x) : Mathf.Abs(b.z - a.z);
            var size = alongX ? new Vector3(length, WallHeight, WallThickness) : new Vector3(WallThickness, WallHeight, length);
            Box(parent, $"Wall_{center.x:0.#}_{center.z:0.#}", center, size, material);
        }

        // ---------------- gameplay objects ----------------

        private static void BuildGameplay(Light sun, Light[] roomLights, Light[] emergencyLights)
        {
            NetRoot("PowerGrid", Vector3.zero, 0f).AddComponent<PowerGrid>();
            BuildGenerator(new Vector3(-19.5f, 0f, -17.5f), -45f);
            var door = BuildPoweredDoor("ShipDoor", ShipDoorPosition, 0f, 8f);
            BuildLaunchButton(new Vector3(-20.6f, 0f, -8.4f), 135f);
            BuildCctvPanel(new Vector3(-15.5f, 0f, -19.4f), 0f);

            // CCTV cameras on the walls outside the ship (root on the wall face, facing into the room).
            BuildCctvCamera(1, new Vector3(-21.8f, 0f, 0f), 90f, 0f);
            BuildCctvCamera(2, new Vector3(-4f, 0f, 9.8f), 180f, -25f);
            BuildCctvCamera(3, new Vector3(-1f, 0f, -19.8f), 0f, -40f);
            BuildCctvCamera(4, new Vector3(21.8f, 0f, -9f), -90f, -30f);
            BuildCctvCamera(5, new Vector3(8.2f, 0f, 13f), 90f, -35f);
            BuildCctvCamera(6, new Vector3(-1f, 0f, 23.8f), 180f, 0f);
            BuildTransmitter(new Vector3(19.5f, 0f, 21.5f), -135f);

            BuildAppliance("TV", "appliance.tv", 40f, 400, new Vector3(-19.5f, 0f, 3f), 90f, Mat("ApplianceDark", new Color(0.12f, 0.12f, 0.13f)),
                new Vector3(0f, 0.3f, 0f), new Vector3(0.75f, 0.55f, 0.45f), new Vector3(0f, 0.31f, 0.231f), new Vector3(0.6f, 0.42f, 0.02f));
            BuildAppliance("Microwave", "appliance.microwave", 30f, 300, new Vector3(-19.5f, 0f, 21.5f), 135f, Mat("ApplianceLight", new Color(0.7f, 0.7f, 0.68f)),
                new Vector3(0f, 0.17f, 0f), new Vector3(0.55f, 0.34f, 0.4f), new Vector3(-0.06f, 0.17f, 0.205f), new Vector3(0.32f, 0.22f, 0.02f));
            BuildAppliance("Radio", "appliance.radio", 20f, 150, new Vector3(2.5f, 0f, 22.5f), 180f, Mat("ApplianceWood", new Color(0.42f, 0.27f, 0.15f)),
                new Vector3(0f, 0.13f, 0f), new Vector3(0.45f, 0.26f, 0.15f), new Vector3(0f, 0.2f, 0.08f), new Vector3(0.35f, 0.06f, 0.02f));
            BuildFan(new Vector3(19.5f, 0f, 1f), -90f);
            BuildAppliance("Toaster", "appliance.toaster", 15f, 120, new Vector3(-5f, 0f, -18f), 0f, Mat("ApplianceMetal", new Color(0.6f, 0.62f, 0.66f)),
                new Vector3(0f, 0.11f, 0f), new Vector3(0.3f, 0.22f, 0.18f), new Vector3(0f, 0.11f, 0.095f), new Vector3(0.18f, 0.08f, 0.01f));

            BuildMonster();

            var spawn = new GameObject(Prefix + "SpawnArea");
            spawn.transform.SetPositionAndRotation(new Vector3(-15f, 1.05f, -13f), Quaternion.Euler(0f, 90f, 0f));
            spawn.AddComponent<SpawnArea>().Spacing = 2f;

            var director = NetRoot("StageDirector", Vector3.zero, 0f).AddComponent<StageDirector>();
            director.RoomLights = roomLights;
            director.EmergencyLights = emergencyLights;
            director.Sun = sun;
            director.ShipMin = ShipMin;
            director.ShipMax = ShipMax;
            director.ShipDoor = door;
        }

        private static void BuildGenerator(Vector3 position, float yaw)
        {
            var root = NetRoot("Generator", position, yaw);
            var body = Mat("Generator", new Color(0.45f, 0.3f, 0.12f));
            var dark = Mat("GeneratorDark", new Color(0.1f, 0.1f, 0.1f));
            Box(root.transform, "Base", new Vector3(0f, 0.6f, 0f), new Vector3(1.6f, 1.2f, 1.1f), body);
            Shape(root.transform, "Exhaust", PrimitiveType.Cylinder, new Vector3(-0.5f, 1.4f, 0.3f), Vector3.zero, new Vector3(0.15f, 0.4f, 0.15f), dark);
            var rotor = new GameObject("Rotor").transform;
            rotor.SetParent(root.transform, false);
            rotor.localPosition = new Vector3(0.86f, 0.7f, 0f);
            Shape(rotor, "Wheel", PrimitiveType.Cylinder, Vector3.zero, new Vector3(0f, 0f, 90f), new Vector3(0.8f, 0.05f, 0.8f), dark);
            Shape(rotor, "Handle", PrimitiveType.Cube, new Vector3(0.08f, 0.3f, 0f), Vector3.zero, new Vector3(0.08f, 0.22f, 0.08f), body);
            var lamp = PointLight(root.transform, "Lamp", new Vector3(0f, 2.2f, 0f), new Color(1f, 0.65f, 0.25f), 0.3f, 8f);
            Obstacle(root, new Vector3(0f, 0.6f, 0f), new Vector3(1.6f, 1.2f, 1.1f));
            var generator = root.AddComponent<Generator>();
            generator.Rotor = rotor;
            generator.Lamp = lamp;
        }

        // Door along the Z axis (the wall runs north-south); the panel slides up when powered.
        private static PoweredDoor BuildPoweredDoor(string name, Vector3 position, float yaw, float capacity)
        {
            var root = NetRoot(name, position, yaw);
            var panelMat = Mat("DoorPanel", new Color(0.3f, 0.32f, 0.3f));
            var lampMat = Mat("Indicator", Color.white, unlit: true);
            var panel = Box(root.transform, "Panel", new Vector3(0f, WallHeight / 2f, 0f), new Vector3(0.3f, WallHeight, DoorWidth), panelMat);
            Shape(panel.transform, "Stripe", PrimitiveType.Cube, new Vector3(0f, -0.3f, 0f), Vector3.zero, new Vector3(1.05f, 0.06f, 1f), Mat("Stripe", new Color(0.85f, 0.7f, 0.1f)));
            var obstacle = panel.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = Vector3.zero;
            obstacle.size = Vector3.one;
            obstacle.carving = true;
            var inside = Shape(root.transform, "IndicatorInside", PrimitiveType.Cube, new Vector3(-0.25f, 1.6f, DoorWidth / 2f + 0.25f), Vector3.zero, new Vector3(0.08f, 0.25f, 0.25f), lampMat);
            Shape(root.transform, "IndicatorOutside", PrimitiveType.Cube, new Vector3(0.25f, 1.6f, DoorWidth / 2f + 0.25f), Vector3.zero, new Vector3(0.08f, 0.25f, 0.25f), lampMat);
            var door = root.AddComponent<PoweredDoor>();
            door.Capacity = capacity;
            door.Panel = panel.transform;
            door.Indicator = inside.GetComponent<Renderer>();
            return door;
        }

        private static void BuildLaunchButton(Vector3 position, float yaw)
        {
            var root = NetRoot("LaunchButton", position, yaw);
            var console = Mat("Console", new Color(0.18f, 0.2f, 0.24f));
            Box(root.transform, "Console", new Vector3(0f, 0.5f, 0f), new Vector3(0.9f, 1f, 0.6f), console);
            Shape(root.transform, "Screen", PrimitiveType.Cube, new Vector3(0f, 1.35f, -0.2f), new Vector3(-20f, 0f, 0f), new Vector3(0.8f, 0.5f, 0.05f), Mat("Screen", new Color(0.05f, 0.25f, 0.2f), unlit: true));
            var lamp = Shape(root.transform, "Button", PrimitiveType.Cylinder, new Vector3(0f, 1.03f, 0.1f), Vector3.zero, new Vector3(0.3f, 0.04f, 0.3f), Mat("Indicator", Color.white, unlit: true));
            root.AddComponent<LaunchButton>().Lamp = lamp.GetComponent<Renderer>();
        }

        private static void BuildCctvPanel(Vector3 position, float yaw)
        {
            var root = NetRoot("CctvPanel", position, yaw);
            var console = Mat("Console", new Color(0.18f, 0.2f, 0.24f));
            var dark = Mat("GeneratorDark", new Color(0.1f, 0.1f, 0.1f));
            Box(root.transform, "Desk", new Vector3(0f, 0.45f, 0f), new Vector3(1.6f, 0.9f, 0.7f), console);
            Box(root.transform, "MonitorFrame", new Vector3(0f, 1.45f, -0.2f), new Vector3(1.3f, 1f, 0.12f), dark);
            Shape(root.transform, "Keyboard", PrimitiveType.Cube, new Vector3(0f, 0.92f, 0.1f), Vector3.zero, new Vector3(0.6f, 0.03f, 0.2f), dark);
            var screen = Shape(root.transform, "Screen", PrimitiveType.Quad, new Vector3(0f, 1.45f, -0.135f), new Vector3(0f, 180f, 0f), new Vector3(1.2f, 0.9f, 1f), CctvFeedMaterial());
            PointLight(root.transform, "ScreenGlow", new Vector3(0f, 1.5f, 0.5f), new Color(0.55f, 1f, 0.7f), 0.5f, 3.5f);
            root.AddComponent<CctvPanel>().Screen = screen.GetComponent<Renderer>();
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
            var cctv = root.AddComponent<CctvCamera>();
            cctv.NameKey = "cctv.cam" + number;
            cctv.Order = number;
            cctv.DefaultYaw = defaultYaw;
            cctv.DefaultPitch = 25f;
            cctv.Head = head;
            cctv.Lens = lens;
            cctv.Led = led.GetComponent<Renderer>();
            head.localRotation = Quaternion.Euler(cctv.DefaultPitch, defaultYaw, 0f);
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
            Materials["CctvFeed"] = material;
            return material;
        }

        private static void BuildTransmitter(Vector3 position, float yaw)
        {
            var root = NetRoot("Transmitter", position, yaw);
            var body = Mat("Transmitter", new Color(0.16f, 0.2f, 0.28f));
            var lampMat = Mat("Indicator", Color.white, unlit: true);
            var holeMat = Mat("Hole", new Color(0.01f, 0.01f, 0.01f), unlit: true);

            // Everything that sinks lives under Body; the hole it leaves stays at the root.
            var bodyRoot = new GameObject("Body").transform;
            bodyRoot.SetParent(root.transform, false);
            Box(bodyRoot, "Cabinet", new Vector3(0f, 1.25f, 0f), new Vector3(2.6f, 2.5f, 0.8f), body);
            Box(bodyRoot, "Shelf", new Vector3(0f, 0.25f, 0.75f), new Vector3(2.6f, 0.5f, 0.7f), body);
            Shape(bodyRoot, "Mast", PrimitiveType.Cylinder, new Vector3(0f, 3.4f, 0f), Vector3.zero, new Vector3(0.12f, 0.9f, 0.12f), body);
            Shape(bodyRoot, "Dish", PrimitiveType.Sphere, new Vector3(0f, 4.3f, 0f), Vector3.zero, Vector3.one * 0.4f, lampMat);
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
            var beacon = PointLight(bodyRoot, "Beacon", new Vector3(0f, 4.5f, 0.6f), new Color(0.8f, 0.2f, 0.15f), 0.6f, 10f);
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

        // Appliance root pivot is at its bottom so it rests on floors, shelves and sockets.
        private static Appliance BuildAppliance(string name, string nameKey, float capacity, int value, Vector3 position, float yaw, Material body,
            Vector3 bodyCenter, Vector3 bodySize, Vector3 indicatorCenter, Vector3 indicatorSize)
        {
            var root = NetRoot("Appliance_" + name, position, yaw);
            Shape(root.transform, "Body", PrimitiveType.Cube, bodyCenter, Vector3.zero, bodySize, body);
            var indicator = Shape(root.transform, "Indicator", PrimitiveType.Cube, indicatorCenter, Vector3.zero, indicatorSize, Mat("Indicator", Color.white, unlit: true));
            var collider = root.AddComponent<BoxCollider>();
            collider.center = bodyCenter;
            collider.size = bodySize;
            return FinishAppliance(root, nameKey, capacity, value, indicator.GetComponent<Renderer>(), bodyCenter.y * 2f);
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
            FinishAppliance(root, "appliance.fan", 25f, 200, hub.GetComponent<Renderer>(), 0.72f);
        }

        private static Appliance FinishAppliance(GameObject root, string nameKey, float capacity, int value, Renderer indicator, float height)
        {
            var glow = PointLight(root.transform, "Glow", new Vector3(0f, height + 0.25f, 0.3f), new Color(0.35f, 1f, 0.85f), 1.6f, 3f);
            glow.enabled = false;
            var appliance = root.AddComponent<Appliance>();
            appliance.NameKey = nameKey;
            appliance.Capacity = capacity;
            appliance.BaseValue = value;
            appliance.Indicator = indicator;
            appliance.Glow = glow;
            return appliance;
        }

        private static void BuildMonster()
        {
            var root = NetRoot("TempMonster", new Vector3(15f, 1.1f, -13f), -90f);
            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.height = 2.2f;
            capsule.radius = 0.5f;
            capsule.center = Vector3.zero;
            var agent = root.AddComponent<NavMeshAgent>();
            agent.baseOffset = 1.1f;
            agent.radius = 0.5f;
            agent.height = 2.2f;
            agent.speed = TempMonster.PatrolSpeed;
            agent.acceleration = 12f;
            agent.angularSpeed = 540f;
            agent.stoppingDistance = 0.4f;
            agent.autoBraking = true;
            var sync = root.AddComponent<NetworkTransform>();
            sync.AuthorityMode = NetworkTransform.AuthorityModes.Server;
            sync.SyncScaleX = sync.SyncScaleY = sync.SyncScaleZ = false;
            sync.SyncRotAngleX = sync.SyncRotAngleZ = false;

            var skin = Mat("Monster", new Color(0.09f, 0.03f, 0.03f));
            var eyeMat = Mat("MonsterEye", new Color(0.55f, 0.08f, 0.05f), unlit: true);
            Shape(root.transform, "Body", PrimitiveType.Capsule, Vector3.zero, Vector3.zero, new Vector3(1f, 1.1f, 1f), skin);
            Shape(root.transform, "ArmL", PrimitiveType.Cube, new Vector3(-0.6f, -0.05f, 0.2f), new Vector3(20f, 0f, 0f), new Vector3(0.15f, 1.3f, 0.15f), skin);
            Shape(root.transform, "ArmR", PrimitiveType.Cube, new Vector3(0.6f, -0.05f, 0.2f), new Vector3(20f, 0f, 0f), new Vector3(0.15f, 1.3f, 0.15f), skin);
            var eyeL = Shape(root.transform, "EyeL", PrimitiveType.Sphere, new Vector3(-0.17f, 0.75f, 0.42f), Vector3.zero, Vector3.one * 0.13f, eyeMat);
            var eyeR = Shape(root.transform, "EyeR", PrimitiveType.Sphere, new Vector3(0.17f, 0.75f, 0.42f), Vector3.zero, Vector3.one * 0.13f, eyeMat);

            // Patrol ring through the outer zones and the middle, never inside the ship. The NavMesh finds the doors.
            var route = new[]
            {
                new Vector3(15f, 0f, -6f), new Vector3(15f, 0f, 2f), new Vector3(15f, 0f, 10f), new Vector3(15f, 0f, 17f),
                new Vector3(8f, 0f, 17f), new Vector3(0f, 0f, 17f), new Vector3(-8f, 0f, 17f), new Vector3(-15f, 0f, 17f),
                new Vector3(-15f, 0f, 10f), new Vector3(-15f, 0f, 2f), new Vector3(-8f, 0f, 2f), new Vector3(0f, 0f, 2f),
                new Vector3(0f, 0f, -6f), new Vector3(0f, 0f, -13f), new Vector3(8f, 0f, -13f), new Vector3(15f, 0f, -13f),
            };
            var waypointRoot = new GameObject(Prefix + "MonsterWaypoints").transform;
            var waypoints = route.Select((p, i) =>
            {
                var t = new GameObject("Waypoint_" + i).transform;
                t.SetParent(waypointRoot, false);
                t.position = p;
                return t;
            }).ToArray();

            var monster = root.AddComponent<TempMonster>();
            monster.Waypoints = waypoints;
            monster.Eyes = new[] { eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>() };
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

        private static Material Mat(string name, Color color, bool unlit = false)
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
            EditorUtility.SetDirty(material);
            Materials[name] = material;
            return material;
        }
    }
}
