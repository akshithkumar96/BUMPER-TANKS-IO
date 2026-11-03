using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRateLimiter(options => options.AddFixedWindowLimiter("rewards", limiter =>
{
    limiter.PermitLimit = 60;
    limiter.Window = TimeSpan.FromMinutes(1);
    limiter.QueueLimit = 0;
}));
builder.Services.Configure<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>(options =>
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests);
var allowedOrigins = (builder.Configuration["Cors:AllowedOrigins"] ?? "http://localhost:5173,http://localhost:8080")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));
var app = builder.Build();
var profiles = new ProfileRepository(builder.Configuration);
var telemetry = new TelemetryRepository(builder.Configuration);
var realtime = new RealtimeMatchHub();
var attestationSecret = builder.Configuration["Rewards:MatchAttestationSecret"];
var requireMatchAttestation = bool.TryParse(builder.Configuration["Rewards:RequireMatchAttestation"], out var configuredAttestationRequirement)
    ? configuredAttestationRequirement
    : !app.Environment.IsDevelopment();
app.UseCors();
app.UseRateLimiter();
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(20)
});
app.Lifetime.ApplicationStopping.Register(realtime.Dispose);
app.Map("/v1/realtime", async context =>
{
    var origin = context.Request.Headers.Origin.ToString();
    if (!string.IsNullOrEmpty(origin) && !allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }
    await realtime.HandleAsync(context);
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/v1/profiles/{playerId}", (string playerId) =>
{
    if (!RequestValidation.IsPlayerId(playerId)) return Results.BadRequest(new { error = "invalid playerId" });
    return Results.Ok(profiles.GetOrCreate(playerId));
});

app.MapPost("/v1/rewards/daily", (DailyRewardRequest request) =>
{
    if (!RequestValidation.IsPlayerId(request.PlayerId)) return Results.BadRequest(new { error = "invalid playerId" });
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    if (!profiles.TryClaimDailyReward(request.PlayerId, today, out var updated, out var reward))
        return Results.Conflict(new { error = "daily reward already claimed" });
    return Results.Ok(new DailyRewardResponse(updated, reward));
}).RequireRateLimiting("rewards");

app.MapPost("/v1/matches/complete", (MatchCompleteRequest request, HttpRequest httpRequest) =>
{
    if (!RequestValidation.IsPlayerId(request.PlayerId) || !RequestValidation.IsMatchId(request.MatchId) || request.Gems is < 0 or > 1000 || request.Eliminations is < 0 or > 7)
        return Results.BadRequest(new { error = "invalid match result" });

    if (requireMatchAttestation)
    {
        if (!MatchOutcomeAttestation.IsConfigured(attestationSecret))
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        var signature = httpRequest.Headers["X-Match-Attestation"].FirstOrDefault();
        if (string.IsNullOrEmpty(signature)) return Results.Unauthorized();
        if (!MatchOutcomeAttestation.Verify(attestationSecret, request, signature))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    if (!profiles.TryCompleteMatch(request.PlayerId, request.MatchId, request.Gems, request.Eliminations, out var updated, out var award))
        return Results.Conflict(new { error = "match result already submitted" });
    return Results.Ok(new { profile = updated, award });
}).RequireRateLimiting("rewards");

app.MapPost("/v1/telemetry", (TelemetryRequest request) =>
{
    if (!RequestValidation.IsPlayerId(request.PlayerId) || !RequestValidation.IsTelemetryName(request.Name))
        return Results.BadRequest(new { error = "invalid telemetry event" });
    telemetry.Track(new TelemetryEvent(request.Name, request.PlayerId, DateTimeOffset.MinValue));
    return Results.Accepted();
}).RequireRateLimiting("rewards");

app.MapGet("/v1/leaderboard", () => Results.Ok(new LeaderboardResponse(profiles.All.OrderByDescending(profile => profile.BattleCoins).Take(50).ToArray())));
app.Run();

public sealed record PlayerProfile(string PlayerId, int BattleCoins, int MatchesPlayed, DateOnly LastDailyRewardUtc);
public sealed record DailyRewardRequest(string PlayerId);
public sealed record DailyRewardResponse(PlayerProfile Profile, int Award);
public sealed record MatchCompleteRequest(string PlayerId, string MatchId, int Gems, int Eliminations);
public sealed record TelemetryRequest(string PlayerId, string Name);
public sealed record LeaderboardResponse(PlayerProfile[] Entries);
