# PostgreSQL storage

The server selects storage at startup. In Development it defaults to the existing JSON profile and identity repositories. Set `Storage__Provider=PostgreSQL` and `ConnectionStrings__GameDatabase` to use PostgreSQL in any environment. Outside Development, PostgreSQL is the default and startup fails if its connection string is missing; explicitly selecting JSON outside Development also fails.

The PostgreSQL provider applies EF Core migrations during startup. It stores profiles and owned cosmetics, SHA-256 identity-token hashes, completed match IDs, daily-spin challenges, verified rewarded-ad event bindings, telemetry events, and crash reports. Reward and purchase mutations lock the player's profile row and commit related idempotency records in the same database transaction, so separate API processes serialize those operations. Database constraints enforce token, challenge, match, provider-event, and crash-fingerprint uniqueness. Telemetry writes use a transaction-scoped advisory lock while pruning to the newest 10,000 events; crash-report upserts use a transaction-scoped advisory lock while deduplicating and pruning to 1,000 records/30 days.

The migrations do not import existing `profiles.json`, `identities.json`, `telemetry.json`, or `crash-reports.json` files. Back up and explicitly migrate existing Development data before switching a live installation to PostgreSQL; credentials are not recoverable from token hashes alone.

Run the local API and database with `docker compose up --build`. The compose password fallback is for loopback development only. For deployment, provide a managed PostgreSQL connection string through `ConnectionStrings__GameDatabase` and a secret password through your platform's secret manager.

This storage phase does not make realtime multiplayer multi-instance safe. Room membership, match simulation, and connected-player ownership remain process-local. Route reconnects to the room-owning instance and run one realtime service replica until distributed room ownership is implemented and load-tested. Do not mount the same Development JSON files into multiple service replicas.
