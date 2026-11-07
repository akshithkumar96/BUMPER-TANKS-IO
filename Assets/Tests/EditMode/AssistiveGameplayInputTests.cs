using BumperTanks.Core;
using BumperTanks.Combat;
using BumperTanks.Gameplay;
using NUnit.Framework;
using System.Reflection;
using UnityEngine;

namespace BumperTanks.Tests
{
    public sealed class AssistiveGameplayInputTests
    {
        private GameObject tankObject;
        private TankController2D tank;

        [SetUp]
        public void SetUp()
        {
            tankObject = new GameObject("Assistive input test tank");
            tank = tankObject.AddComponent<TankController2D>();
            var body = tankObject.GetComponent<Rigidbody2D>();
            typeof(TankController2D).GetField("body", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(tank, body);
            tank.Configure(TankArchetype.SpeedyBumper, true);
            typeof(TankController2D).GetField("weapon", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(tank, new TankWeapon(0.35f, 6f));
        }

        [TearDown]
        public void TearDown()
        {
            if (tankObject != null) Object.DestroyImmediate(tankObject);
            foreach (var projectile in Object.FindObjectsByType<Projectile2D>(FindObjectsSortMode.None))
                Object.DestroyImmediate(projectile.gameObject);
        }

        [Test]
        public void DirectionalMovementCreatesBoundedInputPulse()
        {
            tank.SetAccessibleSteering(new Vector2(2f, 2f), 0.5f);

            var input = tank.ConsumeNetworkInput(3);

            Assert.That(input.Movement.magnitude, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(input.Movement.x, Is.EqualTo(1f / Mathf.Sqrt(2f)).Within(0.0001f));
            Assert.That(input.Movement.y, Is.EqualTo(1f / Mathf.Sqrt(2f)).Within(0.0001f));
        }

        [Test]
        public void CardinalAimAndFireUsesNormalWeaponAndNetworkInputPath()
        {
            tank.AccessibleAimAndFire(Vector2.right);

            var input = tank.ConsumeNetworkInput(4);

            Assert.That(input.Aim, Is.EqualTo(Vector2.right));
            Assert.That(input.Fire, Is.True);
        }
    }
}
