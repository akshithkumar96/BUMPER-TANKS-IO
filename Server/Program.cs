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
var identities = new PlayerIdentityRepository(builder.Configuration);
var telemetry = new TelemetryRepository(builder.Configuration);
var realtime = new RealtimeMatchHub(identities, profiles);
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

app.MapPost("/v1/identity/anonymous", () =>
{
    var identity = identities.CreateAnonymous();
    return Results.Ok(new AnonymousIdentityResponse(identity.PlayerId, identity.AccessToken));
}).RequireRateLimiting("rewards");

app.MapGet("/v1/profiles/{playerId}", (string playerId, HttpRequest request) =>
{
    if (!TryGetAuthenticatedPlayer(identities, request, out var authenticatedPlayerId)) return Results.Unauthorized();
    if (!string.Equals(playerId, authenticatedPlayerId, StringComparison.Ordinal)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (!RequestValidation.IsPlayerId(playerId)) return Results.BadRequest(new { error = "invalid playerId" });
    return Results.Ok(profiles.GetOrCreate(playerId));
});

app.MapPost("/v1/rewards/daily", (DailyRewardRequest request, HttpRequest httpRequest) =>
{
    if (!TryGetAuthenticatedPlayer(identities, httpRequest, out var authenticatedPlayerId)) return Results.Unauthorized();
    if (!string.Equals(request.PlayerId, authenticatedPlayerId, StringComparison.Ordinal)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (!RequestValidation.IsPlayerId(request.PlayerId)) return Results.BadRequest(new { error = "invalid playerId" });
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    if (!profiles.TryClaimDailyReward(request.PlayerId, today, out var updated, out var reward))
        return Results.Conflict(new { error = "daily reward already claimed" });
    return Results.Ok(new DailyRewardResponse(updated, reward));
}).RequireRateLimiting("rewards");

app.MapPost("/v1/rewards/ad/challenge", (DailyRewardRequest request, HttpRequest httpRequest) =>
{
    if (!TryGetAuthenticatedPlayer(identities, httpRequest, out var authenticatedPlayerId)) return Results.Unauthorized();
    if (request.PlayerId != authenticatedPlayerId) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (!RequestValidation.IsPlayerId(request.PlayerId)) return Results.BadRequest(new { error = "invalid playerId" });
    if (string.IsNullOrWhiteSpace(builder.Configuration["Rewards:LevelPlayPrivateKey"])) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    try
    {
        var challenge = profiles.CreateAdSpinChallenge(request.PlayerId, DateOnly.FromDateTime(DateTime.UtcNow));
        return Results.Ok(new AdSpinChallengeResponse(challenge.ChallengeId, challenge.ExpiresUtc));
    }
    catch (InvalidOperationException) { return Results.Conflict(new { error = "bonus spin already claimed today" }); }
}).RequireRateLimiting("rewards");

app.MapGet("/v1/rewards/ad/status/{challengeId}", (string challengeId, HttpRequest httpRequest) =>
{
    if (!TryGetAuthenticatedPlayer(identities, httpRequest, out var authenticatedPlayerId)) return Results.Unauthorized();
    var challenge = profiles.GetAdSpinChallenge(challengeId, authenticatedPlayerId);
    if (challenge == null) return Results.NotFound();
    var profile = challenge.Completed ? profiles.GetOrCreate(authenticatedPlayerId) : null;
    return Results.Ok(new AdSpinStatusResponse(challenge.Completed, challenge.Award, profile));
}).RequireRateLimiting("rewards");

app.MapGet("/v1/rewards/ad/levelplay-callback", (HttpRequest request) =>
{
    var key = builder.Configuration["Rewards:LevelPlayPrivateKey"];
    if (string.IsNullOrWhiteSpace(key)) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    var query = request.Query;
    string Pick(params string[] names) => names.Select(name => query[name].FirstOrDefault()).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    var userId = Pick("userId", "USER_ID", "appUserId", "applicationUserId", "userid");
    var eventId = Pick("eventId", "EVENT_ID");
    var rewards = Pick("rewards", "REWARDS", "rewardAmount");
    var timestamp = Pick("timestamp", "TIMESTAMP");
    var signature = Pick("signature", "SIGNATURE");
    var challengeId = Pick("challengeId", "custom_challengeId", "customParams", "CUSTOM_PARAMS");
    if (challengeId.StartsWith("challengeId=", StringComparison.Ordinal)) challengeId = challengeId[12..];
    if (!RequestValidation.IsPlayerId(userId) || eventId.Length is 0 or > 128 || !int.TryParse(rewards, out var rewardUnits) || rewardUnits <= 0 || timestamp.Length == 0 || challengeId.Length != 32)
        return Results.BadRequest("invalid callback parameters");
    if (!DateTimeOffset.TryParseExact(timestamp, "yyyyMMddHHmm", System.Globalization.CultureInfo.InvariantCulture,
        System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var callbackAt))
        return Results.BadRequest("invalid timestamp");
    var callbackAge = DateTimeOffset.UtcNow - callbackAt;
    if (callbackAge < TimeSpan.FromMinutes(-5) || callbackAge > TimeSpan.FromDays(8)) return Results.BadRequest("expired callback");
    var expected = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(timestamp + eventId + userId + rewards + key))).ToLowerInvariant();
    if (signature.Length != expected.Length || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(signature.ToLowerInvariant()), System.Text.Encoding.ASCII.GetBytes(expected)))
        return Results.Unauthorized();
    if (!profiles.TryCompleteAdSpin(challengeId, eventId, userId, callbackAt, out _, out _, out _)) return Results.Conflict("invalid or consumed challenge");
    return Results.Text(eventId + ":OK");
}).RequireRateLimiting("rewards");

