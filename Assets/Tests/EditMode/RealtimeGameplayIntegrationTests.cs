using BumperTanks.Core;
using BumperTanks.Gameplay;
using BumperTanks.Network;
using NUnit.Framework;
using System.Reflection;
using UnityEngine;

namespace BumperTanks.Tests
{
    public sealed class RealtimeGameplayIntegrationTests
    {
        private GameObject tankObject;
        private TankController2D tank;

        [SetUp]
        public void SetUp()
        {
            tankObject = new GameObject("Realtime input test tank");
            tank = tankObject.AddComponent<TankController2D>();
            var body = tankObject.GetComponent<Rigidbody2D>();
            typeof(TankController2D).GetField("body", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(tank, body);
            tank.Configure(TankArchetype.SpeedyBumper, true);
        }

        [TearDown]
        public void TearDown()
        {
            if (tankObject != null) Object.DestroyImmediate(tankObject);
            foreach (var projectile in Object.FindObjectsByType<Projectile2D>(FindObjectsSortMode.None))
                Object.DestroyImmediate(projectile.gameObject);
        }

        [Test]
        public void AbilityIntentIsIncludedInOnlyTheNextNetworkInputFrame()
        {
            tank.MarkNetworkAbilityActivated();

            var first = tank.ConsumeNetworkInput(12);
            var second = tank.ConsumeNetworkInput(13);

            Assert.That(first.Tick, Is.EqualTo(12));
            Assert.That(first.Ability, Is.True);
            Assert.That(second.Tick, Is.EqualTo(13));
            Assert.That(second.Ability, Is.False);
        }

        [Test]
        public void ReconciliationMovesLocalTankTowardAuthoritativeSnapshotAndUpdatesHealth()
        {
            tankObject.GetComponent<Rigidbody2D>().position = new Vector2(5f, 0f);
            var snapshot = new TankSnapshot(4, 1, new Vector2(1f, 3f), Vector2.right, 60f);

            tank.ReconcileNetworkState(snapshot, 0.5f);

            Assert.That(tank.Position, Is.EqualTo(new Vector2(3f, 1.5f)));
            Assert.That(tank.Health, Is.EqualTo(60f));
        }

        [Test]
        public void PausedUiSendsNeutralInputWithoutCarryingActionRequests()
        {
            tank.MarkNetworkAbilityActivated();

            var paused = tank.ConsumeNetworkInput(21, false);

            Assert.That(paused.Movement, Is.EqualTo(Vector2.zero));
            Assert.That(paused.Fire, Is.False);
            Assert.That(paused.Ability, Is.False);
        }
    }
}
