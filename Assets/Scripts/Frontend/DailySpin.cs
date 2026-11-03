using System;
using System.Collections.Generic;

namespace BumperTanks.Frontend
{
    public static class DailySpin
    {
        private static readonly IReadOnlyList<int> rewards = Array.AsReadOnly(new[] { 25, 50, 75, 100 });
        public static IReadOnlyList<int> RewardOptions => rewards;
    }
}
