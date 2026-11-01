using UnityEngine;

namespace BumperTanks.Combat
{
    public readonly struct ShotRequest
    {
        public ShotRequest(Vector2 origin, Vector2 direction, float impulse)
        {
            Origin = origin;
            Direction = direction.sqrMagnitude > 0f ? direction.normalized : Vector2.right;
            Impulse = Mathf.Max(0f, impulse);
        }

        public Vector2 Origin { get; }
        public Vector2 Direction { get; }
        public float Impulse { get; }
    }
}
