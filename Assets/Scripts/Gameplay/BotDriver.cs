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
            tank.Drive(towardPlayer.normalized, towardPlayer);
            if (towardPlayer.sqrMagnitude < 100f && Time.time >= nextShot)
            {
                tank.Fire();
                nextShot = Time.time + Random.Range(0.5f, 1.2f);
            }
        }
    }
}
