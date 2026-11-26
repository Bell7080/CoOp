using System.Collections.Generic;
using UnityEngine;

namespace CoOpHorror.Editor
{
    // Level layout on top of the 4 x 3 grid of 18 m cells, in the spirit of Lethal Company / REPO facilities:
    // some cells are corridors whose shape follows their open doorways (ㅡ straight, ㅗ/ㅜ tee, ㄱ corner,
    // + cross) and the rest are rooms broken up by a central structure, pillars or a partition wall, picked
    // per room from a seeded random list (re-picking when a pattern would cover gameplay spots).
    // Everything walkable is described by a 1 m mask per cell; furniture and clutter that end up inside
    // solid areas are culled.
    public static partial class ElecMolePrototypeSetup
    {
        public const int LayoutSeed = 7080;
        private const int CellSize = 18;

        private enum Pattern { Open, Corridor, Island, Pillars, PartitionX, PartitionZ, CornerSW, CornerSE, CornerNW, CornerNE }

        // Doorways closed off to shape the corridors (edges between two cells).
        private static readonly Vector3[] ClosedDoorways =
        {
            new(9f, 0f, -10f),   // storage | break room   -> storage becomes a straight ㅡ corridor
            new(27f, 0f, 10f),   // east warehouse | transmitter room -> the warehouse becomes a ㄱ corner
        };

        private static readonly HashSet<RoomKind> CorridorCells = new()
            { RoomKind.HallS, RoomKind.Storage, RoomKind.OfficeA, RoomKind.WarehouseE, RoomKind.CorridorN };

        private static readonly Dictionary<RoomKind, bool[,]> Walk = new();
        private static readonly Dictionary<RoomKind, Pattern> Patterns = new();

        private static bool IsCorridor(RoomKind kind) => CorridorCells.Contains(kind);

        // Gameplay spots placed by BuildGameplay / doors (kept in step with those calls). The floor ones get a
        // stand under the appliance (OnStand), so the clutter keeps clear of them.
        private static readonly Vector3[] AppliancePositions =
        {
            new(-30f, 0f, 6f), new(14.2f, 0.95f, 9.15f), new(-22f, 0f, 25f), new(-9f, 0f, 6f),
            new(12.2f, 0.95f, 9.15f), new(16.2f, 0.95f, 9.15f), new(12f, 0f, -19.6f),
        };
        private static readonly Vector3[] WardrobeFronts =
        {
            new(-24.6f, 0f, 8.1f), new(-21.2f, 0f, 8.1f), new(-20.2f, 0f, 26.1f), new(19.9f, 0f, 25f),
        };
        private static readonly Vector3[] CreatureStarts = { new(27f, 0f, -19f), new(27f, 0f, 0f), new(-27f, 0f, 0f) };

        private static bool DoorwayClosed(Vector3 mid)
        {
            foreach (var c in ClosedDoorways) if ((c - mid).sqrMagnitude < 1f) return true;
            return false;
        }

        // Spots that must stay walkable (gameplay objects and where you stand to use them).
        private static IEnumerable<Vector3> Reserved()
        {
            foreach (var cam in Cameras) yield return cam.Position + Quaternion.Euler(0f, cam.Yaw, 0f) * Vector3.forward * 1.2f;
            foreach (var p in AppliancePositions) yield return p;
            foreach (var p in WardrobeFronts) yield return p;
            yield return SupplyStationPosition + Quaternion.Euler(0f, SupplyStationYaw, 0f) * Vector3.forward * 1.3f;
            foreach (var p in CreatureStarts) yield return p;
        }

        private static void PlanLayout()
        {
            Walk.Clear();
            Patterns.Clear();
            layoutRandom = new System.Random(LayoutSeed);
            foreach (var room in Rooms)
                if (IsCorridor(room.Kind)) { Walk[room.Kind] = CorridorMask(room); Patterns[room.Kind] = Pattern.Corridor; }
        }

        private static System.Random layoutRandom;

