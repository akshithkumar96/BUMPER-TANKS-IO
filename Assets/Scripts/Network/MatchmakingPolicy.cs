namespace BumperTanks.Network
{
    public static class MatchmakingPolicy
    {
        public const float TargetQueueSeconds = 3f;
        public const float BotFillSeconds = 2.5f;
        public const int MaxPlayers = 8;
        public const int SimulationTicksPerSecond = 30;

        public static int BotsToFill(int connectedPlayers) => System.Math.Max(0, System.Math.Min(MaxPlayers, MaxPlayers - connectedPlayers));
        public static float TickDuration => 1f / SimulationTicksPerSecond;
    }
}
