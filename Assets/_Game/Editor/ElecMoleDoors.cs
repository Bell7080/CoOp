using CoOpHorror.Gameplay;
using UnityEngine;

namespace CoOpHorror.Editor
{
    // Hinged wooden doors in some of the 3 m doorways: double doors, or a single leaf next to a short wall.
    // They are NetworkObjects at the scene root (built after the NavMesh bake, so paths go straight through
    // and monsters shove them open).
    public static partial class ElecMolePrototypeSetup
    {
        private readonly struct SwingDef
        {
            public readonly Vector3 Position;
            public readonly bool AlongZ;   // the wall runs along Z (walking through goes along X)
            public readonly bool Double;
            public readonly bool OneWay;
            public SwingDef(Vector3 position, bool alongZ, bool isDouble, bool oneWay = false) { Position = position; AlongZ = alongZ; Double = isDouble; OneWay = oneWay; }
        }

        private static readonly SwingDef[] SwingDoors =
        {
            new(new Vector3(-18f, 0f, 0f), true, true),    // dorm <-> office A
            new(new Vector3(0f, 0f, 0f), true, false),     // office A <-> break room
            new(new Vector3(0f, 0f, 19f), true, true),     // north corridor <-> server room
            new(new Vector3(-9f, 0f, -10f), false, false), // south hall <-> office A
            new(new Vector3(9f, 0f, 10f), false, true),    // break room <-> server room
            new(new Vector3(-27f, 0f, 10f), false, false, true), // dorm -> office B (opens into office B only)
            new(new Vector3(27f, 0f, -10f), false, true),  // boiler room <-> east warehouse
        };

        private static void BuildSwingDoors()
        {
            for (var i = 0; i < SwingDoors.Length; i++) BuildSwingDoor(i, SwingDoors[i]);
            // Wardrobes (jangnong): step in, click the door shut, and nothing outside can see you.
            BuildWardrobe(0, new Vector3(-24.6f, 0f, 9.06f), 180f);
            BuildWardrobe(1, new Vector3(-21.2f, 0f, 9.06f), 180f);
            BuildWardrobe(2, new Vector3(-20.2f, 0f, 27.06f), 180f);
            BuildWardrobe(3, new Vector3(18.94f, 0f, 25f), 90f); // transmitter room, west wall
        }

        // A walk-in wardrobe: three walls and a roof (carved out of monster paths) and one outward door.
        private static void BuildWardrobe(int index, Vector3 position, float yaw)
        {
            var root = NetRoot($"Wardrobe_{index:00}", position, yaw);
            var wood = Mat("WardrobeWood", new Color(0.42f, 0.28f, 0.18f), tex: Texture("Wood"), tiling: 1f);
            var trim = Mat("DoorFrame", new Color(0.82f, 0.78f, 0.7f), tex: Texture("Wood"), tiling: 1f);
            var metal = Mat("ApplianceMetal", new Color(0.6f, 0.62f, 0.66f));
            const float w = 1.6f, d = 1.4f, h = 2.3f, t = 0.06f;
            // local +Z is the front (door side); the back sits against the wall
            Box(root.transform, "Back", new Vector3(0f, h / 2f, -d / 2f), new Vector3(w, h, t), wood);
            Box(root.transform, "SideL", new Vector3(-w / 2f, h / 2f, 0f), new Vector3(t, h, d), wood);
            Box(root.transform, "SideR", new Vector3(w / 2f, h / 2f, 0f), new Vector3(t, h, d), wood);
            Box(root.transform, "Top", new Vector3(0f, h, 0f), new Vector3(w + 0.1f, 0.08f, d + 0.1f), trim);
            Shape(root.transform, "HangerRail", PrimitiveType.Cylinder, new Vector3(0f, h - 0.3f, -0.1f), new Vector3(0f, 0f, 90f), new Vector3(0.03f, w / 2f - 0.05f, 0.03f), metal);
            var carve = new GameObject("PathBlock").AddComponent<UnityEngine.AI.NavMeshObstacle>();
            carve.transform.SetParent(root.transform, false);
            carve.transform.localPosition = new Vector3(0f, h / 2f, 0f);
            carve.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
            carve.size = new Vector3(w, h, d);
            carve.carving = true;
            var pivot = new GameObject("LeafA").transform;
            pivot.SetParent(root.transform, false);
            pivot.localPosition = new Vector3(w / 2f - 0.02f, 0f, d / 2f);
            Box(pivot, "Panel", new Vector3(-(w - 0.06f) / 2f, h / 2f, 0f), new Vector3(w - 0.08f, h - 0.06f, 0.05f), wood);
            Shape(pivot, "Inset", PrimitiveType.Cube, new Vector3(-(w - 0.06f) / 2f, h * 0.55f, 0.03f), Vector3.zero, new Vector3(w * 0.7f, h * 0.65f, 0.02f), trim);
            Shape(pivot, "Handle", PrimitiveType.Cube, new Vector3(-(w - 0.2f), 1.1f, 0.06f), Vector3.zero, new Vector3(0.03f, 0.25f, 0.03f), metal);
            var door = root.AddComponent<SwingDoor>();
            door.LeafA = pivot;
            door.SideA = 1f;
            door.LeafWidth = w - 0.06f;
            door.OneWay = true; // opens outwards
        }

