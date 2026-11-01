using System;
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
        public TankSnapshot(int tick, int playerId, Vector2 position, Vector2 velocity, float health)
        {
            Tick = tick;
            PlayerId = playerId;
            Position = position;
            Velocity = velocity;
            Health = health;
        }

        public int Tick { get; }
        public int PlayerId { get; }
        public Vector2 Position { get; }
        public Vector2 Velocity { get; }
        public float Health { get; }
    }

    public interface IRealtimeSession : IDisposable
    {
        event Action<TankSnapshot> SnapshotReceived;
        bool IsConnected { get; }
        void SubmitInput(PlayerInputFrame input);
        void Disconnect();
    }
}
