using BumperTanks.Combat;
using BumperTanks.Core;
using BumperTanks.Match;
using UnityEngine;

namespace BumperTanks.Gameplay
{
    [RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
    public sealed class TankController2D : MonoBehaviour
    {
        [SerializeField] private TankArchetype archetype = TankArchetype.SpeedyBumper;
        [SerializeField] private bool playerControlled;
        [SerializeField] private float baseSpeed = 8f;
        [SerializeField] private float baseMass = 1f;
        [SerializeField] private float acceleration = 30f;
        [SerializeField] private float drag = 7f;
        [SerializeField] private float projectileSpeed = 16f;
        [SerializeField] private float projectileImpulse = 6f;
        [SerializeField] private float projectileCooldown = 0.35f;

        private Rigidbody2D body;
        private TankWeapon weapon;
        private TankTuning tuning;
        private Vector2 steering;
        private Vector2 aim = Vector2.up;
        private Vector2 touchOrigin;
        private int steeringFingerId = -1;
        private int aimFingerId = -1;
        private float health = 100f;
        private float nitroUntil;
        private float megaBulletUntil;
        private float magnetUntil;

        public bool IsPlayer => playerControlled;
        public Vector2 Position => body.position;
        public Vector2 Aim => aim;
        public float Health => health;
        public TankArchetype Archetype => archetype;
        public bool HasMagnet => Time.time < magnetUntil;

        public void Configure(TankArchetype value, bool isPlayer)
        {
            archetype = value;
            playerControlled = isPlayer;
            tuning = TankTuning.For(archetype);
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.linearDamping = 0f;
            tuning = TankTuning.For(archetype);
            weapon = new TankWeapon(projectileCooldown, projectileImpulse);
        }

        private void Update()
        {
            if (!playerControlled || Camera.main == null) return;
            if (Input.touchCount > 0)
            {
                ReadTouchInputs();
            }
            else
            {
                ReadDesktopInput();
            }
            if (aim.sqrMagnitude > 0f) transform.up = aim;
        }

        private void ReadDesktopInput()
        {
            steering = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            var mouse = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            aim = ((Vector2)mouse - body.position).normalized;
            if (Input.GetMouseButtonUp(0) || Input.GetKeyUp(KeyCode.Space)) Fire();
        }

        private void ReadTouchInputs()
        {
            var halfWidth = Screen.width * 0.5f;
            for (var index = 0; index < Input.touchCount; index++)
            {
                var touch = Input.GetTouch(index);
                if (touch.phase == TouchPhase.Began)
                {
                    if (touch.position.x < halfWidth && steeringFingerId < 0)
                    {
                        steeringFingerId = touch.fingerId;
                        touchOrigin = touch.position;
                    }
                    else if (touch.position.x >= halfWidth && aimFingerId < 0)
                    {
                        aimFingerId = touch.fingerId;
                    }
                }

                if (touch.fingerId == steeringFingerId)
                {
                    steering = Vector2.ClampMagnitude((touch.position - touchOrigin) / (Screen.width * 0.16f), 1f);
                    if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                    {
                        steering = Vector2.zero;
                        steeringFingerId = -1;
                    }
                    continue;
                }

                if (touch.fingerId != aimFingerId) continue;
                var target = Camera.main.ScreenToWorldPoint(touch.position);
                aim = ((Vector2)target - body.position).normalized;
                if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                {
                    if (touch.phase == TouchPhase.Ended) Fire();
                    aimFingerId = -1;
                }
            }
        }

        private void FixedUpdate()
        {
            var speedMultiplier = Time.time < nitroUntil ? 1.5f : 1f;
            body.linearVelocity = TankMotion.Step(body.linearVelocity, steering, acceleration, tuning.MaxSpeed(baseSpeed) * speedMultiplier, drag, Time.fixedDeltaTime);
            if (body.linearVelocity.sqrMagnitude > 0.01f && !playerControlled)
            {
                transform.up = body.linearVelocity.normalized;
            }
        }

        public void Drive(Vector2 direction, Vector2 facing)
        {
            if (playerControlled) return;
            steering = direction;
            if (facing.sqrMagnitude > 0.01f)
            {
                aim = facing.normalized;
            }
        }

        public bool Fire()
        {
            var velocity = body.linearVelocity;
            var impulseMultiplier = Time.time < megaBulletUntil ? 1.8f : 1f;
            if (!weapon.TryFire(Time.time, body.position, aim, tuning.Mass(baseMass), ref velocity, out var shot, impulseMultiplier)) return false;
            body.linearVelocity = velocity;
            var projectile = new GameObject("Projectile");
            projectile.transform.position = shot.Origin + (shot.Direction * 0.7f);
            projectile.transform.localScale = Vector3.one * 0.22f;
            projectile.AddComponent<SpriteRenderer>().sprite = RuntimeSprite.White;
            projectile.GetComponent<SpriteRenderer>().color = playerControlled ? new Color(0.2f, 0.9f, 1f) : new Color(1f, 0.4f, 0.2f);
            var collider = projectile.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            var projectileBody = projectile.AddComponent<Rigidbody2D>();
            projectileBody.gravityScale = 0f;
            projectileBody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            projectile.AddComponent<Projectile2D>().Launch(this, shot.Direction, projectileSpeed, shot.Impulse, 2.5f);
            return true;
        }

        public bool ApplyProjectileImpact(Vector2 impactPoint, Vector2 impactDirection, float impulse)
        {
            var ability = GetComponent<TankAbilityController>();
            if (ability != null && ability.ShieldActive)
            {
                body.AddForce(impactDirection * (impulse * 0.25f), ForceMode2D.Impulse);
                return false;
            }
            var rearHit = CombatMath.IsRearHit(aim, impactDirection);
            var knockback = CombatMath.Knockback(impulse, tuning.Mass(baseMass), rearHit);
            body.AddForce(impactDirection * knockback, ForceMode2D.Impulse);
            health = Mathf.Max(0f, health - (rearHit ? 25f : 15f));
            var eliminated = health <= 0f;
            if (eliminated) gameObject.SetActive(false);
            return eliminated;
        }

        public void ApplyPowerUp(PowerUpType powerUp)
        {
            switch (powerUp)
            {
                case PowerUpType.Nitro: nitroUntil = Time.time + 5f; break;
                case PowerUpType.MegaBullet: megaBulletUntil = Time.time + 5f; break;
                case PowerUpType.Magnet: magnetUntil = Time.time + 5f; break;
            }
        }
    }
}
