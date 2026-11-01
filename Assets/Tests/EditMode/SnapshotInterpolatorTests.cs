using BumperTanks.Network;
using NUnit.Framework;
using UnityEngine;

namespace BumperTanks.Tests
{
    public class SnapshotInterpolatorTests
    {
        [Test]
        public void InterpolatesBetweenSequentialSnapshots()
        {
            var buffer = new SnapshotInterpolator();
            buffer.Push(new TankSnapshot(1, 1, Vector2.zero, Vector2.right, 100f));
            buffer.Push(new TankSnapshot(2, 1, new Vector2(10f, 0f), Vector2.up, 90f));

            Assert.That(buffer.Position(0.5f), Is.EqualTo(new Vector2(5f, 0f)));
            Assert.That(buffer.Velocity(0.5f), Is.EqualTo(new Vector2(0.5f, 0.5f)));
        }

        [Test]
        public void RejectsOutOfOrderSnapshots()
        {
            var buffer = new SnapshotInterpolator();
            buffer.Push(new TankSnapshot(2, 1, new Vector2(5f, 0f), Vector2.zero, 100f));
            buffer.Push(new TankSnapshot(1, 1, Vector2.zero, Vector2.zero, 100f));

            Assert.That(buffer.Position(1f), Is.EqualTo(new Vector2(5f, 0f)));
        }
    }
}
