using System.Collections.Generic;
using System.IO;
using CoOpHorror.Gameplay;
using UnityEditor;
using UnityEngine;

namespace CoOpHorror.Editor
{
    // ElecMole prototype level: a 72 x 56 m ruined building of twelve 18 m rooms (crashed ship, warehouse,
    // storage, boiler room, dorm, offices, break room, server room, transmitter room...) in a dark low-poly
    // PS1 style. Tiny generated textures with point filtering, hiding furniture (desks, beds, tables with
    // room underneath to crawl in), racks, crates, pipes and hazard stripes. Ceilings stay out of the NavMesh.
    public static partial class ElecMolePrototypeSetup
    {
        private const float WallHeight = 4f;
        private const float WallThickness = 0.4f;
        private const float DoorWidth = 3f;
        private const string TextureDir = MaterialDir + "/Textures";

        private static readonly float[] XCols = { -36f, -18f, 0f, 18f, 36f };
        private static readonly float[] ZRows = { -28f, -10f, 10f, 28f };

        private enum RoomKind { Ship, HallS, Storage, Lair, Dorm, OfficeA, Breakroom, WarehouseE, OfficeB, CorridorN, Server, TransmitterRoom }

        private readonly struct RoomDef
        {
            public readonly RoomKind Kind;
            public readonly int Col, Row;
            public readonly Vector2 LampOffset;
            public RoomDef(RoomKind kind, int col, int row, Vector2 lampOffset = default) { Kind = kind; Col = col; Row = row; LampOffset = lampOffset; }
            public Vector3 Center => new((XCols[Col] + XCols[Col + 1]) / 2f, 0f, (ZRows[Row] + ZRows[Row + 1]) / 2f);
        }

        private static readonly RoomDef[] Rooms =
        {
            new(RoomKind.Ship, 0, 0), new(RoomKind.HallS, 1, 0), new(RoomKind.Storage, 2, 0), new(RoomKind.Lair, 3, 0),
            new(RoomKind.Dorm, 0, 1), new(RoomKind.OfficeA, 1, 1), new(RoomKind.Breakroom, 2, 1), new(RoomKind.WarehouseE, 3, 1),
            new(RoomKind.OfficeB, 0, 2), new(RoomKind.CorridorN, 1, 2), new(RoomKind.Server, 2, 2), new(RoomKind.TransmitterRoom, 3, 2, new Vector2(-5f, -5f)),
        };

        // The dorm sofa, facing the TV cabinet (DormTvStand) from the north.
        private static Vector3 DormSofa => new(-25.5f, 0f, -6.4f);

        // The crashed ship (south-west room).
        private static class Ship
        {
            // A small box of a cabin (8 x 8 m, like REPO's truck / Lethal Company's ship) against the east wall
            // of its cell (the door stays in that wall); the rest of the cell behind the hull is sealed off.
            public static readonly Vector3 Min = new(-26f, 0f, -23f);
            public static readonly Vector3 Max = new(-18f, 0f, -15f);
            public static readonly Vector3 Door = new(-18f, 0f, -19f);
            public static readonly Vector3 Panel = new(-21.2f, 0f, -22.35f);
            public static readonly Vector3 Launch = new(-25.2f, 0f, -15.8f);
            public static readonly Vector3 Spawn = new(-21.5f, 1.05f, -18.4f);
            // the final power transmitter, against the north hull in the middle (faces into the cabin)
            public static readonly Vector3 Transmitter = new(-22f, 0f, -15.62f);
        }


        private readonly struct CameraDef
        {
            public readonly int Number;
            public readonly Vector3 Position;
            public readonly float Yaw, DefaultYaw;
            public CameraDef(int number, Vector3 position, float yaw, float defaultYaw) { Number = number; Position = position; Yaw = yaw; DefaultYaw = defaultYaw; }
        }

        private static readonly CameraDef[] Cameras =
        {
            new(1, new Vector3(-7.15f, 0f, -13.5f), -90f, 35f),  // south hall corridor, north arm
            new(2, new Vector3(-35.8f, 0f, 7.5f), 90f, 30f),     // dorm
            new(3, new Vector3(-7.15f, 0f, 6f), -90f, -35f),     // cross corridor (old office A), north arm
            new(4, new Vector3(17.8f, 0f, -7f), -90f, -35f),     // break room
            new(5, new Vector3(28.85f, 0f, -6f), -90f, 35f),     // corner corridor (old east warehouse)
            new(6, new Vector3(1f, 0f, 27.8f), 180f, -30f),      // server room
            new(7, new Vector3(18.2f, 0f, 13f), 90f, 40f),       // transmitter room
            new(8, new Vector3(35.8f, 0f, -26f), -90f, -35f),    // boiler room
        };

        // ---------------- map ----------------

