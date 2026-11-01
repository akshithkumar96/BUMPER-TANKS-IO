using UnityEngine;

namespace BumperTanks.Combat
{
    public static class CombatMath
    {
        public const float RearHitKnockbackMultiplier = 1.5f;

        public static bool IsRearHit(Vector2 targetForward, Vector2 impactDirection)
        {
            if (targetForward.sqrMagnitude <= 0f || impactDirection.sqrMagnitude <= 0f)
            {
                return false;
            }

            return Vector2.Dot(targetForward.normalized, impactDirection.normalized) < 0f;
        }

        public static float Knockback(float impulse, float targetMass, bool rearHit)
        {
            if (targetMass <= 0f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(targetMass));
            }

            var multiplier = rearHit ? RearHitKnockbackMultiplier : 1f;
            return Mathf.Max(0f, impulse) * multiplier / targetMass;
        }
    }
}
