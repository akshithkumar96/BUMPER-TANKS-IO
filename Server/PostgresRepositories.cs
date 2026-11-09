using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

/// <summary>PostgreSQL-backed identity storage. Only token digests are persisted.</summary>
public sealed class PostgresPlayerIdentityRepository(DbContextOptions<GameStorageContext> options) : IPlayerIdentityRepository
{
    public (string PlayerId, string AccessToken) CreateAnonymous()
    {
        var playerId = Guid.NewGuid().ToString("N");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        using var db = new GameStorageContext(options);
        db.IdentityTokens.Add(new IdentityTokenRow { TokenHash = Hash(token), PlayerId = playerId });
        db.SaveChanges();
        return (playerId, token);
    }

    public bool TryAuthenticate(string? token, out string playerId)
    {
        playerId = string.Empty;
        if (string.IsNullOrWhiteSpace(token) || token.Length != 64) return false;
        using var db = new GameStorageContext(options);
        var match = db.IdentityTokens.AsNoTracking().SingleOrDefault(row => row.TokenHash == Hash(token));
        if (match == null) return false;
        playerId = match.PlayerId;
        return true;
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

/// <summary>
/// PostgreSQL implementation. Mutations lock the player's profile row and commit
/// dependent idempotency records in the same transaction, coordinating replicas.
/// </summary>
public sealed class PostgresProfileRepository(DbContextOptions<GameStorageContext> options) : IProfileRepository
{
    public PlayerProfile GetOrCreate(string playerId)
    {
        using var db = new GameStorageContext(options);
        EnsureProfile(db, playerId);
        return ToProfile(db.Profiles.AsNoTracking().Single(row => row.PlayerId == playerId));
    }

    public IEnumerable<PlayerProfile> All
    {
        get
        {
            using var db = new GameStorageContext(options);
            return db.Profiles.AsNoTracking().OrderByDescending(row => row.BattleCoins).ToArray().Select(ToProfile).ToArray();
        }
    }

    public AdSpinChallenge CreateAdSpinChallenge(string playerId, DateOnly today) => InPlayerTransaction(playerId, (db, current) =>
    {
        var now = DateTimeOffset.UtcNow;
        var existing = db.AdSpinChallenges.AsNoTracking().Where(item => item.PlayerId == playerId && item.ExpiresUtc > now && !item.Completed)
            .OrderByDescending(item => item.CreatedUtc).FirstOrDefault();
        if (existing != null) return ToChallenge(existing);
        if (db.AdSpinChallenges.Any(item => item.PlayerId == playerId && item.Day == today && item.Completed))
            throw new InvalidOperationException("bonus spin already claimed today");

        var challenge = new AdSpinChallengeRow
        {
            ChallengeId = Guid.NewGuid().ToString("N"), PlayerId = playerId, Day = today,
            CreatedUtc = now, ExpiresUtc = now.AddDays(9), Completed = false, Award = 0,
            EventId = string.Empty, CallbackTimestamp = string.Empty
        };
        db.AdSpinChallenges.Add(challenge);
        db.SaveChanges();
        return ToChallenge(challenge);
    });

    public bool TryCompleteAdSpin(string challengeId, string eventId, string playerId, DateTimeOffset callbackAt,
        out PlayerProfile updated, out int award, out bool duplicate)
    {
        AdCompletion result;
        try
        {
            result = InPlayerTransaction(playerId, (db, current) =>
            {
                var priorEvent = db.RewardedAdEvents.AsNoTracking().SingleOrDefault(item => item.EventId == eventId);
                var challenge = db.AdSpinChallenges.SingleOrDefault(item => item.ChallengeId == challengeId);
                if (priorEvent != null)
                {
                    if (priorEvent.PlayerId == playerId && priorEvent.ChallengeId == challengeId && challenge is { Completed: true } && challenge.PlayerId == playerId)
                        return new AdCompletion(true, true, ToProfile(current), challenge.Award);
                    return new AdCompletion(false, true, ToProfile(current), 0);
                }
                if (challenge == null || challenge.Completed || challenge.PlayerId != playerId || challenge.ExpiresUtc < DateTimeOffset.UtcNow ||
                    callbackAt < challenge.CreatedUtc.AddMinutes(-1) || callbackAt > challenge.ExpiresUtc)
                    return new AdCompletion(false, false, ToProfile(current), 0);

                var award = new[] { 25, 50, 75, 100 }[Random.Shared.Next(4)];
                current.BattleCoins = (int)Math.Min(int.MaxValue, (long)current.BattleCoins + award);
                challenge.Day = DateOnly.FromDateTime(callbackAt.UtcDateTime);
                challenge.Completed = true;
                challenge.Award = award;
                challenge.EventId = eventId;
                challenge.CallbackTimestamp = callbackAt.ToString("yyyyMMddHHmm");
                db.RewardedAdEvents.Add(new RewardedAdEventRow { EventId = eventId, PlayerId = playerId, ChallengeId = challengeId });
                db.SaveChanges();
                return new AdCompletion(true, false, ToProfile(current), award);
            });
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            updated = GetOrCreate(playerId);
            award = 0;
            duplicate = true;
            return false;
        }
        updated = result.Profile;
        award = result.Award;
        duplicate = result.Duplicate;
        return result.Success;
    }

    public AdSpinChallenge? GetAdSpinChallenge(string challengeId, string playerId)
    {
        using var db = new GameStorageContext(options);
        var challenge = db.AdSpinChallenges.AsNoTracking().SingleOrDefault(item => item.ChallengeId == challengeId && item.PlayerId == playerId);
        return challenge == null ? null : ToChallenge(challenge);
    }

    public void Save(PlayerProfile profile)
    {
        using var db = new GameStorageContext(options);
        db.Database.ExecuteSqlInterpolated($"""
            INSERT INTO profiles (player_id, battle_coins, matches_played, last_daily_reward_utc, owned_cosmetics)
            VALUES ({profile.PlayerId}, {profile.BattleCoins}, {profile.MatchesPlayed}, {profile.LastDailyRewardUtc}, {profile.OwnedCosmetics})
            ON CONFLICT (player_id) DO UPDATE SET battle_coins = EXCLUDED.battle_coins,
                matches_played = EXCLUDED.matches_played, last_daily_reward_utc = EXCLUDED.last_daily_reward_utc,
                owned_cosmetics = EXCLUDED.owned_cosmetics
            """);
    }

    public bool TryClaimDailyReward(string playerId, DateOnly today, out PlayerProfile updated, out int reward)
    {
        var result = InPlayerTransaction(playerId, (db, current) =>
        {
            if (current.LastDailyRewardUtc == today) return new RewardResult(false, ToProfile(current), 0);
            var amount = new[] { 25, 50, 75, 100 }[Random.Shared.Next(4)];
            current.BattleCoins = (int)Math.Min(int.MaxValue, (long)current.BattleCoins + amount);
            current.LastDailyRewardUtc = today;
            db.SaveChanges();
            return new RewardResult(true, ToProfile(current), amount);
        });
        updated = result.Profile;
        reward = result.Award;
        return result.Success;
    }

    public bool TryCompleteMatch(string playerId, string matchId, int gems, int eliminations, out PlayerProfile updated, out int award)
    {
        var result = InPlayerTransaction(playerId, (db, current) =>
        {
            if (db.CompletedMatches.Any(item => item.PlayerId == playerId && item.MatchId == matchId))
                return new RewardResult(false, ToProfile(current), 0);
            var amount = (int)Math.Min(200L, (long)gems + (long)eliminations * 10);
            current.BattleCoins = (int)Math.Min(int.MaxValue, (long)current.BattleCoins + amount);
            current.MatchesPlayed = current.MatchesPlayed == int.MaxValue ? int.MaxValue : current.MatchesPlayed + 1;
            db.CompletedMatches.Add(new CompletedMatchRow { PlayerId = playerId, MatchId = matchId, Award = amount });
            db.SaveChanges();
            return new RewardResult(true, ToProfile(current), amount);
        });
        updated = result.Profile;
        award = result.Award;
        return result.Success;
    }

    public bool TryPurchaseCosmetic(string playerId, string cosmeticId, out PlayerProfile updated, out string error)
    {
        var result = InPlayerTransaction(playerId, (db, current) =>
        {
            if (!CosmeticCatalog.TryGet(cosmeticId, out var item)) return new PurchaseResult(false, ToProfile(current), "unknown cosmetic");
            if (current.OwnedCosmetics.Contains(cosmeticId, StringComparer.Ordinal)) return new PurchaseResult(false, ToProfile(current), "cosmetic already owned");
            if (current.BattleCoins < item.Price) return new PurchaseResult(false, ToProfile(current), "not enough Battle Coins");
            current.BattleCoins -= item.Price;
            current.OwnedCosmetics = current.OwnedCosmetics.Append(cosmeticId).ToArray();
            db.SaveChanges();
            return new PurchaseResult(true, ToProfile(current), string.Empty);
        });
        updated = result.Profile;
        error = result.Error;
        return result.Success;
    }

    private T InPlayerTransaction<T>(string playerId, Func<GameStorageContext, ProfileRow, T> action)
    {
        using var db = new GameStorageContext(options);
        using var transaction = db.Database.BeginTransaction(IsolationLevel.ReadCommitted);
        EnsureProfile(db, playerId);
        var current = db.Profiles.FromSqlInterpolated($"SELECT * FROM profiles WHERE player_id = {playerId} FOR UPDATE").Single();
        var result = action(db, current);
        transaction.Commit();
        return result;
    }

    private static void EnsureProfile(GameStorageContext db, string playerId) => db.Database.ExecuteSqlInterpolated($"""
        INSERT INTO profiles (player_id, battle_coins, matches_played, last_daily_reward_utc, owned_cosmetics)
        VALUES ({playerId}, 0, 0, DATE '0001-01-01', ARRAY[]::text[])
        ON CONFLICT (player_id) DO NOTHING
        """);

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        for (Exception? inner = exception.InnerException; inner != null; inner = inner.InnerException)
            if (inner is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) return true;
        return false;
    }

    private static PlayerProfile ToProfile(ProfileRow row) => new(row.PlayerId, row.BattleCoins, row.MatchesPlayed, row.LastDailyRewardUtc)
    { OwnedCosmetics = row.OwnedCosmetics ?? Array.Empty<string>() };

    private static AdSpinChallenge ToChallenge(AdSpinChallengeRow row) =>
        new(row.ChallengeId, row.PlayerId, row.Day, row.CreatedUtc, row.ExpiresUtc, row.Completed, row.Award, row.EventId, row.CallbackTimestamp);

    private sealed record RewardResult(bool Success, PlayerProfile Profile, int Award);
    private sealed record AdCompletion(bool Success, bool Duplicate, PlayerProfile Profile, int Award);
    private sealed record PurchaseResult(bool Success, PlayerProfile Profile, string Error);
}

public sealed class PostgresTelemetryRepository(DbContextOptions<GameStorageContext> options) : ITelemetryRepository
{
    private const int MaxRetainedEvents = 10000;
    private const long RetentionLockId = 74199201;

