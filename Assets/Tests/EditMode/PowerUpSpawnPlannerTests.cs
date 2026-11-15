using System.Collections.Generic;
using BumperTanks.Core;
using BumperTanks.Match;
using NUnit.Framework;
using UnityEngine;

namespace BumperTanks.Tests
{
    public sealed class PowerUpSpawnPlannerTests
    {
        [Test]
        public void OpeningDropsAreRepeatableSeparatedAndAwayFromSpawnSlots()
        {
            var bounds = new ArenaBounds(Vector2.zero, new Vector2(16f, 12f));
            var avoid = new List<Vector2>();
            for (var slot = 0; slot < 8; slot++)
                avoid.Add(new Vector2((slot % 4 - 1.5f) * 2.5f, slot / 4 == 0 ? -2.5f : 2.5f));

            var first = PlanDrops(3921, bounds, avoid);
            var repeat = PlanDrops(3921, bounds, avoid);
            Assert.That(first, Is.EqualTo(repeat));
            Assert.That(first, Has.Count.EqualTo(3));
            foreach (var position in first)
            {
                Assert.That(bounds.Contains(position), Is.True);
                foreach (var spawn in avoid) Assert.That(Vector2.Distance(position, spawn), Is.GreaterThanOrEqualTo(1.8f));
            }
            for (var index = 0; index < first.Count; index++)
                for (var other = index + 1; other < first.Count; other++)
                    Assert.That(Vector2.Distance(first[index], first[other]), Is.GreaterThanOrEqualTo(1.8f));

            var otherMatch = PlanDrops(128, bounds, avoid);
            Assert.That(first, Is.Not.EqualTo(otherMatch));
        }

        [Test]
        public void DropsRemainInsideTheFinalShrinkingZoneWhenSpaceIsCrowded()
        {
            var bounds = new ArenaBounds(Vector2.zero, new Vector2(3f, 3f));
            var avoid = new[] { new Vector2(-0.7f, -0.7f), new Vector2(0.7f, 0.7f) };
            var occupied = new[] { new Vector2(-0.7f, 0.7f), new Vector2(0.7f, -0.7f) };
            var position = new PowerUpSpawnPlanner(77).Choose(bounds, avoid, occupied);

            Assert.That(bounds.Contains(position), Is.True);
            Assert.That(float.IsNaN(position.x) || float.IsNaN(position.y), Is.False);
        }

        private static List<Vector2> PlanDrops(int seed, ArenaBounds bounds, IReadOnlyList<Vector2> avoid)
        {
            var planner = new PowerUpSpawnPlanner(seed);
            var drops = new List<Vector2>();
            for (var index = 0; index < 3; index++) drops.Add(planner.Choose(bounds, avoid, drops));
            return drops;
        }
    }
}
