using BumperTanks.Core;
using UnityEngine;

namespace BumperTanks.Gameplay
{
    [RequireComponent(typeof(TankController2D), typeof(Rigidbody2D))]
    public sealed class TankAbilityController : MonoBehaviour
    {
        private TankController2D tank;
        private Rigidbody2D body;
        private float readyAt;
        private bool shieldActive;

        public bool ShieldActive => shieldActive;

        private void Awake()
        {
            tank = GetComponent<TankController2D>();
            body = GetComponent<Rigidbody2D>();
        }

        private void Update()
        {
            if (tank.IsPlayer && Input.GetKeyDown(KeyCode.E)) Activate();
        }

        public bool Activate()
        {
            if (Time.time < readyAt) return false;
            switch (tank.Archetype)
            {
                case TankArchetype.SpeedyBumper:
                    body.AddForce(tank.Aim * 14f, ForceMode2D.Impulse);
                    readyAt = Time.time + 4f;
                    break;
                case TankArchetype.HeavyDozer:
                    StartCoroutine(ShieldRoutine());
                    readyAt = Time.time + 7f;
                    break;
                case TankArchetype.RicochetSpinner:
                    DeflectProjectiles();
                    readyAt = Time.time + 5f;
                    break;
                default:
                    return false;
            }
            return true;
        }

        private System.Collections.IEnumerator ShieldRoutine()
        {
            shieldActive = true;
            yield return new WaitForSeconds(3f);
            shieldActive = false;
        }

        private void DeflectProjectiles()
        {
            foreach (var hit in Physics2D.OverlapCircleAll(transform.position, 2.5f))
            {
                var projectile = hit.GetComponent<Projectile2D>();
                var projectileBody = hit.attachedRigidbody;
                if (projectile != null && projectileBody != null)
                {
                    projectileBody.linearVelocity = (hit.transform.position - transform.position).normalized * projectileBody.linearVelocity.magnitude;
                }
            }
        }
    }
}
