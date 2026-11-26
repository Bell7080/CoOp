using System.Collections.Generic;
using UnityEngine;

namespace CoOpHorror.Editor
{
    // Gives the bare rooms a few themed CC0 props (Kenney, Quaternius) square against the walls, so no room
    // reads as an empty box. Spots are the walkable 1 m cells that touch a wall
    // (outer wall, partition or corridor wall), well away from doorways and gameplay spots, and never on top
    // of furniture that is already there. Seeded, so every rebuild gives the same rooms.
    public static partial class ElecMolePrototypeSetup
    {
        private readonly struct DecorProp
        {
            public readonly string Model;
            public readonly float Height;
            public readonly bool Solid;
            public DecorProp(string model, float height, bool solid = true) { Model = model; Height = height; Solid = solid; }
        }

        // A handful of solid pieces per room, square to the wall with space between them: the rooms read as
        // furnished, not littered. Every model is used once on the whole map (none of them repeats a model the
        // hand-placed clutter already uses), so no two rooms look copied. No loose bits, nothing in corridors.
        private static readonly Dictionary<RoomKind, DecorProp[]> DecorThemes = new()
        {
            // boiler room: drums, a tank, a valve on a pipe stub, a toolbox chest, heavy machinery
            [RoomKind.Lair] = new[]
            {
                new DecorProp("KenneyFactory/machine-fortified", 1.6f), new DecorProp("KenneyCityIndustrial/detail-tank", 1.6f),
                new DecorProp("QuaterniusSciFi/Prop_Barrel_Large", 1.2f), new DecorProp("KenneyFactory/pipe-large-valve", 1f),
                new DecorProp("QuaterniusZombie/Barrel", 1f), new DecorProp("QuaterniusSciFi/Prop_Chest", 0.6f),
            },
            // transmitter room: a loading bay - crates, a container, a robot arm and a scanner gate, a pallet
            [RoomKind.TransmitterRoom] = new[]
            {
                new DecorProp("KenneyFactory/robot-arm-a", 1.8f), new DecorProp("KenneySpaceStation/container", 1.1f),
                new DecorProp("KenneyFactory/box-large", 1.1f), new DecorProp("KenneyFactory/scanner-high", 2f),
                new DecorProp("QuaterniusSciFi/Prop_Crate4", 0.9f), new DecorProp("KenneyFactory/box-long", 0.8f),
                new DecorProp("QuaterniusZombie/Pallet", 0.15f, false),
            },
            // break room: kitchen corner and somewhere to slump
            [RoomKind.Breakroom] = new[]
            {
                new DecorProp("KenneyFurniture/kitchenCabinet", 0.95f), new DecorProp("KenneyFurniture/kitchenFridgeSmall", 0.9f),
                new DecorProp("QuaterniusHouse/Kitchen_Oven", 1f), new DecorProp("QuaterniusHouse/Couch_Medium1", 0.85f),
                new DecorProp("KenneyFurniture/stoolBar", 0.8f), new DecorProp("QuaterniusHouse/Trashcan_Green", 0.8f),
            },
            // dorm: laundry, a sofa, shelves, a floor lamp
            [RoomKind.Dorm] = new[]
            {
                new DecorProp("KenneyFurniture/washerDryerStacked", 1.9f), new DecorProp("KenneyFurniture/loungeSofa", 0.85f),
                new DecorProp("QuaterniusHouse/Bookshelf", 1.9f), new DecorProp("QuaterniusHouse/Drawer_3", 1f),
                new DecorProp("QuaterniusHouse/Light_Floor1", 1.7f), new DecorProp("KenneyFurniture/sideTableDrawers", 0.6f),
            },
            // offices: open shelving, filing, a reading chair, a lamp and a plant
            [RoomKind.OfficeB] = new[]
            {
                new DecorProp("QuaterniusHouse/Shelf_Large", 1.9f), new DecorProp("KenneyFurniture/bookcaseOpen", 1.9f),
                new DecorProp("QuaterniusHouse/Drawer_5", 1f), new DecorProp("KenneyFurniture/loungeChair", 0.9f),
                new DecorProp("KenneyFurniture/lampSquareFloor", 1.6f), new DecorProp("QuaterniusHouse/Houseplant_2", 1f),
            },
            // server room: just a network cabinet and a spare terminal against the walls
            [RoomKind.Server] = new[]
            {
                new DecorProp("QuaterniusSciFi/Prop_AccessPoint", 1.2f), new DecorProp("KenneySpaceStation/computer-system", 1.6f),
            },
        };

        private const float DecorSpacing = 3f;

