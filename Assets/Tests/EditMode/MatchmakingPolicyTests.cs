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
            Assert.That(MatchmakingPolicy.ClientInputSendRateHz, Is.EqualTo(30));
            Assert.That(MatchmakingPolicy.ClientInputSendRateHz, Is.LessThan(60));
            Assert.That(MatchmakingPolicy.BotsToFill(5), Is.EqualTo(3));
        }

        [Test]
        public void InputCadenceCapsAtServerRateAndDoesNotBurstAfterSlowFrames()
        {
            var cadence = new InputSendCadence();
            Assert.That(cadence.TryConsume(1f, 30), Is.True);
            Assert.That(cadence.TryConsume(1.01f, 30), Is.False);
            Assert.That(cadence.TryConsume(1.034f, 30), Is.True);
            Assert.That(cadence.TryConsume(3f, 30), Is.True);
            Assert.That(cadence.TryConsume(3.001f, 30), Is.False);
            Assert.That(cadence.TryConsume(3.04f, 120), Is.True);
            Assert.That(cadence.TryConsume(3.074f, 120), Is.True);
        }
    }
}
