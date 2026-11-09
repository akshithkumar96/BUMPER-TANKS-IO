using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__GameDatabase");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("Set ConnectionStrings__GameDatabase to run the PostgreSQL integration smoke.");

var values = new Dictionary<string, string?>
{
    ["Storage:Provider"] = "PostgreSQL",
    ["ConnectionStrings:GameDatabase"] = connectionString
};
var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
var environment = new SmokeEnvironment { EnvironmentName = Environments.Production };
var first = GameStorageFactory.Create(configuration, environment);
var second = GameStorageFactory.Create(configuration, environment);

var identity = first.Identities.CreateAnonymous();
if (!second.Identities.TryAuthenticate(identity.AccessToken, out var authenticated) || authenticated != identity.PlayerId)
    throw new InvalidOperationException("A second repository instance could not authenticate a persisted identity.");

var playerId = identity.PlayerId;
var initial = new PlayerProfile(playerId, 500, 0, DateOnly.MinValue);
first.Profiles.Save(initial);
var today = DateOnly.FromDateTime(DateTime.UtcNow);
var dailyClaims = await ParallelResults(20, index =>
{
    var store = index % 2 == 0 ? first.Profiles : second.Profiles;
    return store.TryClaimDailyReward(playerId, today, out _, out _);
});
if (dailyClaims.Count(success => success) != 1)
    throw new InvalidOperationException($"Expected one daily reward claim across repository instances; got {dailyClaims.Count(success => success)}.");

var matchId = Guid.NewGuid().ToString("N");
var matchClaims = await ParallelResults(20, index =>
{
    var store = index % 2 == 0 ? first.Profiles : second.Profiles;
    return store.TryCompleteMatch(playerId, matchId, 7, 1, out _, out _);
});
if (matchClaims.Count(success => success) != 1)
    throw new InvalidOperationException($"Expected one idempotent match award; got {matchClaims.Count(success => success)}.");

var purchases = await ParallelResults(20, index =>
{
    var store = index % 2 == 0 ? first.Profiles : second.Profiles;
    return store.TryPurchaseCosmetic(playerId, "flag_checkered", out _, out _);
});
if (purchases.Count(success => success) != 1)
    throw new InvalidOperationException($"Expected one cosmetic purchase; got {purchases.Count(success => success)}.");

var challenge = first.Profiles.CreateAdSpinChallenge(playerId, today);
var callbackResults = new ConcurrentBag<(bool Success, bool Duplicate, int Award)>();
await Parallel.ForEachAsync(Enumerable.Range(0, 20), async (index, cancellationToken) =>
{
    var store = index % 2 == 0 ? first.Profiles : second.Profiles;
    var success = store.TryCompleteAdSpin(challenge.ChallengeId, "event-" + Guid.NewGuid().ToString("N"), playerId,
        DateTimeOffset.UtcNow, out _, out var award, out var duplicate);
    callbackResults.Add((success, duplicate, award));
    await Task.CompletedTask;
});
var acceptedCallbacks = callbackResults.Where(result => result.Success).ToArray();
if (acceptedCallbacks.Length != 1 || acceptedCallbacks[0].Award is not (25 or 50 or 75 or 100))
    throw new InvalidOperationException($"Expected one rewarded callback for a challenge; accepted {acceptedCallbacks.Length}.");

var finalProfile = second.Profiles.GetOrCreate(playerId);
if (finalProfile.MatchesPlayed != 1 || !finalProfile.OwnedCosmetics.Contains("flag_checkered", StringComparer.Ordinal))
    throw new InvalidOperationException("Concurrent database transactions lost profile state.");
var dailyAward = finalProfile.BattleCoins - 500 - acceptedCallbacks[0].Award + 20 - 17;
if (dailyAward is not (25 or 50 or 75 or 100))
    throw new InvalidOperationException("Daily and match awards were not persisted atomically.");

var secondIdentity = second.Identities.CreateAnonymous();
var secondChallenge = second.Profiles.CreateAdSpinChallenge(secondIdentity.PlayerId, today);
var firstCollisionChallenge = first.Profiles.CreateAdSpinChallenge(playerId, today.AddDays(1));
var sharedProviderEvent = "provider-collision-" + Guid.NewGuid().ToString("N");
var collisionResults = await Task.WhenAll(
    Task.Run(() => first.Profiles.TryCompleteAdSpin(firstCollisionChallenge.ChallengeId, sharedProviderEvent, playerId,
        DateTimeOffset.UtcNow, out _, out _, out _)),
    Task.Run(() => second.Profiles.TryCompleteAdSpin(secondChallenge.ChallengeId, sharedProviderEvent, secondIdentity.PlayerId,
        DateTimeOffset.UtcNow, out _, out _, out _)));
if (collisionResults.Count(success => success) != 1)
    throw new InvalidOperationException("A provider event ID collision across players was not rejected exactly once.");

Console.WriteLine("PostgreSQL integration smoke passed: two-store identity, concurrent daily reward, match idempotency, cosmetic purchase, and rewarded callback.");

static async Task<bool[]> ParallelResults(int count, Func<int, bool> action)
{
    var results = new ConcurrentBag<bool>();
    await Parallel.ForEachAsync(Enumerable.Range(0, count), async (index, cancellationToken) =>
    {
        results.Add(action(index));
        await Task.CompletedTask;
    });
    return results.ToArray();
}

sealed class SmokeEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Production;
    public string ApplicationName { get; set; } = "PostgresStorageIntegrationSmoke";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
}
