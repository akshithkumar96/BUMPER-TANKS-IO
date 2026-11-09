namespace BumperTanks.Network
{
    public static class MatchmakingPolicy
    {
        public const float TargetQueueSeconds = 3f;
        public const float BotFillSeconds = 2.5f;
        public const int MaxPlayers = 8;
        public const int SimulationTicksPerSecond = 30;
        public const int ClientInputSendRateHz = 30;

        public static int BotsToFill(int connectedPlayers) => System.Math.Max(0, System.Math.Min(MaxPlayers, MaxPlayers - connectedPlayers));
        public static float TickDuration => 1f / SimulationTicksPerSecond;
    }

    /// <summary>Spaces input frames without accumulating catch-up sends after a slow frame.</summary>
    public sealed class InputSendCadence
    {
        private float nextSendTime = float.NegativeInfinity;

        public bool TryConsume(float now, int serverTickRate)
        {
            var rate = System.Math.Min(MatchmakingPolicy.ClientInputSendRateHz,
                serverTickRate > 0 ? serverTickRate : MatchmakingPolicy.SimulationTicksPerSecond);
            var interval = 1f / rate;
            if (now < nextSendTime) return false;
            nextSendTime = now + interval;
            return true;
        }

        public void Reset() => nextSendTime = float.NegativeInfinity;
    }
}
