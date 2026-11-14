using BumperTanks.Core;
using UnityEngine;

namespace BumperTanks.Match
{
    public static class MatchRules
    {
        public const float OpenArenaEnd = 30f;
        public const float CollapseEnd = 60f;
        public const float FinalZoneEnd = 90f;
        public const float CollapseInterval = 5f;

        public static MatchPhase PhaseAt(float elapsedSeconds)
        {
            if (elapsedSeconds < OpenArenaEnd) return MatchPhase.OpenArena;
            if (elapsedSeconds < CollapseEnd) return MatchPhase.Collapse;
            if (elapsedSeconds < FinalZoneEnd) return MatchPhase.FinalZone;
            return MatchPhase.Complete;
        }

        public static int CollapseStepsAt(float elapsedSeconds)
        {
            if (elapsedSeconds < OpenArenaEnd) return 0;
            return Mathf.Clamp(Mathf.FloorToInt((elapsedSeconds - OpenArenaEnd) / CollapseInterval) + 1, 0, 6);
        }

        public static ArenaBounds BoundsAt(float elapsedSeconds)
        {
            var steps = CollapseStepsAt(elapsedSeconds);
            var progress = steps / 6f;
            var width = Mathf.Lerp(16f, 3f, progress);
            var height = Mathf.Lerp(12f, 3f, progress);
            return new ArenaBounds(Vector2.zero, new Vector2(width, height));
        }

        public static float KnockbackMultiplier(float elapsedSeconds) => PhaseAt(elapsedSeconds) == MatchPhase.FinalZone ? 1.5f : 1f;
        public static int GemMultiplier(float elapsedSeconds) => PhaseAt(elapsedSeconds) == MatchPhase.FinalZone ? 2 : 1;
        public static float BoundaryDamagePerSecond(float elapsedSeconds) => PhaseAt(elapsedSeconds) switch
        {
            MatchPhase.Collapse => 0.75f,
            MatchPhase.FinalZone => 1.5f,
            _ => 0f
        };

        public static int CompareStanding(bool firstAlive, int firstGems, int firstEliminations, float firstHealth,
            bool secondAlive, int secondGems, int secondEliminations, float secondHealth)
        {
            var alive = firstAlive.CompareTo(secondAlive);
            if (alive != 0) return alive;
            var gems = firstGems.CompareTo(secondGems);
            if (gems != 0) return gems;
            var eliminations = firstEliminations.CompareTo(secondEliminations);
            if (eliminations != 0) return eliminations;
            return firstHealth.CompareTo(secondHealth);
        }
    }
}
