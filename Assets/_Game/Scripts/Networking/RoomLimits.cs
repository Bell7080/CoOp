namespace CoOpHorror.Networking
{
    public static class RoomLimits
    {
        public const int RecommendedPlayers = 2;
        public const int MaxPlayers = 6;
        public const uint TickRate = 30;
        public const ushort ProtocolVersion = 4; // 2: first-person fighting (v0.2.0), 3: ElecMole prototype (v0.3.0), 4: telekinesis, cords, bodies, CCTV map (v0.4.0)
    }
}
