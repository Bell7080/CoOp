using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CoOpHorror.Editor
{
    // Low-poly CC0 prop models (Kenney, Quaternius; see Assets/_Game/ThirdParty/*/License.txt) dressed over the
    // primitive blockout. The primitives keep their colliders (NavMesh and hiding spaces stay exactly the same);
    // their renderers are dropped and a model is fitted into the same box. Colours are pulled towards the
    // grimy, dark industrial palette and textures are point filtered at low resolution for the PS1 look.
    public static partial class ElecMolePrototypeSetup
    {
        private const string ThirdPartyDir = "Assets/_Game/ThirdParty";
        private const string PropMaterialDir = MaterialDir + "/Props";
        private const string ShipPropDir = "Assets/_Game/Art/Props";

        // Tripo-made ship equipment (paid credits; tools/blender/glb_to_prop.py), used through Dress().
        private static string ShipProp(string name) => $"{ShipPropDir}/{name}/{name}";

        // Hides the named primitives' renderers but keeps their colliders and transforms (logic still uses them).
        private static void HideRenderers(Transform root, params string[] names)
        {
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                if (System.Array.IndexOf(names, r.name) >= 0 && !IsModel(r.transform)) r.enabled = false;
        }

        private static readonly Dictionary<string, Material> PropMaterials = new();
        private static readonly Color PropTint = new(0.78f, 0.76f, 0.72f);

        private static GameObject PropModel(string path)
        {
            var full = path.StartsWith("Assets/") ? path + ".fbx" : $"{ThirdPartyDir}/{path}.fbx";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(full);
            if (model == null) Debug.LogWarning($"[ElecMole] Missing prop model {path}");
            return model;
        }

        // Low-res, point-filtered imports for every third-party texture.
        private static void PreparePropTextures()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ThirdPartyDir, ShipPropDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer) continue;
                var maxSize = path.Contains("Quaternius") || path.Contains("/Art/Props/") ? 128 : 64;
                if (importer.filterMode == FilterMode.Point && importer.maxTextureSize == maxSize && !importer.mipmapEnabled) continue;
                importer.filterMode = FilterMode.Point;
                importer.maxTextureSize = maxSize;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
        }

        // Places `model` so that it fills the local box (bottom centre at `bottom`, size `size`) under `parent`.
        // uniform = keep the model's proportions (fits inside the box, sits on its floor).
        private static Transform Dress(Transform parent, string model, Vector3 bottom, Vector3 size, float yaw = 0f, bool uniform = false)
        {
            var source = PropModel(model);
            if (source == null) return null;
            var holder = new GameObject("Model_" + Path.GetFileName(model)).transform;
            holder.SetParent(parent, false);
            holder.localPosition = bottom;
            holder.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var fit = new GameObject("Fit").transform;
            fit.SetParent(holder, false);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            instance.transform.SetParent(fit, false);

            // Measure in holder space (holder temporarily at the world origin).
            var keepParent = holder.parent;
            holder.SetParent(null, false);
            holder.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var bounds = Bounds(instance);
            var scale = new Vector3(size.x / Mathf.Max(bounds.size.x, 0.001f), size.y / Mathf.Max(bounds.size.y, 0.001f), size.z / Mathf.Max(bounds.size.z, 0.001f));
            if (uniform)
            {
                var min = float.MaxValue;
                for (var axis = 0; axis < 3; axis++) if (bounds.size[axis] > 0.01f) min = Mathf.Min(min, scale[axis]);
                scale = Vector3.one * (min == float.MaxValue ? 1f : min);
            }
            else
                for (var axis = 0; axis < 3; axis++) if (bounds.size[axis] <= 0.01f) scale[axis] = 1f;
            fit.localScale = scale;
            fit.localPosition = -Vector3.Scale(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z), scale);
            holder.SetParent(keepParent, false);
            holder.localPosition = bottom;
            holder.localRotation = Quaternion.Euler(0f, yaw, 0f);

            foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
            {
                var materials = renderer.sharedMaterials;
                for (var i = 0; i < materials.Length; i++) materials[i] = PropMaterial(model, materials[i]);
                renderer.sharedMaterials = materials;
            }
            foreach (var collider in instance.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(collider);
            return holder;
        }

        private static Bounds Bounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        // URP Lit copy of an imported material, greyed and darkened towards the level's palette.
        private static Material PropMaterial(string model, Material source)
        {
            if (source == null) return Mat("GeneratorDark", new Color(0.1f, 0.1f, 0.1f));
            var pack = model.Split('/')[0];
            var key = pack + "_" + source.name;
            if (PropMaterials.TryGetValue(key, out var cached) && cached != null) return cached;
            Directory.CreateDirectory(PropMaterialDir);
            var path = $"{PropMaterialDir}/{key}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            var color = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : source.HasProperty("_Color") ? source.color : Color.white;
            var texture = source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") : source.mainTexture;
            var gray = color.r * 0.3f + color.g * 0.59f + color.b * 0.11f;
            // Kenney's furniture colours are bright pastel; push them further into the grime.
            // The factory / station packs and the sci-fi barrels are just as toy-bright.
            var furniture = pack.StartsWith("Kenney") || pack == "QuaterniusSciFi";
            color = Color.Lerp(color, new Color(gray, gray, gray), furniture ? 0.5f : 0.35f) * PropTint * (furniture ? 0.62f : 1f);
            color.a = 1f;
            material.SetColor("_BaseColor", color);
            material.SetTexture("_BaseMap", texture);
            material.SetTextureScale("_BaseMap", Vector2.one);
            material.SetFloat("_Smoothness", 0.05f);
            material.SetFloat("_Metallic", 0f);
            // Tripo models (Art/Props) have open faces here and there (the TV's screen): draw both sides so
            // they never look see-through.
            material.SetFloat("_Cull", pack == "Assets" ? 0f : 2f);
            material.doubleSidedGI = pack == "Assets";
            EditorUtility.SetDirty(material);
            PropMaterials[key] = material;
            return material;
        }

        // Drops the blockout's look but keeps its colliders: renderers are removed, visual-only primitives deleted.
        private static void Unrender(Transform root)
        {
            var doomed = new List<GameObject>();
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
            {
                if (IsModel(renderer.transform)) continue;
                if (renderer.GetComponent<Collider>() == null) { doomed.Add(renderer.gameObject); continue; }
                var filter = renderer.GetComponent<MeshFilter>();
                Object.DestroyImmediate(renderer);
                if (filter != null) Object.DestroyImmediate(filter);
            }
            foreach (var go in doomed) Object.DestroyImmediate(go);
        }

        // Clutter: a model with a simple box collider (or none, for flat or tiny things).
        private static Transform Clutter(Transform parent, string model, Vector3 position, float yaw, float height, bool solid = true)
        {
            var group = Group(parent, "Clutter", position, yaw);
            var source = PropModel(model);
            if (source == null) return group;
            var probe = (GameObject)Object.Instantiate(source);
            var size = Bounds(probe).size;
            Object.DestroyImmediate(probe);
            var s = size.y < 0.05f ? 1f : height / size.y;
            var fitted = size * s;
            var holder = Dress(group, model, Vector3.zero, fitted, 0f, true);
            if (solid && height > 0.25f)
            {
                var box = group.gameObject.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, fitted.y / 2f, 0f);
                box.size = fitted;
            }
            return group;
        }

        private static bool IsModel(Transform t)
        {
            for (; t != null; t = t.parent) if (t.name.StartsWith("Model_")) return true;
            return false;
        }

        private static void Dressed(Transform group, string model, Vector3 bottom, Vector3 size, float yaw = 0f, bool uniform = false)
        {
            if(group.name=="Desk"||group.name=="Table"||group.name=="Bench")
            {
                Unrender(group);
                DressOpenFurniture(group,bottom,size);
                FitFurnitureCollision(group);
                return;
            }
            if (PropModel(model) == null) return; // keep the blockout look when a model is missing
            Unrender(group);
            Dress(group, model, bottom, size, yaw, uniform);
        }

        // Yaw that turns a Kenney model's front towards local -Z (where the player stands / sits).
        private const float FrontYaw = 0f;
        private const float StationFrontYaw = 0f;

        // Set dressing, placed on purpose so it reads as part of the room (a bin corner, a cone line in front of
        // a hazard zone, tyres stacked in a corner), never strewn about. Small or flat things have no collider.
        private static void Scatter(Transform map)
        {
            var root = new GameObject("Clutter").transform;
            root.SetParent(map, false);
            // break room (Korean office pantry): the kimchi fridge next to the big fridge
            Clutter(root, ShipProp("KrKimchiFridge"), new Vector3(3.2f, 0f, 9.1f), 180f, 0.9f);
            // break room: the bin corner by the door
            Clutter(root, "QuaterniusHouse/Trashcan_Large", new Vector3(16.8f, 0f, -8.6f), 0f, 1.05f);
            Clutter(root, "QuaterniusZombie/TrashBag_1", new Vector3(15.9f, 0f, -9.2f), 30f, 0.65f, false);
            Clutter(root, "QuaterniusZombie/TrashBag_2", new Vector3(16.9f, 0f, -7.6f), 80f, 0.6f, false);
            Clutter(root, "KenneyFurniture/radio", new Vector3(5.3f, 1.05f, -4f), 160f, 0.24f, false);
            Clutter(root, "KenneyFurniture/televisionVintage", new Vector3(16.6f, 0.95f, 9.2f), 180f, 0.5f, false);
            // south hall: a coned-off hazard strip
            for (var k = 0; k < 4; k++)
                Clutter(root, "QuaterniusZombie/TrafficCone_1", new Vector3(-12f + k * 1.6f, 0f, -20.4f), k * 25f, 0.95f, false);
            // storage: tyres in the corner, cinder blocks stacked against the wall
            Clutter(root, "QuaterniusZombie/Wheels_Stack", new Vector3(16.5f, 0f, -12f), 0f, 0.9f);
            for (var k = 0; k < 3; k++)
                Clutter(root, "QuaterniusZombie/CinderBlock", new Vector3(8.4f, k * 0.23f, -27.5f), k % 2 * 90f, 0.22f, false);
            // boiler room: spare pipes against the wall, the stain where something happened
            Clutter(root, "QuaterniusZombie/Pipes", new Vector3(20.6f, 0f, -12.5f), 90f, 0.7f);
            Clutter(root, "QuaterniusZombie/Blood_2", new Vector3(25f, 0.01f, -18f), 0f, 0.02f, false);
            // dorm: night stands between the beds
            Clutter(root, "QuaterniusHouse/NightStand_1", new Vector3(-35.2f, 0f, -5.5f), 90f, 0.65f);
            Clutter(root, "QuaterniusHouse/NightStand_1", new Vector3(-35.2f, 0f, 5.5f), 90f, 0.65f);
            // offices: plants and a coat rack by the door, bins under the desks' ends
            Clutter(root, "QuaterniusHouse/Houseplant_4", new Vector3(-1f, 0f, 8.5f), 0f, 1.1f);
            Clutter(root, "KenneyFurniture/coatRackStanding", new Vector3(-17f, 0f, -8.5f), 0f, 1.9f);
            Clutter(root, "KenneyFurniture/trashcan", new Vector3(-12.6f, 0f, -5.9f), 0f, 0.5f, false);
            Clutter(root, "KenneyFurniture/bookcaseClosedWide", new Vector3(-27f, 0f, 27.3f), 180f, 2.1f);
            Clutter(root, "QuaterniusHouse/Houseplant_4", new Vector3(-35f, 0f, 11f), 0f, 1.1f);
            // east warehouse: a traffic post at the loading lane
            Clutter(root, "KenneyFactory/warning-traffic", new Vector3(19.5f, 0f, -8.5f), 0f, 1.3f);
            // north corridor: bags waiting for collection at the end of the lockers
            Clutter(root, "QuaterniusZombie/TrashBag_2", new Vector3(-9.6f, 0f, 27.2f), 0f, 0.6f, false);
            Clutter(root, "QuaterniusZombie/TrashBag_1", new Vector3(-10.4f, 0f, 26.8f), 50f, 0.6f, false);
            // server room: cable runs between the rack rows
            Clutter(root, "QuaterniusSciFi/Prop_Cable_1", new Vector3(9f, 0f, 19f), 0f, 0.12f, false);
            Clutter(root, "QuaterniusSciFi/Prop_Cable_1", new Vector3(5f, 0f, 19.5f), 180f, 0.12f, false);
            // transmitter room
            Clutter(root, "QuaterniusZombie/Pipes", new Vector3(35f, 0f, 15f), 0f, 0.7f);
            // then fill what is still bare along the walls of every room and corridor
            DressEmptyRooms(root);
        }
    }
}
