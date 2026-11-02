using BumperTanks.Core;
using System.Collections.Generic;
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
        private float whirlwindEndsAt;
        private readonly HashSet<int> deflectedProjectiles = new();
        private readonly HashSet<int> knockedTanks = new();

        public bool ShieldActive => shieldActive;
        public bool Whirling => Time.time < whirlwindEndsAt;
        public float ReadyIn => Mathf.Max(0f, readyAt - Time.time);

        private void Awake()
        {
            tank = GetComponent<TankController2D>();
            body = GetComponent<Rigidbody2D>();
        }

        private void Update()
        {
            if (Time.timeScale > 0f && tank.IsPlayer && Input.GetKeyDown(KeyCode.E)) Activate();
            if (!Whirling) return;
            transform.Rotate(0f, 0f, 1440f * Time.deltaTime);
            ApplyWhirlwind();
        }

        public bool Activate()
        {
            if (Time.time < readyAt) return false;
            switch (tank.Archetype)
            {
                case TankArchetype.SpeedyBumper:
                    body.AddForce(tank.Aim * 14f, ForceMode2D.Impulse);
                    tank.BeginDash(0.4f);
                    readyAt = Time.time + 4f;
                    break;
                case TankArchetype.HeavyDozer:
                    StartCoroutine(ShieldRoutine());
                    readyAt = Time.time + 7f;
                    break;
                case TankArchetype.RicochetSpinner:
                    whirlwindEndsAt = Time.time + 2f;
                    deflectedProjectiles.Clear();
                    knockedTanks.Clear();
                    readyAt = Time.time + 5f;
                    ApplyWhirlwind();
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

        private void ApplyWhirlwind()
        {
            foreach (var hit in Physics2D.OverlapCircleAll(transform.position, 2.5f))
            {
                var projectile = hit.GetComponent<Projectile2D>();
                if (projectile != null && deflectedProjectiles.Add(projectile.GetInstanceID()))
                {
                    var direction = ((Vector2)hit.transform.position - (Vector2)transform.position).normalized;
                    projectile.Deflect(tank, direction, 1f);
                    continue;
                }

                var foe = hit.GetComponent<TankController2D>();
                if (foe == null || foe == tank || !knockedTanks.Add(foe.GetInstanceID())) continue;
                var away = (foe.Position - tank.Position).normalized;
                foe.ApplyCollisionKnockback(away, foe.GetComponent<Rigidbody2D>().mass * 6f);
            }
        }
    }
}
