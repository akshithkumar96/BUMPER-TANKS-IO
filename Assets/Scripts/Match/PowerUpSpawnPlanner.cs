using System.Collections.Generic;
using BumperTanks.Core;
using UnityEngine;

namespace BumperTanks.Match
{
    /// <summary>Chooses seeded, arena-bounded pickup locations away from tanks and other drops.</summary>
    public sealed class PowerUpSpawnPlanner
    {
        private readonly System.Random random;

        public PowerUpSpawnPlanner(int seed) => random = new System.Random(seed);

        public Vector2 Choose(ArenaBounds bounds, IReadOnlyList<Vector2> avoidPositions,
            IReadOnlyList<Vector2> occupiedPositions, float desiredClearance = 1.8f)
        {
            const float edgeInset = 0.5f;
            var min = bounds.Min + Vector2.one * edgeInset;
            var max = bounds.Max - Vector2.one * edgeInset;
            min = Vector2.Min(min, bounds.Center);
            max = Vector2.Max(max, bounds.Center);
            var best = bounds.Center;
            var bestClearance = float.NegativeInfinity;

            for (var attempt = 0; attempt < 96; attempt++)
            {
                var candidate = new Vector2(
                    Mathf.Lerp(min.x, max.x, (float)random.NextDouble()),
                    Mathf.Lerp(min.y, max.y, (float)random.NextDouble()));
                var clearance = float.PositiveInfinity;
                if (avoidPositions != null)
                    foreach (var position in avoidPositions)
                        clearance = Mathf.Min(clearance, Vector2.Distance(candidate, position));
                if (occupiedPositions != null)
                    foreach (var position in occupiedPositions)
                        clearance = Mathf.Min(clearance, Vector2.Distance(candidate, position));

                if (clearance > bestClearance)
                {
                    best = candidate;
                    bestClearance = clearance;
                }
                if (clearance >= desiredClearance) return candidate;
            }

            // In the final 3x3 zone, a point may not meet the desired clearance.
            // Preserve the pickup and choose the best sampled in-bounds alternative.
            return best;
        }
    }
}
