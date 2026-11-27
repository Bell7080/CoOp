namespace CoOpHorror.Networking
{
    public static class RoomLimits
    {
        public const int RecommendedPlayers = 2;
        public const int MaxPlayers = 6;
        public const uint TickRate = 30;
        public const ushort ProtocolVersion = 8; // 8: clock spider climbing (drop RPCs, full rotation sync); 7: creature modes/latch/drain (monster RPCs and variables); 6: synchronized monster recognition; 5: item destruction
    }
}
