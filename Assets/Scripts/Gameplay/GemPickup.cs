using UnityEngine;

namespace BumperTanks.Gameplay
{
    [RequireComponent(typeof(CircleCollider2D))]
    public sealed class GemPickup : MonoBehaviour
    {
        private void Awake()
        {
            var sprite = gameObject.AddComponent<SpriteRenderer>();
            sprite.sprite = RuntimeSprite.White;
            sprite.color = new Color(0.2f, 1f, 0.85f);
            transform.localScale = Vector3.one * 0.18f;
            GetComponent<CircleCollider2D>().isTrigger = true;
        }

        private void Update()
        {
            foreach (var tank in FindObjectsByType<TankController2D>(FindObjectsSortMode.None))
            {
                if (!tank.HasMagnet || Vector2.Distance(transform.position, tank.Position) > 4f) continue;
                transform.position = Vector2.MoveTowards(transform.position, tank.Position, 7f * Time.deltaTime);
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            var tank = other.GetComponent<TankController2D>();
            if (tank == null) return;
            MatchDirector.Instance?.AwardGem(tank);
            Destroy(gameObject);
        }
    }
}
