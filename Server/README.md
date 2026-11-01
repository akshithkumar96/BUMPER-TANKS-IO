# Bumper Tanks Game Service

Run locally with `dotnet run --project Server/BumperTanks.Server.csproj`.

Endpoints: `GET /health`, `GET /v1/profiles/{playerId}`, `POST /v1/rewards/daily`, `POST /v1/matches/complete`, and `GET /v1/leaderboard`.

This service uses in-memory storage for local development. Production deployment must replace it with authenticated persistent storage and verify match outcomes from the multiplayer authority before awarding currency.

## Container deployment

Build with `docker build -t bumper-tanks-service Server`. Run with `docker run -p 8080:8080 -e Cors__AllowedOrigins=https://your-game.example bumper-tanks-service`.
