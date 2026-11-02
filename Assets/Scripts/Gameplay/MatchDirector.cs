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
        private readonly ScoreService scores = new();
        private bool filledBots;
        private TankController2D winner;

        public float Elapsed => Time.time - startedAt;
        public string MatchId { get; private set; }
        public MatchPhase Phase => phase;
        public int CollapseSteps => collapseSteps;
        public TankController2D Winner => winner;
        public ScoreService Scores => scores;
        public int RemainingPlayers => FindObjectsByType<TankController2D>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;

        private void Awake()
        {
            Instance = this;
            startedAt = Time.time;
            MatchId = System.Guid.NewGuid().ToString("N");
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

        private void Update()
        {
            phase = MatchRules.PhaseAt(Elapsed);
            collapseSteps = MatchRules.CollapseStepsAt(Elapsed);
            FillBotsWhenDue();
            ApplyArenaSize();
            ResolveWinner();
            if (phase == MatchPhase.Complete || winner != null) Time.timeScale = 0f;
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

        private void ApplyArenaSize()
        {
            var halfExtent = phase == MatchPhase.FinalZone ? 1.5f : 8f - collapseSteps;
            foreach (var tank in FindObjectsByType<TankController2D>(FindObjectsSortMode.None))
            {
                var point = tank.transform.position;
                var clamped = new Vector2(Mathf.Clamp(point.x, -halfExtent, halfExtent), Mathf.Clamp(point.y, -halfExtent + 2f, halfExtent - 2f));
                if ((Vector2)point != clamped) tank.transform.position = clamped;
            }
        }

        private void ResolveWinner()
        {
            var alive = FindObjectsByType<TankController2D>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (alive.Length == 1) winner = alive[0];
        }
    }
}
