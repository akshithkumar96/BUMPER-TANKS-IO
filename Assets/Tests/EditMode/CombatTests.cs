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
        }

        [Test]
        public void SuddenDeathAddsFiftyPercentKnockbackAfterFlankBonus()
        {
            var opening = CombatMath.Knockback(6f, 2f, true);
            var suddenDeath = CombatMath.Knockback(6f, 2f, true, 1.5f);

            Assert.That(suddenDeath, Is.EqualTo(opening * 1.5f));
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
