using System.Collections.Generic;
using UnityEngine;

namespace CoOpHorror.Editor
{
    // Second dressing pass, from the Lethal Company / R.E.P.O. study in Docs/MapReference/README.md:
    // - walls are never bare: pictures and a clock in the lived-in rooms, fire extinguishers, power and utility
    //   boxes and cable runs in corridors and plant rooms (an electricity game: the building's wiring shows);
    // - corridors stay clear on the floor (only things on the walls and dead fluorescent fittings on the ceiling);
    // - small things sit on furniture where people leave them (a clipboard and pens on the office desks).
    // Models are CC0 Poly Haven props converted by tools/blender/polyhaven_to_prop.py (low poly, 128 px).
    // Seeded, so every rebuild gives the same rooms. A missing model is skipped.
    public static partial class ElecMolePrototypeSetup
    {
        private static string PH(string name) => $"PolyHaven/{name}/{name}";

        private readonly struct WallProp
        {
            public readonly string Model;
            public readonly float Bottom, Height;
            public WallProp(string model, float bottom, float height) { Model = model; Bottom = bottom; Height = height; }
        }

        // Hung on walls, per room. Bottom = height of the lower edge above the floor.
        private static readonly Dictionary<RoomKind, WallProp[]> WallThemes = new()
        {
            [RoomKind.Dorm] = new[]
            {
                new WallProp(PH("hanging_picture_frame_01"), 1.5f, 0.6f), new WallProp(PH("hanging_picture_frame_02"), 1.45f, 0.7f),
                new WallProp(PH("wall_clock"), 2.1f, 0.35f), new WallProp(PH("fancy_picture_frame_01"), 1.4f, 0.8f),
            },
            [RoomKind.OfficeB] = new[]
            {
                new WallProp(PH("wall_clock"), 2.2f, 0.35f), new WallProp(PH("hanging_picture_frame_03"), 1.5f, 0.6f),
                new WallProp(PH("fire_alarm"), 1.5f, 0.18f), new WallProp(PH("korean_fire_extinguisher_01"), 0.1f, 0.55f),
            },
            [RoomKind.Breakroom] = new[]
            {
                new WallProp(PH("wall_clock"), 2.2f, 0.35f), new WallProp(PH("korean_fire_extinguisher_01"), 0.1f, 0.55f),
                new WallProp(PH("hanging_picture_frame_01"), 1.5f, 0.55f),
            },
            [RoomKind.Lair] = new[]
            {
                new WallProp(PH("power_box_01"), 1.1f, 0.7f), new WallProp(PH("utility_box_01"), 1.2f, 0.6f),
                new WallProp(PH("industrial_caged_sconce"), 2.4f, 0.3f), new WallProp(PH("korean_fire_extinguisher_01"), 0.1f, 0.55f),
            },
            [RoomKind.Server] = new[]
            {
                new WallProp(PH("power_box_01"), 1.1f, 0.7f), new WallProp(PH("utility_box_02"), 1.2f, 0.55f),
                new WallProp(PH("fire_alarm"), 1.5f, 0.18f),
            },
            [RoomKind.TransmitterRoom] = new[]
            {
                new WallProp(PH("utility_box_01"), 1.2f, 0.6f), new WallProp(PH("industrial_wall_lamp"), 2.5f, 0.35f),
                new WallProp(PH("korean_fire_extinguisher_01"), 0.1f, 0.55f),
            },
            // corridors: what a facility corridor has on its walls, nothing on the floor
            [RoomKind.HallS] = new[]
            {
                new WallProp(PH("korean_fire_extinguisher_01"), 0.1f, 0.55f), new WallProp(PH("power_box_01"), 1.1f, 0.7f),
                new WallProp(PH("industrial_caged_sconce"), 2.4f, 0.3f),
            },
            [RoomKind.Storage] = new[]
            {
                new WallProp(PH("utility_box_02"), 1.2f, 0.55f), new WallProp(PH("industrial_caged_sconce"), 2.4f, 0.3f),
            },
            [RoomKind.OfficeA] = new[]
            {
                new WallProp(PH("korean_fire_extinguisher_01"), 0.1f, 0.55f), new WallProp(PH("fire_alarm"), 1.5f, 0.18f),
                new WallProp(PH("hanging_picture_frame_02"), 1.5f, 0.6f),
            },
            [RoomKind.WarehouseE] = new[]
            {
                new WallProp(PH("power_box_01"), 1.1f, 0.7f), new WallProp(PH("industrial_wall_lamp"), 2.5f, 0.35f),
            },
            [RoomKind.CorridorN] = new[]
            {
                new WallProp(PH("korean_fire_extinguisher_01"), 0.1f, 0.55f), new WallProp(PH("utility_box_01"), 1.2f, 0.6f),
                new WallProp(PH("industrial_caged_sconce"), 2.4f, 0.3f),
            },
        };