        private static Transform BuildMap(out Light[] emergencyLights)
        {
            PlanLayout();
            PendingOpenables.Clear();
            var map = new GameObject(Prefix + "Map").transform;
            var ceilings = new GameObject(Prefix + "Ceilings").transform;
            var emergency = new List<Light>();

            var wallInner = Mat("WallPlaster", new Color(0.62f, 0.6f, 0.55f), tex: Texture("Wallpaper"), tiling: 6f, tilingY: 1.5f);
            var wallOuter = Mat("WallConcrete", new Color(0.5f, 0.5f, 0.5f), tex: Texture("Concrete"), tiling: 8f, tilingY: 1.5f);
            var ceiling = Mat("Ceiling", new Color(0.35f, 0.35f, 0.34f), tex: Texture("Concrete"), tiling: 6f);

            foreach (var room in Rooms)
            {
                var c = room.Center;
                var size = new Vector3(XCols[room.Col + 1] - XCols[room.Col], 0.5f, ZRows[room.Row + 1] - ZRows[room.Row]);
                Box(map, "Floor_" + room.Kind, new Vector3(c.x, -0.25f, c.z), size, FloorMaterial(room.Kind));
                // solid (collider kept): nothing - players, monsters, lifted appliances - goes out through the roof.
                // Ceilings are not under the map root, so they stay out of the NavMesh bake.
                Box(ceilings, "Ceiling_" + room.Kind, new Vector3(c.x, WallHeight + 0.15f, c.z), new Vector3(size.x, 0.3f, size.z), ceiling);
                if (room.Kind != RoomKind.Ship)
                {
                    var red = PointLight(map, "Emergency_" + room.Kind, c + new Vector3(4f, 3.5f, 4f), new Color(1f, 0.1f, 0.06f), 0.6f, 12f);
                    red.enabled = false;
                    emergency.Add(red);
                }
            }

            // Outer walls.
            var width = XCols[4] - XCols[0];
            var depth = ZRows[3] - ZRows[0];
            Box(map, "Wall_South", new Vector3(0f, WallHeight / 2f, ZRows[0]), new Vector3(width + WallThickness, WallHeight, WallThickness), wallOuter);
            Box(map, "Wall_North", new Vector3(0f, WallHeight / 2f, ZRows[3]), new Vector3(width + WallThickness, WallHeight, WallThickness), wallOuter);
            Box(map, "Wall_West", new Vector3(XCols[0], WallHeight / 2f, 0f), new Vector3(WallThickness, WallHeight, depth + WallThickness), wallOuter);
            Box(map, "Wall_East", new Vector3(XCols[4], WallHeight / 2f, 0f), new Vector3(WallThickness, WallHeight, depth + WallThickness), wallOuter);

            // Inner walls: a doorway in the middle of every wall except between the ship and the dorm.
            for (var i = 1; i <= 3; i++)
                for (var r = 0; r < 3; r++) WallWithDoorAlongZ(map, wallInner, XCols[i], ZRows[r], ZRows[r + 1], true);
            for (var j = 1; j <= 2; j++)
                for (var c = 0; c < 4; c++)
                    WallWithDoorAlongX(map, wallInner, ZRows[j], XCols[c], XCols[c + 1],
                        !(j == 1 && c == 0) && !DoorwayClosed(new Vector3((XCols[c] + XCols[c + 1]) / 2f, 0f, ZRows[j])));

            PreparePropTextures();
            foreach (var room in Rooms)
            {
                if (IsCorridor(room.Kind)) { DressCorridor(map, room); continue; }
                Furnish(map, room);
                var roomRoot = map.Find("Room_" + room.Kind);
                if (roomRoot == null || room.Kind == RoomKind.Ship) continue;
                ChooseRoomPattern(room, roomRoot);
                BuildBlockers(roomRoot, room);
            }
            BuildShipBreach(map);
            Scatter(map);
            CullBlocked(map.Find("Clutter"));
            emergencyLights = emergency.ToArray();
            return map;
        }

        private static Material FloorMaterial(RoomKind kind)
        {
            switch (kind)
            {
                case RoomKind.Ship:
                case RoomKind.Server:
                case RoomKind.TransmitterRoom:
                    return Mat("FloorMetal", new Color(0.5f, 0.52f, 0.55f), tex: Texture("Metal"), tiling: 9f);
                // Korean interiors: yellow vinyl floor (jangpan) at home, grey carpet tiles in the office,
                // small beige tiles in the pantry and the green epoxy of an old public building's corridor.
                case RoomKind.Breakroom:
                    return Mat("FloorTile", new Color(0.86f, 0.82f, 0.74f), tex: Texture("SmallTile"), tiling: 9f);
                case RoomKind.CorridorN:
                    return Mat("FloorEpoxy", new Color(0.42f, 0.6f, 0.45f), tex: Texture("Epoxy"), tiling: 6f);
                case RoomKind.Dorm:
                case RoomKind.OfficeB:
                    return Mat("FloorJangpan", new Color(0.95f, 0.82f, 0.55f), tex: Texture("Jangpan"), tiling: 6f);
                case RoomKind.OfficeA:
                    return Mat("FloorCarpet", new Color(0.55f, 0.57f, 0.6f), tex: Texture("Carpet"), tiling: 9f);
                default:
                    return Mat("FloorConcrete", new Color(0.45f, 0.45f, 0.43f), tex: Texture("Concrete"), tiling: 9f);
            }
        }

        private static void WallWithDoorAlongZ(Transform parent, Material material, float x, float z0, float z1, bool door)
        {
            var mid = (z0 + z1) / 2f;
            if (!door) { WallSegment(parent, material, new Vector3(x, 0f, z0), new Vector3(x, 0f, z1)); return; }
            WallSegment(parent, material, new Vector3(x, 0f, z0), new Vector3(x, 0f, mid - DoorWidth / 2f));
            WallSegment(parent, material, new Vector3(x, 0f, mid + DoorWidth / 2f), new Vector3(x, 0f, z1));
            Shape(parent, $"Lintel_{x:0}_{mid:0}", PrimitiveType.Cube, new Vector3(x, 3.6f, mid), Vector3.zero, new Vector3(WallThickness + 0.05f, 0.8f, DoorWidth), material);
        }

