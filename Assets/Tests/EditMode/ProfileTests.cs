using System;
using BumperTanks.Frontend;
using NUnit.Framework;

namespace BumperTanks.Tests
{
    public class ProfileTests
    {
        [Test]
        public void DailySpinOnlyClaimsOncePerDay()
        {
            var profile = new PlayerProfile();
            var today = new DateTime(2026, 11, 1);

            Assert.That(DailySpin.Claim(profile, today, 50), Is.EqualTo(50));
            Assert.That(profile.BattleCoins, Is.EqualTo(50));
            Assert.That(DailySpin.Claim(profile, today, 50), Is.Zero);
        }
    }
}
