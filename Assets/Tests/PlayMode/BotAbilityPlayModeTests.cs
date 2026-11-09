using System.Collections;
using BumperTanks.Core;
using BumperTanks.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BumperTanks.Tests
{
    public sealed class BotAbilityPlayModeTests
    {
        private GameObject botObject;

        [SetUp]
        public void SetUp()
        {
            Time.timeScale = 1f;
            GameplayTuning.Reset();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            if (botObject != null) Object.Destroy(botObject);
            yield return null;
            GameplayTuning.Reset();
        }

        [UnityTest]
        public IEnumerator BotUsesArchetypeAbilityWhenOpponentIsNearbyAndRespectsCooldown()
        {
            botObject = new GameObject("PlayMode bot");
            botObject.AddComponent<Rigidbody2D>();
            var tank = botObject.AddComponent<TankController2D>();
            tank.Configure(TankArchetype.SpeedyBumper, false);
            var ability = botObject.AddComponent<TankAbilityController>();
            var bot = botObject.AddComponent<BotDriver>();

            Assert.That(bot.TryUseAbility(6.1f), Is.False, "Bots should save their ability until in range.");
            Assert.That(bot.TryUseAbility(5f), Is.True);
            Assert.That(tank.IsDashing, Is.True);
            Assert.That(ability.ReadyIn, Is.GreaterThan(0f));
            Assert.That(bot.TryUseAbility(1f), Is.False, "A bot should not bypass its ability cooldown.");
            yield return null;
        }
    }
}
