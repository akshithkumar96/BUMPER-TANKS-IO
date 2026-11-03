using System.Collections.Generic;

namespace BumperTanks.Match
{
    public sealed class PowerUpRespawnService
    {
        private readonly List<PendingRespawn> pending = new();
        public int PendingCount => pending.Count;

        public void Schedule(PowerUpType type, float readyAt) => pending.Add(new PendingRespawn(type, readyAt));

        public bool TryTakeDue(float now, out PowerUpType type)
        {
            for (var i = 0; i < pending.Count; i++)
            {
                if (pending[i].ReadyAt > now) continue;
                type = pending[i].Type;
                pending.RemoveAt(i);
                return true;
            }
            type = default;
            return false;
        }

        private readonly struct PendingRespawn
        {
            public readonly PowerUpType Type;
            public readonly float ReadyAt;
            public PendingRespawn(PowerUpType type, float readyAt) => (Type, ReadyAt) = (type, readyAt);
        }
    }
}
