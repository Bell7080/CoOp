using System.Collections.Generic;
using CoOpHorror.Gameplay;
using UnityEngine;

namespace CoOpHorror.Editor
{
    // Furniture you open the R.E.P.O. way (DragOpenable): lockers, wall cupboards, floor cupboards and chests
    // of drawers, built from boxes with real hinged doors and sliding drawers, hollow inside with shelves.
    // Each piece is its own NetworkObject at the scene root, built after the NavMesh bake (a carving obstacle
    // keeps paths around the carcass). Map code queues spots while it furnishes rooms (QueueOpenable) and
    // BuildQueuedOpenables builds them with the doors; other builders (e.g. a random stage generator) can call
    // BuildOpenable directly.
    // Every shelf and drawer floor has empty "StashSpot" children (local +Y up, sized by StashSpotSize): where
    // items may be spawned inside. Spots in drawers move with the drawer.
    // Convention: local origin at the bottom centre of the carcass, the front (doors) faces local +Z.
    public static partial class ElecMolePrototypeSetup
    {
        internal enum OpenableKind { Locker, WallCupboard, FloorCupboard, Drawers }

        internal const string StashSpotName = "StashSpot";

        private readonly struct OpenableSpot
        {
            public readonly OpenableKind Kind;
            public readonly Vector3 Position;
            public readonly float Yaw;
            public OpenableSpot(OpenableKind kind, Vector3 position, float yaw) { Kind = kind; Position = position; Yaw = yaw; }
        }

        private static readonly List<OpenableSpot> PendingOpenables = new();

        // Map furnishing: remember a spot (local to `parent`) and build it after the bake.
        private static void QueueOpenable(Transform parent, OpenableKind kind, Vector3 localPosition, float yaw)
        {
            var world = parent != null ? parent.TransformPoint(localPosition) : localPosition;
            var worldYaw = (parent != null ? parent.eulerAngles.y : 0f) + yaw;
            PendingOpenables.Add(new OpenableSpot(kind, world, worldYaw));
        }

        private static void BuildQueuedOpenables()
        {
            var counts = new Dictionary<OpenableKind, int>();
            foreach (var spot in PendingOpenables)
            {
                counts.TryGetValue(spot.Kind, out var n);
                counts[spot.Kind] = n + 1;
                BuildOpenable(spot.Kind, $"{spot.Kind}_{n:00}", spot.Position, spot.Yaw);
            }
            PendingOpenables.Clear();
        }

        internal static GameObject BuildOpenable(OpenableKind kind, string name, Vector3 position, float yaw)
        {
            switch (kind)
            {
                case OpenableKind.Locker: return BuildLockerOpenable(name, position, yaw);
                case OpenableKind.WallCupboard: return BuildCupboardOpenable(name, position, yaw, new Vector3(1.05f, 0.7f, 0.38f), 1, true);
                case OpenableKind.FloorCupboard: return BuildCupboardOpenable(name, position, yaw, new Vector3(1f, 0.9f, 0.5f), 1, false);
                default: return BuildDrawersOpenable(name, position, yaw, new Vector3(0.8f, 0.9f, 0.5f), 3);
            }
        }

        // A metal locker: one door hinged on the right, two shelves.
        private static GameObject BuildLockerOpenable(string name, Vector3 position, float yaw)
        {
            var root = NetRoot(name, position, yaw);
            var metal = Mat("Locker", new Color(0.35f, 0.42f, 0.4f), tex: Texture("Metal"), tiling: 1f);
            var dark = Mat("GeneratorDark", new Color(0.1f, 0.1f, 0.1f));
            var steel = Mat("ApplianceMetal", new Color(0.6f, 0.62f, 0.66f));
            var size = new Vector3(0.62f, 2f, 0.5f);
            Carcass(root.transform, size, 0.02f, metal, new[] { 1.45f, 1.75f });
            var door = HingedPanel(root.transform, "Door", size, 0.02f, true, size.x / 2f, size.y, metal);
            Shape(door, "Vents", PrimitiveType.Cube, new Vector3(-size.x / 2f, 1.7f, 0.02f), Vector3.zero, new Vector3(0.4f, 0.12f, 0.01f), dark);
            Shape(door, "Handle", PrimitiveType.Cube, new Vector3(-(size.x - 0.09f), 1.05f, 0.03f), Vector3.zero, new Vector3(0.025f, 0.16f, 0.03f), steel);
            Finish(root, "openable.locker", size, new DragPart { Pivot = door, Kind = DragPartKind.Hinge, Min = 0f, Max = 115f, Mass = 0.7f, Friction = 2f, Gravity = 12f });
            return root;
        }