        private static void WallWithDoorAlongX(Transform parent, Material material, float z, float x0, float x1, bool door)
        {
            var mid = (x0 + x1) / 2f;
            if (!door) { WallSegment(parent, material, new Vector3(x0, 0f, z), new Vector3(x1, 0f, z)); return; }
            WallSegment(parent, material, new Vector3(x0, 0f, z), new Vector3(mid - DoorWidth / 2f, 0f, z));
            WallSegment(parent, material, new Vector3(mid + DoorWidth / 2f, 0f, z), new Vector3(x1, 0f, z));
            Shape(parent, $"Lintel_{mid:0}_{z:0}", PrimitiveType.Cube, new Vector3(mid, 3.6f, z), Vector3.zero, new Vector3(DoorWidth, 0.8f, WallThickness + 0.05f), material);
        }

        private static void WallSegment(Transform parent, Material material, Vector3 a, Vector3 b)
        {
            var center = (a + b) / 2f + Vector3.up * (WallHeight / 2f);
            var alongX = Mathf.Abs(b.x - a.x) > Mathf.Abs(b.z - a.z);
            var length = alongX ? Mathf.Abs(b.x - a.x) : Mathf.Abs(b.z - a.z);
            var size = alongX ? new Vector3(length, WallHeight, WallThickness) : new Vector3(WallThickness, WallHeight, length);
            Box(parent, $"Wall_{center.x:0.#}_{center.z:0.#}", center, size, material);
        }

        // ---------------- room contents ----------------

