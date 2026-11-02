using BumperTanks.Match;
using UnityEngine;

namespace BumperTanks.Gameplay
{
    public sealed class ArenaField : MonoBehaviour
    {
        private const int Columns = 16;
        private const int Rows = 12;
        private const float TileSize = 0.92f;
        private const float WarningSeconds = 1f;
        private static readonly Color SafeTile = new(0.08f, 0.15f, 0.22f);
        private static readonly Color WarningTile = new(1f, 0.12f, 0.08f);
        private SpriteRenderer[] tiles;
        private Vector2[] tileCenters;

        private void Awake()
        {
            tiles = new SpriteRenderer[Columns * Rows];
            tileCenters = new Vector2[tiles.Length];
            for (var y = 0; y < Rows; y++)
            for (var x = 0; x < Columns; x++)
            {
                var index = y * Columns + x;
                var center = new Vector2(x - (Columns - 1) * 0.5f, y - (Rows - 1) * 0.5f);
                tileCenters[index] = center;
                var tile = new GameObject($"Arena Tile {x},{y}");
                tile.transform.SetParent(transform, false);
                tile.transform.position = center;
                tile.transform.localScale = new Vector3(TileSize, TileSize, 1f);
                var sprite = tile.AddComponent<SpriteRenderer>();
                sprite.sprite = RuntimeSprite.White;
                sprite.color = SafeTile * (((x + y) & 1) == 0 ? 1f : 0.78f);
                sprite.sortingOrder = -5;
                tiles[index] = sprite;
            }
        }

        private void Update()
        {
            var director = MatchDirector.Instance;
            if (director == null) return;

            var elapsed = director.Elapsed;
            var phase = MatchRules.PhaseAt(elapsed);
            var steps = MatchRules.CollapseStepsAt(elapsed);
            var nextCollapseAt = MatchRules.OpenArenaEnd + steps * MatchRules.CollapseInterval;
            var timeToCollapse = nextCollapseAt - elapsed;
            var warningBounds = MatchRules.BoundsAt(nextCollapseAt);
            var warningActive = phase != MatchPhase.FinalZone && phase != MatchPhase.Complete &&
                                timeToCollapse >= 0f && timeToCollapse <= WarningSeconds;
            var flash = Mathf.PingPong(Time.unscaledTime * 7f, 1f);

            for (var index = 0; index < tiles.Length; index++)
            {
                var sprite = tiles[index];
                var center = tileCenters[index];
                if (!director.Bounds.Contains(center))
                {
                    sprite.enabled = false;
                    continue;
                }

                sprite.enabled = true;
                if (warningActive && !warningBounds.Contains(center))
                    sprite.color = Color.Lerp(SafeTile, WarningTile, flash);
                else
                    sprite.color = SafeTile * ((((index / Columns) + (index % Columns)) & 1) == 0 ? 1f : 0.78f);
            }
        }
    }
}
