# Bumper Tanks IO

Top-down arena brawler for desktop, mobile, and WebGL. The project uses Unity 6, URP, C#, an ASP.NET Core game service, and a Photon Fusion Shared Mode integration boundary.

## Run locally

1. Open the repository with Unity `6000.3.8f1`.
2. Open `Assets/Scenes/Arena.unity` and enter Play Mode.
3. Run the backend in another terminal with `dotnet run --project Server/BumperTanks.Server.csproj`.

The arena starts an eight-tank local match: a human Speedy Bumper, bots, power-ups, gems, phase changes, a final safe zone, and a result/rematch screen. Use WASD or arrows to move, mouse to aim, release click or press Space to fire, E for the archetype ability, and G to open the garage.

On touch devices, drag the left half to steer and release a touch on the right half to fire.

## Backend

The service exposes health, profile, reward, match-completion, and leaderboard endpoints. See [Server/README.md](Server/README.md) for container deployment and persistence configuration. CI builds the service and smoke-tests reward rules on every relevant push.

## Multiplayer

The project includes deterministic session contracts and a Fusion Shared Mode adapter behind the `FUSION_PRESENT` scripting define. Follow [Fusion setup](docs/fusion-setup.md) after importing Photon Fusion 2 and configuring an AppId.

See [the implementation plan](docs/implementation-plan.md) for the delivery roadmap.