        private static void Furnish(Transform map, RoomDef room)
        {
            var root = new GameObject("Room_" + room.Kind).transform;
            root.SetParent(map, false);
            var c = room.Center;
            switch (room.Kind)
            {
                case RoomKind.Ship: FurnishShip(root); break;
                case RoomKind.HallS:
                    Rack(root, new Vector3(c.x - 5f, 0f, -25.6f), 7f);
                    Rack(root, new Vector3(c.x + 5.5f, 0f, -25.6f), 6f);
                    Rack(root, new Vector3(c.x - 5f, 0f, -12.6f), 7f);
                    Rack(root, new Vector3(c.x + 5.5f, 0f, -12.6f), 6f);
                    Railing(root, new Vector3(c.x, 0f, -22.5f), 6f);
                    Barrels(root, new Vector3(-2f, 0f, -15f), 3);
                    Pallet(root, new Vector3(-14f, 0f, -16f));
                    Stripe(root, new Vector3(c.x, 0.01f, -19f), new Vector3(16f, 0.02f, 0.25f));
                    break;
                case RoomKind.Storage:
                    for (var i = 0; i < 6; i++) Crate(root, new Vector3(2.5f + i * 2.4f, 0f, -26.5f), 1.1f + (i % 3) * 0.3f);
                    for (var i = 0; i < 4; i++) Crate(root, new Vector3(16.3f, 0f, -24f + i * 2.6f), 1.2f);
                    Crate(root, new Vector3(3f, 0f, -12.5f), 1.4f);
                    Crate(root, new Vector3(3f, 1.4f, -12.5f), 0.9f);
                    Pallet(root, new Vector3(6f, 0f, -13f));
                    Table(root, new Vector3(13f, 0f, -14f), 0f, 2.2f, 1f);
                    break;
                case RoomKind.Lair:
                    Boiler(root, new Vector3(31.5f, 0f, -24f));
                    Boiler(root, new Vector3(31.5f, 0f, -14.5f));
                    Pipe(root, new Vector3(20f, 3.4f, -27f), new Vector3(35f, 3.4f, -27f), 0.25f);
                    Pipe(root, new Vector3(29f, 3.6f, -27f), new Vector3(29f, 3.6f, -11f), 0.18f);
                    Barrels(root, new Vector3(21f, 0f, -26f), 4);
                    PointLight(root, "LairGlow", new Vector3(31f, 2.5f, -19f), new Color(1f, 0.3f, 0.15f), 0.5f, 9f);
                    break;
                case RoomKind.Dorm:
                    foreach (var z in new[] { -7.5f, -3.5f, 3.5f, 7.5f }) Bed(root, new Vector3(-34.2f, 0f, z), 90f);
                    for (var i = 0; i < 4; i++) Locker(root, new Vector3(-34.8f + i * 0.7f, 0f, 9.45f), 180f);
                    Table(root, new Vector3(-27f, 0f, 5.5f), 0f, 1.6f, 0.9f); // in the back room
                    // the TV corner: the TV cabinet against the south wall (BuildGameplay), a rug, and the sofa
                    // facing the screen from 3 m, like a living room rather than a TV left in the middle of the floor
                    var rug = Group(root, "Rug", new Vector3(DormTvStand.x, 0.005f, DormTvStand.z + 2f), 0f);
                    Dress(rug, "KenneyFurniture/rugRectangle", Vector3.zero, new Vector3(2.6f, 0.02f, 2.2f));
                    Clutter(root, "KenneyFurniture/loungeSofa", DormSofa, 0f, 0.85f); // Kenney fronts face -Z: towards the TV
                    break;
                case RoomKind.OfficeA:
                    foreach (var x in new[] { -14f, -4f })
                        foreach (var z in new[] { -5f, 1.5f })
                        {
                            Desk(root, new Vector3(x, 0f, z), 0f);
                            Chair(root, new Vector3(x, 0f, z - 0.9f), 0f);
                        }
                    for (var i = 0; i < 3; i++) Locker(root, new Vector3(-17.4f, 0f, 7f - i * 0.7f), 90f);
                    break;
                case RoomKind.Breakroom:
                    Table(root, new Vector3(5f, 0f, -4f), 0f, 1.8f, 0.9f);
                    Table(root, new Vector3(12.5f, 0f, -4f), 0f, 1.8f, 0.9f);
                    foreach (var x in new[] { 4.3f, 5.7f, 11.8f, 13.2f })
                    {
                        Chair(root, new Vector3(x, 0f, -5.1f), 0f);
                        Chair(root, new Vector3(x, 0f, -2.9f), 180f);
                    }
                    var counter = Group(root, "Counter", new Vector3(14.3f, 0f, 9.2f), 0f);
                    Box(counter, "Body", new Vector3(0f, 0.475f, 0f), new Vector3(6.6f, 0.95f, 0.8f), Mat("Counter", new Color(0.55f, 0.55f, 0.52f), tex: Texture("Tile"), tiling: 2f));
                    if (PropModel("KenneyFurniture/kitchenCabinet") != null)
                    {
                        Unrender(counter);
                        for (var i = 0; i < 6; i++)
                            Dress(counter, i == 2 ? "KenneyFurniture/kitchenSink" : "KenneyFurniture/kitchenCabinet", new Vector3(-2.75f + i * 1.1f, 0f, 0f), new Vector3(1.1f, 0.95f, 0.8f), FrontYaw);
                    }
                    var fridge = Group(root, "Fridge", new Vector3(1.4f, 0f, 8.9f), 0f);
                    Box(fridge, "Body", new Vector3(0f, 1f, 0f), new Vector3(1f, 2f, 0.8f), Mat("ApplianceLight", new Color(0.7f, 0.7f, 0.68f)));
                    Dressed(fridge, "KenneyFurniture/kitchenFridgeLarge", Vector3.zero, new Vector3(1f, 2f, 0.8f), FrontYaw);
                    Box(root, "Vending", new Vector3(1.2f, 1.1f, -8.5f), new Vector3(1.2f, 2.2f, 0.9f), Mat("Vending", new Color(0.55f, 0.12f, 0.1f), tex: Texture("Metal"), tiling: 1f));
                    break;
                case RoomKind.WarehouseE:
                    Rack(root, new Vector3(22.8f, 0f, -6f), 5f);
                    Rack(root, new Vector3(31.5f, 0f, -6f), 5f);
                    Rack(root, new Vector3(22.8f, 0f, 6f), 5f);
                    Rack(root, new Vector3(31.5f, 0f, 6f), 5f);
                    Pallet(root, new Vector3(33f, 0f, 0f));
                    Barrels(root, new Vector3(20f, 0f, 8.5f), 2);
                    Stripe(root, new Vector3(27f, 0.01f, 0f), new Vector3(0.25f, 0.02f, 18f));
                    break;
                case RoomKind.OfficeB:
                    Desk(root, new Vector3(-32f, 0f, 15f), 0f);
                    // two rows of desks in the open-plan part (the back rooms start at z 22)
                    Desk(root, new Vector3(-32f, 0f, 18.6f), 0f);
                    Desk(root, new Vector3(-24f, 0f, 15f), 0f);
                    Desk(root, new Vector3(-24f, 0f, 18.6f), 0f);
                    Chair(root, new Vector3(-32f, 0f, 14.1f), 0f);
                    Chair(root, new Vector3(-24f, 0f, 14.1f), 0f);
                    Chair(root, new Vector3(-32f, 0f, 17.7f), 0f);
                    Chair(root, new Vector3(-24f, 0f, 17.7f), 0f);
                    for (var i = 0; i < 4; i++) Locker(root, new Vector3(-35.4f, 0f, 18f + i * 0.7f), 90f);
                    break;
                case RoomKind.CorridorN:
                    for (var i = 0; i < 8; i++) Locker(root, new Vector3(-16f + i * 0.7f, 0f, 27.45f), 180f);
                    Table(root, new Vector3(-4f, 0f, 24f), 0f, 2f, 1f);
                    Bench(root, new Vector3(-12f, 0f, 12f));
                    break;
                case RoomKind.Server:
                    foreach (var z in new[] { 14f, 24f })
                    {
                        for (var i = 0; i < 4; i++) ServerRack(root, new Vector3(2.5f + i * 1.1f, 0f, z));
                        for (var i = 0; i < 4; i++) ServerRack(root, new Vector3(11.8f + i * 1.1f, 0f, z));
                    }
                    // a third row through the middle, with a gap in the centre: the racks make a small maze like
                    // the server rooms of both games (clear of the west and east doorways at z 19)
                    for (var i = 0; i < 3; i++) ServerRack(root, new Vector3(4.4f + i * 1.1f, 0f, 19f));
                    for (var i = 0; i < 3; i++) ServerRack(root, new Vector3(11.4f + i * 1.1f, 0f, 19f));
                    break;
                case RoomKind.TransmitterRoom:
                    Stripe(root, new Vector3(32f, 0.01f, 20.8f), new Vector3(7f, 0.02f, 0.3f));   // hazard edge around the lift shaft
                    Stripe(root, new Vector3(28.8f, 0.01f, 24f), new Vector3(0.3f, 0.02f, 6.4f));
                    Crate(root, new Vector3(20f, 0f, 26.5f), 1.2f);
                    Crate(root, new Vector3(34.5f, 0f, 12f), 1.2f);
                    Pipe(root, new Vector3(19f, 3.5f, 27f), new Vector3(35f, 3.5f, 27f), 0.2f);
                    break;
            }
        }

