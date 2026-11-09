using UnityEngine;

namespace BumperTanks.Gameplay
{
    [RequireComponent(typeof(TankController2D))]
    public sealed class BotDriver : MonoBehaviour
    {
        private const float AbilityUseRange = 6f;
        private TankController2D tank;
        private float nextShot;

        private void Awake() => tank = GetComponent<TankController2D>();

        private void Update()
        {
            TankController2D player = null;
            foreach (var candidate in FindObjectsByType<TankController2D>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (candidate.IsPlayer)
                {
                    player = candidate;
                    break;
                }
            }
            if (player == null || player == tank) return;
            var towardPlayer = player.Position - tank.Position;
            var movementTarget = FindNearestCollectible(tank.Position, player.Position);
            tank.Drive((movementTarget - tank.Position).normalized, towardPlayer);
            TryUseAbility(towardPlayer.magnitude);
            if (towardPlayer.sqrMagnitude < 100f && Time.time >= nextShot)
            {
                tank.Fire();
                nextShot = Time.time + Random.Range(0.5f, 1.2f);
            }
        }

        public bool TryUseAbility(float targetDistance)
        {
            if (targetDistance < 0f || targetDistance > AbilityUseRange) return false;
            var ability = GetComponent<TankAbilityController>();
            return ability != null && ability.ReadyIn <= 0f && ability.Activate();
        }

        private static Vector2 FindNearestCollectible(Vector2 origin, Vector2 fallback)
        {
            var target = fallback;
            var bestDistance = 8f;
            foreach (var gem in FindObjectsByType<GemPickup>(FindObjectsSortMode.None))
            {
                var distance = Vector2.Distance(origin, gem.transform.position);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                target = gem.transform.position;
            }
            foreach (var powerUp in FindObjectsByType<PowerUpPickup>(FindObjectsSortMode.None))
            {
                var distance = Vector2.Distance(origin, powerUp.transform.position);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                target = powerUp.transform.position;
            }
            return target;
        }
    }
}
