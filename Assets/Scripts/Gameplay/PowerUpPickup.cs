using BumperTanks.Match;
using UnityEngine;

namespace BumperTanks.Gameplay
{
    [RequireComponent(typeof(CircleCollider2D))]
    public sealed class PowerUpPickup : MonoBehaviour
    {
        public PowerUpType Type { get; set; }
        public bool IsCollected { get; private set; }

        public void Configure(PowerUpType type, Color color)
        {
            Type = type;
            var sprite = gameObject.AddComponent<SpriteRenderer>();
            sprite.sprite = RuntimeSprite.White;
            sprite.color = color;
            transform.localScale = Vector3.one * 0.38f;
            GetComponent<CircleCollider2D>().isTrigger = true;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            var tank = other.GetComponent<TankController2D>();
            if (tank == null) return;
            if (!TryConsume()) return;
            tank.ApplyPowerUp(Type);
            MatchDirector.Instance?.SchedulePowerUpRespawn(Type);
            Destroy(gameObject);
        }

        /// <summary>
        /// Atomically claims this pickup for the current frame. Unity defers Destroy, so
        /// overlapping tank triggers must not enqueue more than one respawn.
        /// </summary>
        public bool TryConsume()
        {
            if (IsCollected) return false;
            IsCollected = true;
            return true;
        }
    }
}