        private static void FurnishShip(Transform root)
        {
            var hull = Mat("ShipHull", new Color(0.42f, 0.45f, 0.5f), tex: Texture("Metal"), tiling: 3f, tilingY: 1f);
            var dark = Mat("GeneratorDark", new Color(0.1f, 0.1f, 0.1f));
            float w = Ship.Min.x, e = Ship.Max.x, so = Ship.Min.z, n = Ship.Max.z;
            float cx = (w + e) / 2f, cz = (so + n) / 2f, depth = n - so, width = e - w;
            // Solid hull walls close the cabin in (west, south, north); the east side is the cell wall with the door.
            Box(root, "HullWest", new Vector3(w - 0.15f, 2f, cz), new Vector3(0.3f, 4f, depth + 0.6f), hull);
            Box(root, "HullSouth", new Vector3(cx, 2f, so - 0.15f), new Vector3(width + 0.3f, 4f, 0.3f), hull);
            Box(root, "HullNorth", new Vector3(cx, 2f, n + 0.15f), new Vector3(width + 0.3f, 4f, 0.3f), hull);
            Shape(root, "HullRoof", PrimitiveType.Cube, new Vector3(cx, 3.95f, cz), Vector3.zero, new Vector3(width + 0.3f, 0.1f, depth + 0.6f), hull);
            for (var x = w + 1.5f; x <= e - 1f; x += 3f)
            {
                if (Mathf.Abs(x - Ship.Panel.x) > 1.2f) // keep the CCTV monitor clear
                    Shape(root, "RibS", PrimitiveType.Cube, new Vector3(x, 2f, so + 0.1f), Vector3.zero, new Vector3(0.3f, 4f, 0.2f), dark);
                Shape(root, "RibN", PrimitiveType.Cube, new Vector3(x, 2f, n - 0.1f), Vector3.zero, new Vector3(0.3f, 4f, 0.2f), dark);
                Shape(root, "RibTop", PrimitiveType.Cube, new Vector3(x, 3.75f, cz), Vector3.zero, new Vector3(0.3f, 0.25f, depth), dark);
            }
            // (the north wall's middle is the final power transmitter, built with the gameplay objects)
            Pipe(root, new Vector3(w + 0.3f, 3.4f, n - 1f), new Vector3(w + 0.3f, 3.4f, so + 1f), 0.18f);
            Pipe(root, new Vector3(w + 0.5f, 3.55f, so + 0.35f), new Vector3(e - 1f, 3.55f, so + 0.35f), 0.13f);
            Crate(root, new Vector3(e - 0.8f, 0f, n - 0.8f), 0.8f); // one crate in the corner by the door
            Stripe(root, new Vector3(e - 0.9f, 0.01f, Ship.Door.z), new Vector3(0.35f, 0.02f, DoorWidth));
            // Ship lights run on the shared grid: dark at first (only the generator's little lamp), then brighter
            // in steps at 30 / 50 / 70 / 100 % charge (ShipLighting). Intensities here are the full-power values.
            var shipLights = new[]
            {
                PointLight(root, "ShipLight", new Vector3(cx, 3.4f, so + 3f), new Color(0.75f, 0.88f, 1f), 13f, 12f),
                PointLight(root, "ShipLight2", new Vector3(cx, 3.4f, n - 3f), new Color(0.75f, 0.88f, 1f), 13f, 12f),
                PointLight(root, "ShipLight3", new Vector3(w + 2.5f, 3.4f, cz), new Color(0.75f, 0.88f, 1f), 9f, 10f),
                PointLight(root, "ShipAlarm", new Vector3(e - 1.5f, 3.5f, n - 1f), new Color(1f, 0.15f, 0.1f), 2f, 6f),
            };
            root.gameObject.AddComponent<ShipLighting>().Lights = shipLights;
        }

        // ---------------- furniture (hiding places have ~0.9 m of room underneath) ----------------

        private static void Desk(Transform parent, Vector3 position, float yaw)
        {
            // Big enough to crawl under (1 m clear, 1.25 m deep), like the rest of the furniture.
            var root = Group(parent, "Desk", position, yaw);
            var wood = Mat("Wood", new Color(0.5f, 0.36f, 0.22f), tex: Texture("Wood"), tiling: 1f);
            Box(root, "Top", new Vector3(0f, 1.02f, 0f), new Vector3(2f, 0.06f, 1.25f), wood);
            foreach (var x in new[] { -0.95f, 0.95f })
                foreach (var z in new[] { -0.58f, 0.58f }) Box(root, "Leg", new Vector3(x, 0.5f, z), new Vector3(0.06f, 1f, 0.06f), wood);
            Shape(root, "Monitor", PrimitiveType.Cube, new Vector3(0.2f, 1.2f, 0.2f), Vector3.zero, new Vector3(0.45f, 0.35f, 0.08f), Mat("GeneratorDark", new Color(0.1f, 0.1f, 0.1f)));
            Dressed(root, "KenneyFurniture/table", Vector3.zero, new Vector3(2f, 1.05f, 1.25f), FrontYaw); // open underneath
            Dress(root, "KenneyFurniture/computerScreen", new Vector3(0.3f, 1.05f, 0.35f), new Vector3(0.6f, 0.45f, 0.16f), FrontYaw);
        }

        private static void Table(Transform parent, Vector3 position, float yaw, float length, float width)
        {
            var root = Group(parent, "Table", position, yaw);
            var top = Mat("TableTop", new Color(0.62f, 0.6f, 0.55f), tex: Texture("Wood"), tiling: 1f);
            width = Mathf.Max(width, 1.25f); // wide enough to hide under
            Box(root, "Top", new Vector3(0f, 1.02f, 0f), new Vector3(length, 0.06f, width), top);
            foreach (var x in new[] { -length / 2f + 0.06f, length / 2f - 0.06f })
                foreach (var z in new[] { -width / 2f + 0.06f, width / 2f - 0.06f }) Box(root, "Leg", new Vector3(x, 0.5f, z), new Vector3(0.06f, 1f, 0.06f), top);
            Dressed(root, "KenneyFurniture/table", Vector3.zero, new Vector3(length, 1.05f, width));
        }