    public void Track(TelemetryEvent telemetryEvent)
    {
        using var db = new GameStorageContext(options);
        using var transaction = db.Database.BeginTransaction();
        AcquireTransactionLock(db, RetentionLockId);
        db.TelemetryEvents.Add(new TelemetryEventRow
        {
            Name = telemetryEvent.Name,
            PlayerId = telemetryEvent.PlayerId,
            OccurredUtc = DateTimeOffset.UtcNow
        });
        db.SaveChanges();
        db.Database.ExecuteSqlInterpolated($"""
            DELETE FROM telemetry_events
            WHERE event_id IN (
                SELECT event_id FROM telemetry_events
                ORDER BY occurred_utc DESC, event_id DESC
                OFFSET {MaxRetainedEvents}
            )
            """);
        transaction.Commit();
    }

    internal static void AcquireTransactionLock(GameStorageContext db, long lockId)
    {
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = "SELECT pg_advisory_xact_lock(@lock_id)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "lock_id";
        parameter.Value = lockId;
        command.Parameters.Add(parameter);
        command.ExecuteScalar();
    }
}

public sealed class PostgresCrashReportRepository : ICrashReportRepository
{
    private const long RetentionLockId = 74199202;
    private readonly DbContextOptions<GameStorageContext> options;

    public PostgresCrashReportRepository(DbContextOptions<GameStorageContext> options)
    {
        this.options = options;
        PruneExpired(DateTimeOffset.UtcNow);
    }