app.MapGet("/v1/cosmetics", () => Results.Ok(new CosmeticCatalogResponse(CosmeticCatalog.All.ToArray())));

app.MapPost("/v1/cosmetics/purchase", (CosmeticPurchaseRequest request, HttpRequest httpRequest) =>
{
    if (!TryGetAuthenticatedPlayer(identities, httpRequest, out var authenticatedPlayerId)) return Results.Unauthorized();
    if (!string.Equals(request.PlayerId, authenticatedPlayerId, StringComparison.Ordinal)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (!RequestValidation.IsPlayerId(request.PlayerId) || !CosmeticCatalog.TryGet(request.CosmeticId, out _))
        return Results.BadRequest(new { error = "invalid cosmetic purchase" });
    if (!profiles.TryPurchaseCosmetic(request.PlayerId, request.CosmeticId, out var updated, out var error))
        return Results.Conflict(new { error });
    return Results.Ok(new CosmeticPurchaseResponse(updated, request.CosmeticId));
}).RequireRateLimiting("rewards");

app.MapPost("/v1/matches/complete", (MatchCompleteRequest request, HttpRequest httpRequest) =>
{
    if (!TryGetAuthenticatedPlayer(identities, httpRequest, out var authenticatedPlayerId)) return Results.Unauthorized();
    if (!string.Equals(request.PlayerId, authenticatedPlayerId, StringComparison.Ordinal)) return Results.StatusCode(StatusCodes.Status403Forbidden);
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

app.MapPost("/v1/telemetry", (TelemetryRequest request, HttpRequest httpRequest) =>
{
    if (!TryGetAuthenticatedPlayer(identities, httpRequest, out var authenticatedPlayerId)) return Results.Unauthorized();
    if (!string.Equals(request.PlayerId, authenticatedPlayerId, StringComparison.Ordinal)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (!RequestValidation.IsPlayerId(request.PlayerId) || !RequestValidation.IsTelemetryName(request.Name))
        return Results.BadRequest(new { error = "invalid telemetry event" });
    telemetry.Track(new TelemetryEvent(request.Name, request.PlayerId, DateTimeOffset.MinValue));
    return Results.Accepted();
}).RequireRateLimiting("rewards");

app.MapGet("/v1/leaderboard", () => Results.Ok(new LeaderboardResponse(profiles.All.OrderByDescending(profile => profile.BattleCoins).Take(50).ToArray())));
app.Run();

static bool TryGetAuthenticatedPlayer(PlayerIdentityRepository identities, HttpRequest request, out string playerId)
{
    var authorization = request.Headers.Authorization.ToString();
    var token = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? authorization[7..].Trim() : null;
    return identities.TryAuthenticate(token, out playerId);
}

public sealed record PlayerProfile(string PlayerId, int BattleCoins, int MatchesPlayed, DateOnly LastDailyRewardUtc)
{
    public string[] OwnedCosmetics { get; init; } = Array.Empty<string>();
}
public sealed record DailyRewardRequest(string PlayerId);
public sealed record DailyRewardResponse(PlayerProfile Profile, int Award);
public sealed record AdSpinChallengeResponse(string ChallengeId, DateTimeOffset ExpiresUtc);
public sealed record AdSpinStatusResponse(bool Completed, int Award, PlayerProfile? Profile);
public sealed record CosmeticPurchaseRequest(string PlayerId, string CosmeticId);
public sealed record CosmeticPurchaseResponse(PlayerProfile Profile, string CosmeticId);
public sealed record CosmeticCatalogResponse(CosmeticDefinition[] Items);
public sealed record MatchCompleteRequest(string PlayerId, string MatchId, int Gems, int Eliminations);
public sealed record TelemetryRequest(string PlayerId, string Name);
public sealed record LeaderboardResponse(PlayerProfile[] Entries);
public sealed record AnonymousIdentityResponse(string PlayerId, string AccessToken);
