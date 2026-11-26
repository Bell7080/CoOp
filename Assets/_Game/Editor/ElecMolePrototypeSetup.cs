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
            Ensure<ChargeGaugeHud>("ChargeGaugeHud");
            Ensure<CrashIntro>("CrashIntro");
            // Charge gauges float a little above everything charged from the grid.
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (mb is not IChargeGauge || mb.GetComponent<ChargeGauge>() != null) continue;
                var top = 0.4f;
                foreach (var r in mb.GetComponentsInChildren<Renderer>()) top = Mathf.Max(top, r.bounds.max.y - mb.transform.position.y);
                var gauge = mb.gameObject.AddComponent<ChargeGauge>();
                gauge.Height = Mathf.Min(top + 0.25f, 1.6f);
            }
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
                if (root.GetComponent<PlayerReviver>() == null) root.AddComponent<PlayerReviver>();
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
                // A low, wide lump: you trip over it and have to step around it.
                var box = root.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.17f, 0f);
                box.size = new Vector3(1.4f, 0.34f, 1.4f);
                AddPhysicsSync(root);
                var corpse = root.AddComponent<Corpse>();
                corpse.Weight = 1;
                // The player's own mole, flattened into a bloody puddle on the floor.
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterFbx("MoleAlien"));
                if (source != null)
                {
                    var mole = (GameObject)PrefabUtility.InstantiatePrefab(source);
                    mole.transform.SetParent(root.transform, false);
                    mole.transform.localPosition = new Vector3(0f, 0.02f, 0f);
                    mole.transform.localRotation = Quaternion.Euler(0f, 25f, 0f);
                    mole.transform.localScale = new Vector3(1.35f, 0.24f, 1.25f);
                    var animator = mole.GetComponent<Animator>();
                    if (animator != null) Object.DestroyImmediate(animator);
                    var material = CharacterMaterial("MoleAlien", Color.white);
                    foreach (var r in mole.GetComponentsInChildren<Renderer>())
                    {
                        r.sharedMaterial = r.name == "MoleBlush" ? BlushMaterial() : material;
                        if (r.name == "MoleAlien") corpse.BodyRenderer = r;
                        if (r.name == "MoleNose") corpse.HeadRenderer = r;
                    }
                    Dress(root.transform, "QuaterniusZombie/Blood_1", new Vector3(0f, 0.012f, 0f), new Vector3(2.3f, 0.01f, 2.3f), 40f, true);
                }
                else
                {
                    var skin = Mat("CorpseSkin", new Color(0.2f, 0.21f, 0.23f));
                    corpse.BodyRenderer = Shape(root.transform, "Body", PrimitiveType.Sphere, new Vector3(0f, 0.15f, 0f), Vector3.zero, new Vector3(1.2f, 0.3f, 1.2f), skin).GetComponent<Renderer>();
                }
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
            RenderSettings.ambientLight = new Color(0.16f, 0.163f, 0.18f); // with the PS1 shadow lift: rooms read dimly without a lamp
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.028f; // darkness closes in: ~60% at 10 m, ~10% at 20 m
            RenderSettings.fogColor = Color.black; // the PS1 lift would turn any fog tint into grey haze
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
            BuildSupply();
            BuildCctvPanel(Ship.Panel, 0f);
            BuildShipMic(Ship.Panel + new Vector3(-0.35f, 0.86f, 0.32f)); // on the desk top, front right of the monitor
            BuildTransmitter(TransmitterPosition, 0f);

            // name, text key, power, value, weight (holders to lift), tiny (inventory), cord length.
            BuildAppliance("TV", "appliance.tv", 40f, 400, 2, false, 6f, new Vector3(-30f, 0f, 6f), 90f, Mat("ApplianceDark", new Color(0.12f, 0.12f, 0.13f)),
                new Vector3(0f, 0.3f, 0f), new Vector3(0.75f, 0.55f, 0.45f), new Vector3(0f, 0.31f, 0.231f), new Vector3(0.6f, 0.42f, 0.02f));
            BuildAppliance("Microwave", "appliance.microwave", 30f, 300, 2, false, 6f, new Vector3(14.2f, 0.95f, 9.15f), 180f, Mat("ApplianceLight", new Color(0.7f, 0.7f, 0.68f)),
                new Vector3(0f, 0.17f, 0f), new Vector3(0.55f, 0.34f, 0.4f), new Vector3(-0.06f, 0.17f, 0.205f), new Vector3(0.32f, 0.22f, 0.02f));
            BuildAppliance("Radio", "appliance.radio", 20f, 150, 1, true, 5f, new Vector3(-22f, 0f, 25f), 180f, Mat("ApplianceWood", new Color(0.42f, 0.27f, 0.15f)),
                new Vector3(0f, 0.13f, 0f), new Vector3(0.45f, 0.26f, 0.15f), new Vector3(0f, 0.2f, 0.08f), new Vector3(0.35f, 0.06f, 0.02f));
            BuildFan(new Vector3(-9f, 0f, 6f), 180f);
            BuildAppliance("Toaster", "appliance.toaster", 15f, 120, 1, true, 4.5f, new Vector3(12.2f, 0.95f, 9.15f), 180f, Mat("ApplianceMetal", new Color(0.6f, 0.62f, 0.66f)),
                new Vector3(0f, 0.11f, 0f), new Vector3(0.3f, 0.22f, 0.18f), new Vector3(0f, 0.11f, 0.095f), new Vector3(0.18f, 0.08f, 0.01f));
            BuildAppliance("Kettle", "appliance.kettle", 15f, 160, 1, true, 4f, new Vector3(16.2f, 0.95f, 9.15f), 180f, Mat("ApplianceRed", new Color(0.55f, 0.12f, 0.1f)),
                new Vector3(0f, 0.14f, 0f), new Vector3(0.22f, 0.28f, 0.22f), new Vector3(0f, 0.2f, 0.115f), new Vector3(0.08f, 0.05f, 0.01f));
            BuildAppliance("RiceCooker", "appliance.ricecooker", 25f, 220, 1, false, 5f, new Vector3(12f, 0f, -19.6f), 0f, Mat("ApplianceLight", new Color(0.7f, 0.7f, 0.68f)),
                new Vector3(0f, 0.17f, 0f), new Vector3(0.38f, 0.34f, 0.38f), new Vector3(0f, 0.2f, 0.195f), new Vector3(0.16f, 0.06f, 0.01f));

            // CCTV cameras on room walls (root on the wall face, facing into the room) and ceiling lamps.
            foreach (var cam in Cameras) BuildCctvCamera(cam.Number, cam.Position, cam.Yaw, IntoRoom(cam.Position, cam.Yaw, cam.DefaultYaw));
            var lampIndex = 0;
            foreach (var room in Rooms)
                if (room.Kind != RoomKind.Ship)
                    foreach (var spot in LampSpots(room)) BuildLamp(++lampIndex, spot + Vector3.up * 3.1f);

            BuildCreatures();
            BuildSwingDoors();

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
            // The ship's core engine (Tripo): reactor window facing the cabin (+X), the hand crank on its side
            // (+Z) turning about the side axis. Primitives stay as the collider.
            Transform engine = null;
            if (PropModel(ShipProp("ShipCoreEngine")) != null)
            {
                engine = Dress(root.transform, ShipProp("ShipCoreEngine"), Vector3.zero, new Vector3(1.5f, 1.9f, 1.5f), 90f, true);
                HideRenderers(root.transform, "Base", "Exhaust", "Stripe", "Wheel", "Handle");
                root.transform.Find("Base").localScale = new Vector3(1.5f, 1.3f, 1.5f);
                root.transform.Find("Base").localPosition = new Vector3(0f, 0.65f, 0f);
                rotor.localPosition = new Vector3(0f, 0.85f, 0.84f);
                rotor.localRotation = Quaternion.Euler(0f, 90f, 0f); // its local X (the spin axis) now points sideways
                var crank = PropModel(ShipProp("ShipCrank"));
                if (crank != null)
                {
                    var c = (GameObject)PrefabUtility.InstantiatePrefab(crank);
                    c.transform.SetParent(rotor, false);
                    c.transform.localScale = Vector3.one * 1.1f;
                    foreach (var r in c.GetComponentsInChildren<Renderer>())
                    {
                        var mats = r.sharedMaterials;
                        for (var i = 0; i < mats.Length; i++) mats[i] = PropMaterial(ShipProp("ShipCrank"), mats[i]);
                        r.sharedMaterials = mats;
                    }
                    c.name = "Model_ShipCrank";
                }
                lamp.transform.localPosition = new Vector3(1.1f, 1.2f, 0f); // glow in front of the reactor window
            }
            else if (Dress(root.transform, ShipProp("ShipGenerator"), Vector3.zero, new Vector3(1.1f, 1.5f, 1.6f), 90f, true) != null)
                HideRenderers(root.transform, "Base", "Exhaust", "Stripe", "Wheel", "Handle");
            var generator = root.AddComponent<Generator>();
            generator.Rotor = rotor;
            generator.Lamp = lamp;
            generator.Body = engine;
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
            // Door buttons on the wall, left of the doorway seen from each side (inside the ship: +Z).
            var plate = Mat("Console", new Color(0.18f, 0.2f, 0.24f), tex: Texture("Metal"), tiling: 1f);
            GameObject Button(string name, float x, float z)
            {
                var frame = Box(root.transform, name, new Vector3(x, 1.3f, z), new Vector3(0.1f, 0.45f, 0.32f), plate);
                Shape(frame.transform, "Lamp", PrimitiveType.Cube, new Vector3(Mathf.Sign(x) * 0.55f, 0.08f, 0f), Vector3.zero, new Vector3(0.3f, 0.35f, 0.6f), lampMat);
                return frame;
            }
            var inside = Button("ButtonInside", -0.3f, DoorWidth / 2f + 0.35f);
            var outside = Button("ButtonOutside", 0.34f, -(DoorWidth / 2f + 0.12f));
            var door = root.AddComponent<PoweredDoor>();
            door.Capacity = capacity;
            door.Panel = panel.transform;
            door.Indicator = inside.transform.Find("Lamp").GetComponent<Renderer>();
            door.OutsideIndicator = outside.transform.Find("Lamp").GetComponent<Renderer>();
            door.Buttons = new[] { inside.GetComponent<Collider>(), outside.GetComponent<Collider>() };
            DressDoorButton(inside.transform);
            DressDoorButton(outside.transform);
            return door;
        }

        private static void BuildLaunchButton(Vector3 position, float yaw)
        {
            var root = NetRoot("LaunchButton", position, yaw);
            var console = Mat("Console", new Color(0.18f, 0.2f, 0.24f), tex: Texture("Metal"), tiling: 1f);
            Box(root.transform, "Console", new Vector3(0f, 0.5f, 0f), new Vector3(0.9f, 1f, 0.6f), console);
            Shape(root.transform, "Screen", PrimitiveType.Cube, new Vector3(0f, 1.35f, -0.2f), new Vector3(-20f, 0f, 0f), new Vector3(0.8f, 0.5f, 0.05f), Mat("Screen", new Color(0.05f, 0.25f, 0.2f), unlit: true));
            var lamp = Shape(root.transform, "Button", PrimitiveType.Cylinder, new Vector3(0f, 1.03f, 0.1f), Vector3.zero, new Vector3(0.3f, 0.04f, 0.3f), Mat("Indicator", Color.white, unlit: true));
            var model = Dress(root.transform, ShipProp("ShipLaunchConsole"), Vector3.zero, new Vector3(0.9f, 1.3f, 0.6f), 0f, true);
            if (model != null)
            {
                // the model's own mushroom button is the button: the primitive lamp stays hidden inside it
                HideRenderers(root.transform, "Console", "Screen", "Button");
            }
            var button = root.AddComponent<LaunchButton>();
            button.Lamp = lamp.GetComponent<Renderer>();
            // the red mushroom is its own piece of the model (tools/blender/split_launch_cap.py)
            if (model != null)
                foreach (var t in model.GetComponentsInChildren<Transform>())
                    if (t.name == "Cap") button.Cap = t;
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
            FitCctvDesk(root.transform, screen.transform);
            // Status board on the hull wall behind the desk: two rows of small live monitors.
            var board = Box(root.transform, "StatusBoard", new Vector3(0f, 2.55f, -0.52f), new Vector3(3.3f, 1.35f, 0.06f), dark);
            Object.DestroyImmediate(board.GetComponent<Collider>());
            var tileMat = Mat("CctvTile", Color.white, unlit: true);
            var tiles = new Renderer[CctvWall.Columns * CctvWall.Rows];
            for (var i = 0; i < tiles.Length; i++)
            {
                var col = i % CctvWall.Columns;
                var row = i / CctvWall.Columns;
                tiles[i] = Shape(root.transform, "Tile" + (i + 1), PrimitiveType.Quad, new Vector3(-1.17f + col * 0.78f, 2.86f - row * 0.62f, -0.485f), new Vector3(0f, 180f, 0f), new Vector3(0.72f, 0.52f, 1f), tileMat).GetComponent<Renderer>();
            }
            root.AddComponent<CctvWall>().Tiles = tiles;
            FitPreviewBoard(root.transform);
            PointLight(root.transform, "ScreenGlow", new Vector3(0f, 1.5f, 0.5f), new Color(0.55f, 1f, 0.7f), 0.5f, 3.5f);
            root.AddComponent<CctvPanel>().Screen = screen.GetComponent<Renderer>();
        }

        // A wall-mounted camera with its own lamp, plus the salvage it drops when ripped off.
        // Tripo surveillance desk with its own CRT: the live CCTV quad is moved onto the model's screen
        // (found by a ray against a temporary mesh collider), and the old desk primitives are hidden.
        private static void FitCctvDesk(Transform root, Transform screen)
        {
            var model = Dress(root, ShipProp("ShipCctvDesk"), Vector3.zero, new Vector3(1.7f, 1.9f, 1.15f), 0f, true);
            if (model == null) return;
            HideRenderers(root, "Desk", "MonitorFrame", "Keyboard");
            var desk = root.Find("Desk");
            if (desk != null) desk.localScale = new Vector3(1.6f, 0.9f, 1.05f);
            FitFurnitureCollision(root);
            var bounds = Bounds(model.gameObject);
            var probes = new List<MeshCollider>();
            foreach (var f in model.GetComponentsInChildren<MeshFilter>()) { var mc = f.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = f.sharedMesh; probes.Add(mc); }
            Physics.SyncTransforms();
            // The CRT sits on the left part of the desk top seen from the front (+X side, ~0.2 W off centre, ~0.8 H up).
            var target = new Vector3(bounds.center.x + 0.2f * bounds.size.x, bounds.min.y + 0.8f * bounds.size.y, bounds.max.z + 1f);
            var direction = root.rotation * Vector3.back;
            var origin = new Vector3(target.x, target.y, root.position.z) - direction * 2f;
            var best = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(origin, direction, 4f))
            {
                if (!probes.Contains(hit.collider as MeshCollider) || hit.distance >= best) continue;
                best = hit.distance;
                screen.position = hit.point - direction * 0.012f;
                screen.localScale = new Vector3(bounds.size.x * 0.3f, bounds.size.y * 0.26f, 1f);
            }
            foreach (var mc in probes) Object.DestroyImmediate(mc);
            if (best < float.MaxValue)
            {
                // The CRT glass (measured by a ray grid over the model) is about 0.61 x 0.45 m, spans local
                // x -0.12..0.49 and y 1.11..1.56, and leans back ~8.7 deg with a slight bulge low down. A flat,
                // upright quad was too small, off to one side, and the bulging glass poked through its middle at
                // a distance. Match the opening and lean with the glass, a few millimetres in front of it.
                const float lean = 8.7f;
                var up = new Vector3(0f, Mathf.Cos(lean * Mathf.Deg2Rad), -Mathf.Sin(lean * Mathf.Deg2Rad));
                var into = new Vector3(0f, -Mathf.Sin(lean * Mathf.Deg2Rad), -Mathf.Cos(lean * Mathf.Deg2Rad));
                screen.localRotation = Quaternion.LookRotation(into, up);
                screen.localPosition = new Vector3(0.185f, 1.335f, 0.141f);
                screen.localScale = new Vector3(0.6f, 0.445f, 1f);
            }
            Debug.Log($"[ElecMole] CCTV screen fitted: {(best < float.MaxValue ? screen.localPosition.ToString("F2") : "no hit")}");
        }

        // Floor rectangle of the room containing a point (the room grid of ElecMoleMap).
        private static void RoomBounds(Vector3 p, out Vector3 min, out Vector3 max)
        {
            min = max = p;
            foreach (var room in Rooms)
                if (p.x >= XCols[room.Col] && p.x <= XCols[room.Col + 1] && p.z >= ZRows[room.Row] && p.z <= ZRows[room.Row + 1])
                {
                    min = new Vector3(XCols[room.Col], 0f, ZRows[room.Row]);
                    max = new Vector3(XCols[room.Col + 1], 0f, ZRows[room.Row + 1]);
                }
        }

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
            RoomBounds(position + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * 2f, out cctv.RoomMin, out cctv.RoomMax);
            cctv.Fill = fill;
            cctv.Capacity = 6f;
            cctv.DetachSeconds = 4f;
            cctv.SalvageSpawn = head;
            head.localRotation = Quaternion.Euler(cctv.DefaultPitch, defaultYaw, 0f);
            // Salvage: the camera unit with a battery cable, hidden until ripped off.
            cctv.Salvage = BuildSalvage($"Cctv_{number:00}", "salvage.cctv", 180, 3f, root.transform.position + root.transform.forward * 0.6f, housingMat,
                new Vector3(0f, 0.1f, 0f), new Vector3(0.22f, 0.18f, 0.4f));
            // Tripo surveillance camera on its wall bracket (lens along the model's -X: turned a quarter so it
            // points out of the wall). It is mounted still - the feed still pans with the hidden head - so the
            // bracket stays on the wall. The primitive housing keeps its collider (rip it off as before).
            var mount = Dress(root.transform, ShipProp("CctvCam"), new Vector3(0f, 2.95f, 0.36f), new Vector3(0.75f, 0.64f, 0.49f), 90f, true);
            if (mount != null)
            {
                HideRenderers(root.transform, "Bracket", "Housing", "LensGlass", "Led");
                cctv.Mount = mount.gameObject;
            }
            // the ripped-off camera looks like the same unit
            if (Dress(cctv.Salvage.transform, ShipProp("CctvCam"), Vector3.zero, new Vector3(0.42f, 0.36f, 0.28f), 90f, true) != null)
                HideRenderers(cctv.Salvage.transform, "Body");
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

        // The settlement point: a cargo lift sunk flush in the floor under two hatch lids. Power it (hold) and
        // the lids open on a platform with three sockets around a pylon; plug the cords in and they are reeled
        // into the pylon, cracks spread, and the ground around the hatch caves into the open shaft underneath
        // (ElecMoleMap leaves a 5 x 5 m hole in the floor here, with a deep pit you can fall to your death in).
        private static void BuildTransmitter(Vector3 position, float yaw)
        {
            var root = NetRoot("Transmitter", position, yaw);
            var steel = Mat("Transmitter", new Color(0.3f, 0.36f, 0.45f), tex: Texture("Metal"), tiling: 2f);
            var hatch = Mat("HatchSteel", new Color(0.45f, 0.42f, 0.35f), tex: Texture("Metal"), tiling: 1f);
            var hazard = Mat("Hazard", Color.white, tex: Texture("Hazard"), tiling: 4f);
            var lampMat = Mat("Indicator", Color.white, unlit: true);
            var holeMat = Mat("Hole", new Color(0.01f, 0.01f, 0.01f), unlit: true);
            var ground = FloorMaterial(RoomKind.TransmitterRoom);
            const float hatchHalf = 1.4f;   // 2.8 m hatch
            const float pit = TransmitterPitHalf;

            // ---- the lift (everything under Body sinks) ----
            var bodyRoot = new GameObject("Body").transform;
            bodyRoot.SetParent(root.transform, false);
            Box(bodyRoot, "Platform", new Vector3(0f, -0.15f, 0f), new Vector3(2.7f, 0.3f, 2.7f), steel);
            Box(bodyRoot, "Pylon", new Vector3(0f, 0.6f, 0f), new Vector3(0.6f, 1.2f, 0.6f), steel);
            Shape(bodyRoot, "PylonBand", PrimitiveType.Cube, new Vector3(0f, 0.25f, 0f), Vector3.zero, new Vector3(0.62f, 0.12f, 0.62f), hazard);
            Shape(bodyRoot, "MouthRim", PrimitiveType.Cylinder, new Vector3(0f, 1.21f, 0f), Vector3.zero, new Vector3(0.45f, 0.02f, 0.45f), holeMat);
            var mouth = new GameObject("Mouth").transform;
            mouth.SetParent(bodyRoot, false);
            mouth.localPosition = new Vector3(0f, 1.25f, 0f);
            // charge strip set in the floor by the hatch (the lift itself waits below until powered)
            var power = Shape(root.transform, "PowerLamp", PrimitiveType.Cube, new Vector3(0f, 0.012f, -(hatchHalf + 0.35f)), Vector3.zero, new Vector3(1.6f, 0.02f, 0.12f), lampMat);
            var sockets = new Transform[3];
            var lamps = new Renderer[3];
            for (var i = 0; i < 3; i++)
            {
                // three socket posts on the platform's corners (where the lift model has its socket housings),
                // facing out diagonally
                var corner = i == 0 ? new Vector3(1f, 0f, 1f) : i == 1 ? new Vector3(-1f, 0f, 1f) : new Vector3(1f, 0f, -1f);
                var dir = corner.normalized;
                var post = Group(bodyRoot, "SocketPost_" + i, corner * 1.02f, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg);
                Box(post, "Post", new Vector3(0f, 0.3f, 0f), new Vector3(0.25f, 0.6f, 0.2f), steel);
                sockets[i] = new GameObject("Socket_" + i).transform;
                sockets[i].SetParent(post, false);
                sockets[i].localPosition = new Vector3(0f, 0.3f, 0.14f);
                lamps[i] = Shape(post, "SocketLamp", PrimitiveType.Sphere, new Vector3(0f, 0.5f, 0f), Vector3.zero, Vector3.one * 0.12f, lampMat).GetComponent<Renderer>();
            }
            var beacon = PointLight(bodyRoot, "Beacon", new Vector3(0f, 1.6f, 0f), new Color(0.8f, 0.2f, 0.15f), 0.6f, 10f);
            // Tripo cargo lift (platform, tall pylon, corner socket housings) over the primitives, its deck
            // flush with the primitive platform's top; the primitives keep their colliders
            if (Dress(bodyRoot, ShipProp("SettleLift"), new Vector3(0f, -0.37f, 0f), new Vector3(2.6f, 3.4f, 2.6f), 0f, true) != null)
                HideRenderers(bodyRoot, "Platform", "Pylon", "PylonBand", "MouthRim", "Post", "SocketLamp"); // the monitor shows the sockets

            // the big settlement monitor clamped round the pylon: four screens (one per side), rises and sinks
            // with the lift (it is under Body)
            const float faceW = 1.1f, faceH = 0.6f, displayY = 2.2f, half = faceW / 2f + 0.04f;
            var displayRoot = new GameObject("Display").transform;
            displayRoot.SetParent(bodyRoot, false);
            displayRoot.localPosition = new Vector3(0f, displayY, 0f);
            Shape(displayRoot, "Casing", PrimitiveType.Cube, Vector3.zero, Vector3.zero, new Vector3(faceW + 0.12f, faceH + 0.12f, faceW + 0.12f), Mat("GeneratorDark", new Color(0.1f, 0.1f, 0.1f)));
            var faces = new Transform[4];
            for (var f = 0; f < 4; f++)
            {
                var outward = Quaternion.Euler(0f, f * 90f, 0f) * Vector3.forward;
                Shape(displayRoot, "Screen" + f, PrimitiveType.Quad, outward * (half + 0.025f), new Vector3(0f, f * 90f + 180f, 0f), new Vector3(faceW, faceH, 1f), Mat("KioskScreen", new Color(0.01f, 0.04f, 0.02f), unlit: true));
                faces[f] = new GameObject("Face" + f).transform;
                faces[f].SetParent(displayRoot, false);
                faces[f].localPosition = outward * (half + 0.03f);
                faces[f].localRotation = Quaternion.Euler(0f, f * 90f + 180f, 0f); // UI faces its -Z: outwards
            }
            PointLight(displayRoot, "DisplayGlow", Vector3.down * 0.6f, new Color(0.45f, 1f, 0.55f), 0.5f, 4f);

            // ---- hatch lids, hinged on the east / west edges, closed flush with the floor ----
            Transform Lid(string name, float side)
            {
                var pivot = new GameObject(name).transform;
                pivot.SetParent(root.transform, false);
                pivot.localPosition = new Vector3(side * hatchHalf, 0f, 0f);
                Box(pivot, "Plate", new Vector3(-side * hatchHalf / 2f, -0.04f, 0f), new Vector3(hatchHalf, 0.08f, hatchHalf * 2f), hatch);
                Shape(pivot, "Stripe", PrimitiveType.Cube, new Vector3(-side * hatchHalf / 2f, 0.005f, 0f), Vector3.zero, new Vector3(hatchHalf * 0.8f, 0.01f, 0.25f), hazard);
                return pivot;
            }
            var lidA = Lid("LidA", 1f);
            var lidB = Lid("LidB", -1f);

            // ---- the ground ring between the hatch and the pit edge: jagged slabs that cave in ----
            var chunks = new List<Transform>();
            var rng = new System.Random(31);
            for (var gx = -pit; gx < pit - 0.01f; gx += 1f)
            for (var gz = -pit; gz < pit - 0.01f; gz += 1f)
            {
                var cx = gx + 0.5f; var cz = gz + 0.5f;
                if (Mathf.Abs(cx) < hatchHalf && Mathf.Abs(cz) < hatchHalf) continue;
                var slab = Box(root.transform, "GroundChunk", new Vector3(cx, -0.25f, cz), new Vector3(1.02f, 0.5f, 1.02f), ground);
                slab.transform.localRotation = Quaternion.Euler((float)rng.NextDouble() * 4f - 2f, (float)rng.NextDouble() * 30f - 15f, (float)rng.NextDouble() * 4f - 2f);
                chunks.Add(slab.transform);
            }
            // order: closest to the hatch give way first
            chunks.Sort((p, q) => p.localPosition.sqrMagnitude.CompareTo(q.localPosition.sqrMagnitude));

            // ---- crack lines running out from the hatch (grow along local Z) ----
            var cracks = new List<Transform>();
            for (var k = 0; k < 9; k++)
            {
                var c = new GameObject("Crack").transform;
                c.SetParent(root.transform, false);
                c.localPosition = new Vector3(0f, 0.012f, 0f);
                var heading = k * 40f + (float)rng.NextDouble() * 25f;
                c.localRotation = Quaternion.Euler(0f, heading, 0f);
                // distance from the centre to the square pit's edge along this heading
                var edgeAt = pit / Mathf.Max(Mathf.Abs(Mathf.Cos(heading * Mathf.Deg2Rad)), Mathf.Abs(Mathf.Sin(heading * Mathf.Deg2Rad)));
                var length = 2.4f + (float)rng.NextDouble() * 2.2f;
                // the inner part runs over the slabs that cave in (hidden once they fall), the outer part
                // stays on the solid floor around the shaft
                var bend = (float)rng.NextDouble() * 14f - 7f;
                Shape(c, "Inner", PrimitiveType.Cube, new Vector3(0f, 0f, (hatchHalf + edgeAt) / 2f), new Vector3(0f, bend, 0f), new Vector3(0.07f, 0.01f, edgeAt - hatchHalf), holeMat);
                Shape(c, "Outer", PrimitiveType.Cube, new Vector3(0f, 0f, edgeAt + 0.05f + length / 2f), Vector3.zero, new Vector3(0.05f, 0.01f, length), holeMat);
                c.localScale = new Vector3(1f, 1f, 0.001f);
                c.gameObject.SetActive(false);
                cracks.Add(c);
            }

            var hole = Shape(root.transform, "Abyss", PrimitiveType.Cube, new Vector3(0f, -24f, 0f), Vector3.zero, new Vector3(pit * 2f, 0.1f, pit * 2f), holeMat);
            Object.DestroyImmediate(hole.GetComponent<Collider>());
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
            transmitter.LidA = lidA;
            transmitter.LidB = lidB;
            transmitter.Chunks = chunks.ToArray();
            transmitter.Cracks = cracks.ToArray();
            var display = displayRoot.gameObject.AddComponent<TransmitterDisplay>();
            display.Transmitter = transmitter;
            display.Faces = faces;
            display.FaceSize = new Vector2(faceW, faceH);
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
            // Korean 90s appliance model (Tripo) over the box; the small charge light stays on the front.
            // Some models lie the other way round (the toaster is long along its depth, its box is wide):
            // turn the model a quarter so its long side matches the box instead of shrinking it to fit.
            var fitSize = bodySize;
            var fitYaw = 0f;
            var source = PropModel(ShipProp("Kr" + name));
            if (source != null)
            {
                var probe = (GameObject)Object.Instantiate(source);
                var raw = Bounds(probe).size;
                Object.DestroyImmediate(probe);
                var modelWide = raw.x > raw.z * 1.15f;
                var modelDeep = raw.z > raw.x * 1.15f;
                var boxWide = bodySize.x > bodySize.z * 1.15f;
                var boxDeep = bodySize.z > bodySize.x * 1.15f;
                if ((modelWide && boxDeep) || (modelDeep && boxWide))
                {
                    fitYaw = 90f;
                    fitSize = new Vector3(bodySize.z, bodySize.y, bodySize.x);
                }
            }
            var model = Dress(root.transform, ShipProp("Kr" + name), bodyCenter - Vector3.up * bodySize.y / 2f, fitSize, fitYaw, true);
            if (model != null)
            {
                HideRenderers(root.transform, "Body");
                // the charge light shrinks to a small LED on the model's front, lower right corner
                var mb = Bounds(model.gameObject);
                var min = root.transform.InverseTransformPoint(mb.min);
                var max = root.transform.InverseTransformPoint(mb.max);
                var lo = Vector3.Min(min, max);
                var hi = Vector3.Max(min, max);
                // the boxy ones have an open screen / window in the scan: a dark core well inside shows as dark
                // glass there. Rounded models get none (a box would poke out through their curves).
                if (name == "TV" || name == "Microwave")
                {
                    var core = Shape(root.transform, "Core", PrimitiveType.Cube, (lo + hi) / 2f, Vector3.zero, (hi - lo) * 0.78f, Mat("ScreenGlass", new Color(0.03f, 0.04f, 0.045f)));
                    core.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                }
                // the charge light: a small LED dot sitting on the model's actual front surface (found with a
                // ray against the mesh), low on the right, instead of floating at the bounding box corner
                indicator.transform.localScale = new Vector3(0.022f, 0.016f, 0.006f);
                indicator.transform.localPosition = SurfacePoint(root.transform, model, new Vector2(Mathf.Lerp(lo.x, hi.x, 0.75f), Mathf.Lerp(lo.y, hi.y, 0.25f)), hi.z)
                    ?? SurfacePoint(root.transform, model, new Vector2((lo.x + hi.x) / 2f, (lo.y + hi.y) / 2f), hi.z)
                    ?? new Vector3((lo.x + hi.x) / 2f, (lo.y + hi.y) / 2f, hi.z);
            }
            var anchor = new Vector3(0f, bodyCenter.y * 0.7f, bodyCenter.z - bodySize.z / 2f - 0.01f);
            return FinishAppliance(root, name, nameKey, capacity, value, weight, tiny, cord, anchor, indicator.GetComponent<Renderer>(), bodyCenter.y * 2f);
        }

        // Where a ray from the front (local +Z, at local x/y) meets the model's mesh, in the root's local space,
        // a hair in front of the surface. Null when it misses.
        private static Vector3? SurfacePoint(Transform root, Transform model, Vector2 xy, float frontZ)
        {
            var probes = new List<MeshCollider>();
            foreach (var f in model.GetComponentsInChildren<MeshFilter>()) { var mc = f.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = f.sharedMesh; probes.Add(mc); }
            Physics.SyncTransforms();
            Vector3? result = null;
            var origin = root.TransformPoint(new Vector3(xy.x, xy.y, frontZ + 0.3f));
            var best = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(origin, -root.forward, 1f))
            {
                if (!probes.Contains(hit.collider as MeshCollider) || hit.distance >= best) continue;
                best = hit.distance;
                result = root.InverseTransformPoint(hit.point + root.forward * 0.002f);
            }
            foreach (var mc in probes) Object.DestroyImmediate(mc);
            return result;
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
            // a Korean standing fan (Tripo), a bit taller than the old shapes
            if (Dress(root.transform, ShipProp("KrFan"), Vector3.zero, new Vector3(0.5f, 1.05f, 0.4f), 0f, true) != null)
            {
                HideRenderers(root.transform, "Base", "Pole", "Head");
                collider.center = new Vector3(0f, 0.52f, 0f);
                collider.size = new Vector3(0.5f, 1.04f, 0.4f);
                hub.transform.localPosition = new Vector3(0f, 0.15f, 0.2f);
                hub.transform.localScale = Vector3.one * 0.07f;
            }
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
            // modelled plug (tools/blender/make_plug.py: round two-pin plug, pins towards +Z) over the primitives
            var plugModel = Dress(visual, ShipProp("CordPlug"), new Vector3(0f, -0.033f, 0f), new Vector3(0.066f, 0.066f, 0.127f), 0f, true);
            if (plugModel != null)
            {
                HideRenderers(visual, "Head", "PinL", "PinR");
                // the FBX carries no colours: black plastic body, rubber ribs and tail, brass-ish pins
                foreach (var r in plugModel.GetComponentsInChildren<Renderer>())
                {
                    var mats = r.sharedMaterials;
                    for (var m = 0; m < mats.Length; m++)
                    {
                        var n = mats[m] != null ? mats[m].name : string.Empty;
                        mats[m] = n.Contains("Pins") ? Mat("PlugPins", new Color(0.75f, 0.68f, 0.5f))
                            : n.Contains("Rubber") ? Mat("PlugRubber", new Color(0.05f, 0.05f, 0.055f))
                            : Mat("PlugPlastic", new Color(0.1f, 0.1f, 0.11f));
                    }
                    r.sharedMaterials = mats;
                }
            }
            // the cord itself: a round rubber tube rebuilt every frame by CordPlug (the line is the fallback)
            var tube = new GameObject("CordTube");
            tube.transform.SetParent(root.transform, false);
            var tubeFilter = tube.AddComponent<MeshFilter>();
            var tubeRenderer = tube.AddComponent<MeshRenderer>();
            tubeRenderer.sharedMaterial = Mat("CordRubber", new Color(0.05f, 0.05f, 0.055f));
            tubeRenderer.shadowCastingMode = ShadowCastingMode.Off;
            var plug = root.AddComponent<CordPlug>();
            plug.Appliance = appliance;
            plug.Cord = line;
            plug.CordMesh = tubeFilter;
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
            if (Dress(root.transform, ShipProp("ShipMic"), Vector3.zero, new Vector3(0.3f, 0.36f, 0.3f), 0f, true) != null)
                HideRenderers(root.transform, "Base", "Stem", "Head");
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
        // Reusable by the scene builder and by a targeted editor refresh; never rewrites unrelated prefabs.
        public static void RefreshShipPresentation()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
            var panel = Object.FindFirstObjectByType<CctvPanel>();
            if (panel != null) FitPreviewBoard(panel.transform);
            var door = Object.FindFirstObjectByType<PoweredDoor>();
            if (door != null && door.Buttons != null && door.Buttons.Length > 1)
            {
                var button = door.Buttons[1].transform;
                button.localPosition = new Vector3(0.34f, 1.3f, -(DoorWidth / 2f + 0.12f));
                DressDoorButton(button);
                EditorUtility.SetDirty(button);
            }
            var map = GameObject.Find(Prefix + "Map");
            if (map != null) BuildShipBreach(map.transform);
            Physics.SyncTransforms();
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
        }

        private static void FitPreviewBoard(Transform root)
        {
            var board = root.Find("StatusBoard");
            if (board != null)
            {
                board.localPosition = new Vector3(0f, 2.19f, -0.52f);
                board.localScale = new Vector3(3.04f, 1.15f, 0.04f);
            }
            for (var i = 0; i < CctvWall.Columns * CctvWall.Rows; i++)
            {
                var tile = root.Find("Tile" + (i + 1));
                if (tile == null) continue;
                tile.localPosition = new Vector3(-1.1325f + (i % 4) * 0.755f, 2.4725f - (i / 4) * 0.565f, -0.495f);
                tile.localScale = new Vector3(0.745f, 0.555f, 1f);
            }
        }

        private static void BuildShipBreach(Transform map)
        {
            var previous = map.Find("ShipImpactBreach");
            if (previous != null) Object.DestroyImmediate(previous.gameObject);
            // Break back the last 1.6 m of the corridor side walls, exposing the actual collision seam.
            // Keep their far ends fixed. Renaming makes repeated targeted refreshes idempotent.
            foreach (var wall in map.GetComponentsInChildren<BoxCollider>())
            {
                if (wall.name != "CorridorWall") continue;
                var b = wall.bounds;
                if (Mathf.Abs(b.min.x - (Ship.Door.x - 0.15f)) > 0.2f || b.size.x < 3f
                    || Mathf.Abs(Mathf.Abs(b.center.z - Ship.Door.z) - 2f) > 0.2f) continue;
                wall.transform.position += Vector3.right * 0.8f;
                wall.transform.localScale -= Vector3.right * 1.6f;
                wall.name = "ImpactTrimmedCorridorWall";
            }
            var root = Group(map, "ShipImpactBreach", Ship.Door, 0f);
            var concrete = Mat("WallConcrete", new Color(0.5f, 0.5f, 0.5f), tex: Texture("Concrete"), tiling: 8f, tilingY: 1.5f);
            var hull = Mat("ShipHull", new Color(0.42f, 0.45f, 0.5f), tex: Texture("Metal"), tiling: 3f, tilingY: 1f);
            var charred = Mat("GeneratorDark", new Color(0.1f, 0.1f, 0.1f));
            // The cabin nose has punched through the building plane. Folded plates bridge the seam;
            // masonry is thrown into the hallway along the direction of impact (+X).
            for (var side = -1; side <= 1; side += 2)
            {
                for (var i = 0; i < 5; i++)
                {
                    var z = side * (2.05f + i * 0.12f);
                    Shape(root, "TornHull", PrimitiveType.Cube, new Vector3(0.15f + i * 0.08f, 0.55f + i * 0.63f, z),
                        new Vector3(side * (12f + i * 7f), 10f + i * 3f, side * 14f), new Vector3(1.15f, 0.66f, 0.09f), hull);
                    Shape(root, "FracturedWall", PrimitiveType.Cube, new Vector3(0.55f, 0.4f + i * 0.7f, side * (2.3f + (i % 2) * 0.2f)),
                        new Vector3(i * 13f, side * 18f, 9f + i * 6f), new Vector3(0.65f, 0.86f, 0.72f), concrete);
                    Shape(root, "ExposedRebar", PrimitiveType.Cube, new Vector3(0.9f, 0.7f + i * 0.58f, side * (2.15f + i * 0.08f)),
                        new Vector3(side * 21f, 37f + i * 9f, 65f), new Vector3(0.045f, 1.2f, 0.045f), charred);
                }
                for (var i = 0; i < 12; i++)
                {
                    var forward = 0.25f + (i % 4) * 0.57f;
                    var z = side * (1.75f + (i / 4) * 0.48f);
                    Shape(root, "EjectedMasonry", PrimitiveType.Cube, new Vector3(forward, 0.07f + (i % 3) * 0.035f, z),
                        new Vector3(9f + i * 17f, i * 47f, i * 23f), new Vector3(0.25f + (i % 3) * 0.12f, 0.15f, 0.24f + (i % 2) * 0.18f), concrete);
                }
                Shape(root, "ImpactSkid", PrimitiveType.Cube, new Vector3(1.25f, 0.006f, side * 1.25f),
                    Vector3.zero, new Vector3(2.8f, 0.008f, 0.16f), charred);
            }
            for (var i = 0; i < 5; i++)
                Shape(root, "BrokenLintel", PrimitiveType.Cube, new Vector3(0.35f + (i % 2) * 0.25f, 3.55f + (i % 2) * 0.12f, -1.6f + i * 0.8f),
                    new Vector3(i * 7f, 11f, 14f - i * 6f), new Vector3(0.72f, 0.38f, 0.86f), concrete);
            // Visual fragments never snag players, appliances, or the door; the existing building
            // wall owns collision. The central 3 m doorway and both button approaches remain clear.
            foreach (var collider in root.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(collider);
            // Solid torn hull edges enclose the widened seam. They are visible, sit outside the
            // 3 m entrance, and do not create invisible walls or loose physics clutter.
            for (var side = -1; side <= 1; side += 2)
                Shape(root, "FoldedHullEdge", PrimitiveType.Cube, new Vector3(0.65f, 1.95f, side * 2.5f),
                    new Vector3(side * 3f, 0f, side * 4f), new Vector3(1.9f, 3.9f, 0.24f), hull);
            // Keep even rotated debris completely outside the cabin-side wall plane.
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
            {
                var penetration = Ship.Door.x + 0.24f - renderer.bounds.min.x;
                if (penetration > 0f) renderer.transform.position += Vector3.right * penetration;
            }
        }

    }
}
