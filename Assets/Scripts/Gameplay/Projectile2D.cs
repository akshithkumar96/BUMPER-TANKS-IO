using BumperTanks.Combat;
using System.Collections.Generic;
using UnityEngine;

namespace BumperTanks.Gameplay
{
    public enum ProjectileHitResult
    {
        Consumed,
        Deflected,
        EliminatedTarget
    }

    [RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
    public sealed class Projectile2D : MonoBehaviour
    {
        private float impulse;
        private float expiresAt;
        private Rigidbody2D body;
        private TankController2D owner;
        private int remainingBounces;
        private readonly HashSet<int> hitTanks = new();

        public void Launch(TankController2D firingTank, Vector2 direction, float projectileSpeed, float knockbackImpulse, float lifetime, int bounceCount = 0)
        {
            owner = firingTank;
            body = GetComponent<Rigidbody2D>();
            impulse = knockbackImpulse;
            remainingBounces = Mathf.Max(0, bounceCount);
            expiresAt = Time.time + lifetime;
            body.linearVelocity = direction.normalized * projectileSpeed;
        }

        public void Deflect(TankController2D newOwner, Vector2 direction, float impulseMultiplier)
        {
            if (direction.sqrMagnitude <= 0f) return;
            owner = newOwner;
            impulse *= Mathf.Max(0f, impulseMultiplier);
            var speed = body.linearVelocity.magnitude;
            var normalizedDirection = direction.normalized;
            transform.position = newOwner.Position + normalizedDirection * 0.7f;
            body.linearVelocity = normalizedDirection * speed;
        }

        private void Update()
        {
            if (Time.time >= expiresAt) Destroy(gameObject);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            var tank = other.GetComponent<TankController2D>();
            if (tank != null && tank != owner)
            {
                if (hitTanks.Add(tank.GetInstanceID()))
                {
                    var result = tank.ApplyProjectileImpact(this, owner, body.linearVelocity.normalized, impulse);
                    if (result == ProjectileHitResult.Deflected) return;
                    if (result == ProjectileHitResult.EliminatedTarget)
                        MatchDirector.Instance?.AwardElimination(owner);
                }
                if (!TryBounce(other)) Destroy(gameObject);
                return;
            }
            if (tank == null && other.isTrigger) return;
            if (tank == null && !TryBounce(other)) Destroy(gameObject);
        }

        private bool TryBounce(Collider2D obstacle)
        {
            if (remainingBounces <= 0) return false;
            var position = (Vector2)transform.position;
            var normal = (position - obstacle.ClosestPoint(position)).normalized;
            if (normal.sqrMagnitude <= 0f)
            {
                var bounds = obstacle.bounds;
                var faceDistances = new[]
                {
                    Mathf.Abs(position.x - bounds.min.x), Mathf.Abs(bounds.max.x - position.x),
                    Mathf.Abs(position.y - bounds.min.y), Mathf.Abs(bounds.max.y - position.y)
                };
                var nearestFace = 0;
                for (var index = 1; index < faceDistances.Length; index++)
                    if (faceDistances[index] < faceDistances[nearestFace]) nearestFace = index;
                normal = nearestFace switch
                {
                    0 => Vector2.left,
                    1 => Vector2.right,
                    2 => Vector2.down,
                    _ => Vector2.up
                };
            }
            if (normal.sqrMagnitude <= 0f) normal = -body.linearVelocity.normalized;

            var speed = body.linearVelocity.magnitude;
            var direction = Vector2.Reflect(body.linearVelocity.normalized, normal).normalized;
            if (direction.sqrMagnitude <= 0f) direction = -body.linearVelocity.normalized;
            remainingBounces--;
            transform.position = position + normal * 0.08f;
            body.linearVelocity = direction * speed;
            return true;
        }
    }
}
