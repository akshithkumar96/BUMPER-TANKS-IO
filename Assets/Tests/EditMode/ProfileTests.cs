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

        [Test]
        public void TrailCosmeticChoiceSurvivesProfileSerialization()
        {
            var profile = new PlayerProfile { TrailId = "neon" };
            var restored = UnityEngine.JsonUtility.FromJson<PlayerProfile>(UnityEngine.JsonUtility.ToJson(profile));

            Assert.That(restored.TrailId, Is.EqualTo("neon"));
        }
    }
}