    /// <summary>Upserts one player's fingerprint, refreshing its timestamp and count atomically across service instances.</summary>
    public bool Record(string playerId, string fingerprint, string buildVersion, string platform, DateTimeOffset now)
    {
        using var db = new GameStorageContext(options);
        using var transaction = db.Database.BeginTransaction();
        PostgresTelemetryRepository.AcquireTransactionLock(db, RetentionLockId);
        PruneExpired(db, now);

        var existing = db.CrashReports.SingleOrDefault(item => item.PlayerId == playerId && item.Fingerprint == fingerprint);
        var inserted = existing == null;
        if (existing == null)
        {
            db.CrashReports.Add(new CrashReportRow
            {
                PlayerId = playerId,
                Fingerprint = fingerprint,
                BuildVersion = buildVersion,
                Platform = platform,
                FirstSeenUtc = now,
                LastSeenUtc = now,
                Occurrences = 1
            });
        }
        else
        {
            existing.LastSeenUtc = now;
            existing.Occurrences = existing.Occurrences == int.MaxValue ? int.MaxValue : existing.Occurrences + 1;
        }
        db.SaveChanges();
        db.Database.ExecuteSqlInterpolated($"""
            DELETE FROM crash_reports
            WHERE report_id NOT IN (
                SELECT report_id FROM crash_reports ORDER BY report_id DESC LIMIT {CrashReportRepository.MaxRetainedReports}
            )
            """);
        transaction.Commit();
        return inserted;
    }

    public IReadOnlyList<CrashReportRecord> Snapshot()
    {
        using var db = new GameStorageContext(options);
        return db.CrashReports.AsNoTracking().OrderBy(item => item.ReportId)
            .Select(item => new CrashReportRecord(item.PlayerId, item.Fingerprint, item.BuildVersion, item.Platform,
                item.FirstSeenUtc, item.LastSeenUtc, item.Occurrences)).ToArray();
    }

    private void PruneExpired(DateTimeOffset now)
    {
        using var db = new GameStorageContext(options);
        using var transaction = db.Database.BeginTransaction();
        PruneExpired(db, now);
        transaction.Commit();
    }

    private static void PruneExpired(GameStorageContext db, DateTimeOffset now)
    {
        PostgresTelemetryRepository.AcquireTransactionLock(db, RetentionLockId);
        db.Database.ExecuteSqlInterpolated($"DELETE FROM crash_reports WHERE last_seen_utc < {now - CrashReportRepository.Retention}");
    }
}
