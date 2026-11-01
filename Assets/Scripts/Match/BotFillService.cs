using System;
using System.Collections.Generic;

namespace BumperTanks.Match
{
    public sealed class BotFillService
    {
        public const float FillDeadlineSeconds = 2.5f;
        public const int TargetPlayers = 8;

        public bool FillDue(float elapsedSeconds) => elapsedSeconds >= FillDeadlineSeconds;

        public int BotsRequired(int currentPlayers) => Math.Clamp(TargetPlayers - currentPlayers, 0, TargetPlayers);

        public IEnumerable<int> BotSlots(int currentPlayers)
        {
            for (var slot = 0; slot < BotsRequired(currentPlayers); slot++) yield return slot;
        }
    }
}
