using System.Collections;
using BumperTanks.Core;
using BumperTanks.Gameplay;
using BumperTanks.Match;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BumperTanks.Tests
{
    public sealed class RuntimePlayModeTests
    {
        private GameObject tankObject;
        private GameObject directorObject;

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
            if (tankObject != null) Object.Destroy(tankObject);
            if (directorObject != null) Object.Destroy(directorObject);
            yield return null;
            GameplayTuning.Reset();
        }

        [UnityTest]
        public IEnumerator TankControllerMovesAndClampsVelocityAtArchetypeSpeed()
        {
            tankObject = new GameObject("PlayMode tank");
            var body = tankObject.AddComponent<Rigidbody2D>();
            var tank = tankObject.AddComponent<TankController2D>();
            tank.Configure(TankArchetype.SpeedyBumper, false);
            tank.Drive(Vector2.right * 4f, Vector2.up);

            for (var frame = 0; frame < 40; frame++)
                yield return new WaitForFixedUpdate();

            Assert.That(body.linearVelocity.magnitude, Is.GreaterThan(0.5f));
            Assert.That(body.linearVelocity.magnitude, Is.LessThanOrEqualTo(8f * 1.3f + 0.05f));
        }

        [UnityTest]
        public IEnumerator MatchDirectorAdvancesAuthoritativePhaseAndArenaState()
        {
            directorObject = new GameObject("PlayMode match director");
            var director = directorObject.AddComponent<MatchDirector>();
            yield return null;

            director.ApplyRealtimeState(35f, new Vector2(10f, 8f));
            Assert.That(director.Phase, Is.EqualTo(MatchPhase.Collapse));
            Assert.That(director.CollapseSteps, Is.EqualTo(2));
            Assert.That(director.Bounds.Size, Is.EqualTo(new Vector2(10f, 8f)));

            director.ApplyRealtimeState(62f, new Vector2(3f, 3f));
            Assert.That(director.Phase, Is.EqualTo(MatchPhase.FinalZone));
            Assert.That(director.CollapseSteps, Is.EqualTo(6));

            director.ApplyRealtimeState(90f, new Vector2(3f, 3f));
            Assert.That(director.Phase, Is.EqualTo(MatchPhase.Complete));
        }
    }
}
