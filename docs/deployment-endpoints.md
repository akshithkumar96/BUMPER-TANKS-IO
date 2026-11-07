# Service endpoint configuration

Unity reads `Assets/Resources/ServiceEndpointSettings.asset` at startup. Set **API Base URL** to the deployed ASP.NET service origin (for example, `https://api.example.com`). Optionally set **Realtime WebSocket URL** to an explicit `wss://` endpoint. When the WebSocket field is blank, the client derives `/v1/realtime` from the API URL and maps HTTPS to WSS.

In the Editor and Development builds, blank fields resolve to `http://127.0.0.1:5188` and `ws://127.0.0.1:5188/v1/realtime`. Release builds do not use those defaults; they disable online features with a visible status message unless an HTTPS API and WSS realtime endpoint are configured. Release builds reject HTTP, WS, and loopback hosts.

For development overrides, `ArenaBootstrap.ConfigureRealtimeEndpoint("ws://127.0.0.1:5188/v1/realtime")` persists a WebSocket URL in PlayerPrefs under `bumper-tanks.realtime-endpoint`. Only WS/WSS URLs are accepted; Release builds accept only WSS on a non-loopback host. The endpoint settings contain no secrets. Deploy the server separately with its signing and attestation keys in the server environment.

Configure the asset before creating Android, iOS, Windows, or WebGL release builds. WebGL uses the same HTTPS/WSS rules, so the API and WebSocket services must be reachable from the browser origin and the server must allow the deployed game origin through CORS.

This change adds endpoint configuration only. The Unity WebGL support module is not installed in the current editor environment, and WebGL builds plus live service deployment still need to be completed and verified.
