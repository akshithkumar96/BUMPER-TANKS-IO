using UnityEngine;

namespace BumperTanks.Core
{
    public readonly struct ArenaBounds
    {
        public ArenaBounds(Vector2 center, Vector2 size)
        {
            if (size.x <= 0f || size.y <= 0f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(size), "Arena dimensions must be positive.");
            }

            Center = center;
            Size = size;
        }

        public Vector2 Center { get; }
        public Vector2 Size { get; }
        public Vector2 Min => Center - (Size * 0.5f);
        public Vector2 Max => Center + (Size * 0.5f);

        public bool Contains(Vector2 point) => point.x >= Min.x && point.x <= Max.x && point.y >= Min.y && point.y <= Max.y;

        public Vector2 Clamp(Vector2 point) => new Vector2(
            Mathf.Clamp(point.x, Min.x, Max.x),
            Mathf.Clamp(point.y, Min.y, Max.y));
    }
}
