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

        public void Launch(Vector2 direction, float projectileSpeed, float knockbackImpulse, float lifetime)
        {
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
            if (tank != null)
            {
                tank.ApplyProjectileImpact(transform.position, body.linearVelocity.normalized, impulse);
            }
            Destroy(gameObject);
        }
    }
}
