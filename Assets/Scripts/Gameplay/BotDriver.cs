using UnityEngine;

namespace BumperTanks.Gameplay
{
    [RequireComponent(typeof(TankController2D))]
    public sealed class BotDriver : MonoBehaviour
    {
        private TankController2D tank;
        private float nextShot;

        private void Awake() => tank = GetComponent<TankController2D>();

        private void Update()
        {
            var player = FindFirstObjectByType<TankController2D>();
            if (player == null || player == tank || !player.IsPlayer) return;
            var towardPlayer = player.Position - tank.Position;
            tank.Drive(towardPlayer.normalized, towardPlayer);
            if (towardPlayer.sqrMagnitude < 100f && Time.time >= nextShot)
            {
                tank.Fire();
                nextShot = Time.time + Random.Range(0.5f, 1.2f);
            }
        }
    }
}