        // A wooden cupboard with two doors that meet in the middle (or one door if narrow). Wall cupboards hang
        // with their bottom at `position`; their doors are light and drift open once unlatched.
        private static GameObject BuildCupboardOpenable(string name, Vector3 position, float yaw, Vector3 size, int shelves, bool wall)
        {
            var root = NetRoot(name, position, yaw);
            var wood = Mat("CupboardWood", new Color(0.78f, 0.72f, 0.62f), tex: Texture("Wood"), tiling: 1f);
            var trim = Mat("DoorFrame", new Color(0.82f, 0.78f, 0.7f), tex: Texture("Wood"), tiling: 1f);
            var steel = Mat("ApplianceMetal", new Color(0.6f, 0.62f, 0.66f));
            var shelfHeights = new List<float>();
            for (var i = 1; i <= shelves; i++) shelfHeights.Add(size.y * i / (shelves + 1f));
            Carcass(root.transform, size, 0.025f, wood, shelfHeights.ToArray());
            var parts = new List<DragPart>();
            var leaves = size.x > 0.7f ? 2 : 1;
            for (var l = 0; l < leaves; l++)
            {
                var right = l == 0;
                var leafWidth = leaves == 2 ? size.x / 2f : size.x;
                var pivot = HingedPanel(root.transform, right ? "DoorR" : "DoorL", new Vector3(leafWidth, size.y, size.z), 0.022f, right, right ? size.x / 2f : -size.x / 2f, size.y, wood);
                var side = right ? -1f : 1f; // towards the free edge
                Shape(pivot, "Inset", PrimitiveType.Cube, new Vector3(side * leafWidth / 2f, size.y / 2f, 0.016f), Vector3.zero, new Vector3(leafWidth * 0.72f, size.y * 0.72f, 0.008f), trim);
                Shape(pivot, "Knob", PrimitiveType.Cube, new Vector3(side * (leafWidth - 0.06f), wall ? 0.12f : size.y - 0.14f, 0.03f), Vector3.zero, new Vector3(0.022f, 0.08f, 0.025f), steel);
                parts.Add(new DragPart { Pivot = pivot, Kind = DragPartKind.Hinge, Min = right ? 0f : -110f, Max = right ? 110f : 0f, Mass = 0.45f, Friction = 2.4f, Gravity = wall ? 40f : 20f });
            }
            Finish(root, "openable.cupboard", size, parts.ToArray());
            return root;
        }

        // A chest of drawers: each drawer is a tray with a front that slides out along +Z.
        private static GameObject BuildDrawersOpenable(string name, Vector3 position, float yaw, Vector3 size, int count)
        {
            var root = NetRoot(name, position, yaw);
            var wood = Mat("DrawerWood", new Color(0.5f, 0.36f, 0.24f), tex: Texture("Wood"), tiling: 1f);
            var front = Mat("CupboardWood", new Color(0.78f, 0.72f, 0.62f), tex: Texture("Wood"), tiling: 1f);
            var steel = Mat("ApplianceMetal", new Color(0.6f, 0.62f, 0.66f));
            const float t = 0.025f;
            // carcass without shelves; a plinth at the bottom
            Carcass(root.transform, size, t, wood, System.Array.Empty<float>());
            var parts = new List<DragPart>();
            var plinth = 0.06f;
            var slot = (size.y - plinth - t) / count;
            var innerW = size.x - 2f * t - 0.01f;
            var innerD = size.z - t - 0.02f;
            for (var i = 0; i < count; i++)
            {
                var y = plinth + slot * i;
                var pivot = new GameObject($"Drawer{i}").transform;
                pivot.SetParent(root.transform, false);
                pivot.localPosition = new Vector3(0f, y, 0f);
                var h = slot - 0.02f;
                var trayH = h * 0.7f;
                Box(pivot, "Front", new Vector3(0f, h / 2f + 0.01f, size.z / 2f + 0.011f), new Vector3(size.x - 0.02f, h, 0.022f), front); // just proud of the carcass
                Box(pivot, "Bottom", new Vector3(0f, 0.015f, size.z / 2f - innerD / 2f), new Vector3(innerW, 0.012f, innerD), wood);
                Box(pivot, "Back", new Vector3(0f, trayH / 2f + 0.015f, size.z / 2f - innerD), new Vector3(innerW, trayH, 0.012f), wood);
                foreach (var sx in new[] { -1f, 1f })
                    Box(pivot, "Side", new Vector3(sx * innerW / 2f, trayH / 2f + 0.015f, size.z / 2f - innerD / 2f), new Vector3(0.012f, trayH, innerD), wood);
                Shape(pivot, "Pull", PrimitiveType.Cube, new Vector3(0f, h / 2f + 0.01f, size.z / 2f + 0.034f), Vector3.zero, new Vector3(0.16f, 0.022f, 0.025f), steel);
                StashSpot(pivot, new Vector3(0f, 0.025f, size.z / 2f - innerD / 2f), new Vector3(innerW - 0.04f, trayH - 0.02f, innerD - 0.04f));
                parts.Add(new DragPart { Pivot = pivot, Kind = DragPartKind.Slide, Min = 0f, Max = innerD * 0.8f, Mass = 0.8f, Friction = 6f, LatchBelow = 0.02f });
            }
            Finish(root, "openable.drawer", size, parts.ToArray());
            return root;
        }

