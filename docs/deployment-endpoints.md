# Service endpoint configuration

Unity loads `Assets/Resources/ServiceEndpointSettings.asset` at startup. Set **API Base URL** to the deployed ASP.NET service origin (for example, `https://api.example.com`). The WebSocket endpoint is derived as `/v1/realtime`, mapping HTTPS to WSS. Set **Realtime WebSocket URL** only when the realtime service uses a separate host or path.

In the Editor and Development builds, blank asset values resolve to `http://127.0.0.1:5188` and its matching `ws://` endpoint. Development builds also show a **Server URL (Dev)** control on the main menu. It accepts an API base URL, derives the WebSocket endpoint, persists the override on that device, and retries anonymous identity setup against the selected host. Switching hosts clears the backend-scoped identity and token so a new identity is created there; the locally saved player profile and cosmetics remain on the device. Android Development builds allow cleartext HTTP for local testing through a temporary build manifest.

For LAN testing, run the Development server with `ASPNETCORE_URLS=http://0.0.0.0:5188`, then enter its reachable LAN address in the game's development server field, such as `http://192.168.1.20:5188`. The device and development server need network connectivity to each other. HTTP access is for trusted development networks.

Release builds do not use loopback defaults or development overrides. Configure a non-loopback HTTPS API endpoint in `ServiceEndpointSettings.asset`; Unity derives WSS from that URL unless a separate WSS endpoint is set. Release builds disable online features and show a status message when secure service endpoints are missing or invalid. They reject HTTP, WS, and loopback endpoints.

WebGL uses the same HTTPS/WSS requirements. Configure the deployed browser origin in the server's CORS allowlist. Android, iOS, Windows, and WebGL release builds all need reachable production endpoints. Endpoint settings contain no secrets; deploy the server with its signing and match-attestation keys in the server environment.

The Development Android APK and WebGL player have been built locally. The APK is saved under ignored `Build/AndroidDevelopment/`; the WebGL player is saved under ignored `Build/WebGLDevelopment/`. Neither is committed. CI player builds and tests require repository variable `UNITY_CI_ENABLED=true` and the `UNITY_LICENSE` secret. Physical-device QA and a live service deployment remain outstanding.
