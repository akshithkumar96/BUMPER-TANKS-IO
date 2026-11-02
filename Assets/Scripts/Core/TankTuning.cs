using UnityEngine;

namespace BumperTanks.Core
{
    public readonly struct TankTuning
    {
        public TankTuning(float speedMultiplier, float massMultiplier, float shotKnockbackMultiplier,
            float projectileSpeedMultiplier = 1f, float projectileCooldownMultiplier = 1f,
            float projectileSizeMultiplier = 1f, int projectileBounces = 0)
        {
            SpeedMultiplier = speedMultiplier;
            MassMultiplier = massMultiplier;
            ShotKnockbackMultiplier = shotKnockbackMultiplier;
            ProjectileSpeedMultiplier = projectileSpeedMultiplier;
            ProjectileCooldownMultiplier = projectileCooldownMultiplier;
            ProjectileSizeMultiplier = projectileSizeMultiplier;
            ProjectileBounces = projectileBounces;
        }

        public float SpeedMultiplier { get; }
        public float MassMultiplier { get; }
        public float ShotKnockbackMultiplier { get; }
        public float ProjectileSpeedMultiplier { get; }
        public float ProjectileCooldownMultiplier { get; }
        public float ProjectileSizeMultiplier { get; }
        public int ProjectileBounces { get; }

        public static TankTuning For(TankArchetype archetype)
        {
            return archetype switch
            {
                TankArchetype.SpeedyBumper => new TankTuning(1.3f, 0.7f, 0.75f, 1.2f, 0.65f, 0.75f),
                TankArchetype.HeavyDozer => new TankTuning(0.8f, 1.5f, 1.8f, 0.7f, 1.8f, 1.6f),
                TankArchetype.RicochetSpinner => new TankTuning(1f, 1f, 1f, 1f, 1.15f, 1f, 3),
                _ => throw new System.ArgumentOutOfRangeException(nameof(archetype), archetype, null)
            };
        }

        public float MaxSpeed(float baseSpeed) => baseSpeed * SpeedMultiplier;

        public float Mass(float baseMass) => baseMass * MassMultiplier;
    }
}
