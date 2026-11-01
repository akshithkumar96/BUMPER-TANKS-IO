using BumperTanks.Combat;
using NUnit.Framework;
using UnityEngine;

namespace BumperTanks.Tests
{
    public class CombatTests
    {
        [Test]
        public void RearHitAppliesGddKnockbackBonus()
        {
            var normal = CombatMath.Knockback(6f, 2f, false);
            var rear = CombatMath.Knockback(6f, 2f, true);

            Assert.That(rear, Is.EqualTo(normal * 1.5f));
            Assert.That(CombatMath.IsRearHit(Vector2.up, Vector2.down), Is.True);
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
