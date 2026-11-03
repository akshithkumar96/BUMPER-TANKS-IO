# Bumper Tanks IO Implementation Plan

## Technology decision

Unity 6.3 LTS with URP and C# is the delivery stack. The multiplayer path is a first-party ASP.NET Core WebSocket relay, one of the GDD's two supported network options. The Unity client uses `ClientWebSocket` on native targets and a browser WebSocket bridge on WebGL. Unreal is not selected because it has no supported HTML5 export path and adds unnecessary runtime and production complexity for this game.

## Delivery rules

- Every module must compile, include targeted automated tests, be manually smoke-tested in a development build, and have one focused Git commit.
- Gameplay simulation stays deterministic where practical: fixed time steps, explicit tuning values, and no frame-rate-dependent movement.
- Networking, ads, analytics, credentials, and production service identifiers are not introduced until their dedicated modules.

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

Status: a native and WebGL Unity WebSocket client and ASP.NET Core room service are implemented. The server caps rooms at eight, waits up to 2.5 seconds for real players, fills remaining slots with bots, validates input, and broadcasts 30 Hz authoritative movement snapshots. The integration smoke test covers origin policy, queue fill, movement, room capacity, disconnects, and invalid input. Gameplay input/rendering is not yet connected to the client session; replicated combat, match phase, authoritative result receipts, durable distributed rooms, identity, reconnection, and two-client gameplay QA remain.

Client-side snapshot interpolation and fixed-30-Hz prediction/reconciliation helpers are implemented and covered by Edit Mode tests. Fusion scaffolding remains optional and is not required for the selected relay path.

## Module 5 UX and Progression

Create menu, matchmaking, HUD, results screen, accessibility controls, cosmetic garage, local persistence, battle coins, and daily-spin flow.

Acceptance criteria: full menu-to-rematch journey works on touch and mouse; cosmetics persist; essential UI is screen-reader and contrast reviewed.

Status: front menu, local arena HUD, result/rematch, persistent color and trail garage cosmetics, daily spin, Battle Coins, and leaderboard are implemented. The garage now includes server-priced unlocks for flag attachments and procedural bump sounds; the server debits coins and persists ownership, while the client only equips owned items. Mobile TalkBack/VoiceOver actions cover menu, garage, leaderboard, and tank ability; garage controls use 44-pixel targets, descriptive text, and responsive positioning. Menu, HUD, result, garage, leaderboard, and accessibility action frames honor display safe areas, with Edit Mode coverage for insets and narrow/short layouts. Mobile screen-reader access to movement/fire, device review, matchmaking UI, and device-scale QA remain.

## Module 6 Live Operations

Add approved ad mediation, analytics, consent-aware telemetry, remote tuning, crash reporting, and server-side validation for rewards.

Acceptance criteria: services remain disabled without configured environment-specific credentials; rewards cannot be doubled locally; privacy/consent paths are tested.

Status: profile/reward/leaderboard APIs, persistent JSON storage, match idempotency, server-owned cosmetic purchases, basic telemetry, CORS configuration, request throttling, bounded identifier/event validation, capped telemetry retention, and production HMAC match attestation enforcement are implemented. The Unity client syncs coin balances and cosmetic ownership from server responses. HMAC enforcement requires a trusted authority to sign verified results and is not integrated with live multiplayer yet. Production player identity, durable multi-instance storage, consent flow, and ad mediation remain.

## Module 7 Release QA

Profile Android, iOS, and WebGL; test browser/mobile control paths, matchmaking load behavior, reconnects, accessibility, and build size/performance budgets.

Acceptance criteria: release checklist passes on target devices and browsers; CI archives builds and test results; tagged release is reproducible.
