using BumperTanks.Core;
using UnityEngine;

namespace BumperTanks.Combat
{
    public sealed class TankWeapon
    {
        private float nextReadyTime;

        public TankWeapon(float cooldownSeconds, float recoilImpulse)
        {
            if (cooldownSeconds < 0f || recoilImpulse < 0f)
            {
                throw new System.ArgumentOutOfRangeException("Weapon tuning must not be negative.");
            }

            CooldownSeconds = cooldownSeconds;
            RecoilImpulse = recoilImpulse;
        }

        public float CooldownSeconds { get; }
        public float RecoilImpulse { get; }

        public bool TryFire(float time, Vector2 position, Vector2 direction, float mass, ref Vector2 velocity, out ShotRequest shot, float impulseMultiplier = 1f)
        {
            shot = default;
            if (time < nextReadyTime || direction.sqrMagnitude <= 0f)
            {
                return false;
            }

            var impulse = RecoilImpulse * Mathf.Max(0f, impulseMultiplier);
            shot = new ShotRequest(position, direction, impulse);
            velocity = TankMotion.Recoil(velocity, shot.Direction, impulse, mass);
            nextReadyTime = time + CooldownSeconds;
            return true;
        }
    }
}
