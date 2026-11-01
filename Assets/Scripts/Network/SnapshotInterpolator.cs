using UnityEngine;

namespace BumperTanks.Network
{
    public sealed class SnapshotInterpolator
    {
        private TankSnapshot previous;
        private TankSnapshot latest;
        private bool hasPrevious;
        private bool hasLatest;

        public void Push(TankSnapshot snapshot)
        {
            if (!hasLatest)
            {
                latest = snapshot;
                hasLatest = true;
                return;
            }

            if (snapshot.Tick <= latest.Tick) return;
            previous = latest;
            latest = snapshot;
            hasPrevious = true;
        }

        public Vector2 Position(float alpha)
        {
            if (!hasLatest) return Vector2.zero;
            if (!hasPrevious) return latest.Position;
            return Vector2.Lerp(previous.Position, latest.Position, Mathf.Clamp01(alpha));
        }

        public Vector2 Velocity(float alpha)
        {
            if (!hasLatest) return Vector2.zero;
            if (!hasPrevious) return latest.Velocity;
            return Vector2.Lerp(previous.Velocity, latest.Velocity, Mathf.Clamp01(alpha));
        }
    }
}
