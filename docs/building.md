# Player builds

The project exposes repeatable Unity command-line build entry points through `BumperTanksBuildEntrypoint`:

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

Development builds use the local service defaults unless a realtime PlayerPrefs override is configured. Release build endpoints are described in [deployment-endpoints.md](deployment-endpoints.md). Player build output is ignored under `Build/` and is not committed.

The WebGL Development player was built successfully with Unity 6000.3.8f1 at `Build/WebGLDevelopment/BumperTanksIO` (108 MiB BuildReport). Serve this directory through an HTTP server to run it in a browser; opening `index.html` directly from disk does not provide the required WebAssembly fetch behavior. Browser and device gameplay QA is still pending.

GitHub Actions workflow `Unity CI` runs Edit Mode and Play Mode tests, then builds WebGL and Windows x64 Development players and uploads the test reports and players as 14-day artifacts. It runs for Unity project changes and can also be started from the Actions tab. Unity jobs are skipped with a workflow summary until both repository settings are configured: variable `UNITY_CI_ENABLED` set to the exact value `true`, and Actions secret `UNITY_LICENSE` containing the activated Unity 6000.3.8f1 license contents. The workflow checks only whether the secret is present and never prints or commits its contents.
