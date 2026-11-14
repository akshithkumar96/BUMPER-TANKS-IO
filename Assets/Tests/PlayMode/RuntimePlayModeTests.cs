using System.Collections;
using BumperTanks.Core;
using BumperTanks.Gameplay;
using BumperTanks.Match;
using NUnit.Framework;
using System.Reflection;
using UnityEngine;
using UnityEngine.TestTools;

namespace BumperTanks.Tests
{
    public sealed class RuntimePlayModeTests
    {
        private GameObject tankObject;
        private GameObject opponentObject;
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
            if (opponentObject != null) Object.Destroy(opponentObject);
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

        [Test]
        public void TankControllerClearsHeldTouchAndQueuedActionsWhenAppPauses()
        {
            tankObject = new GameObject("Paused mobile tank");
            tankObject.AddComponent<Rigidbody2D>();
            var tank = tankObject.AddComponent<TankController2D>();
            tank.Configure(TankArchetype.SpeedyBumper, true);
            tank.SetAccessibleSteering(Vector2.right);

            SetPrivateField(tank, "steeringFingerId", 4);
            SetPrivateField(tank, "aimFingerId", 7);
            SetPrivateField(tank, "pendingNetworkFire", true);
            SetPrivateField(tank, "pendingNetworkAbility", true);
            tankObject.SendMessage("OnApplicationPause", true);

            Assert.That(tank.Steering, Is.EqualTo(Vector2.zero));
            Assert.That(GetPrivateField<int>(tank, "steeringFingerId"), Is.EqualTo(-1));
            Assert.That(GetPrivateField<int>(tank, "aimFingerId"), Is.EqualTo(-1));
            Assert.That(GetPrivateField<bool>(tank, "pendingNetworkFire"), Is.False);
            Assert.That(GetPrivateField<bool>(tank, "pendingNetworkAbility"), Is.False);
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

        [UnityTest]
        public IEnumerator OfflineCollapseDamagesOutOfBoundsTanksWithoutTeleportingAndOnlineStateStaysAuthoritative()
        {
            directorObject = new GameObject("Boundary damage match director");
            var director = directorObject.AddComponent<MatchDirector>();
            tankObject = CreateStationaryTank("Out of bounds tank", new Vector2(6.8f, 0f));
            opponentObject = CreateStationaryTank("Arena opponent", Vector2.zero);
            var tank = tankObject.GetComponent<TankController2D>();
            SetPrivateField(director, "filledBots", true);
            SetPrivateField(director, "startedAt", Time.time - 35f);

            yield return null;
            Assert.That(tank.Health, Is.LessThan(100f));
            Assert.That(tankObject.transform.position.x, Is.EqualTo(6.8f).Within(0.001f));
            var offlineHealth = tank.Health;

            director.ApplyRealtimeState(35f, new Vector2(8f, 8f));
            yield return null;
            Assert.That(tank.Health, Is.EqualTo(offlineHealth));
            Assert.That(tankObject.transform.position.x, Is.EqualTo(6.8f).Within(0.001f));
        }

        private static void SetPrivateField<T>(object target, string name, T value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private static T GetPrivateField<T>(object target, string name) =>
            (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

        private static GameObject CreateStationaryTank(string name, Vector2 position)
        {
            var tank = new GameObject(name);
            tank.transform.position = position;
            var body = tank.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            tank.AddComponent<CircleCollider2D>();
            tank.AddComponent<TankController2D>().Configure(TankArchetype.SpeedyBumper, false);
            return tank;
        }
    }
}