        private static void Bed(Transform parent, Vector3 position, float yaw)
        {
            var root = Group(parent, "Bed", position, yaw);
            var frame = Mat("BedFrame", new Color(0.35f, 0.36f, 0.38f), tex: Texture("Metal"), tiling: 1f);
            var sheet = Mat("Mattress", new Color(0.55f, 0.55f, 0.5f), tex: Texture("Concrete"), tiling: 1f);
            Box(root, "Frame", new Vector3(0f, 1f, 0f), new Vector3(1.35f, 0.08f, 2.1f), frame);
            Box(root, "Mattress", new Vector3(0f, 1.12f, 0f), new Vector3(1.27f, 0.16f, 2f), sheet);
            Shape(root, "Pillow", PrimitiveType.Cube, new Vector3(0f, 1.25f, 0.78f), Vector3.zero, new Vector3(0.75f, 0.1f, 0.3f), sheet);
            foreach (var x in new[] { -0.63f, 0.63f })
                foreach (var z in new[] { -1.01f, 1.01f }) Box(root, "Leg", new Vector3(x, 0.48f, z), new Vector3(0.06f, 0.96f, 0.06f), frame);
        }

        private static void Chair(Transform parent, Vector3 position, float yaw)
        {
            var root = Group(parent, "Chair", position, yaw);
            var mat = Mat("ChairMat", new Color(0.25f, 0.25f, 0.27f));
            Shape(root, "Seat", PrimitiveType.Cube, new Vector3(0f, 0.48f, 0f), Vector3.zero, new Vector3(0.45f, 0.06f, 0.45f), mat); // no collider
            Shape(root, "Back", PrimitiveType.Cube, new Vector3(0f, 0.75f, -0.2f), Vector3.zero, new Vector3(0.45f, 0.5f, 0.05f), mat);
            foreach (var x in new[] { -0.2f, 0.2f })
                foreach (var z in new[] { -0.2f, 0.2f }) Shape(root, "Leg", PrimitiveType.Cube, new Vector3(x, 0.23f, z), Vector3.zero, new Vector3(0.04f, 0.46f, 0.04f), mat);
            Dressed(root, "KenneyFurniture/chairDesk", Vector3.zero, new Vector3(0.55f, 1.05f, 0.55f), FrontYaw + 180f);
        }

        private static void Bench(Transform parent, Vector3 position)
        {
            var root = Group(parent, "Bench", position, 0f);
            Box(root, "Seat", new Vector3(0f, 0.45f, 0f), new Vector3(2.5f, 0.08f, 0.45f), Mat("Wood", new Color(0.5f, 0.36f, 0.22f), tex: Texture("Wood"), tiling: 1f));
            Dressed(root, "KenneyFurniture/bench", Vector3.zero, new Vector3(2.5f, 0.55f, 0.5f));
        }

        // A locker you open with your nose (ElecMoleOpenables): built after the NavMesh bake as its own NetworkObject.
        private static void Locker(Transform parent, Vector3 position, float yaw) => QueueOpenable(parent, OpenableKind.Locker, position, yaw);

        // Metal shelving along X with stuff on it. The bottom shelf is too low to crawl under.
        private static void Rack(Transform parent, Vector3 position, float length)
        {
            var root = Group(parent, "Rack", position, 0f);
            var metal = Mat("RackMetal", new Color(0.3f, 0.45f, 0.6f), tex: Texture("Metal"), tiling: 1f);
            var yellow = Mat("RackBeam", new Color(0.85f, 0.65f, 0.1f));
            for (var x = -length / 2f; x <= length / 2f + 0.01f; x += length / 2f)
                foreach (var z in new[] { -0.45f, 0.45f }) Box(root, "Post", new Vector3(x, 1.5f, z), new Vector3(0.08f, 3f, 0.08f), metal);
            foreach (var y in new[] { 0.15f, 1.1f, 2.05f, 2.95f })
                Box(root, "Shelf", new Vector3(0f, y, 0f), new Vector3(length, 0.05f, 0.95f), yellow);
            Box(root, "KickPlate", new Vector3(0f, 0.08f, 0f), new Vector3(length, 0.16f, 0.95f), yellow);
            for (var bx = -length / 2f + 0.35f; bx < length / 2f - 0.2f; bx += 0.62f) // bottom shelf full of boxes
                Dress(root, "KenneyFurniture/cardboardBoxClosed", new Vector3(bx, 0.18f, 0f), new Vector3(0.58f, 0.7f, 0.8f), (bx * 37f) % 12f, true);
            var seed = Mathf.Abs(position.GetHashCode());
            for (var i = 0; i < (int)length; i++)
            {
                var level = (seed + i) % 3;
                var size = 0.4f + ((seed >> i) & 3) * 0.12f;
                var boxBottom = new Vector3(-length / 2f + 0.5f + i, 0.15f + level * 0.95f + 0.03f, 0f);
                if (Dress(root, i % 3 == 1 ? "KenneyFurniture/cardboardBoxOpen" : "KenneyFurniture/cardboardBoxClosed", boxBottom, Vector3.one * size, (seed + i * 37) % 30, true) == null)
                    Shape(root, "Box", PrimitiveType.Cube, boxBottom + Vector3.up * size / 2f, new Vector3(0f, (seed + i * 37) % 30, 0f), Vector3.one * size, Mat("Cardboard", new Color(0.55f, 0.42f, 0.25f), tex: Texture("Wood"), tiling: 1f));
            }
        }

