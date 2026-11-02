using BumperTanks.Combat;
using NUnit.Framework;
using UnityEngine;

namespace BumperTanks.Tests
{
    public class CombatTests
    {
        [Test]
        public void FlankHitAppliesGddKnockbackBonus()
        {
            var normal = CombatMath.Knockback(6f, 2f, false);
            var flank = CombatMath.Knockback(6f, 2f, true);

            Assert.That(flank, Is.EqualTo(normal * 1.5f));
            Assert.That(CombatMath.IsFlankHit(Vector2.up, Vector2.right), Is.True);
            Assert.That(CombatMath.IsFlankHit(Vector2.up, Vector2.left), Is.True);
            Assert.That(CombatMath.IsFlankHit(Vector2.up, Vector2.up), Is.False);
            Assert.That(CombatMath.IsFlankHit(Vector2.up, Vector2.down), Is.False);
            Assert.That(CombatMath.IsFrontHit(Vector2.up, Vector2.down), Is.True);
            Assert.That(CombatMath.IsFrontHit(Vector2.up, Vector2.up), Is.False);
        }

        [Test]
        public void SuddenDeathAddsFiftyPercentKnockbackAfterFlankBonus()
        {
            var opening = CombatMath.Knockback(6f, 2f, true);
            var suddenDeath = CombatMath.Knockback(6f, 2f, true, 1.5f);

            Assert.That(suddenDeath, Is.EqualTo(opening * 1.5f));
        }

        [Test]
        public void BumpImpulseUsesRelativeSpeedAndBothTankMasses()
        {
            var speedyAgainstHeavy = CombatMath.CollisionImpulse(10f, 0.7f, 1.5f);
            var heavyAgainstSpeedy = CombatMath.CollisionImpulse(10f, 1.5f, 0.7f);

            Assert.That(speedyAgainstHeavy, Is.EqualTo(heavyAgainstSpeedy));
            Assert.That(speedyAgainstHeavy, Is.EqualTo(10f * 0.7f * 1.5f / 2.2f).Within(0.0001f));
            Assert.That(CombatMath.CollisionImpulse(0f, 0.7f, 1.5f), Is.Zero);
        }

        [Test]
        public void WeaponAppliesRecoilAndRespectsCooldown()
        {
            var weapon = new TankWeapon(0.5f, 4f);
            var velocity = Vector2.zero;

            Assert.That(weapon.TryFire(1f, Vector2.zero, Vector2.right, 2f, ref velocity, out var shot), Is.True);
            Assert.That(shot.Direction, Is.EqualTo(Vector2.right));
            Assert.That(velocity, Is.EqualTo(new Vector2(-2f, 0f)));
            Assert.That(weapon.TryFire(1.49f, Vector2.zero, Vector2.right, 2f, ref velocity, out _), Is.False);
            Assert.That(weapon.TryFire(1.5f, Vector2.zero, Vector2.right, 2f, ref velocity, out _), Is.True);
        }
    }
}
