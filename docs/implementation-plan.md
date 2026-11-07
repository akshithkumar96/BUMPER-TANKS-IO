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

Status: the 90-second phase schedule, shared arena bounds, centered 3×3 sudden-death zone, five-second boundary warnings, out-of-bounds pickup cleanup, phase gem multiplier, phase knockback multiplier, 2.5-second bot fill, and bot pickup seeking are implemented. Matches now end on a sole survivor or at the time limit, resolve time-limit standings by survival, gems, eliminations, and remaining health, then submit the local player result once. Collected power-ups respawn after 12 seconds inside the current arena bounds. Play Mode verification remains.

## Module 4 Multiplayer

Integrate the WebSocket relay: matchmaking target under three seconds, bot fill after 2.5 seconds, 30 Hz replication, client prediction/interpolation, and reconnect handling.

Acceptance criteria: two clients complete a match; movement/projectiles stay within agreed error tolerances at simulated latency; no secrets are committed.

Status: the native and WebGL Unity WebSocket client is wired into the arena. It sends 30 Hz movement, aim, fire, ability, and archetype input; predicts and reconciles the local tank; interpolates remote tanks; and follows server match timing and shrinking bounds. The ASP.NET Core relay caps rooms at eight, waits up to 2.5 seconds for players, fills remaining slots with bots, simulates authoritative movement, archetype combat, eliminations, pickups, and 30 Hz snapshots. The server owns gem and power-up collection, magnet attraction, timed pickup respawn, Nitro and Mega-Bullet effects, and doubled sudden-death gem value. The full realtime smoke covers queue fill, capacity, input validation, pickup effects, combat, abilities, disconnects, and match-phase boundaries. Projectile visuals/deflection, authenticated WebSocket identity, trusted match reward receipts, durable distributed rooms, deployed endpoint configuration, reconnect UX, and two-device gameplay QA remain.

Client-side snapshot interpolation and fixed-30-Hz prediction/reconciliation helpers are implemented and covered by Edit Mode tests. Fusion scaffolding remains optional and is not required for the selected relay path.

## Module 5 UX and Progression

Create menu, matchmaking, HUD, results screen, accessibility controls, cosmetic garage, local persistence, battle coins, and daily-spin flow.

Acceptance criteria: full menu-to-rematch journey works on touch and mouse; cosmetics persist; essential UI is screen-reader and contrast reviewed.

Status: the front menu, offline arena, online matchmaking status, HUD, result/rematch, persistent color and trail garage cosmetics, daily spin, Battle Coins, and leaderboard are implemented. The garage includes server-priced flag and procedural bump-sound unlocks; the server debits coins and persists ownership, while the client equips only owned items. Mobile TalkBack/VoiceOver actions cover menu, garage, leaderboard, tank ability, and match connection status; garage controls use 44-pixel targets and safe-area-aware positioning. Mobile screen-reader access to movement/fire, device review, and device-scale QA remain.

## Module 6 Live Operations

Add approved ad mediation, analytics, consent-aware telemetry, remote tuning, crash reporting, and server-side validation for rewards.

Acceptance criteria: services remain disabled without configured environment-specific credentials; rewards cannot be doubled locally; privacy/consent paths are tested.

Status: profile/reward/leaderboard APIs, persistent JSON storage, match idempotency, server-owned cosmetic purchases, basic telemetry, CORS configuration, request throttling, bounded identifier/event validation, capped telemetry retention, and production HMAC match attestation enforcement are implemented. The Unity client syncs coin balances and cosmetic ownership from server responses. Server-issued anonymous identity and bearer authentication for profile/reward/purchase/telemetry requests are in progress; WebSocket join binding and authenticated live match reward receipts are still required. HMAC enforcement requires a trusted authority to sign verified results and is not integrated with live multiplayer yet. Durable multi-instance storage, consent flow, and ad mediation remain.

## Module 7 Release QA

Profile Android, iOS, and WebGL; test browser/mobile control paths, matchmaking load behavior, reconnects, accessibility, and build size/performance budgets.

Acceptance criteria: release checklist passes on target devices and browsers; CI archives builds and test results; tagged release is reproducible.
