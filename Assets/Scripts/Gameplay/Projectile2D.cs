using BumperTanks.Combat;
using UnityEngine;

namespace BumperTanks.Gameplay
{
    [RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
    public sealed class Projectile2D : MonoBehaviour
    {
        private float impulse;
        private float expiresAt;
        private Rigidbody2D body;
        private TankController2D owner;

        public void Launch(TankController2D firingTank, Vector2 direction, float projectileSpeed, float knockbackImpulse, float lifetime)
        {
            owner = firingTank;
            body = GetComponent<Rigidbody2D>();
            impulse = knockbackImpulse;
            expiresAt = Time.time + lifetime;
            body.linearVelocity = direction.normalized * projectileSpeed;
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
                if (tank.ApplyProjectileImpact(transform.position, body.linearVelocity.normalized, impulse))
                {
                    MatchDirector.Instance?.AwardElimination(owner);
                }
                Destroy(gameObject);
                return;
            }
            if (tank == null) Destroy(gameObject);
        }
    }
}
