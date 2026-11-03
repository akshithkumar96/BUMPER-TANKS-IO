using BumperTanks.Frontend;
using NUnit.Framework;

namespace BumperTanks.Tests
{
    public class ProfileTests
    {
        [Test]
        public void DailySpinRewardChoicesAreShownToPlayers()
        {
            Assert.That(DailySpin.RewardOptions, Is.EqualTo(new[] { 25, 50, 75, 100 }));
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
