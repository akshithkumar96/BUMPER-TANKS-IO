using BumperTanks.Network;
using NUnit.Framework;
using UnityEngine;

namespace BumperTanks.Tests
{
    public class TankPredictionTests
    {
        [Test]
        public void PredictionUsesThirtyHertzInputStep()
        {
            var input = new PlayerInputFrame(1, Vector2.right, Vector2.right, false, false);
            var velocity = TankPrediction.PredictVelocity(Vector2.zero, input, 30f, 8f, 0f);

            Assert.That(velocity.x, Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void ReconciliationBlendsTowardAuthority()
        {
            Assert.That(TankPrediction.ReconcilePosition(Vector2.zero, new Vector2(10f, 0f), 0.25f), Is.EqualTo(new Vector2(2.5f, 0f)));
        }
    }
}
