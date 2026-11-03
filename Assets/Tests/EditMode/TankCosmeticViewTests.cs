using BumperTanks.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace BumperTanks.Tests
{
    public class TankCosmeticViewTests
    {
        [Test]
        public void CheckeredFlagCreatesAnAttachedVisibleDecoration()
        {
            var tank = new GameObject("tank");
            try
            {
                var cosmetics = tank.AddComponent<TankCosmeticView>();
                cosmetics.ApplyFlag("checkered");

                var flag = tank.transform.Find("Tank Flag");
                Assert.That(flag, Is.Not.Null);
                Assert.That(flag.Find("Flag Pole"), Is.Not.Null);
                Assert.That(flag.Find("Flag Cloth"), Is.Not.Null);
                Assert.That(flag.Find("Flag Cloth/Flag Stripe"), Is.Not.Null);
                Assert.That(flag.GetComponentsInChildren<SpriteRenderer>().Length, Is.EqualTo(3));
            }
            finally
            {
                Object.DestroyImmediate(tank);
            }
        }

        [Test]
        public void NoneFlagRemovesTheVisibleDecoration()
        {
            var tank = new GameObject("tank");
            try
            {
                var cosmetics = tank.AddComponent<TankCosmeticView>();
                cosmetics.ApplyFlag("comet");
                cosmetics.ApplyFlag("none");

                Assert.That(tank.transform.Find("Tank Flag"), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(tank);
            }
        }
    }
}