        private static void ServerRack(Transform parent, Vector3 position)
        {
            var root = Group(parent, "ServerRack", position, 0f);
            Box(root, "Body", new Vector3(0f, 1.1f, 0f), new Vector3(0.9f, 2.2f, 1.1f), Mat("ServerBody", new Color(0.12f, 0.13f, 0.15f), tex: Texture("Metal"), tiling: 1f));
            Dressed(root, "QuaterniusSciFi/Prop_Computer", Vector3.zero, new Vector3(0.9f, 2.2f, 1.1f));
            var led = Mat("LedGreen", new Color(0.2f, 1f, 0.35f), unlit: true);
            for (var i = 0; i < 6; i++)
                Shape(root, "Led", PrimitiveType.Cube, new Vector3(-0.3f + (i % 3) * 0.12f, 0.6f + i * 0.25f, 0.56f), Vector3.zero, new Vector3(0.04f, 0.04f, 0.01f), led);
        }

        private static void Crate(Transform parent, Vector3 position, float size)
        {
            var root = Group(parent, "Crate", position, 0f);
            Box(root, "Body", Vector3.up * size / 2f, Vector3.one * size, Mat("Crate", new Color(0.5f, 0.38f, 0.22f), tex: Texture("Wood"), tiling: 1f));
            var pick = Mathf.Abs(Mathf.RoundToInt(position.x * 7f + position.z * 3f)) % 3;
            Dressed(root, pick == 0 ? "QuaterniusSciFi/Prop_Crate4" : pick == 1 ? "KenneySpaceStation/container" : "KenneyFactory/box-wide", Vector3.zero, Vector3.one * size, pick * 90f);
        }

        private static void Pallet(Transform parent, Vector3 position)
        {
            var root = Group(parent, "Pallet", position, 0f);
            Box(root, "Base", Vector3.up * 0.07f, new Vector3(1.2f, 0.14f, 1f), Mat("Wood", new Color(0.5f, 0.36f, 0.22f), tex: Texture("Wood"), tiling: 1f));
            Shape(root, "PalletLoad", PrimitiveType.Cube, new Vector3(0f, 0.55f, 0f), Vector3.zero, new Vector3(1f, 0.8f, 0.85f), Mat("Wrap", new Color(0.6f, 0.62f, 0.62f), tex: Texture("Concrete"), tiling: 1f));
            Dressed(root, "QuaterniusZombie/Pallet", Vector3.zero, new Vector3(1f, 0.14f, 1.2f), 90f);
            Dress(root, "KenneyFurniture/cardboardBoxClosed", new Vector3(-0.25f, 0.14f, -0.2f), new Vector3(0.5f, 0.5f, 0.5f), 10f, true);
            Dress(root, "KenneyFurniture/cardboardBoxClosed", new Vector3(0.25f, 0.14f, 0.1f), new Vector3(0.55f, 0.55f, 0.55f), -15f, true);
            Dress(root, "KenneyFurniture/cardboardBoxOpen", new Vector3(-0.15f, 0.64f, 0f), new Vector3(0.45f, 0.4f, 0.45f), 30f, true);
        }

        private static void Barrels(Transform parent, Vector3 position, int count)
        {
            var mat = Mat("Barrel", new Color(0.35f, 0.4f, 0.3f), tex: Texture("Rust"), tiling: 1f);
            for (var i = 0; i < count; i++)
            {
                var root = Group(parent, "Barrel", position + new Vector3((i % 2) * 0.75f, 0f, (i / 2) * 0.75f), i * 47f);
                var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.name = "Body";
                go.transform.SetParent(root, false);
                go.transform.localPosition = new Vector3(0f, 0.45f, 0f);
                go.transform.localScale = new Vector3(0.62f, 0.45f, 0.62f);
                go.GetComponent<Renderer>().sharedMaterial = mat;
                Dressed(root, i % 3 == 2 ? "QuaterniusSciFi/Prop_Barrel_Large" : "QuaterniusZombie/Barrel", Vector3.zero, new Vector3(0.62f, 0.95f, 0.62f));
            }
        }

        private static void Boiler(Transform parent, Vector3 position)
        {
            var root = Group(parent, "Boiler", position, 0f);
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Body";
            go.transform.SetParent(root, false);
            go.transform.localPosition = Vector3.up * 1.6f;
            go.transform.localScale = new Vector3(2.2f, 1.6f, 2.2f);
            go.GetComponent<Renderer>().sharedMaterial = Mat("Boiler", new Color(0.45f, 0.3f, 0.2f), tex: Texture("Rust"), tiling: 2f);
            Shape(root, "BoilerBand", PrimitiveType.Cylinder, Vector3.up * 2.4f, Vector3.zero, new Vector3(2.3f, 0.08f, 2.3f), Mat("Hazard", Color.white, tex: Texture("Hazard"), tiling: 4f));
            Dressed(root, "KenneyFactory/hopper-high-round", Vector3.zero, new Vector3(2.3f, 3.3f, 2.3f));
        }

        private static void Pipe(Transform parent, Vector3 a, Vector3 b, float radius)
        {
            var mid = (a + b) / 2f;
            var length = Vector3.Distance(a, b);
            var go = Shape(parent, "Pipe", PrimitiveType.Cylinder, mid, Vector3.zero, new Vector3(radius * 2f, length / 2f, radius * 2f), Mat("PipeMetal", new Color(0.4f, 0.38f, 0.32f), tex: Texture("Rust"), tiling: 1f));
            go.transform.up = (b - a).normalized;
        }

        private static void Railing(Transform parent, Vector3 position, float length)
        {
            var yellow = Mat("RackBeam", new Color(0.85f, 0.65f, 0.1f));
            Box(parent, "Rail", position + new Vector3(0f, 1f, 0f), new Vector3(length, 0.06f, 0.06f), yellow);
            Shape(parent, "RailMid", PrimitiveType.Cube, position + new Vector3(0f, 0.55f, 0f), Vector3.zero, new Vector3(length, 0.05f, 0.05f), yellow);
            for (var x = -length / 2f; x <= length / 2f + 0.01f; x += 1.5f)
                Box(parent, "RailPost", position + new Vector3(x, 0.5f, 0f), new Vector3(0.06f, 1f, 0.06f), yellow);
        }

