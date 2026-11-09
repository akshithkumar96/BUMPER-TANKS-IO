# Player builds

The project exposes repeatable Unity command-line build entry points through `BumperTanksBuildEntrypoint`:

- `BuildWindowsDevelopment` creates `Build/StandaloneWindows64Development/BumperTanksIO.exe`.
- `BuildWebGlDevelopment` creates `Build/WebGLDevelopment` when the Unity WebGL module is installed.
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
