using System.Collections.Generic;
using UnityEngine;

namespace CoOpHorror.Editor
{
    // Hand-drawn floor plans for the lived-in rooms, from the Lethal Company / R.E.P.O. study
    // (Docs/MapReference/README.md): instead of one 18 m hall with a random block in it, a cell is split into
    // rooms you can read at a glance - a bunk room and a living room with the TV, an open-plan office with a
    // filing room and the manager's office behind it, a kitchen and a dining area. Small side rooms with one
    // door are where things pile up (both games' "loot rooms"). Walls are 1 m thick like the other partitions,
    // doorways are 2 m wide with a lintel at 2.6 m.
    // Coordinates are 1 m cells local to the room's cell: ix from its west edge, iz from its south edge
    // (row 1 is 20 m deep, so iz runs to 19 there). Rectangles are inclusive.
    public static partial class ElecMolePrototypeSetup
    {
        private readonly struct CellRect
        {
            public readonly int X0, Z0, X1, Z1;
            public CellRect(int x0, int z0, int x1, int z1) { X0 = x0; Z0 = z0; X1 = x1; Z1 = z1; }
            public bool Contains(int ix, int iz) => ix >= X0 && ix <= X1 && iz >= Z0 && iz <= Z1;
        }

        private sealed class RoomPlan
        {
            public CellRect[] Walls;
            public CellRect[] Doors;   // cut out of the walls, lintel above
            public Vector2[] Lamps;    // local metres from the cell's south-west corner
        }

        private static readonly Dictionary<RoomKind, RoomPlan> RoomPlans = new()
        {
            // Dorm (x -36..-18, z -10..10): a long bunk room along the west wall (the beds, lockers, night stands)
            // behind a wall with two doors; the rest is a TV lounge (south) and a back room with the wardrobes and
            // the table (north), joined by an open passage along the east side from the dorm's two doors.
            [RoomKind.Dorm] = new RoomPlan
            {
                Walls = new[] { new CellRect(5, 0, 5, 19), new CellRect(6, 11, 13, 11) },
                Doors = new[] { new CellRect(5, 4, 5, 5), new CellRect(5, 14, 5, 15) },
                Lamps = new[] { new Vector2(2.6f, 10f), new Vector2(12f, 5f), new Vector2(10f, 16f) },
            },
            // Office B (x -36..-18, z 10..28): open-plan desks in front; behind a wall, a filing room (west) and
            // the manager's office with the radio and a wardrobe (east), each with its own door.
            [RoomKind.OfficeB] = new RoomPlan
            {
                Walls = new[] { new CellRect(0, 12, 17, 12), new CellRect(9, 13, 9, 17) },
                Doors = new[] { new CellRect(3, 12, 4, 12), new CellRect(12, 12, 13, 12) },
                Lamps = new[] { new Vector2(9f, 6f), new Vector2(4.5f, 15.5f), new Vector2(13.5f, 15.5f) },
            },
            // Break room (x 0..18, z -10..10): the kitchen along the north wall (counter, fridges) and the dining
            // area in front, split by a wall with a wide opening facing the server room door.
            [RoomKind.Breakroom] = new RoomPlan
            {
                Walls = new[] { new CellRect(0, 14, 6, 14), new CellRect(12, 14, 17, 14) },
                Doors = new CellRect[0],
                Lamps = new[] { new Vector2(9f, 17.5f), new Vector2(9f, 7f) },
            },
        };

        private static int CellDepth(RoomDef room) => Mathf.RoundToInt(ZRows[room.Row + 1] - ZRows[room.Row]);
        private static int CellWidth(RoomDef room) => Mathf.RoundToInt(XCols[room.Col + 1] - XCols[room.Col]);

        private static bool PlanSolid(RoomPlan plan, int ix, int iz)
        {
            var wall = false;
            foreach (var w in plan.Walls) if (w.Contains(ix, iz)) { wall = true; break; }
            if (!wall) return false;
            foreach (var d in plan.Doors) if (d.Contains(ix, iz)) return false;
            return true;
        }

        private static bool[,] PlanMask(RoomDef room, RoomPlan plan)
        {
            var mask = new bool[CellSize, CellSize];
            for (var ix = 0; ix < CellSize; ix++)
            for (var iz = 0; iz < CellSize; iz++) mask[ix, iz] = !PlanSolid(plan, ix, iz);
            return mask;
        }

        // The planned walls (merged per row, full cell depth) and a lintel over every doorway.
        private static void BuildPlanWalls(Transform parent, RoomDef room, RoomPlan plan, Material material)
        {
            float x0 = XCols[room.Col], z0 = ZRows[room.Row];
            int width = CellWidth(room), depth = CellDepth(room);
            for (var iz = 0; iz < depth; iz++)
            {
                var start = -1;
                for (var ix = 0; ix <= width; ix++)
                {
                    var solid = ix < width && PlanSolid(plan, ix, iz);
                    if (solid && start < 0) start = ix;
                    if (!solid && start >= 0)
                    {
                        Box(parent, "Partition", new Vector3(x0 + (start + ix) / 2f, WallHeight / 2f, z0 + iz + 0.5f), new Vector3(ix - start, WallHeight, 1f), material);
                        start = -1;
                    }
                }
            }
            const float lintelBottom = 2.6f;
            foreach (var d in plan.Doors)
            {
                var size = new Vector3(d.X1 - d.X0 + 1, WallHeight - lintelBottom, d.Z1 - d.Z0 + 1);
                var center = new Vector3(x0 + (d.X0 + d.X1 + 1) / 2f, lintelBottom + size.y / 2f, z0 + (d.Z0 + d.Z1 + 1) / 2f);
                Shape(parent, "Lintel", PrimitiveType.Cube, center, Vector3.zero, size, material);
            }
        }

        // Plans are fixed, so a gameplay spot or a piece of furniture inside a planned wall is a mistake in the
        // plan: say so in the build log instead of quietly picking something else.
        private static void CheckPlan(RoomDef room, bool[,] mask, Transform furniture)
        {
            if (CoversReserved(room, mask)) Debug.LogWarning($"[ElecMole] Room plan {room.Kind} covers a gameplay spot or doorway approach");
            if (furniture == null) return;
            foreach (Transform child in furniture)
                if (InCell(room, child.position) && !MaskAt(room, mask, child.position))
                    Debug.LogWarning($"[ElecMole] Room plan {room.Kind}: {child.name} at {child.position} is inside a wall");
        }
    }
}
