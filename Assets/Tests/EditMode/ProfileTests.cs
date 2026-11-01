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

        [Test]
        public void PlayerIdIsAnAppScopedIdentifier()
        {
            var playerId = ProfileStore.GetOrCreatePlayerId();
            Assert.That(playerId, Does.Match("^[a-f0-9]{32}$"));
            Assert.That(ProfileStore.GetOrCreatePlayerId(), Is.EqualTo(playerId));
        }
    }
}
