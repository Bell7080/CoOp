using CoOpHorror.Player;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    // Who hears whom (game rules only; the future voice provider asks these questions and plays audio).
    // - Near: everyone alive within ProximityRange (3D, walls later).
    // - Walkie-talkie (E outside): heard by the players inside the ship, through the ship's speakers.
    // - Ship microphone (E at the mic): heard by every player, in-ear.
    // Dead players (ghosts) hear the living but nobody hears them.
    public static class VoiceRouting
    {
        public const float ProximityRange = 18f;

        public static bool HearsNearby(PlayerRadio listener, PlayerRadio speaker)
        {
            if (listener == null || speaker == null || listener == speaker || IsDead(speaker)) return false;
            return (listener.transform.position - speaker.transform.position).sqrMagnitude <= ProximityRange * ProximityRange;
        }

        public static bool HearsOverRadio(PlayerRadio listener, PlayerRadio speaker, RadioMode mode)
        {
            if (listener == null || speaker == null || listener == speaker || IsDead(speaker)) return false;
            switch (mode)
            {
                case RadioMode.ShipMic:
                    return true;
                case RadioMode.Walkie:
                    var stage = StageDirector.Instance;
                    return stage == null || stage.InShip(listener.transform.position) || IsDead(listener);
                default:
                    return false;
            }
        }

        private static bool IsDead(Component player) =>
            player.TryGetComponent<PlayerHealth>(out var health) && health.IsDead.Value;
    }
}
