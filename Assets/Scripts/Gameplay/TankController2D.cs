using BumperTanks.Combat;
using BumperTanks.Core;
using BumperTanks.Match;
using BumperTanks.Network;
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
        private LineRenderer aimTrajectory;
        private Vector2 steering;
        private Vector2 aim = Vector2.up;
        private Vector2 touchOrigin;
        private int steeringFingerId = -1;
        private int aimFingerId = -1;
        private float health = 100f;
        private float nitroUntil;
        private float megaBulletUntil;
        private float magnetUntil;
        private float dashUntil;
        private bool pendingNetworkFire;
        private bool pendingNetworkAbility;
        private bool realtimeNitroActive;
        private bool realtimeMegaBulletActive;
        private bool realtimeMagnetActive;
        private float accessibleSteeringUntil;
        private Vector2 accessibleSteering;

        public bool IsPlayer => playerControlled;
        public Vector2 Position => body.position;
        public Vector2 Aim => aim;
        public Vector2 Steering => steering;
        public float Health => health;
        public TankArchetype Archetype => archetype;
        public bool HasMagnet => realtimeMagnetActive || Time.time < magnetUntil;
        public bool NitroActive => realtimeNitroActive || Time.time < nitroUntil;
        public bool MegaBulletActive => realtimeMegaBulletActive || Time.time < megaBulletUntil;
        public bool RealtimeMagnetActive => realtimeMagnetActive;
        public bool IsDashing => Time.time < dashUntil;
        public float BumpKnockbackMultiplier => IsDashing ? 2.5f : 1f;

        public void Configure(TankArchetype value, bool isPlayer)
        {
            archetype = value;
            playerControlled = isPlayer;
            tuning = TankTuning.For(archetype);
            if (body != null) body.mass = tuning.Mass(baseMass);
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.linearDamping = 0f;
            tuning = TankTuning.For(archetype);
            body.mass = tuning.Mass(baseMass);
            weapon = new TankWeapon(projectileCooldown * tuning.ProjectileCooldownMultiplier * GameplayTuning.ProjectileCooldownMultiplier,
                projectileImpulse * tuning.ShotKnockbackMultiplier * GameplayTuning.KnockbackMultiplier);
            var trajectory = new GameObject("Aim Trajectory");
            trajectory.transform.SetParent(transform, false);
            aimTrajectory = trajectory.AddComponent<LineRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            if (shader != null) aimTrajectory.material = new Material(shader);
            aimTrajectory.positionCount = 2;
            aimTrajectory.startWidth = 0.035f;
            aimTrajectory.endWidth = 0.012f;
            aimTrajectory.startColor = new Color(0.35f, 0.95f, 1f, 0.9f);
            aimTrajectory.endColor = new Color(0.35f, 0.95f, 1f, 0.05f);
            aimTrajectory.sortingOrder = 2;
            aimTrajectory.enabled = false;
        }

        private void Update()
        {
            if (!playerControlled || Camera.main == null) return;
            if (Time.timeScale <= 0f)
            {
                steering = Vector2.zero;
                if (aimTrajectory != null) aimTrajectory.enabled = false;
                return;
            }
            if (AccessibleUiBridge.AssistiveMatchControlsVisible)
            {
                steering = Time.unscaledTime < accessibleSteeringUntil ? accessibleSteering : Vector2.zero;
            }
            else if (Time.unscaledTime < accessibleSteeringUntil)
            {
                steering = accessibleSteering;
            }
            else if (Input.touchCount > 0)
            {
                ReadTouchInputs();
            }
            else
            {
                ReadDesktopInput();
            }
            var ability = GetComponent<TankAbilityController>();
            if (ability == null || !ability.Whirling)
                if (aim.sqrMagnitude > 0f) transform.up = aim;
            UpdateAimTrajectory(Input.touchCount > 0 ? aimFingerId >= 0 : Input.GetMouseButton(0));
        }

        private void UpdateAimTrajectory(bool aiming)
        {
            if (aimTrajectory == null) return;
            aimTrajectory.enabled = aiming && aim.sqrMagnitude > 0f;
            if (!aimTrajectory.enabled) return;
            var origin = body.position + aim * 0.65f;
            aimTrajectory.SetPosition(0, origin);
            aimTrajectory.SetPosition(1, origin + aim * 3.5f);
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
            var speedMultiplier = NitroActive ? 1.5f : 1f;
            body.linearVelocity = TankMotion.Step(body.linearVelocity, steering, acceleration,
                tuning.MaxSpeed(baseSpeed) * speedMultiplier * GameplayTuning.SpeedMultiplier, drag, Time.fixedDeltaTime);
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
            var impulseMultiplier = MegaBulletActive ? 1.8f : 1f;
            if (!weapon.TryFire(Time.time, body.position, aim, tuning.Mass(baseMass), ref velocity, out var shot, impulseMultiplier)) return false;
            if (playerControlled) pendingNetworkFire = true;
            body.linearVelocity = velocity;
            var projectile = new GameObject("Projectile");
            projectile.transform.position = shot.Origin + (shot.Direction * 0.7f);
            projectile.transform.localScale = Vector3.one * (0.22f * tuning.ProjectileSizeMultiplier);
            projectile.AddComponent<SpriteRenderer>().sprite = RuntimeSprite.White;
            projectile.GetComponent<SpriteRenderer>().color = playerControlled ? new Color(0.2f, 0.9f, 1f) : new Color(1f, 0.4f, 0.2f);
            var collider = projectile.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            var projectileBody = projectile.AddComponent<Rigidbody2D>();
            projectileBody.gravityScale = 0f;
            projectileBody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            projectile.AddComponent<Projectile2D>().Launch(this, shot.Direction,
                projectileSpeed * tuning.ProjectileSpeedMultiplier, shot.Impulse, 2.5f, tuning.ProjectileBounces);
            return true;
        }

        public PlayerInputFrame ConsumeNetworkInput(int tick, bool allowGameplayInput = true)
        {
            var input = new PlayerInputFrame(tick, allowGameplayInput ? steering : Vector2.zero, aim,
                allowGameplayInput && pendingNetworkFire, allowGameplayInput && pendingNetworkAbility);
            pendingNetworkFire = false;
            pendingNetworkAbility = false;
            return input;
        }

        public void MarkNetworkAbilityActivated()
        {
            if (playerControlled) pendingNetworkAbility = true;
        }

        /// <summary>Moves in a short, predictable pulse for assistive controls.</summary>
        public void SetAccessibleSteering(Vector2 direction, float durationSeconds = 0.45f)
        {
            if (!playerControlled) return;
            accessibleSteering = Vector2.ClampMagnitude(direction, 1f);
            steering = accessibleSteering;
            accessibleSteeringUntil = Time.unscaledTime + Mathf.Clamp(durationSeconds, 0.1f, 1.5f);
        }

        /// <summary>Aims in a cardinal direction and fires for one-shot accessible controls.</summary>
        public void AccessibleAimAndFire(Vector2 direction)
        {
            if (!playerControlled || direction.sqrMagnitude < 0.01f) return;
            aim = direction.normalized;
            Fire();
        }

        public void SyncRealtimePowerUpState(bool nitroActive, bool megaBulletActive, bool magnetActive)
        {
            realtimeNitroActive = nitroActive;
            realtimeMegaBulletActive = megaBulletActive;
            realtimeMagnetActive = magnetActive;
        }

        public void ReconcileNetworkState(TankSnapshot snapshot, float correction)
        {
            if (body == null) return;
            body.position = TankPrediction.ReconcilePosition(body.position, snapshot.Position, correction);
            body.linearVelocity = snapshot.Velocity;
            health = snapshot.Alive ? Mathf.Clamp(snapshot.Health, 0f, 100f) : 0f;
            if (health <= 0f) gameObject.SetActive(false);
            else if (!gameObject.activeSelf) gameObject.SetActive(true);
        }

        public ProjectileHitResult ApplyProjectileImpact(Projectile2D projectile, TankController2D attacker, Vector2 impactDirection, float impulse)
        {
            var ability = GetComponent<TankAbilityController>();
            if (ability != null && ability.ShieldActive && CombatMath.IsFrontHit(aim, impactDirection))
            {
                attacker?.ApplyCollisionKnockback(-impactDirection, impulse * 0.5f);
                projectile.Deflect(this, -impactDirection, 0.5f);
                return ProjectileHitResult.Deflected;
            }
            if (ability != null && ability.Whirling)
            {
                var away = ((Vector2)projectile.transform.position - body.position).normalized;
                projectile.Deflect(this, away, 1f);
                return ProjectileHitResult.Deflected;
            }

            var flankHit = CombatMath.IsFlankHit(aim, impactDirection);
            var elapsed = MatchDirector.Instance != null ? MatchDirector.Instance.Elapsed : 0f;
            var knockback = CombatMath.Knockback(impulse, tuning.Mass(baseMass), flankHit, MatchRules.KnockbackMultiplier(elapsed));
            body.AddForce(impactDirection * knockback * GameplayTuning.KnockbackMultiplier, ForceMode2D.Impulse);
            health = Mathf.Max(0f, health - (flankHit ? 25f : 15f));
            var eliminated = health <= 0f;
            if (eliminated) gameObject.SetActive(false);
            return eliminated ? ProjectileHitResult.EliminatedTarget : ProjectileHitResult.Consumed;
        }

        public void BeginDash(float durationSeconds) => dashUntil = Time.time + Mathf.Max(0f, durationSeconds);

        public void ApplyCollisionKnockback(Vector2 direction, float impulse)
        {
            if (direction.sqrMagnitude <= 0f || impulse <= 0f) return;
            body.AddForce(direction.normalized * impulse * GameplayTuning.KnockbackMultiplier, ForceMode2D.Impulse);
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            var other = collision.collider.GetComponent<TankController2D>();
            if (other == null || other == this || GetInstanceID() > other.GetInstanceID()) return;

            GetComponent<TankCosmeticView>()?.PlayBump(collision.relativeVelocity.magnitude);
            other.GetComponent<TankCosmeticView>()?.PlayBump(collision.relativeVelocity.magnitude);

            var source = this;
            var target = other;
            if (other.BumpKnockbackMultiplier > BumpKnockbackMultiplier ||
                (other.BumpKnockbackMultiplier == BumpKnockbackMultiplier && other.body.linearVelocity.sqrMagnitude > body.linearVelocity.sqrMagnitude))
            {
                source = other;
                target = this;
            }

            var direction = (target.Position - source.Position).normalized;
            var flankHit = CombatMath.IsFlankHit(target.Aim, direction);
            var elapsed = MatchDirector.Instance != null ? MatchDirector.Instance.Elapsed : 0f;
            var multiplier = Mathf.Max(source.BumpKnockbackMultiplier, target.BumpKnockbackMultiplier) *
                             (flankHit ? CombatMath.FlankHitKnockbackMultiplier : 1f) *
                             MatchRules.KnockbackMultiplier(elapsed);
            var baseImpulse = CombatMath.CollisionImpulse(collision.relativeVelocity.magnitude, source.body.mass, target.body.mass);
            var bonusImpulse = baseImpulse * (multiplier - 1f);
            target.ApplyCollisionKnockback(direction, bonusImpulse);
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
