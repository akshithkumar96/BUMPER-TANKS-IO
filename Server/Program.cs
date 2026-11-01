using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
var profiles = new ConcurrentDictionary<string, PlayerProfile>();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/v1/profiles/{playerId}", (string playerId) =>
{
    return Results.Ok(profiles.GetOrAdd(playerId, static id => new PlayerProfile(id, 0, 0, DateOnly.MinValue)));
});

app.MapPost("/v1/rewards/daily", (DailyRewardRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.PlayerId)) return Results.BadRequest(new { error = "playerId is required" });
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var profile = profiles.GetOrAdd(request.PlayerId, static id => new PlayerProfile(id, 0, 0, DateOnly.MinValue));
    if (profile.LastDailyRewardUtc == today) return Results.Conflict(new { error = "daily reward already claimed" });

    var updated = profile with { BattleCoins = profile.BattleCoins + 50, LastDailyRewardUtc = today };
    profiles[request.PlayerId] = updated;
    return Results.Ok(updated);
});

app.MapPost("/v1/matches/complete", (MatchCompleteRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.PlayerId) || request.Gems < 0 || request.Eliminations < 0)
        return Results.BadRequest(new { error = "invalid match result" });

    var profile = profiles.GetOrAdd(request.PlayerId, static id => new PlayerProfile(id, 0, 0, DateOnly.MinValue));
    var award = Math.Min(200, request.Gems + request.Eliminations * 10);
    var updated = profile with { BattleCoins = profile.BattleCoins + award, MatchesPlayed = profile.MatchesPlayed + 1 };
    profiles[request.PlayerId] = updated;
    return Results.Ok(new { profile = updated, award });
});

app.MapGet("/v1/leaderboard", () => Results.Ok(profiles.Values.OrderByDescending(profile => profile.BattleCoins).Take(50)));
app.Run();

public sealed record PlayerProfile(string PlayerId, int BattleCoins, int MatchesPlayed, DateOnly LastDailyRewardUtc);
public sealed record DailyRewardRequest(string PlayerId);
public sealed record MatchCompleteRequest(string PlayerId, int Gems, int Eliminations);
