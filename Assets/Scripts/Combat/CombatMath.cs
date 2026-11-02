using UnityEngine;

namespace BumperTanks.Combat
{
    public static class CombatMath
    {
        public const float FlankHitKnockbackMultiplier = 1.5f;

        public static bool IsFlankHit(Vector2 targetForward, Vector2 impactDirection)
        {
            if (targetForward.sqrMagnitude <= 0f || impactDirection.sqrMagnitude <= 0f)
            {
                return false;
            }

            return Mathf.Abs(Vector2.Dot(targetForward.normalized, impactDirection.normalized)) <= 0.5f;
        }

        public static float Knockback(float impulse, float targetMass, bool flankHit, float phaseMultiplier = 1f)
        {
            if (targetMass <= 0f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(targetMass));
            }
            if (phaseMultiplier < 0f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(phaseMultiplier));
            }

            var flankHitMultiplier = flankHit ? FlankHitKnockbackMultiplier : 1f;
            return Mathf.Max(0f, impulse) * flankHitMultiplier * phaseMultiplier / targetMass;
        }
    }
}
