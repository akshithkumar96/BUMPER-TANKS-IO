# Bumper Tanks IO Implementation Plan

## Technology decision

Unity 6.3 LTS with URP and C# is the delivery stack. The multiplayer path is a first-party ASP.NET Core WebSocket relay, one of the GDD's two supported network options. The Unity client uses `ClientWebSocket` on native targets and a browser WebSocket bridge on WebGL. Unreal is not selected because it has no supported HTML5 export path and adds unnecessary runtime and production complexity for this game.

## Delivery rules

- Every module must compile, include targeted automated tests, be manually smoke-tested in a development build, and have one focused Git commit.
- Gameplay simulation stays deterministic where practical: fixed time steps, explicit tuning values, and no frame-rate-dependent movement.
- External service credentials remain environment-specific. Networking and telemetry must pass through their dedicated modules and never expose secrets in the client.

## Module 1 Foundation

Create a Unity 6 project with URP and Input System dependencies; establish test assemblies; provide an arena grid model and frame-rate-independent tank steering/motion primitives.

Acceptance criteria: Unity opens the project on 6000.3.8f1; the Edit Mode test suite validates arena bounds, archetype tuning, speed limits, and friction; no runtime service credentials are committed.

Commit: `feat(foundation): bootstrap Unity tank simulation core`

Status: complete and pushed as `3a6c003`.

## Module 2 Offline Combat Vertical Slice

Implement a local playable arena with the three GDD archetypes, virtual joystick and mouse controls, projectile recoil, vehicle impulses, rear-hit multiplier, and abilities.

Acceptance criteria: each tank is controllable; a released shot applies recoil; collision response differs by mass; flank hits apply 1.5x knockback; ability cooldown behavior is tested.

Status: in progress. The runtime arena, keyboard/mouse/touch controls, aim trajectory, mass-scaled bump physics, archetype-tuned weapons, bouncing spinner disc, projectile recoil, flank knockback, power-ups, scoring, and GDD-aligned abilities are implemented. Play Mode verification remains.

## Module 3 Match Director and Bots

Add a 90-120 second state machine: broad arena/power-ups, 5-second tile collapse, 3x3 safe zone, doubled gems, increased knockback, elimination/scoring, and fill bots.

Acceptance criteria: match phases transition at their defined times; arena changes are deterministic; a bot can seek targets and power-ups; a match ends with a winner.

Status: the 90-second phase schedule, shared arena bounds, centered 3×3 sudden-death zone, five-second boundary warnings, out-of-bounds pickup cleanup, phase gem multiplier, phase knockback multiplier, and 2.5-second bot fill are implemented. Bots seek pickup items, target opponents, fire, use archetype abilities, and steer clear of shrinking boundaries. Matches now end on a sole survivor or at the time limit, resolve time-limit standings by survival, gems, eliminations, and remaining health, then submit the local player result once. Collected power-ups respawn after 12 seconds inside the current arena bounds. Play Mode verification remains.

## Module 4 Multiplayer

Integrate the WebSocket relay: matchmaking target under three seconds, bot fill after 2.5 seconds, 30 Hz replication, client prediction/interpolation, and reconnect handling.

Acceptance criteria: two clients complete a match; movement/projectiles stay within agreed error tolerances at simulated latency; no secrets are committed.