        // A room's pattern is picked once its furniture is placed: seeded shuffle, first pattern that keeps
        // every gameplay spot, every doorway approach and every piece of furniture in open floor.
        private static void ChooseRoomPattern(RoomDef room, Transform furniture)
        {
            var options = new List<Pattern> { Pattern.Island, Pattern.Pillars, Pattern.PartitionX, Pattern.PartitionZ };
            // an L-shaped room (one corner walled off) as the fallback family
            var corners = new List<Pattern> { Pattern.CornerSW, Pattern.CornerSE, Pattern.CornerNW, Pattern.CornerNE };
            for (var i = corners.Count - 1; i > 0; i--) { var j = layoutRandom.Next(i + 1); (corners[i], corners[j]) = (corners[j], corners[i]); }
            for (var i = options.Count - 1; i > 0; i--) { var j = layoutRandom.Next(i + 1); (options[i], options[j]) = (options[j], options[i]); }
            options.AddRange(corners);
            options.Add(Pattern.Open);
            // the server room's rack rows are structure enough: no big block in the middle of it
            if (room.Kind == RoomKind.Server) { options.Clear(); options.Add(Pattern.Open); }
            var anchors = new List<Vector3>();
            if (furniture != null) foreach (Transform child in furniture) anchors.Add(child.position);
            foreach (var pattern in options)
            {
                var mask = RoomMask(room, pattern, layoutRandom);
                if (CoversReserved(room, mask)) continue;
                var hits = false;
                foreach (var a in anchors) if (InCell(room, a) && !MaskAt(room, mask, a)) { hits = true; break; }
                if (hits) continue;
                Walk[room.Kind] = mask;
                Patterns[room.Kind] = pattern;
                return;
            }
        }

        private static bool CoversReserved(RoomDef room, bool[,] mask)
        {
            foreach (var p in Reserved())
            {
                if (!InCell(room, p)) continue;
                for (var dx = -1; dx <= 1; dx++)
                for (var dz = -1; dz <= 1; dz++)
                    if (!MaskAt(room, mask, p + new Vector3(dx * 0.9f, 0f, dz * 0.9f))) return true;
            }
            // doorways (and 2 m in front of them) stay open
            foreach (var d in Doorways(room))
                for (var k = 0.5f; k <= 2.5f; k += 1f)
                    if (!MaskAt(room, mask, d.mid + d.inward * k)) return true;
            return false;
        }

        private static bool InCell(RoomDef room, Vector3 p) =>
            p.x >= XCols[room.Col] && p.x < XCols[room.Col + 1] && p.z >= ZRows[room.Row] && p.z < ZRows[room.Row + 1];

        private static bool MaskAt(RoomDef room, bool[,] mask, Vector3 p)
        {
            var ix = Mathf.FloorToInt(p.x - XCols[room.Col]);
            var iz = Mathf.FloorToInt(p.z - ZRows[room.Row]);
            if (ix < 0 || iz < 0 || ix >= CellSize || iz >= CellSize) return true;
            return mask[ix, iz];
        }

        // Is this world point in open space? (Cells without a mask - the ship - are all open.)
        private static bool Walkable(Vector3 p)
        {
            foreach (var room in Rooms)
                if (InCell(room, p)) return !Walk.TryGetValue(room.Kind, out var mask) || MaskAt(room, mask, p);
            return true;
        }

        private static Vector3 NearestWalkable(Vector3 p)
        {
            if (Walkable(p)) return p;
            for (var r = 1; r < 10; r++)
                for (var a = 0; a < 16; a++)
                {
                    var q = p + Quaternion.Euler(0f, a * 22.5f, 0f) * Vector3.forward * r;
                    if (Walkable(q)) return new Vector3(Mathf.Floor(q.x) + 0.5f, p.y, Mathf.Floor(q.z) + 0.5f);
                }
            return p;
        }

        // Open doorways of a cell: their midpoint on the border and the direction into the cell.
        private static IEnumerable<(Vector3 mid, Vector3 inward)> Doorways(RoomDef room)
        {
            var c = room.Center;
            float x0 = XCols[room.Col], x1 = XCols[room.Col + 1], z0 = ZRows[room.Row], z1 = ZRows[room.Row + 1];
            if (room.Col > 0) { var m = new Vector3(x0, 0f, c.z); if (!DoorwayClosed(m)) yield return (m, Vector3.right); }
            if (room.Col < 3) { var m = new Vector3(x1, 0f, c.z); if (!DoorwayClosed(m)) yield return (m, Vector3.left); }
            // the ship | dorm wall has no doorway
            if (room.Row > 0 && !(room.Row == 1 && room.Col == 0)) { var m = new Vector3(c.x, 0f, z0); if (!DoorwayClosed(m)) yield return (m, Vector3.forward); }
            if (room.Row < 2 && !(room.Row == 0 && room.Col == 0)) { var m = new Vector3(c.x, 0f, z1); if (!DoorwayClosed(m)) yield return (m, Vector3.back); }
        }