        private static void BuildSwingDoor(int index, SwingDef def)
        {
            // Door space: X across the opening, Z = walking direction.
            var root = NetRoot($"SwingDoor_{index:00}", def.Position, def.AlongZ ? 90f : 0f);
            var wood = Mat("DoorWood", new Color(0.55f, 0.4f, 0.27f), tex: Texture("Wood"), tiling: 1f);
            var frame = Mat("DoorFrame", new Color(0.82f, 0.78f, 0.7f), tex: Texture("Wood"), tiling: 1f);
            var metal = Mat("ApplianceMetal", new Color(0.6f, 0.62f, 0.66f));
            const float height = 3.15f;
            var half = DoorWidth / 2f;
            // frame posts (visual)
            foreach (var x in new[] { -half, half })
                Shape(root.transform, "Jamb", PrimitiveType.Cube, new Vector3(x, height / 2f, 0f), Vector3.zero, new Vector3(0.1f, height, WallThickness + 0.06f), frame);

            Transform Leaf(string name, float hingeX, float side, float width)
            {
                var pivot = new GameObject(name).transform;
                pivot.SetParent(root.transform, false);
                pivot.localPosition = new Vector3(hingeX, 0f, 0f);
                var panel = Box(pivot, "Panel", new Vector3(-side * width / 2f, height / 2f, 0f), new Vector3(width - 0.04f, height - 0.05f, 0.07f), wood);
                Shape(pivot, "Inset", PrimitiveType.Cube, new Vector3(-side * width / 2f, height * 0.62f, 0f), Vector3.zero, new Vector3(width * 0.6f, height * 0.3f, 0.085f), frame);
                foreach (var z in new[] { -0.06f, 0.06f })
                    Shape(pivot, "Handle", PrimitiveType.Cube, new Vector3(-side * (width - 0.14f), 1.05f, z), Vector3.zero, new Vector3(0.12f, 0.03f, 0.03f), metal);
                return pivot;
            }

            var door = root.AddComponent<SwingDoor>();
            door.OneWay = def.OneWay;
            if (def.Double)
            {
                door.LeafWidth = half;
                door.SideA = 1f;
                door.SideB = -1f;
                door.LeafA = Leaf("LeafA", half, 1f, half);
                door.LeafB = Leaf("LeafB", -half, -1f, half);
            }
            else
            {
                // a short wall piece on one side, one wide leaf on the other
                const float filler = 1.3f;
                var wall = Box(root.transform, "Filler", new Vector3(half - filler / 2f, height / 2f, 0f), new Vector3(filler, height, WallThickness), Mat("WallPlaster", new Color(0.62f, 0.6f, 0.55f), tex: Texture("Wallpaper"), tiling: 6f, tilingY: 1.5f));
                var obstacle = wall.AddComponent<UnityEngine.AI.NavMeshObstacle>(); // paths keep to the open half
                obstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
                obstacle.center = Vector3.zero;
                obstacle.size = Vector3.one;
                obstacle.carving = true;
                door.LeafWidth = DoorWidth - filler;
                door.SideA = 1f;
                door.LeafA = Leaf("LeafA", half - filler, 1f, DoorWidth - filler);
            }
        }
    }
}
