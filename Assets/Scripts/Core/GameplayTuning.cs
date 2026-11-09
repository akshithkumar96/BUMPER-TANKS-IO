using UnityEngine;

namespace BumperTanks.Core
{
    /// <summary>Validated values applied from the first-party tuning service.</summary>
    public static class GameplayTuning
    {
        public const float DefaultSpeedMultiplier = 1f;
        public const float DefaultProjectileCooldownMultiplier = 1f;
        public const float DefaultKnockbackMultiplier = 1f;

        public static float SpeedMultiplier { get; private set; } = DefaultSpeedMultiplier;
        public static float ProjectileCooldownMultiplier { get; private set; } = DefaultProjectileCooldownMultiplier;
        public static float KnockbackMultiplier { get; private set; } = DefaultKnockbackMultiplier;

        public static bool TryApply(float speedMultiplier, float projectileCooldownMultiplier, float knockbackMultiplier)
        {
            if (!IsValid(speedMultiplier, projectileCooldownMultiplier, knockbackMultiplier)) return false;

            SpeedMultiplier = speedMultiplier;
            ProjectileCooldownMultiplier = projectileCooldownMultiplier;
            KnockbackMultiplier = knockbackMultiplier;
            return true;
        }

        public static void Reset()
        {
            SpeedMultiplier = DefaultSpeedMultiplier;
            ProjectileCooldownMultiplier = DefaultProjectileCooldownMultiplier;
            KnockbackMultiplier = DefaultKnockbackMultiplier;
        }

        public static bool IsValid(float speedMultiplier, float projectileCooldownMultiplier, float knockbackMultiplier) =>
            IsInRange(speedMultiplier, 0.75f, 1.25f) &&
            IsInRange(projectileCooldownMultiplier, 0.75f, 1.5f) &&
            IsInRange(knockbackMultiplier, 0.75f, 1.5f);

        private static bool IsInRange(float value, float minimum, float maximum) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && value >= minimum && value <= maximum;
    }
}
