using BumperTanks.Match;
using UnityEngine;

namespace BumperTanks.Gameplay
{
    [RequireComponent(typeof(CircleCollider2D))]
    public sealed class PowerUpPickup : MonoBehaviour
    {
        public PowerUpType Type { get; set; }

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
            tank.ApplyPowerUp(Type);
            MatchDirector.Instance?.SchedulePowerUpRespawn(Type);
            Destroy(gameObject);
        }
    }
}
