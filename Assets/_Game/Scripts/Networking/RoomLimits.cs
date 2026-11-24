namespace CoOpHorror.Networking
{
    public static class RoomLimits
    {
        public const int RecommendedPlayers = 2;
        public const int MaxPlayers = 6;
        public const uint TickRate = 30;
        public const ushort ProtocolVersion = 2; // 2: first-person fighting (v0.2.0)
    }
}
