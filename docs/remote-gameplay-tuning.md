# Remote gameplay tuning

The game service publishes its current gameplay tuning at `GET /v1/gameplay/tuning`. This public read-only endpoint returns a revision and the speed, projectile cooldown, and knockback multipliers used by the realtime authority. The values use the same bounded settings for Unity's local prediction and offline combat.

Set these server environment variables to tune a deployment:

| Variable | Default | Accepted range |
| --- | ---: | ---: |
| `GameplayTuning__SpeedMultiplier` | `1.0` | `0.75` to `1.25` |
| `GameplayTuning__ProjectileCooldownMultiplier` | `1.0` | `0.75` to `1.5` |
| `GameplayTuning__KnockbackMultiplier` | `1.0` | `0.75` to `1.5` |

Out-of-range values are clamped. Non-finite values fall back to the default. Restart the service to activate changed values; the realtime room captures one settings snapshot when it is created. The endpoint returns an `ETag`, accepts `If-None-Match`, and advertises a 60-second public cache policy.

The Unity client fetches tuning before marking its authenticated identity ready and again before each realtime connection. It sends the cached ETag and accepts `304 Not Modified`. A valid response is saved locally and reused offline for up to seven days. Invalid, incompatible, expired, or unreachable configuration falls back to the built-in defaults or the last valid cache. The multipliers are validated locally to the same ranges.

The endpoint contains no player-specific data or credentials, so it does not require authentication. Configure HTTPS/WSS endpoints for release builds as described in [deployment-endpoints.md](deployment-endpoints.md).
