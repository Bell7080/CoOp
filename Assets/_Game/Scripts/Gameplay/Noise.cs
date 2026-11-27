using System;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    // What made a noise. Each creature listens to its own channels (CreatureSenses): the TV walker
    // follows light, the fan crawler hears footsteps and doors, the clock spider barely listens at all.
    public enum NoiseKind : byte
    {
        Bang,      // generic loud event (a failed charge, a crash)
        Drain,     // an appliance (or a stunned creature) being drained: its flare and charging crackle
        Footstep,  // a player walking or running (crouching is silent)
        Door,      // a door button, a swing door clicked open or shut
        Radio,     // a walkie-talkie click, the ship's broadcast crackling out of an outside radio
        Alarm,     // the clock spider ringing
    }

    // Server-side loud events. Monsters within the radius (scaled by how well they hear that kind) walk over
    // to look; once there they spot players by sight as usual.
    public static class Noise
    {
        public const float DrainBaseRadius = 4f;    // even a tiny radio is heard this far
        public const float DrainRadiusPer100W = 3f; // grows with the square root of the appliance's watts

        public const float RunRadius = 10f;         // running footsteps
        public const float WalkRadius = 4f;         // walking footsteps
        public const float DoorRadius = 8f;         // sliding door buttons
        public const float SwingDoorRadius = 6f;    // hinged doors clicked open or shut
        public const float RadioRadius = 6f;        // pressing V outside
        public const float ShipCallRadius = 5f;     // the ship's broadcast crackling out of an outside radio

        public static event Action<Vector3, float, NoiseKind> Server;

        // How far the light and charging sound of a drained appliance carry: 30 W ~5.6 m, 120 W ~7.3 m,
        // 650 W ~11.6 m, 1000 W ~13.5 m, 1500 W ~15.6 m.
        public static float DrainRadius(int watts) =>
            DrainBaseRadius + DrainRadiusPer100W * Mathf.Sqrt(Mathf.Max(0, watts) / 100f);

        public static void Emit(Vector3 position, float radius, NoiseKind kind = NoiseKind.Bang)
        {
            if (radius > 0f) Server?.Invoke(position, radius, kind);
        }
    }
}
