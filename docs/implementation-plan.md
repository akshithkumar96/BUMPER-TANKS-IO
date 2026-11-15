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

Status: in progress. The runtime arena, keyboard/mouse/touch controls, aim trajectory, mass-scaled bump physics, archetype-tuned weapons, bouncing spinner disc, projectile recoil, flank knockback, power-ups, scoring, and GDD-aligned abilities are implemented. Offline tanks outside shrinking bounds now take gradual phase damage instead of being clamped inward, matching the realtime authority. Play Mode tests exercise Rigidbody2D movement/speed bounds and match phase/authoritative bounds; the full five-test Play Mode suite, including the boundary integration regression, passes when the local Unity CLI is invoked without `-runSynchronously`. Visual gameplay and device QA remain.

## Module 3 Match Director and Bots

Add a 90-120 second state machine: broad arena/power-ups, 5-second tile collapse, 3x3 safe zone, doubled gems, increased knockback, elimination/scoring, and fill bots.

Acceptance criteria: match phases transition at their defined times; arena changes are deterministic; a bot can seek targets and power-ups; a match ends with a winner.

Status: the 90-second phase schedule, shared arena bounds, centered 3×3 sudden-death zone, five-second boundary warnings, out-of-bounds pickup cleanup, phase gem multiplier, phase knockback multiplier, and 2.5-second bot fill are implemented. Bots seek pickup items, target opponents, fire, use archetype abilities when opponents are within range and cooldown is ready, and steer clear of shrinking boundaries. Matches now end on a sole survivor or at the time limit, resolve time-limit standings by survival, gems, eliminations, and remaining health, then submit the local player result once. Collected power-ups respawn after 12 seconds inside the current arena bounds. Play Mode tests cover bot ability range/cooldown behavior and server-authoritative phase and bounds state; full local match, visual, and device QA remain.

## Module 4 Multiplayer

Integrate the WebSocket relay: matchmaking target under three seconds, bot fill after 2.5 seconds, 30 Hz replication, client prediction/interpolation, and reconnect handling.

Acceptance criteria: two clients complete a match; movement/projectiles stay within agreed error tolerances at simulated latency; no secrets are committed.

Status: the native and WebGL Unity WebSocket client is wired into the arena. It sends 30 Hz movement, aim, fire, ability, and archetype input; predicts and reconciles the local tank; interpolates remote tanks; and follows server match timing and shrinking bounds. The ASP.NET Core relay caps rooms at eight, waits up to 2.5 seconds for players, fills remaining slots with bots, simulates authoritative movement, archetype combat, eliminations, pickups, and 30 Hz snapshots. Bots target, attack, and use their archetype abilities. The server owns gem and power-up collection, magnet attraction, timed pickup respawn, Nitro and Mega-Bullet effects, and doubled sudden-death gem value. REST endpoints and WebSocket joins require server-issued credentials bound to the player identity. At match end the server persists an idempotent reward for each authenticated player and sends a result receipt; the client updates its Battle Coin balance and result screen. Heavy shield reflection and Spinner's three-bounce laser are authoritative and covered by the realtime smoke. Deployment endpoint configuration is centralized in a Resources settings asset, derives the WebSocket URL from the API URL, and requires HTTPS/WSS URLs on non-loopback hosts in release builds. A dropped connection retains its player slot and authoritative state for a configurable 30-second grace period; the same authenticated identity resumes that slot, while expired or finished matches cannot be resumed. Resume resets the per-connection input sequence and is covered by realtime smoke. Idle WebSocket upgrades close after a configurable join deadline. WebGL input uses a paced 30 Hz scheduler; browser QA observed stable 30 Hz snapshots and no server errors or socket closes. The client keeps bounded automatic retries and exposes an accessible Retry Online control when its configured service is available. The realtime smoke covers resume identity/state, restarted input ticks, expiry into a fresh room, queue fill, capacity, token validation, input validation, pickup effects, combat, abilities, match-phase boundaries, and the final receipt. PostgreSQL-backed service instances use an advisory lease to reject a second realtime owner sharing the same database, and a watchdog shuts down the owner if its lease session is lost. This prevents split-brain rooms but does not distribute room state or route clients across replicas; horizontal realtime scaling, actual service deployment, and two-device gameplay QA remain. The PostgreSQL lease integration smoke and the complete Server CI workflow pass on GitHub Actions.

Client-side snapshot interpolation and fixed-30-Hz prediction/reconciliation helpers are implemented and covered by Edit Mode tests. Fusion scaffolding remains optional and is not required for the selected relay path.

## Module 5 UX and Progression

Create menu, matchmaking, HUD, results screen, accessibility controls, cosmetic garage, local persistence, battle coins, and daily-spin flow.

Acceptance criteria: full menu-to-rematch journey works on touch and mouse; cosmetics persist; essential UI is screen-reader and contrast reviewed.

