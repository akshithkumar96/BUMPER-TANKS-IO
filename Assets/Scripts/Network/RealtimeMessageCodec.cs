using System;
using System.Collections.Generic;
using BumperTanks.Match;
using UnityEngine;

namespace BumperTanks.Network
{
    /// <summary>Wire-format codec shared by transports and protocol tests.</summary>
    public static class RealtimeMessageCodec
    {
        public static string EncodeJoin(string stablePlayerId, string displayName, string archetype = "SpeedyBumper", string accessToken = "")
        {
            if (string.IsNullOrEmpty(stablePlayerId) || stablePlayerId.Length != 32)
                throw new ArgumentException("Player ID must contain 32 characters.", nameof(stablePlayerId));
            return JsonUtility.ToJson(new RealtimeJoinMessage
            {
                type = "join",
                playerId = stablePlayerId,
                displayName = displayName ?? string.Empty,
                archetype = NormalizeArchetype(archetype),
                accessToken = accessToken ?? string.Empty
            });
        }

        public static string NormalizeArchetype(string archetype) => archetype switch
        {
            "HeavyDozer" => "HeavyDozer",
            "RicochetSpinner" => "RicochetSpinner",
            _ => "SpeedyBumper"
        };

        public static string EncodeInput(PlayerInputFrame input) => JsonUtility.ToJson(new RealtimeInputMessage
        {
            type = "input",
            tick = input.Tick,
            movement = new RealtimeVector { x = input.Movement.x, y = input.Movement.y },
            aim = new RealtimeVector { x = input.Aim.x, y = input.Aim.y },
            fire = input.Fire,
            ability = input.Ability
        });

        public static string ReadType(string json)
        {
            var header = JsonUtility.FromJson<RealtimeMessageHeader>(json);
            return header?.type;
        }

        public static bool TryReadJoined(string json, out RealtimeRoomJoined joined)
        {
            joined = default;
            var data = JsonUtility.FromJson<RealtimeJoinedMessage>(json);
            if (data == null || data.type != "joined" || string.IsNullOrEmpty(data.roomId) ||
                data.playerId <= 0 || data.tickRate <= 0) return false;
            joined = new RealtimeRoomJoined(data.roomId, data.playerId, data.tickRate, data.players ?? Array.Empty<RealtimePlayerInfo>());
            return true;
        }

        public static bool TryReadSnapshots(string json, out int tick, out TankSnapshot[] snapshots)
            => TryReadSnapshotFrame(json, out tick, out snapshots, out _, out _);

        public static bool TryReadSnapshotFrame(string json, out int tick, out TankSnapshot[] snapshots,
            out GemPickupSnapshot[] gems, out PowerUpPickupSnapshot[] powerUps)
        {
            tick = -1;
            snapshots = Array.Empty<TankSnapshot>();
            gems = Array.Empty<GemPickupSnapshot>();
            powerUps = Array.Empty<PowerUpPickupSnapshot>();
            var data = JsonUtility.FromJson<RealtimeSnapshotMessage>(json);
            if (data == null || data.type != "snapshot" || data.tick < 0 || data.players == null ||
                !Finite(data.elapsedSeconds) || !Finite(data.remainingSeconds)) return false;
            var arenaSize = Vector2.zero;
            var hasArenaBounds = json.IndexOf("\"arenaBounds\"", StringComparison.Ordinal) >= 0;
            if (hasArenaBounds && data.arenaBounds != null)
            {
                if (!Finite(data.arenaBounds.halfWidth) || !Finite(data.arenaBounds.halfHeight) ||
                    data.arenaBounds.halfWidth <= 0f || data.arenaBounds.halfHeight <= 0f) return false;
                arenaSize = new Vector2(data.arenaBounds.halfWidth * 2f, data.arenaBounds.halfHeight * 2f);
            }
            if (!TryReadGemPickups(data.gems, out gems) || !TryReadPowerUpPickups(data.powerUps, out powerUps)) return false;
            var hasAliveState = json.IndexOf("\"alive\"", StringComparison.Ordinal) >= 0;
            var parsed = new TankSnapshot[data.players.Length];
            for (var index = 0; index < data.players.Length; index++)
            {
                var player = data.players[index];
                if (player == null || player.playerId == 0 || player.position == null || player.velocity == null ||
                    !Finite(player.position.x) || !Finite(player.position.y) ||
                    !Finite(player.velocity.x) || !Finite(player.velocity.y) || !Finite(player.health) || player.gems < 0 || player.gems > 1000000) return false;
                parsed[index] = new TankSnapshot(data.tick, player.playerId,
                    new Vector2(player.position.x, player.position.y),
                    new Vector2(player.velocity.x, player.velocity.y), player.health,
                    hasAliveState ? player.alive : player.health > 0f, player.eliminations,
                    data.phase, data.elapsedSeconds, data.remainingSeconds, data.winnerPlayerId,
                    arenaSize, player.shielded, NormalizeArchetype(player.archetype), player.whirling,
                    player.gems, player.nitroActive, player.megaBulletActive, player.magnetActive);
            }
            tick = data.tick;
            snapshots = parsed;
            return true;
        }

        private static bool TryReadGemPickups(RealtimeGemPickupMessage[] messages, out GemPickupSnapshot[] gems)
        {
            gems = Array.Empty<GemPickupSnapshot>();
            if (messages == null) return true;
            if (messages.Length > 256) return false;
            var parsed = new GemPickupSnapshot[messages.Length];
            var ids = new HashSet<int>();
            for (var index = 0; index < messages.Length; index++)
            {
                var item = messages[index];
                if (item == null || item.pickupId <= 0 || !ids.Add(item.pickupId) || item.value < 1 || item.value > 1000 ||
                    item.position == null || !Finite(item.position.x) || !Finite(item.position.y)) return false;
                parsed[index] = new GemPickupSnapshot(item.pickupId, new Vector2(item.position.x, item.position.y), item.value);
            }
            gems = parsed;
            return true;
        }

        private static bool TryReadPowerUpPickups(RealtimePowerUpPickupMessage[] messages, out PowerUpPickupSnapshot[] powerUps)
        {
            powerUps = Array.Empty<PowerUpPickupSnapshot>();
            if (messages == null) return true;
            if (messages.Length > 256) return false;
            var parsed = new PowerUpPickupSnapshot[messages.Length];
            var ids = new HashSet<int>();
            for (var index = 0; index < messages.Length; index++)
            {
                var item = messages[index];
                if (item == null || item.pickupId <= 0 || !ids.Add(item.pickupId) || item.position == null ||
                    !Finite(item.position.x) || !Finite(item.position.y) ||
                    !Enum.TryParse(item.type, false, out PowerUpType type) || !Enum.IsDefined(typeof(PowerUpType), type)) return false;
                parsed[index] = new PowerUpPickupSnapshot(item.pickupId, type, new Vector2(item.position.x, item.position.y));
            }
            powerUps = parsed;
            return true;
        }

        public static string ReadError(string json)
        {
            var data = JsonUtility.FromJson<RealtimeErrorMessage>(json);
            if (data == null || data.type != "error") return string.Empty;
            if (!string.IsNullOrEmpty(data.message)) return data.message;
            return data.code ?? string.Empty;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
