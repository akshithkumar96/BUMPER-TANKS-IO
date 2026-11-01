using BumperTanks.Network;
using NUnit.Framework;

namespace BumperTanks.Tests
{
    public class MatchmakingPolicyTests
    {
        [Test]
        public void NetworkPolicyMatchesGddQueueAndTickTargets()
        {
            Assert.That(MatchmakingPolicy.TargetQueueSeconds, Is.LessThanOrEqualTo(3f));
            Assert.That(MatchmakingPolicy.BotFillSeconds, Is.EqualTo(2.5f));
            Assert.That(MatchmakingPolicy.SimulationTicksPerSecond, Is.EqualTo(30));
            Assert.That(MatchmakingPolicy.BotsToFill(5), Is.EqualTo(3));
        }
    }
}