Status: the front menu, offline arena, online matchmaking status, HUD, result/rematch, persistent color and trail garage cosmetics, daily spin, Battle Coins, and leaderboard are implemented. The garage includes server-priced flag and procedural bump-sound unlocks; the server debits coins and persists ownership, while the client equips only owned items. Gameplay telemetry is disabled until an opt-in choice in the main menu; players can decline without blocking play and change their choice later. Mobile TalkBack/VoiceOver actions cover menu, garage, leaderboard, tank ability, and match connection status; garage controls use 44-pixel targets and safe-area-aware positioning. The first-run consent explanation fits above its 44-pixel buttons on portrait screens. Browser checks at 390×844 and 1280×720 confirmed the helper and controls do not overlap. Screen-reader match controls provide visible directional movement pulses and cardinal aim-and-fire actions. Physical-device review remains.

## Module 6 Live Operations

Add approved ad mediation, analytics, consent-aware telemetry, remote tuning, crash reporting, and server-side validation for rewards.

Acceptance criteria: services remain disabled without configured environment-specific credentials; rewards cannot be doubled locally; privacy/consent paths are tested.

Status: profile/reward/leaderboard APIs, Development JSON stores, transactional PostgreSQL persistence, match idempotency, server-owned cosmetic purchases, consent-gated telemetry and crash reports, CORS configuration, request throttling, bounded identifier/event validation, capped retention, production HMAC match attestation enforcement, and server-controlled remote gameplay tuning are implemented. PostgreSQL stores profiles, identity-token hashes, match/reward idempotency, telemetry, and crash reports; mutations use transactions and database uniqueness constraints across service instances. The tuning endpoint publishes bounded speed, projectile cooldown, and knockback multipliers with ETag caching; both the realtime authority and Unity prediction/local combat apply the same settings, with a seven-day offline client cache. The Unity client syncs coin balances and cosmetic ownership from server responses. Server-issued anonymous identities authenticate profile, reward, purchase, telemetry, crash, and WebSocket join requests. Authenticated online match results are computed and persisted by the realtime server and returned once to the client. Rewarded daily spins use an authenticated one-time challenge and a verified, idempotent LevelPlay server-to-server callback; rewards are disabled until external Android/iOS ad-unit, callback URL, and secret configuration is supplied. HMAC enforcement remains for externally submitted match results. A PostgreSQL advisory lease prevents multiple realtime servers from owning separate process-local room sets against the same database. Distributed realtime rooms, actual service deployment, and mobile/browser device QA remain.

## Module 7 Release QA

Profile Android, iOS, and WebGL; test browser/mobile control paths, matchmaking load behavior, reconnects, accessibility, and build size/performance budgets.

Status: GitHub Actions can run Edit/Play Mode tests and archive WebGL, Windows, and Android Development builds when repository variable `UNITY_CI_ENABLED=true` and secret `UNITY_LICENSE` are configured. Without those settings, the workflow records a clear skip. Local Unity verification passes 103 Edit Mode tests and all 5 Play Mode tests, including the offline boundary regression. The targeted boundary test also passed independently. The local Play Mode CLI must omit `-runSynchronously`; with that flag, the command consumed CPU in the player-build pipeline and produced no XML, while the normal asynchronous runner completed successfully. WebGL, Windows, Mono/ARMv7 Android, and IL2CPP/ARM64 Android Development builds have succeeded. The Android entrypoints apply and restore their temporary architecture, backend, resolver, template, and Development-only manifest settings. Browser checks cover online WebGL startup and the consent menu at portrait and desktop sizes. Mobile input clears held steering/actions when the app pauses or loses focus. Development builds expose a server URL field that derives the WebSocket endpoint; Android Development builds temporarily allow cleartext only for local testing, while release builds retain HTTPS/WSS validation. Physical Android/iOS device QA, deployed-service QA, load/performance budgets, and a tagged release remain.

Acceptance criteria: release checklist passes on target devices and browsers; CI archives builds and test results; tagged release is reproducible.

## Latest verified updates (November 2026)

