using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRateLimiter(options => options.AddFixedWindowLimiter("rewards", limiter =>
{
    limiter.PermitLimit = 10;
    limiter.Window = TimeSpan.FromMinutes(1);
    limiter.QueueLimit = 0;
}));
var allowedOrigins = (builder.Configuration["Cors:AllowedOrigins"] ?? "http://localhost:5173,http://localhost:8080")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));
var app = builder.Build();
var profiles = new ProfileRepository(builder.Configuration);
var telemetry = new TelemetryRepository(builder.Configuration);
app.UseCors();
app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/v1/profiles/{playerId}", (string playerId) =>
{
    return Results.Ok(profiles.GetOrCreate(playerId));
});

app.MapPost("/v1/rewards/daily", (DailyRewardRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.PlayerId)) return Results.BadRequest(new { error = "playerId is required" });
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    if (!profiles.TryClaimDailyReward(request.PlayerId, today, 50, out var updated))
        return Results.Conflict(new { error = "daily reward already claimed" });
    return Results.Ok(updated);
}).RequireRateLimiting("rewards");

app.MapPost("/v1/matches/complete", (MatchCompleteRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.PlayerId) || string.IsNullOrWhiteSpace(request.MatchId) || request.MatchId.Length > 80 || request.Gems < 0 || request.Eliminations < 0)
        return Results.BadRequest(new { error = "invalid match result" });

    if (!profiles.TryCompleteMatch(request.PlayerId, request.MatchId, request.Gems, request.Eliminations, out var updated, out var award))
        return Results.Conflict(new { error = "match result already submitted" });
    return Results.Ok(new { profile = updated, award });
}).RequireRateLimiting("rewards");

app.MapPost("/v1/telemetry", (TelemetryRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.PlayerId) || string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 64)
        return Results.BadRequest(new { error = "invalid telemetry event" });
    telemetry.Track(new TelemetryEvent(request.Name, request.PlayerId, DateTimeOffset.MinValue));
    return Results.Accepted();
}).RequireRateLimiting("rewards");

app.MapGet("/v1/leaderboard", () => Results.Ok(new LeaderboardResponse(profiles.All.OrderByDescending(profile => profile.BattleCoins).Take(50).ToArray())));
app.Run();

public sealed record PlayerProfile(string PlayerId, int BattleCoins, int MatchesPlayed, DateOnly LastDailyRewardUtc);
public sealed record DailyRewardRequest(string PlayerId);
public sealed record MatchCompleteRequest(string PlayerId, string MatchId, int Gems, int Eliminations);
public sealed record TelemetryRequest(string PlayerId, string Name);
public sealed record LeaderboardResponse(PlayerProfile[] Entries);