        private static void DressEmptyRooms(Transform clutter)
        {
            Physics.SyncTransforms();
            for (var r = 0; r < Rooms.Length; r++)
            {
                var room = Rooms[r];
                if (!Walk.TryGetValue(room.Kind, out var mask)) continue; // the ship has its own fit-out
                if (!DecorThemes.TryGetValue(room.Kind, out var props)) continue;

                var random = new System.Random(LayoutSeed * 31 + r);
                var spots = WallSpots(room, mask);
                for (var i = spots.Count - 1; i > 0; i--) { var j = random.Next(i + 1); (spots[i], spots[j]) = (spots[j], spots[i]); }
                var used = new List<Vector3>();
                // each model once: it takes the first free wall spot (shuffled) it fits in, or is skipped
                foreach (var prop in props)
                {
                    var size = FittedSize(prop);
                    if (size == Vector3.zero) continue;
                    foreach (var (cell, outward) in spots)
                    {
                        // pushed against the wall, front facing into the room; the back face just clear of the
                        // wall (walls reach up to ~0.2 m into the edge cell), deep props stick out into the room
                        var yaw = Quaternion.LookRotation(-outward).eulerAngles.y;
                        var position = cell + outward * (0.5f - 0.24f - size.z / 2f);
                        if (!SpotFree(position, yaw, size, used)) continue;
                        Clutter(clutter, prop.Model, position, yaw, prop.Height, prop.Solid);
                        Physics.SyncTransforms();
                        used.Add(position);
                        break;
                    }
                }
            }
        }

        // Walkable 1 m cells of a room that border a wall (blocked cell or the cell's outer edge), with the
        // direction towards that wall. Cells near a doorway or a gameplay spot are left out.
        private static List<(Vector3 cell, Vector3 outward)> WallSpots(RoomDef room, bool[,] mask)
        {
            var list = new List<(Vector3, Vector3)>();
            float x0 = XCols[room.Col], z0 = ZRows[room.Row];
            var dirs = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
            for (var ix = 0; ix < CellSize; ix++)
            for (var iz = 0; iz < CellSize; iz++)
            {
                if (!mask[ix, iz]) continue;
                var p = new Vector3(x0 + ix + 0.5f, 0f, z0 + iz + 0.5f);
                var near = false;
                foreach (var (mid, _) in Doorways(room)) if ((new Vector3(mid.x, 0f, mid.z) - p).magnitude < 3.6f) near = true;
                foreach (var res in Reserved()) if ((new Vector3(res.x, 0f, res.z) - p).magnitude < 2.2f) near = true;
                if (near) continue;
                foreach (var (dx, dz) in dirs)
                {
                    int nx = ix + dx, nz = iz + dz;
                    var wall = nx < 0 || nz < 0 || nx >= CellSize || nz >= CellSize || !mask[nx, nz];
                    if (!wall) continue;
                    list.Add((p, new Vector3(dx, 0f, dz)));
                    break;
                }
            }
            return list;
        }

        private static readonly Dictionary<string, Vector3> FittedSizes = new();

        // The model's footprint once scaled to its height (x across its front, z its depth).
        private static Vector3 FittedSize(DecorProp prop)
        {
            var key = prop.Model + "@" + prop.Height;
            if (FittedSizes.TryGetValue(key, out var cached)) return cached;
            var source = PropModel(prop.Model);
            var size = Vector3.zero;
            if (source != null)
            {
                var probe = (GameObject)Object.Instantiate(source);
                var raw = Bounds(probe).size;
                Object.DestroyImmediate(probe);
                size = raw * (raw.y < 0.05f ? 1f : prop.Height / raw.y);
            }
            FittedSizes[key] = size;
            return size;
        }

        // Nothing solid (furniture, walls, other props) in the footprint, and a little air to the last props.
        private static bool SpotFree(Vector3 position, float yaw, Vector3 size, List<Vector3> used)
        {
            foreach (var u in used) if ((u - position).sqrMagnitude < DecorSpacing * DecorSpacing) return false;
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            var half = new Vector3(Mathf.Max(0.1f, size.x / 2f - 0.04f), Mathf.Max(0.1f, Mathf.Min(size.y, 2f) / 2f), Mathf.Max(0.1f, size.z / 2f - 0.04f));
            var center = position + Vector3.up * (half.y + 0.06f);
            foreach (var c in Physics.OverlapBox(center, half, rotation, ~0, QueryTriggerInteraction.Ignore))
                if (!c.name.StartsWith("Floor") && !c.name.StartsWith("Ceiling")) return false;
            // stays inside walkable floor at every footprint corner
            for (var sx = -1; sx <= 1; sx += 2)
            for (var sz = -1; sz <= 1; sz += 2)
                if (!Walkable(position + rotation * new Vector3(sx * half.x, 0f, sz * half.z))) return false;
            return true;
        }
    }
}
