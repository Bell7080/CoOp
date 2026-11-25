using System;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    // Server-side loud events (generator blowing up, failed charge). Monsters within the radius come to look.
    public static class Noise
    {
        public static event Action<Vector3, float> Server;

        public static void Emit(Vector3 position, float radius) => Server?.Invoke(position, radius);
    }
}
