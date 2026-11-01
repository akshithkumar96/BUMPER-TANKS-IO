using System;

namespace BumperTanks.Frontend
{
    public static class DailySpin
    {
        public static bool CanClaim(PlayerProfile profile, DateTime utcNow) => profile.DailySpinDay != utcNow.DayOfYear;

        public static int Claim(PlayerProfile profile, DateTime utcNow, int reward)
        {
            if (!CanClaim(profile, utcNow)) return 0;
            profile.DailySpinDay = utcNow.DayOfYear;
            profile.BattleCoins += reward;
            return reward;
        }
    }
}
