using BumperTanks.Core;
using UnityEngine;

namespace BumperTanks.Network
{
    public static class TankPrediction
    {
        public static Vector2 PredictVelocity(Vector2 velocity, PlayerInputFrame input, float acceleration, float maxSpeed, float drag)
        {
            return TankMotion.Step(velocity, input.Movement, acceleration, maxSpeed, drag, MatchmakingPolicy.TickDuration);
        }

        public static Vector2 ReconcilePosition(Vector2 predicted, Vector2 authoritative, float correction)
        {
            return Vector2.Lerp(predicted, authoritative, Mathf.Clamp01(correction));
        }
    }
}
