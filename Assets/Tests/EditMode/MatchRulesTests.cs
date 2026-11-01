using BumperTanks.Match;
using NUnit.Framework;

namespace BumperTanks.Tests
{
    public class MatchRulesTests
    {
        [TestCase(0f, MatchPhase.OpenArena)]
        [TestCase(29.99f, MatchPhase.OpenArena)]
        [TestCase(30f, MatchPhase.Collapse)]
        [TestCase(60f, MatchPhase.FinalZone)]
        [TestCase(90f, MatchPhase.Complete)]
        public void PhaseTransitionsMatchGddTiming(float elapsed, MatchPhase expected)
        {
            Assert.That(MatchRules.PhaseAt(elapsed), Is.EqualTo(expected));
        }

        [Test]
        public void CollapseAddsOneRingEveryFiveSeconds()
        {
            Assert.That(MatchRules.CollapseStepsAt(30f), Is.EqualTo(1));
            Assert.That(MatchRules.CollapseStepsAt(34.99f), Is.EqualTo(1));
            Assert.That(MatchRules.CollapseStepsAt(35f), Is.EqualTo(2));
            Assert.That(MatchRules.CollapseStepsAt(55f), Is.EqualTo(6));
        }

        [Test]
        public void FinalZoneBoostsGemsAndKnockback()
        {
            Assert.That(MatchRules.GemMultiplier(60f), Is.EqualTo(2));
            Assert.That(MatchRules.KnockbackMultiplier(60f), Is.EqualTo(1.5f));
        }

        [Test]
        public void BotFillTargetsExactlyEightParticipants()
        {
            var fill = new BotFillService();

            Assert.That(fill.FillDue(2.5f), Is.True);
            Assert.That(fill.BotsRequired(3), Is.EqualTo(5));
            Assert.That(fill.BotsRequired(8), Is.Zero);
        }
    }
}
