# Photon Fusion Shared Mode Setup

The multiplayer adapter is intentionally gated behind the `FUSION_PRESENT` scripting define so the local game always compiles without a third-party SDK.

1. Download and import Photon Fusion 2 from the Photon dashboard or the official Fusion download page.
2. Create a Fusion 2 App in the Photon dashboard and set its AppId in Unity through `Tools > Fusion > Realtime Settings`.
3. Add `FUSION_PRESENT` to the project scripting defines after import.
4. Add a `FusionIntegration` component to the bootstrap object and call `Connect` with the selected room name.

Do not commit Photon AppIds, service credentials, or dashboard exports. The gameplay code exchanges only `PlayerInputFrame` and `TankSnapshot` through `IRealtimeSession`.
