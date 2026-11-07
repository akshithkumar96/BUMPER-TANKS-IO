using System;
using BumperTanks.Match;
using UnityEngine;

namespace BumperTanks.Network
{
    public readonly struct PlayerInputFrame
    {
        public PlayerInputFrame(int tick, Vector2 movement, Vector2 aim, bool fire, bool ability)
        {
            Tick = tick;
            Movement = Vector2.ClampMagnitude(movement, 1f);
            Aim = aim.sqrMagnitude > 0f ? aim.normalized : Vector2.up;
            Fire = fire;
            Ability = ability;
        }

        public int Tick { get; }
        public Vector2 Movement { get; }
        public Vector2 Aim { get; }
        public bool Fire { get; }
        public bool Ability { get; }
    }

    public readonly struct TankSnapshot
    {
        public TankSnapshot(int tick, int playerId, Vector2 position, Vector2 velocity, float health,
            bool alive = true, int eliminations = 0, string phase = null, float elapsedSeconds = 0f,
            float remainingSeconds = 0f, int winnerPlayerId = 0, Vector2 arenaSize = default, bool shielded = false,
            string archetype = "SpeedyBumper", bool whirling = false, int gems = 0,
            bool nitroActive = false, bool megaBulletActive = false, bool magnetActive = false)
        {
            Tick = tick;
            PlayerId = playerId;
            Position = position;
            Velocity = velocity;
            Health = health;
            Alive = alive;
            Eliminations = eliminations;
            Phase = phase ?? string.Empty;
            ElapsedSeconds = elapsedSeconds;
            RemainingSeconds = remainingSeconds;
            WinnerPlayerId = winnerPlayerId;
            ArenaSize = arenaSize;
            Shielded = shielded;
            Archetype = archetype ?? "SpeedyBumper";
            Whirling = whirling;
            Gems = gems;
            NitroActive = nitroActive;
            MegaBulletActive = megaBulletActive;
            MagnetActive = magnetActive;
        }

        public int Tick { get; }
        public int PlayerId { get; }
        public Vector2 Position { get; }
        public Vector2 Velocity { get; }
        public float Health { get; }
        public bool Alive { get; }
        public int Eliminations { get; }
        public string Phase { get; }
        public float ElapsedSeconds { get; }
        public float RemainingSeconds { get; }
        /// <summary>Zero means the server has not selected a winner yet.</summary>
        public int WinnerPlayerId { get; }
        public Vector2 ArenaSize { get; }
        public bool Shielded { get; }
        public string Archetype { get; }
        public bool Whirling { get; }
        public int Gems { get; }
        public bool NitroActive { get; }
        public bool MegaBulletActive { get; }
        public bool MagnetActive { get; }
    }

    public readonly struct GemPickupSnapshot
    {
        public GemPickupSnapshot(int pickupId, Vector2 position, int value)
        {
            PickupId = pickupId;
            Position = position;
            Value = value;
        }

        public int PickupId { get; }
        public Vector2 Position { get; }
        public int Value { get; }
    }

    public readonly struct PowerUpPickupSnapshot
    {
        public PowerUpPickupSnapshot(int pickupId, PowerUpType type, Vector2 position)
        {
            PickupId = pickupId;
            Type = type;
            Position = position;
        }

        public int PickupId { get; }
        public PowerUpType Type { get; }
        public Vector2 Position { get; }
    }

    public readonly struct RealtimePickupFrame
    {
        public RealtimePickupFrame(int tick, GemPickupSnapshot[] gems, PowerUpPickupSnapshot[] powerUps)
        {
            Tick = tick;
            Gems = gems ?? Array.Empty<GemPickupSnapshot>();
            PowerUps = powerUps ?? Array.Empty<PowerUpPickupSnapshot>();
        }

        public int Tick { get; }
        public GemPickupSnapshot[] Gems { get; }
        public PowerUpPickupSnapshot[] PowerUps { get; }
    }

    public readonly struct RealtimeShotTrace
    {
        public RealtimeShotTrace(int shotId, int ownerPlayerId, int segmentIndex, int bounceIndex, string type,
            Vector2 start, Vector2 end, int hitPlayerId, bool hit, bool shieldBlocked, bool deflected = false)
        {
            ShotId = shotId;
            OwnerPlayerId = ownerPlayerId;
            SegmentIndex = segmentIndex;
            BounceIndex = bounceIndex;
            Type = type ?? string.Empty;
            Start = start;
            End = end;
            HitPlayerId = hitPlayerId;
            Hit = hit;
            ShieldBlocked = shieldBlocked;
            Deflected = deflected;
        }

        public int ShotId { get; }
        public int OwnerPlayerId { get; }
        public int SegmentIndex { get; }
        public int BounceIndex { get; }
        public string Type { get; }
        public Vector2 Start { get; }
        public Vector2 End { get; }
        public int HitPlayerId { get; }
        public bool Hit { get; }
        public bool ShieldBlocked { get; }
        public bool Deflected { get; }
    }

    public readonly struct RealtimeShotFrame
    {
        public RealtimeShotFrame(int tick, RealtimeShotTrace[] traces)
        {
            Tick = tick;
            Traces = traces ?? Array.Empty<RealtimeShotTrace>();
        }

        public int Tick { get; }
        public RealtimeShotTrace[] Traces { get; }
    }

    public interface IRealtimeSession : IDisposable
    {
        event Action<TankSnapshot> SnapshotReceived;
        bool IsConnected { get; }
        void SubmitInput(PlayerInputFrame input);
        void Disconnect();
    }

    [Serializable]
    public sealed class RealtimeMatchReceipt
    {
        public string matchId;
        public string playerId;
        public int gems;
        public int eliminations;
        public int award;
        public RealtimeProfileReceipt profile;
    }

    [Serializable]
    public sealed class RealtimeProfileReceipt
    {
        public string playerId;
        public int battleCoins;
        public int matchesPlayed;
        public string[] ownedCosmetics;
    }
}