        // Corridor: a 4 m wide strip from the centre to every open doorway.
        private static bool[,] CorridorMask(RoomDef room)
        {
            var mask = new bool[CellSize, CellSize];
            var c = room.Center;
            void Fill(float xa, float xb, float za, float zb)
            {
                for (var ix = 0; ix < CellSize; ix++)
                for (var iz = 0; iz < CellSize; iz++)
                {
                    var x = XCols[room.Col] + ix + 0.5f;
                    var z = ZRows[room.Row] + iz + 0.5f;
                    if (x >= xa && x <= xb && z >= za && z <= zb) mask[ix, iz] = true;
                }
            }
            Fill(c.x - 2f, c.x + 2f, c.z - 2f, c.z + 2f);
            foreach (var (mid, _) in Doorways(room))
            {
                if (Mathf.Abs(mid.x - c.x) > 1f) Fill(Mathf.Min(mid.x, c.x), Mathf.Max(mid.x, c.x), c.z - 2f, c.z + 2f);
                else Fill(c.x - 2f, c.x + 2f, Mathf.Min(mid.z, c.z), Mathf.Max(mid.z, c.z));
            }
            return mask;
        }

        private static bool[,] RoomMask(RoomDef room, Pattern pattern, System.Random random)
        {
            var mask = new bool[CellSize, CellSize];
            for (var ix = 0; ix < CellSize; ix++) for (var iz = 0; iz < CellSize; iz++) mask[ix, iz] = true;
            void Block(int xa, int xb, int za, int zb)
            {
                for (var ix = Mathf.Max(0, xa); ix <= Mathf.Min(CellSize - 1, xb); ix++)
                for (var iz = Mathf.Max(0, za); iz <= Mathf.Min(CellSize - 1, zb); iz++) mask[ix, iz] = false;
            }
            switch (pattern)
            {
                case Pattern.Island: Block(6, 11, 6, 11); break;                                  // big central structure
                case Pattern.Pillars: Block(4, 5, 4, 5); Block(12, 13, 4, 5); Block(4, 5, 12, 13); Block(12, 13, 12, 13); break;
                case Pattern.PartitionX:                                                           // wall across, gap at one end
                    if (random.Next(2) == 0) Block(0, 11, 8, 9); else Block(6, 17, 8, 9);
                    break;
                case Pattern.PartitionZ:
                    if (random.Next(2) == 0) Block(8, 9, 0, 11); else Block(8, 9, 6, 17);
                    break;
                case Pattern.CornerSW: Block(0, 6, 0, 6); break;
                case Pattern.CornerSE: Block(11, 17, 0, 6); break;
                case Pattern.CornerNW: Block(0, 6, 11, 17); break;
                case Pattern.CornerNE: Block(11, 17, 11, 17); break;
            }
            return mask;
        }

        // Where a room's ceiling lamps hang. A room cut in two by a partition or wrapped around a central
        // structure gets one lamp over the middle of each side instead of one squeezed against the wall in the
        // centre; other rooms keep a single lamp over their (offset) centre.
        private static IEnumerable<Vector3> LampSpots(RoomDef room)
        {
            var c = room.Center;
            float x0 = XCols[room.Col], z0 = ZRows[room.Row];
            Patterns.TryGetValue(room.Kind, out var pattern);
            switch (pattern)
            {
                case Pattern.PartitionX: // wall along X across the room: south and north halves
                    yield return NearestWalkable(new Vector3(c.x, 0f, z0 + 4f));
                    yield return NearestWalkable(new Vector3(c.x, 0f, z0 + 14f));
                    break;
                case Pattern.PartitionZ: // wall along Z: west and east halves
                    yield return NearestWalkable(new Vector3(x0 + 4f, 0f, c.z));
                    yield return NearestWalkable(new Vector3(x0 + 14f, 0f, c.z));
                    break;
                case Pattern.Island: // the room rings a central block: one lamp each side of it
                    yield return NearestWalkable(new Vector3(x0 + 3f, 0f, c.z));
                    yield return NearestWalkable(new Vector3(x0 + 15f, 0f, c.z));
                    break;
                default:
                    yield return NearestWalkable(c + new Vector3(room.LampOffset.x, 0f, room.LampOffset.y));
                    break;
            }
        }

        // Walls around the solid parts of a corridor cell (the cell border already has walls).
        private static void BuildMaskWalls(Transform parent, RoomDef room, Material material)
        {
            var mask = Walk[room.Kind];
            float x0 = XCols[room.Col], z0 = ZRows[room.Row];
            // vertical edges (between ix-1 and ix), merged along z
            for (var ix = 1; ix < CellSize; ix++)
            {
                var start = -1;
                for (var iz = 0; iz <= CellSize; iz++)
                {
                    var edge = iz < CellSize && mask[ix - 1, iz] != mask[ix, iz];
                    if (edge && start < 0) start = iz;
                    if (!edge && start >= 0)
                    {
                        Box(parent, "CorridorWall", new Vector3(x0 + ix, WallHeight / 2f, z0 + (start + iz) / 2f), new Vector3(0.3f, WallHeight, iz - start + 0.3f), material);
                        start = -1;
                    }
                }
            }
            for (var iz = 1; iz < CellSize; iz++)
            {
                var start = -1;
                for (var ix = 0; ix <= CellSize; ix++)
                {
                    var edge = ix < CellSize && mask[ix, iz - 1] != mask[ix, iz];
                    if (edge && start < 0) start = ix;
                    if (!edge && start >= 0)
                    {
                        Box(parent, "CorridorWall", new Vector3(x0 + (start + ix) / 2f, WallHeight / 2f, z0 + iz), new Vector3(ix - start + 0.3f, WallHeight, 0.3f), material);
                        start = -1;
                    }
                }
            }
        }

