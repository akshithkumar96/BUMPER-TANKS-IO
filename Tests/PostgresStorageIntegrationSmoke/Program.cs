using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__GameDatabase");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("Set ConnectionStrings__GameDatabase to run the PostgreSQL integration smoke.");

if (args.Length == 3 && args[0] == "--verify-telemetry")
{
    var verifyOptions = new DbContextOptionsBuilder<GameStorageContext>().UseNpgsql(connectionString).Options;
    using var verifyDb = new GameStorageContext(verifyOptions);
    if (!verifyDb.TelemetryEvents.Any(item => item.PlayerId == args[1] && item.Name == args[2]))
        throw new InvalidOperationException($"No PostgreSQL telemetry event '{args[2]}' was found for player '{args[1]}'.");
    Console.WriteLine($"PostgreSQL telemetry persistence passed for {args[1]} / {args[2]}.");
    return;
}

var values = new Dictionary<string, string?>
{
    ["Storage:Provider"] = "PostgreSQL",
    ["ConnectionStrings:GameDatabase"] = connectionString
};
var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
var environment = new SmokeEnvironment { EnvironmentName = Environments.Production };
using (var lease = RealtimeReplicaLease.Acquire(connectionString))
{
    var duplicateRejected = false;
    try { using var duplicate = RealtimeReplicaLease.Acquire(connectionString); }
    catch (InvalidOperationException exception) when (exception.Message.Contains("Another realtime server instance", StringComparison.Ordinal))
    { duplicateRejected = true; }
    if (!duplicateRejected)
        throw new InvalidOperationException("A second realtime server acquired the single-instance advisory lease.");
}
using (var releasedLease = RealtimeReplicaLease.Acquire(connectionString))
{
    // A fresh process can acquire the lease after the prior connection closes.
}
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

var postgresOptions = new DbContextOptionsBuilder<GameStorageContext>().UseNpgsql(connectionString).Options;
first.Telemetry.Track(new TelemetryEvent("match_started", playerId, DateTimeOffset.MinValue));
using (var db = new GameStorageContext(postgresOptions))
{
    if (!db.TelemetryEvents.Any(item => item.PlayerId == playerId && item.Name == "match_started"))
        throw new InvalidOperationException("A second database view could not observe the telemetry event.");
    var seedBase = DateTimeOffset.UtcNow.AddHours(-1);
    db.TelemetryEvents.AddRange(Enumerable.Range(0, 10001).Select(index => new TelemetryEventRow
    {
        Name = "match_started", PlayerId = playerId, OccurredUtc = seedBase.AddMilliseconds(index)
    }));
    db.SaveChanges();
}
second.Telemetry.Track(new TelemetryEvent("match_started", playerId, DateTimeOffset.MinValue));
using (var db = new GameStorageContext(postgresOptions))
{
    if (db.TelemetryEvents.Count(item => item.PlayerId == playerId) != 10000)
        throw new InvalidOperationException("PostgreSQL telemetry retention did not cap the player's test events at the newest 10,000.");
}

var reportFingerprint = new string('a', 64);
var reportNow = DateTimeOffset.UtcNow;
if (!first.CrashReports.Record(playerId, reportFingerprint, "1.2.3", "Windows", reportNow) ||
    !second.CrashReports.Snapshot().Any(item => item.PlayerId == playerId && item.Fingerprint == reportFingerprint))
    throw new InvalidOperationException("A second crash-report repository could not observe the first instance's report.");

var concurrentCrashFingerprint = new string('b', 64);
var concurrentCrashResults = await ParallelResults(20, index =>
    (index % 2 == 0 ? first.CrashReports : second.CrashReports)
        .Record(playerId, concurrentCrashFingerprint, "1.2.3", "Windows", DateTimeOffset.UtcNow));
var duplicateRecord = first.CrashReports.Snapshot().Single(item => item.PlayerId == playerId && item.Fingerprint == concurrentCrashFingerprint);
if (concurrentCrashResults.Count(inserted => inserted) != 1 || duplicateRecord.Occurrences != 20)
    throw new InvalidOperationException("Concurrent crash report submissions were not deduplicated across repository instances.");

var expiredFingerprint = new string('e', 64);
using (var db = new GameStorageContext(postgresOptions))
{
    var expiredAt = DateTimeOffset.UtcNow - CrashReportRepository.Retention - TimeSpan.FromMinutes(1);
    db.CrashReports.Add(new CrashReportRow
    {
        PlayerId = playerId, Fingerprint = expiredFingerprint, BuildVersion = "old", Platform = "Windows",
        FirstSeenUtc = expiredAt, LastSeenUtc = expiredAt, Occurrences = 1
    });
    db.CrashReports.AddRange(Enumerable.Range(0, 1001).Select(index => new CrashReportRow
    {
        PlayerId = playerId, Fingerprint = index.ToString("x").PadLeft(64, '0'), BuildVersion = "1.2.3", Platform = "Windows",
        FirstSeenUtc = reportNow.AddSeconds(index), LastSeenUtc = reportNow.AddSeconds(index), Occurrences = 1
    }));
    db.SaveChanges();
}
first.CrashReports.Record(playerId, new string('f', 64), "1.2.3", "Windows", DateTimeOffset.UtcNow);
var retainedCrashReports = second.CrashReports.Snapshot().Where(item => item.PlayerId == playerId).ToArray();
if (retainedCrashReports.Length != CrashReportRepository.MaxRetainedReports || retainedCrashReports.Any(item => item.Fingerprint == expiredFingerprint))
    throw new InvalidOperationException("PostgreSQL crash report retention did not enforce the 30-day expiry and 1,000-record cap.");

Console.WriteLine("PostgreSQL integration smoke passed: single realtime replica lease, two-store identity, concurrent rewards/purchases, rewarded callback idempotency, telemetry sharing/retention, and crash report dedupe/retention.");

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