        private static void Stripe(Transform parent, Vector3 position, Vector3 size)
        {
            Shape(parent, "Hazard", PrimitiveType.Cube, position, Vector3.zero, size, Mat("Hazard", Color.white, tex: Texture("Hazard"), tiling: 4f));
        }

        private static Transform Group(Transform parent, string name, Vector3 position, float yaw)
        {
            var go = new GameObject(name).transform;
            go.SetParent(parent, false);
            go.localPosition = position;
            go.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return go;
        }

        // ---------------- generated low-res textures ----------------

        private static readonly Dictionary<string, Texture2D> Textures = new();

        // 32x32, point filtered: grime, tiles, plates, grain. Generated once and saved as assets.
        private static Texture2D Texture(string name)
        {
            if (Textures.TryGetValue(name, out var cached) && cached != null) return cached;
            Directory.CreateDirectory(TextureDir);
            var path = $"{TextureDir}/{name}.asset";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            var created = texture == null;
            if (created) texture = new Texture2D(32, 32, TextureFormat.RGBA32, false) { name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            // Always repaint (patterns change between versions).
            var random = new System.Random(name.GetHashCode());
            var pixels = new Color[32 * 32];
            for (var y = 0; y < 32; y++)
            for (var x = 0; x < 32; x++)
                pixels[y * 32 + x] = Pixel(name, x, y, (float)random.NextDouble());
            texture.SetPixels(pixels);
            texture.Apply();
            if (created) AssetDatabase.CreateAsset(texture, path);
            else EditorUtility.SetDirty(texture);
            Textures[name] = texture;
            return texture;
        }

        private static Color Pixel(string name, int x, int y, float noise)
        {
            var n = noise - 0.5f;
            switch (name)
            {
                case "Tile":
                    var white = ((x / 8) + (y / 8)) % 2 == 0;
                    var grout = x % 8 == 0 || y % 8 == 0;
                    var t = grout ? 0.35f : white ? 0.9f : 0.1f;
                    return Gray(t + n * 0.12f - (noise > 0.93f ? 0.25f : 0f));
                case "Metal":
                    var seam = x % 16 == 0 || y % 16 == 0;
                    var rivet = (x % 16 == 2 || x % 16 == 13) && (y % 16 == 2 || y % 16 == 13);
                    return Gray((seam ? 0.35f : rivet ? 0.85f : 0.6f) + n * 0.1f);
                case "Wood":
                    var grain = Mathf.Sin((y + Mathf.Sin(x * 0.4f) * 2f) * 0.9f) * 0.08f;
                    return new Color(0.62f + grain + n * 0.06f, 0.45f + grain + n * 0.05f, 0.28f + grain * 0.5f);
                case "Wallpaper":
                    // Korean apartment wallpaper: cream with a faint woven texture and tiny scattered flowers,
                    // stained near the floor.
                    var weave = ((x + y) % 4 == 0 ? -0.025f : 0f) + ((x - y) % 6 == 0 ? 0.015f : 0f);
                    var flower = ((x % 16 == 4 && y % 16 == 4) || (x % 16 == 12 && y % 16 == 12)) ? -0.09f : 0f;
                    var stain = y < 8 ? (8 - y) * 0.025f : 0f;
                    return new Color(0.88f + weave + flower * 0.6f - stain + n * 0.04f, 0.84f + weave + flower - stain + n * 0.04f, 0.74f + weave + flower * 0.4f - stain + n * 0.03f);
                case "Jangpan":
                    // oiled-paper vinyl floor: warm ochre, a woven grain and the seam between sheets
                    var grainJ = Mathf.Sin((x * 0.9f + Mathf.Sin(y * 0.5f) * 1.5f)) * 0.035f;
                    var sheetSeam = y % 16 == 0 ? -0.12f : 0f;
                    return new Color(0.82f + grainJ + sheetSeam + n * 0.05f, 0.64f + grainJ + sheetSeam + n * 0.04f, 0.36f + grainJ * 0.5f + sheetSeam + n * 0.03f);
                case "SmallTile":
                    var groutS = x % 4 == 0 || y % 4 == 0;
                    return groutS ? Gray(0.55f + n * 0.05f) : new Color(0.9f + n * 0.06f, 0.86f + n * 0.06f, 0.76f + n * 0.05f);
                case "Epoxy":
                    var scuff = noise > 0.9f ? -0.15f : 0f;
                    var line = y == 1 ? 0.25f : 0f; // a painted walking line
                    return new Color(0.4f + n * 0.05f + scuff + line, 0.62f + n * 0.05f + scuff + line * 0.7f, 0.45f + n * 0.04f + scuff);
                case "Rust":
                    return new Color(0.55f + n * 0.25f, 0.32f + n * 0.15f, 0.18f + n * 0.08f);
                case "Carpet":
                    // office carpet tiles, alternating pile direction
                    var tileC = ((x / 16) + (y / 16)) % 2 == 0 ? 0.04f : -0.04f;
                    return Gray(0.55f + tileC + n * 0.22f);
                case "Hazard":
                    var band = ((x + y) / 6) % 2 == 0;
                    return band ? new Color(0.95f, 0.75f, 0.1f) : new Color(0.08f, 0.08f, 0.08f);
                default: // Concrete
                    var crack = (x == (y * 3 / 5 + 7) % 32 && noise > 0.3f) ? 0.2f : 0f;
                    return Gray(0.62f + n * 0.16f - crack);
            }
        }

        private static Color Gray(float v) => new(v, v, v, 1f);
    }
}