        // Solid structures that break a room up: a block of machinery / containers in the middle, concrete
        // pillars, or a partition wall with a gap.
        private static void BuildBlockers(Transform parent, RoomDef room)
        {
            if (!Patterns.TryGetValue(room.Kind, out var pattern)) return;
            var concrete = Mat("WallConcrete", new Color(0.5f, 0.5f, 0.5f), tex: Texture("Concrete"), tiling: 8f, tilingY: 1.5f);
            var plaster = Mat("WallPlaster", new Color(0.62f, 0.6f, 0.55f), tex: Texture("Wallpaper"), tiling: 6f, tilingY: 1.5f);
            var mask = Walk[room.Kind];
            float x0 = XCols[room.Col], z0 = ZRows[room.Row];
            switch (pattern)
            {
                case Pattern.Island:
                {
                    var c = room.Center;
                    var core = Group(parent, "CentralStructure", c, 0f);
                    Box(core, "Block", new Vector3(0f, 1.6f, 0f), new Vector3(5.6f, 3.2f, 5.6f), concrete);
                    // dressed as a stack of containers / machinery depending on the room
                    var model = room.Kind == RoomKind.Server ? "KenneyFactory/machine" : "QuaterniusZombie/Container_Red";
                    Dressed(core, model, Vector3.zero, new Vector3(5.6f, 3.2f, 5.6f), room.Kind == RoomKind.Server ? 0f : 90f);
                    break;
                }
                case Pattern.Pillars:
                    foreach (var (ix, iz) in new[] { (5, 5), (13, 5), (5, 13), (13, 13) })
                        Box(parent, "Pillar", new Vector3(x0 + ix, WallHeight / 2f, z0 + iz), new Vector3(2f, WallHeight, 2f), concrete);
                    break;
                case Pattern.PartitionX:
                case Pattern.PartitionZ:
                case Pattern.CornerSW:
                case Pattern.CornerSE:
                case Pattern.CornerNW:
                case Pattern.CornerNE:
                    // build boxes over the blocked mask cells, merged per row
                    for (var iz = 0; iz < CellSize; iz++)
                    {
                        var start = -1;
                        for (var ix = 0; ix <= CellSize; ix++)
                        {
                            var solid = ix < CellSize && !mask[ix, iz];
                            if (solid && start < 0) start = ix;
                            if (!solid && start >= 0)
                            {
                                Box(parent, "Partition", new Vector3(x0 + (start + ix) / 2f, WallHeight / 2f, z0 + iz + 0.5f), new Vector3(ix - start, WallHeight, 1f), plaster);
                                start = -1;
                            }
                        }
                    }
                    break;
            }
        }

        // Corridors: pipes along the ceiling of every arm, hazard paint at the junction.
        private static void DressCorridor(Transform map, RoomDef room)
        {
            var root = new GameObject("Room_" + room.Kind).transform;
            root.SetParent(map, false);
            var wall = Mat("WallConcrete", new Color(0.5f, 0.5f, 0.5f), tex: Texture("Concrete"), tiling: 8f, tilingY: 1.5f);
            BuildMaskWalls(root, room, wall);
            var c = room.Center;
            Stripe(root, new Vector3(c.x, 0.01f, c.z), new Vector3(3.6f, 0.02f, 0.25f));
            foreach (var (mid, _) in Doorways(room))
            {
                var side = Mathf.Abs(mid.x - c.x) > 1f ? new Vector3(0f, 0f, 1.6f) : new Vector3(1.6f, 0f, 0f);
                Pipe(root, c + side + Vector3.up * 3.5f, mid + side + Vector3.up * 3.5f, 0.14f);
            }
        }

        // Removes furniture / clutter whose anchor ended up inside a solid area.
        private static void CullBlocked(Transform root)
        {
            var doomed = new List<GameObject>();
            foreach (Transform child in root)
                if (!Walkable(child.position) && !child.name.StartsWith("Partition") && !child.name.StartsWith("Pillar")
                    && !child.name.StartsWith("CentralStructure") && !child.name.StartsWith("CorridorWall")) doomed.Add(child.gameObject);
            foreach (var go in doomed) Object.DestroyImmediate(go);
        }
    }
}
