# Bumper Tanks IO Implementation Plan

## Technology decision

Unity 6.3 LTS with URP, C#, and Photon Fusion Shared Mode is the delivery stack. It directly supports the GDD target of WebGL and mobile play, makes a 2D physics-first game lightweight, and provides an established path to shared-authority eight-player sessions. Unreal is not selected because it has no supported HTML5 export path and adds unnecessary runtime and production complexity for this game.

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

Acceptance criteria: each tank is controllable; a released shot applies recoil; collision response differs by mass; rear hits apply 1.5x knockback; ability cooldown behavior is tested.

Status: in progress. The runtime arena, keyboard/mouse/touch controls, bots, projectiles, recoil, rear-hit knockback, power-ups, scoring, and initial three abilities are implemented; Play Mode verification remains.

## Module 3 Match Director and Bots

Add a 90-120 second state machine: broad arena/power-ups, 5-second tile collapse, 3x3 safe zone, doubled gems, increased knockback, elimination/scoring, and fill bots.

Acceptance criteria: match phases transition at their defined times; arena changes are deterministic; a bot can seek targets and power-ups; a match ends with a winner.

## Module 4 Multiplayer

Integrate Photon Fusion Shared Mode: matchmaking target under three seconds, bot fill after 2.5 seconds, 30 Hz replication, client prediction/interpolation, and reconnect handling.

Acceptance criteria: two clients complete a match; movement/projectiles stay within agreed error tolerances at simulated latency; no secrets are committed.

Status: contracts, prediction, interpolation, and an SDK-gated Shared Mode adapter are in place. Fusion SDK import, Photon AppId, live two-client testing, and reconnect handling remain.

Client-side snapshot interpolation and fixed-30-Hz prediction/reconciliation helpers are implemented and covered by Edit Mode tests; the adapter will feed them from Fusion snapshots after SDK installation.

## Module 5 UX and Progression

Create menu, matchmaking, HUD, results screen, accessibility controls, cosmetic garage, local persistence, battle coins, and daily-spin flow.

Acceptance criteria: full menu-to-rematch journey works on touch and mouse; cosmetics persist; essential UI is screen-reader and contrast reviewed.

Status: front menu, local arena HUD, result/rematch, color garage, daily reward, battle coins, and leaderboard are implemented. Garage and scores open from the menu and return to it while pausing the arena. Accessible UI review, matchmaking UI, and device-scale UX pass remain.

## Module 6 Live Operations

Add approved ad mediation, analytics, consent-aware telemetry, remote tuning, crash reporting, and server-side validation for rewards.

Acceptance criteria: services remain disabled without configured environment-specific credentials; rewards cannot be doubled locally; privacy/consent paths are tested.

Status: profile/reward/leaderboard APIs, persistent JSON storage, match idempotency, basic telemetry, CORS configuration, request throttling, bounded identifier/event validation, and capped telemetry retention are implemented. The Unity client syncs its coin balance from successful server reward responses. Production identity, authoritative match validation, consent flow, and ad mediation remain.

## Module 7 Release QA

Profile Android, iOS, and WebGL; test browser/mobile control paths, matchmaking load behavior, reconnects, accessibility, and build size/performance budgets.

Acceptance criteria: release checklist passes on target devices and browsers; CI archives builds and test results; tagged release is reproducible.