        private const float WallPropSpacing = 3.5f;

        private static void DressWallsAndDesks(Transform clutter)
        {
            Physics.SyncTransforms();
            for (var r = 0; r < Rooms.Length; r++)
            {
                var room = Rooms[r];
                if (!Walk.TryGetValue(room.Kind, out var mask) || !WallThemes.TryGetValue(room.Kind, out var props)) continue;
                var random = new System.Random(LayoutSeed * 53 + r);
                var spots = WallSpots(room, mask);
                for (var i = spots.Count - 1; i > 0; i--) { var j = random.Next(i + 1); (spots[i], spots[j]) = (spots[j], spots[i]); }
                var used = new List<Vector3>();
                foreach (var prop in props)
                {
                    var size = FittedSize(new DecorProp(prop.Model, prop.Height));
                    if (size == Vector3.zero) continue;
                    foreach (var (cell, outward) in spots)
                    {
                        // against the wall face (walls reach ~0.2 m into the edge cell), front to the room
                        var yaw = Quaternion.LookRotation(-outward).eulerAngles.y;
                        var position = cell + outward * (0.5f - 0.22f - size.z / 2f) + Vector3.up * prop.Bottom;
                        if (!WallSpotFree(position, yaw, size, used)) continue;
                        Clutter(clutter, prop.Model, position, yaw, prop.Height, false);
                        used.Add(position);
                        break;
                    }
                }
            }
            DressCorridorCeilings(clutter);
            DressDesks(clutter);
        }

        // Nothing solid where the prop hangs (furniture tall enough to cover that bit of wall, other props), and
        // some air to the previous ones.
        private static bool WallSpotFree(Vector3 position, float yaw, Vector3 size, List<Vector3> used)
        {
            foreach (var u in used) if ((u - position).sqrMagnitude < WallPropSpacing * WallPropSpacing) return false;
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            var half = new Vector3(Mathf.Max(0.1f, size.x / 2f + 0.05f), Mathf.Max(0.1f, size.y / 2f), Mathf.Max(0.1f, size.z / 2f + 0.25f));
            var center = position + Vector3.up * size.y / 2f + rotation * Vector3.forward * 0.3f;
            foreach (var c in Physics.OverlapBox(center, half, rotation, ~0, QueryTriggerInteraction.Collide))
                if (!c.name.StartsWith("Floor") && !c.name.StartsWith("Ceiling")) return false;
            return true;
        }

        // Dead fluorescent fittings along every corridor arm (the corridor's real light is the pull-cord lamp in
        // the junction): the long dark corridors of a facility whose power is gone.
        private static void DressCorridorCeilings(Transform clutter)
        {
            var model = PH("mounted_fluorescent_lights");
            if (PropModel(model) == null) return;
            foreach (var room in Rooms)
            {
                if (!IsCorridor(room.Kind)) continue;
                var c = room.Center;
                foreach (var (mid, _) in Doorways(room))
                {
                    var along = new Vector3(mid.x - c.x, 0f, mid.z - c.z);
                    var length = along.magnitude;
                    var dir = along / length;
                    var yaw = Quaternion.LookRotation(dir).eulerAngles.y + 90f;
                    for (var d = 3.5f; d < length - 1.5f; d += 4.5f)
                        Clutter(clutter, model, c + dir * d + Vector3.up * (WallHeight - 0.06f), yaw, 0.045f, false);
                }
            }
        }

        // Paperwork left on the office desks, beside the monitor.
        private static void DressDesks(Transform clutter)
        {
            var map = clutter.parent;
            foreach (var roomName in new[] { "Room_OfficeB", "Room_OfficeA" })
            {
                var room = map.Find(roomName);
                if (room == null) continue;
                var k = 0;
                foreach (Transform child in room)
                {
                    if (child.name != "Desk") continue;
                    var top = child.position + Vector3.up * 1.05f;
                    var right = child.rotation * Vector3.right;
                    var front = child.rotation * Vector3.back; // the chair side
                    Clutter(clutter, PH("clipboard"), top - right * 0.35f + front * 0.25f, child.eulerAngles.y + 12f * (k % 3 - 1), 0.04f, false);
                    if (k % 2 == 0) Clutter(clutter, PH("stationery_supplies"), top + right * 0.75f + front * 0.1f, child.eulerAngles.y + 20f, 0.14f, false);
                    k++;
                }
            }
        }
    }
}
