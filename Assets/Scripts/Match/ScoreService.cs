using System.Collections.Generic;

namespace BumperTanks.Match
{
    public sealed class ScoreService
    {
        private readonly Dictionary<int, int> gems = new();
        private readonly Dictionary<int, int> eliminations = new();

        public int GemsFor(int playerId) => gems.TryGetValue(playerId, out var value) ? value : 0;
        public int EliminationsFor(int playerId) => eliminations.TryGetValue(playerId, out var value) ? value : 0;

        public void AwardGem(int playerId, int multiplier)
        {
            gems[playerId] = GemsFor(playerId) + multiplier;
        }

        public void AwardElimination(int playerId)
        {
            eliminations[playerId] = EliminationsFor(playerId) + 1;
        }
    }
}
