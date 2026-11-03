using BumperTanks.Network;
using NUnit.Framework;
using UnityEngine;

namespace BumperTanks.Network.Tests
{
    public class RealtimeMessageCodecTests
    {
        [Test]
        public void JoinMessageUsesProtocolKeysAndJsonEscapesNames()
        {
            const string playerId = "0123456789abcdef0123456789abcdef";
            const string name = "Ace \"Bumper\"";
            var json = RealtimeMessageCodec.EncodeJoin(playerId, name);
            var decoded = JsonUtility.FromJson<JoinShape>(json);

            Assert.That(decoded.type, Is.EqualTo("join"));
            Assert.That(decoded.playerId, Is.EqualTo(playerId));
            Assert.That(decoded.displayName, Is.EqualTo(name));
        }

        [Test]
        public void InputMessageCarriesNormalizedMovementAndAimFlags()
        {
            var json = RealtimeMessageCodec.EncodeInput(new PlayerInputFrame(17, new Vector2(3f, 4f), Vector2.right, true, false));
            var decoded = JsonUtility.FromJson<InputShape>(json);

            Assert.That(decoded.type, Is.EqualTo("input"));
            Assert.That(decoded.tick, Is.EqualTo(17));
            Assert.That(new Vector2(decoded.movement.x, decoded.movement.y).magnitude, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(decoded.aim.x, Is.EqualTo(1f));
            Assert.That(decoded.fire, Is.True);
            Assert.That(decoded.ability, Is.False);
        }

        [Test]
        public void JoinedMessageParsesAssignedRoomAndPlayer()
        {
            const string json = "{\"type\":\"joined\",\"roomId\":\"room-01\",\"playerId\":3,\"tickRate\":30,\"players\":[{\"playerId\":3,\"displayName\":\"Ace\"}]}";

            Assert.That(RealtimeMessageCodec.TryReadJoined(json, out var joined), Is.True);
            Assert.That(joined.RoomId, Is.EqualTo("room-01"));
            Assert.That(joined.PlayerId, Is.EqualTo(3));
            Assert.That(joined.TickRate, Is.EqualTo(30));
            Assert.That(joined.Players.Length, Is.EqualTo(1));
            Assert.That(joined.Players[0].displayName, Is.EqualTo("Ace"));
        }

        [Test]
        public void SnapshotMessageParsesEveryPlayerState()
        {
            const string json = "{\"type\":\"snapshot\",\"tick\":42,\"players\":[{\"playerId\":1,\"position\":{\"x\":1.5,\"y\":-2},\"velocity\":{\"x\":3,\"y\":4},\"health\":75},{\"playerId\":-1,\"position\":{\"x\":-1,\"y\":2},\"velocity\":{\"x\":0,\"y\":-1},\"health\":100}]}";

            Assert.That(RealtimeMessageCodec.TryReadSnapshots(json, out var tick, out var snapshots), Is.True);
            Assert.That(tick, Is.EqualTo(42));
            Assert.That(snapshots.Length, Is.EqualTo(2));
            Assert.That(snapshots[0].Position, Is.EqualTo(new Vector2(1.5f, -2f)));
            Assert.That(snapshots[0].Velocity, Is.EqualTo(new Vector2(3f, 4f)));
            Assert.That(snapshots[0].Health, Is.EqualTo(75f));
            Assert.That(snapshots[1].PlayerId, Is.EqualTo(-1));
        }

        [Test]
        public void MalformedSnapshotsAndWrongTypesAreRejected()
        {
            Assert.That(RealtimeMessageCodec.TryReadSnapshots("{\"type\":\"joined\"}", out _, out _), Is.False);
            Assert.That(RealtimeMessageCodec.TryReadSnapshots("{\"type\":\"snapshot\",\"tick\":-1,\"players\":[]}", out _, out _), Is.False);
            Assert.That(RealtimeMessageCodec.TryReadSnapshots("{\"type\":\"snapshot\",\"tick\":5,\"players\":[{\"playerId\":0}]}", out _, out _), Is.False);
            Assert.That(RealtimeMessageCodec.TryReadJoined("{\"type\":\"joined\",\"roomId\":\"\",\"playerId\":1,\"tickRate\":30}", out _), Is.False);
        }

        [Test]
        public void ServerErrorCodeIsExposedWhenNoHumanReadableMessageExists()
        {
            Assert.That(RealtimeMessageCodec.ReadError("{\"type\":\"error\",\"code\":\"player_already_connected\"}"),
                Is.EqualTo("player_already_connected"));
        }

        [System.Serializable]
        private sealed class JoinShape { public string type; public string playerId; public string displayName; }

        [System.Serializable]
        private sealed class InputShape
        {
            public string type;
            public int tick;
            public VectorShape movement;
            public VectorShape aim;
            public bool fire;
            public bool ability;
        }

        [System.Serializable]
        private sealed class VectorShape { public float x; public float y; }
    }
}
