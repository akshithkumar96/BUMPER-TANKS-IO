using BumperTanks.Core;
using BumperTanks.Match;
using UnityEngine;

namespace BumperTanks.Gameplay
{
    public sealed class MatchDirector : MonoBehaviour
    {
        public static MatchDirector Instance { get; private set; }
        private float startedAt;
        private MatchPhase phase;
        private int collapseSteps;
        private readonly BotFillService botFill = new();
        private readonly PowerUpRespawnService powerUpRespawns = new();
        private readonly ScoreService scores = new();
        private bool filledBots;
        private bool resultSubmitted;
        private bool networkAuthoritative;
        private float authoritativeElapsed;
        private TankController2D winner;

        public float Elapsed => networkAuthoritative ? authoritativeElapsed : Time.time - startedAt;
        public string MatchId { get; private set; }
        public MatchPhase Phase => phase;
        public int CollapseSteps => collapseSteps;
        public TankController2D Winner => winner;
        public ScoreService Scores => scores;
        public int RemainingPlayers => FindObjectsByType<TankController2D>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
        public ArenaBounds Bounds { get; private set; }

        private void Awake()
        {
            Instance = this;
            startedAt = Time.time;
            MatchId = System.Guid.NewGuid().ToString("N");
            Bounds = MatchRules.BoundsAt(0f);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void AwardGem(TankController2D tank)
        {
            Scores.AwardGem(tank.GetInstanceID(), MatchRules.GemMultiplier(Elapsed));
        }

        public int GemsFor(TankController2D tank) => Scores.GemsFor(tank.GetInstanceID());

        public void AwardElimination(TankController2D tank)
        {
            if (tank != null) Scores.AwardElimination(tank.GetInstanceID());
        }

        public void ApplyRealtimeState(float elapsedSeconds, Vector2 authoritativeArenaSize)
        {
            networkAuthoritative = true;
            authoritativeElapsed = Mathf.Max(0f, elapsedSeconds);
            phase = MatchRules.PhaseAt(authoritativeElapsed);
            collapseSteps = MatchRules.CollapseStepsAt(authoritativeElapsed);
            Bounds = authoritativeArenaSize.x > 0f && authoritativeArenaSize.y > 0f
                ? new ArenaBounds(Vector2.zero, authoritativeArenaSize)
                : MatchRules.BoundsAt(authoritativeElapsed);
            ApplyArenaSize();
        }

        public void SchedulePowerUpRespawn(PowerUpType type, float delaySeconds = 12f)
        {
            powerUpRespawns.Schedule(type, Elapsed + Mathf.Max(0f, delaySeconds));
        }

        private void Update()
        {
            phase = MatchRules.PhaseAt(Elapsed);
            collapseSteps = MatchRules.CollapseStepsAt(Elapsed);
            FillBotsWhenDue();
            RespawnPowerUpsWhenDue();
            ApplyArenaSize();
            ResolveWinner();
            if (phase == MatchPhase.Complete || winner != null)
            {
                SubmitResultOnce();
                Time.timeScale = 0f;
            }
        }

        private void FillBotsWhenDue()
        {
            if (filledBots || !botFill.FillDue(Elapsed)) return;
            var current = FindObjectsByType<TankController2D>(FindObjectsSortMode.None).Length;
            foreach (var slot in botFill.BotSlots(current))
            {
                var bot = ArenaBootstrap.CreateBot($"Fill Bot {slot + 1}", new Vector2(-6f + (slot % 4) * 4f, 4f + (slot / 4) * 1.2f));
                bot.gameObject.AddComponent<BotDriver>();
            }
            filledBots = true;
        }

        private void RespawnPowerUpsWhenDue()
        {
            while (powerUpRespawns.TryTakeDue(Elapsed, out var type))
            {
                var half = Bounds.Size * 0.5f;
                var position = new Vector2(Random.Range(-half.x, half.x), Random.Range(-half.y, half.y));
                ArenaBootstrap.CreatePowerUp(position, type, PowerUpColor(type));
            }
        }

        private static Color PowerUpColor(PowerUpType type) => type switch
        {
            PowerUpType.Nitro => new Color(1f, 0.9f, 0.1f),
            PowerUpType.MegaBullet => new Color(1f, 0.25f, 0.25f),
            _ => new Color(0.3f, 1f, 0.45f)
        };

        private void ApplyArenaSize()
        {
            if (!networkAuthoritative) Bounds = MatchRules.BoundsAt(Elapsed);
            foreach (var tank in FindObjectsByType<TankController2D>(FindObjectsSortMode.None))
            {
                var point = tank.transform.position;
                var clamped = Bounds.Clamp(point);
                if ((Vector2)point != clamped) tank.transform.position = clamped;
            }
            foreach (var gem in FindObjectsByType<GemPickup>(FindObjectsSortMode.None))
                if (!Bounds.Contains(gem.transform.position)) Destroy(gem.gameObject);
            foreach (var powerUp in FindObjectsByType<PowerUpPickup>(FindObjectsSortMode.None))
                if (!Bounds.Contains(powerUp.transform.position)) Destroy(powerUp.gameObject);
        }

        private void ResolveWinner()
        {
            var alive = FindObjectsByType<TankController2D>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (alive.Length == 1)
            {
                winner = alive[0];
                return;
            }
            if (phase != MatchPhase.Complete) return;

            var participants = FindObjectsByType<TankController2D>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var participant in participants)
            {
                if (winner == null || IsHigherStanding(participant, winner)) winner = participant;
            }
        }

        private bool IsHigherStanding(TankController2D candidate, TankController2D current)
        {
            var comparison = MatchRules.CompareStanding(
                candidate.gameObject.activeInHierarchy, Scores.GemsFor(candidate.GetInstanceID()), Scores.EliminationsFor(candidate.GetInstanceID()), candidate.Health,
                current.gameObject.activeInHierarchy, Scores.GemsFor(current.GetInstanceID()), Scores.EliminationsFor(current.GetInstanceID()), current.Health);
            return comparison > 0 || (comparison == 0 && candidate.GetInstanceID() < current.GetInstanceID());
        }

        private void SubmitResultOnce()
        {
            if (resultSubmitted) return;
            resultSubmitted = true;
            var player = FindObjectsByType<TankController2D>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var participant in player)
            {
                if (!participant.IsPlayer) continue;
                var playerId = BumperTanks.Frontend.ProfileStore.GetOrCreatePlayerId();
                BumperTanks.Frontend.GameServiceClient.Instance?.SubmitMatch(playerId, MatchId,
                    Scores.GemsFor(participant.GetInstanceID()), Scores.EliminationsFor(participant.GetInstanceID()));
                break;
            }
        }
    }
}
