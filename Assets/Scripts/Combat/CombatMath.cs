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

        public static bool IsFrontHit(Vector2 targetForward, Vector2 impactDirection)
        {
            if (targetForward.sqrMagnitude <= 0f || impactDirection.sqrMagnitude <= 0f)
            {
                return false;
            }

            return Vector2.Dot(targetForward.normalized, impactDirection.normalized) <= -0.5f;
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

        public static float CollisionImpulse(float relativeSpeed, float firstMass, float secondMass)
        {
            if (firstMass <= 0f) throw new System.ArgumentOutOfRangeException(nameof(firstMass));
            if (secondMass <= 0f) throw new System.ArgumentOutOfRangeException(nameof(secondMass));
            var reducedMass = firstMass * secondMass / (firstMass + secondMass);
            return Mathf.Max(0f, relativeSpeed) * reducedMass;
        }
    }
}