Status: the native and WebGL Unity WebSocket client is wired into the arena. It sends 30 Hz movement, aim, fire, ability, and archetype input; predicts and reconciles the local tank; interpolates remote tanks; and follows server match timing and shrinking bounds. The ASP.NET Core relay caps rooms at eight, waits up to 2.5 seconds for players, fills remaining slots with bots, simulates authoritative movement, archetype combat, eliminations, pickups, and 30 Hz snapshots. Bots target and attack opponents. The server owns gem and power-up collection, magnet attraction, timed pickup respawn, Nitro and Mega-Bullet effects, and doubled sudden-death gem value. REST endpoints and WebSocket joins require server-issued credentials bound to the player identity. At match end the server persists an idempotent reward for each authenticated player and sends a result receipt; the client updates its Battle Coin balance and result screen. Heavy shield reflection and Spinner's three-bounce laser are authoritative and covered by the realtime smoke. Deployment endpoint configuration is centralized in a Resources settings asset, derives the WebSocket URL from the API URL, and requires HTTPS/WSS URLs on non-loopback hosts in release builds. A dropped connection retains its player slot and authoritative state for a configurable 30-second grace period; the same authenticated identity resumes that slot, while expired or finished matches cannot be resumed. The client keeps bounded automatic retries and exposes an accessible Retry Online control when its configured service is available. The realtime smoke covers resume identity/state, expiry into a fresh room, queue fill, capacity, token validation, input validation, pickup effects, combat, abilities, match-phase boundaries, and the final receipt. Durable distributed rooms, actual service deployment, and two-device gameplay QA remain.

Client-side snapshot interpolation and fixed-30-Hz prediction/reconciliation helpers are implemented and covered by Edit Mode tests. Fusion scaffolding remains optional and is not required for the selected relay path.

## Module 5 UX and Progression

Create menu, matchmaking, HUD, results screen, accessibility controls, cosmetic garage, local persistence, battle coins, and daily-spin flow.

Acceptance criteria: full menu-to-rematch journey works on touch and mouse; cosmetics persist; essential UI is screen-reader and contrast reviewed.

Status: the front menu, offline arena, online matchmaking status, HUD, result/rematch, persistent color and trail garage cosmetics, daily spin, Battle Coins, and leaderboard are implemented. The garage includes server-priced flag and procedural bump-sound unlocks; the server debits coins and persists ownership, while the client equips only owned items. Gameplay telemetry is disabled until an opt-in choice in the main menu; players can decline without blocking play and change their choice later. Mobile TalkBack/VoiceOver actions cover menu, garage, leaderboard, tank ability, and match connection status; garage controls use 44-pixel targets and safe-area-aware positioning. Screen-reader match controls now provide visible directional movement pulses and cardinal aim-and-fire actions. Device review and device-scale QA remain.

## Module 6 Live Operations

Add approved ad mediation, analytics, consent-aware telemetry, remote tuning, crash reporting, and server-side validation for rewards.

Acceptance criteria: services remain disabled without configured environment-specific credentials; rewards cannot be doubled locally; privacy/consent paths are tested.

Status: profile/reward/leaderboard APIs, Development JSON stores, transactional PostgreSQL persistence, match idempotency, server-owned cosmetic purchases, consent-gated telemetry and crash reports, CORS configuration, request throttling, bounded identifier/event validation, capped retention, production HMAC match attestation enforcement, and server-controlled remote gameplay tuning are implemented. PostgreSQL stores profiles, identity-token hashes, match/reward idempotency, telemetry, and crash reports; mutations use transactions and database uniqueness constraints across service instances. The tuning endpoint publishes bounded speed, projectile cooldown, and knockback multipliers with ETag caching; both the realtime authority and Unity prediction/local combat apply the same settings, with a seven-day offline client cache. The Unity client syncs coin balances and cosmetic ownership from server responses. Server-issued anonymous identities authenticate profile, reward, purchase, telemetry, crash, and WebSocket join requests. Authenticated online match results are computed and persisted by the realtime server and returned once to the client. Rewarded daily spins use an authenticated one-time challenge and a verified, idempotent LevelPlay server-to-server callback; rewards are disabled until external Android/iOS ad-unit, callback URL, and secret configuration is supplied. HMAC enforcement remains for externally submitted match results. Distributed realtime rooms, actual service deployment, and mobile/browser device QA remain.

## Module 7 Release QA

Profile Android, iOS, and WebGL; test browser/mobile control paths, matchmaking load behavior, reconnects, accessibility, and build size/performance budgets.

Acceptance criteria: release checklist passes on target devices and browsers; CI archives builds and test results; tagged release is reproducible.
