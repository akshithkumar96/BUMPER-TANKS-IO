using UnityEngine;

namespace BumperTanks.Core
{
    public readonly struct TankTuning
    {
        public TankTuning(float speedMultiplier, float massMultiplier, float shotKnockbackMultiplier)
        {
            SpeedMultiplier = speedMultiplier;
            MassMultiplier = massMultiplier;
            ShotKnockbackMultiplier = shotKnockbackMultiplier;
        }

        public float SpeedMultiplier { get; }
        public float MassMultiplier { get; }
        public float ShotKnockbackMultiplier { get; }

        public static TankTuning For(TankArchetype archetype)
        {
            return archetype switch
            {
                TankArchetype.SpeedyBumper => new TankTuning(1.3f, 0.7f, 1f),
                TankArchetype.HeavyDozer => new TankTuning(0.8f, 1.5f, 1f),
                TankArchetype.RicochetSpinner => new TankTuning(1f, 1f, 1f),
                _ => throw new System.ArgumentOutOfRangeException(nameof(archetype), archetype, null)
            };
        }

        public float MaxSpeed(float baseSpeed) => baseSpeed * SpeedMultiplier;

        public float Mass(float baseMass) => baseMass * MassMultiplier;
    }
}
