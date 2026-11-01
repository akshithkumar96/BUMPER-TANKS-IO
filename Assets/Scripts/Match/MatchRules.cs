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

        public static float KnockbackMultiplier(float elapsedSeconds) => PhaseAt(elapsedSeconds) == MatchPhase.FinalZone ? 1.5f : 1f;
        public static int GemMultiplier(float elapsedSeconds) => PhaseAt(elapsedSeconds) == MatchPhase.FinalZone ? 2 : 1;
    }
}
