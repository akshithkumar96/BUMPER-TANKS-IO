using UnityEngine;

namespace BumperTanks.Core
{
    public static class TankMotion
    {
        public static Vector2 Step(
            Vector2 velocity,
            Vector2 steering,
            float acceleration,
            float maxSpeed,
            float drag,
            float fixedDeltaTime)
        {
            if (acceleration < 0f || maxSpeed < 0f || drag < 0f || fixedDeltaTime < 0f)
            {
                throw new System.ArgumentOutOfRangeException("Motion tuning values must not be negative.");
            }

            var input = Vector2.ClampMagnitude(steering, 1f);
            var accelerated = velocity + (input * acceleration * fixedDeltaTime);
            var damped = Vector2.MoveTowards(accelerated, Vector2.zero, drag * fixedDeltaTime);
            return Vector2.ClampMagnitude(damped, maxSpeed);
        }

        public static Vector2 Recoil(Vector2 velocity, Vector2 shotDirection, float impulse, float mass)
        {
            if (mass <= 0f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(mass));
            }

            return velocity - (shotDirection.normalized * (impulse / mass));
        }
    }
}
