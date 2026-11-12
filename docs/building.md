# Player builds

The project exposes repeatable Unity command-line build entry points through `BumperTanksBuildEntrypoint`:

- `BuildAndroidDevelopment` creates `Build/AndroidDevelopment/BumperTanksIO.apk` as a 32-bit ARM Development player with Mono2x. It temporarily sets these Android build settings and restores the project's previous backend and architecture after the build. For this Development APK only, it temporarily merges Android's cleartext-traffic flag so an HTTP/WS backend on a trusted local network can be used; the project manifest is restored after the build.
- `BuildWindowsDevelopment` creates `Build/StandaloneWindows64Development/BumperTanksIO.exe`.
- `BuildWebGlDevelopment` creates the browser player at `Build/WebGLDevelopment/BumperTanksIO/index.html` when the Unity WebGL module is installed.
- `BuildWindowsRelease` and `BuildWebGlRelease` require valid HTTPS/WSS values in `Assets/Resources/ServiceEndpointSettings.asset` before building.

Run a Windows development build from the repository root:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe' `
  -batchmode -nographics -projectPath (Get-Location).Path `
  -executeMethod BumperTanks.Editor.BumperTanksBuildEntrypoint.BuildWindowsDevelopment `
  -quit `
  -logFile Temp\windows-development-build.log
```

Run an Android Development APK build from the repository root (install the Android Build Support module, including SDK & NDK Tools and OpenJDK, for this Unity editor):

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe' `
  -batchmode -nographics -projectPath (Get-Location).Path `
  -executeMethod BumperTanks.Editor.BumperTanksBuildEntrypoint.BuildAndroidDevelopment `
  -quit `
  -logFile Temp\android-development-build.log
```

In the Unity Editor and Development builds, the main menu exposes **Server URL (Dev)**. Enter the API base URL reachable from the device, such as `http://192.168.1.20:5188`; the matching `/v1/realtime` WebSocket URL is derived automatically. To let an Android device reach a backend on the development PC, run the server bound to the LAN interface and allow its port through the PC firewall on the private network:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://0.0.0.0:5188'
dotnet run --project Server/BumperTanks.Server.csproj
```

The device and PC must be on a network that permits peer connections. Saving a different API host creates a new anonymous server identity there; the local player ID, profile, coins, and cosmetics remain stored locally. Development HTTP access is intended only for trusted local testing. Release builds hide the control, reject the override, and keep the normal HTTPS/WSS endpoint requirements. Release build endpoints are described in [deployment-endpoints.md](deployment-endpoints.md). Player build output is ignored under `Build/` and is not committed.

The Android Development APK is written to `Build/AndroidDevelopment/BumperTanksIO.apk`; a verified Unity 6000.3.8f1 build was 66.9 MiB (70,212,318 bytes). Android native dependencies are resolved through the External Dependency Manager package. The WebGL Development player was built successfully with Unity 6000.3.8f1 at `Build/WebGLDevelopment/BumperTanksIO` (108 MiB BuildReport). Serve this directory through an HTTP server to run it in a browser; opening `index.html` directly from disk does not provide the required WebAssembly fetch behavior. Browser and device gameplay QA is still pending.

GitHub Actions workflow `Unity CI` runs Edit Mode and Play Mode tests, then builds WebGL and Windows x64 Development players and uploads the test reports and players as 14-day artifacts. It runs for Unity project changes and can also be started from the Actions tab. Unity jobs are skipped with a workflow summary until both repository settings are configured: variable `UNITY_CI_ENABLED` set to the exact value `true`, and Actions secret `UNITY_LICENSE` containing the activated Unity 6000.3.8f1 license contents. The workflow checks only whether the secret is present and never prints or commits its contents.