- Commit `7bb97ce` adds a reproducible ARM64/IL2CPP Development APK entrypoint and Unity CI artifact. The generated APK was verified to contain all seven `arm64-v8a` native libraries, including IL2CPP and Unity, and is debug-signed rather than store-ready.
- Commit `b6eaaab` adds the post-match rewarded video offer to double the finalized Battle Coin award. The authenticated server challenge is bound to the persisted player/match award; the LevelPlay server callback grants the additional award once and rejects mismatched or replayed events. JSON/API smoke passed, and Server CI #55 passed concurrent PostgreSQL reward integration plus the complete service/realtime smoke.
- The WebGL Development build was manually smoke-tested in Chrome against a local Development server: identity/catalog/tuning requests succeeded, bot fill started an online match, the match completed, and PostgreSQL-independent JSON storage recorded the result and coin award.
- The latest Unity CI workflow is still opt-in and skipped test/build jobs because `UNITY_CI_ENABLED=true` and `UNITY_LICENSE` are not configured. The targeted offline boundary Play Mode test passed 1/1 and the full Play Mode suite passed 5/5 with no failures or skips. The local CLI runs successfully without `-runSynchronously`; the GitHub Unity workflow uses its standard runner invocation.
- Realtime rooms now stop after sending the final snapshot and reward receipt, then leave the matchmaking registry. Unity explicitly disconnects on a valid match receipt and preserves the result accessibility screen. The realtime smoke verifies that no more snapshots or duplicate receipts arrive after the result; the targeted terminal-disconnect Edit Mode test passed, and the full Edit Mode suite passed 103/103.
- The realtime smoke now measures client-observed solo matchmaking through bot fill over five fresh queues. The local Development backend averaged 2.512 seconds (worst 2.525 seconds), meeting the GDD's average-under-3-second target on this local baseline; deployed network and load measurements remain part of release QA.
- A slow-consumer regression passed locally: a healthy peer received 20 snapshots in 0.930 seconds while another socket stopped reading; the server build completed with zero warnings or errors. Match-end profile persistence rolls back partial in-memory writes and retries after transient storage failures, keeping the finalized award stable across idempotent retries.
- Authenticated `GET /v1/matches/{matchId}/result` recovers a persisted award/profile by owner only. Unity checks for a missing WebSocket receipt after match finish or disconnect and restores the result/profile. Repository tests cover JSON rollback/retry and result lookup; the full local realtime smoke covers authorized, unauthorized, and cross-player result lookups.
- The full local realtime smoke passed after result recovery and persistence rollback changes, including final match result and terminal room retirement. Five bot-fill queues averaged 2.521 seconds (worst 2.542 seconds).
- Server CI #60 passed after the match-result persistence retry change, including the PostgreSQL and realtime checks. The Unity Development CI workflow still skipped build/test jobs because the opt-in flag and license secret are not configured.
- A fresh WebGL Development build after archetype selection completed successfully (108 MiB). Browser visual and touch QA remain outstanding; no Android device is connected for physical-device testing in this environment.
- The ARM64/IL2CPP Development APK was rebuilt after archetype selection and contains the expected seven native libraries under `lib/arm64-v8a/`; device installation and interaction remain unverified because no Android device is attached.
- After match-result recovery was added, the WebGL Development build completed successfully (108 MiB), and a fresh ARM64/IL2CPP Android Development build completed successfully (83.7 MiB). The APK contains all seven expected `lib/arm64-v8a/` native libraries; no Android device is attached for installation QA.
- Server CI #61 passed for match-result recovery, including the server build, PostgreSQL integration checks, and realtime smoke. Unity CI #20 completed with test/build jobs skipped because `UNITY_CI_ENABLED` and `UNITY_LICENSE` are not configured.
- Garage catalog and leaderboard overlays now distinguish loading, empty, and failed requests and expose retry actions in the GUI and accessibility hierarchy. Garage retry controls use 44-pixel touch targets. Unity Edit Mode passed 108/108 and Play Mode passed 5/5.
- Power-up drops are now seeded per match across the opening arena, avoid player spawn slots and other drops, and respawn at randomized clear locations within current arena bounds. Unity planner tests cover deterministic seeds, spread, separation, and crowded end-zone bounds; the realtime smoke verifies randomized cross-room drops, collection effects, and independently timed respawns.
- After these changes, the WebGL Development build succeeded (108 MiB), and the ARM64/IL2CPP Android Development APK succeeded (137.5 MiB) with all seven expected `lib/arm64-v8a/` libraries. No physical Android device is attached for installation QA.
- Full local realtime smoke passed with randomized pickups and result recovery; five solo queues averaged 2.521 seconds (worst 2.534 seconds). Server CI #65 passed the Compose secret-forwarding check, backend build, PostgreSQL integration, and full realtime smoke. Unity CI #21 completed successfully, with test/build jobs skipped because the opt-in variable and license secret remain unconfigured.
- Docker Compose forwards LevelPlay and match-attestation secrets from deployment environment variables into the service without committing secret values; CI verifies all three reward-related mappings.
- The main menu now offers persistent Speedy Bumper, Heavy Bumper, and Spinner selection and applies the selected archetype to local and online matches; the full Edit Mode suite passed 103/103.
- Remaining release work: configure LevelPlay platform IDs/callback key and production match-attestation secret, deploy an HTTPS/WSS service, verify ad delivery/callbacks with real LevelPlay credentials, run physical Android/iOS QA, complete load/performance budgets, and produce a signed/tagged release.
