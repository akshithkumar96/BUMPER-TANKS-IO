using BumperTanks.Core;
using NUnit.Framework;
using UnityEngine;

namespace BumperTanks.Tests
{
    public class TankSimulationTests
    {
        [Test]
        public void ArchetypesExposeGddSpeedAndMassModifiers()
        {
            var speedy = TankTuning.For(TankArchetype.SpeedyBumper);
            var heavy = TankTuning.For(TankArchetype.HeavyDozer);
            var spinner = TankTuning.For(TankArchetype.RicochetSpinner);

            Assert.That(speedy.SpeedMultiplier, Is.EqualTo(1.3f));
            Assert.That(speedy.MassMultiplier, Is.EqualTo(0.7f));
            Assert.That(heavy.SpeedMultiplier, Is.EqualTo(0.8f));
            Assert.That(heavy.MassMultiplier, Is.EqualTo(1.5f));
            Assert.That(spinner.SpeedMultiplier, Is.EqualTo(1f));
            Assert.That(spinner.MassMultiplier, Is.EqualTo(1f));
        }

        [Test]
        public void ArenaClampKeepsTankInsideGrid()
        {
            var arena = new ArenaBounds(Vector2.zero, new Vector2(10f, 8f));
            var clamped = arena.Clamp(new Vector2(9f, -7f));

            Assert.That(clamped, Is.EqualTo(new Vector2(5f, -4f)));
            Assert.That(arena.Contains(clamped), Is.True);
        }

        [Test]
        public void MotionNeverExceedsConfiguredSpeed()
        {
            var velocity = TankMotion.Step(Vector2.zero, Vector2.right, 100f, 6f, 0f, 0.02f);

            Assert.That(velocity.magnitude, Is.LessThanOrEqualTo(6f));
        }

        [Test]
        public void RecoilPushesTankOppositeShotDirection()
        {
            var velocity = TankMotion.Recoil(Vector2.zero, Vector2.right, 4f, 2f);

            Assert.That(velocity, Is.EqualTo(new Vector2(-2f, 0f)));
        }
    }
}