        // ---------- parts ----------

        // Back, sides, top and bottom (all solid), open at the front, with shelves at the given heights and a
        // stash spot on the floor and on every shelf.
        private static void Carcass(Transform root, Vector3 size, float t, Material material, float[] shelves)
        {
            float w = size.x, h = size.y, d = size.z;
            Box(root, "Back", new Vector3(0f, h / 2f, -d / 2f + t / 2f), new Vector3(w, h, t), material);
            Box(root, "SideL", new Vector3(-w / 2f + t / 2f, h / 2f, 0f), new Vector3(t, h, d), material);
            Box(root, "SideR", new Vector3(w / 2f - t / 2f, h / 2f, 0f), new Vector3(t, h, d), material);
            Box(root, "Top", new Vector3(0f, h - t / 2f, 0f), new Vector3(w, t, d), material);
            Box(root, "Bottom", new Vector3(0f, t / 2f, 0f), new Vector3(w, t, d), material);
            var inner = new Vector3(w - 2f * t - 0.04f, 0f, d - t - 0.06f);
            var floors = new List<float> { t };
            foreach (var y in shelves)
            {
                Box(root, "Shelf", new Vector3(0f, y, 0f), new Vector3(w - 2f * t, t, d - t - 0.03f), material);
                floors.Add(y + t / 2f);
            }
            for (var i = 0; i < floors.Count; i++)
            {
                var top = i + 1 < floors.Count ? floors[i + 1] - t / 2f : h - t;
                StashSpot(root, new Vector3(0f, floors[i], -t / 2f), new Vector3(inner.x, Mathf.Max(0.05f, top - floors[i] - 0.03f), inner.z));
            }
        }

        // A door `size.x` wide hinged at hingeX on the front (z = size.z / 2) of a carcass; the door extends
        // towards -X from a right hinge and towards +X from a left one. Returns the pivot.
        private static Transform HingedPanel(Transform root, string name, Vector3 size, float thickness, bool hingeRight, float hingeX, float height, Material material)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(root, false);
            pivot.localPosition = new Vector3(hingeX - (hingeRight ? 0.005f : -0.005f), 0f, size.z / 2f + thickness / 2f);
            var toFree = hingeRight ? -1f : 1f;
            Box(pivot, "Panel", new Vector3(toFree * size.x / 2f, height / 2f, 0f), new Vector3(size.x - 0.012f, height - 0.012f, thickness), material);
            return pivot;
        }

        private static void StashSpot(Transform parent, Vector3 bottomCentre, Vector3 size)
        {
            var spot = new GameObject(StashSpotName).transform;
            spot.SetParent(parent, false);
            spot.localPosition = bottomCentre;
            spot.localScale = size; // read by item spawners: a box from the spot up by size.y
        }

        private static void Finish(GameObject root, string nameKey, Vector3 size, params DragPart[] parts)
        {
            var openable = root.AddComponent<DragOpenable>();
            openable.NameKey = nameKey;
            openable.Parts = parts;
            Obstacle(root, new Vector3(0f, size.y / 2f, 0f), size);
        }
    }
}
