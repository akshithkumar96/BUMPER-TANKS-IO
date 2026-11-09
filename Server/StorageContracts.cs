public interface IPlayerIdentityRepository
{
    (string PlayerId, string AccessToken) CreateAnonymous();
    bool TryAuthenticate(string? token, out string playerId);
}

public interface IProfileRepository
{
    PlayerProfile GetOrCreate(string playerId);
    IEnumerable<PlayerProfile> All { get; }
    AdSpinChallenge CreateAdSpinChallenge(string playerId, DateOnly today);
    bool TryCompleteAdSpin(string challengeId, string eventId, string playerId, DateTimeOffset callbackAt,
        out PlayerProfile updated, out int award, out bool duplicate);
    AdSpinChallenge? GetAdSpinChallenge(string challengeId, string playerId);
    void Save(PlayerProfile profile);
    bool TryClaimDailyReward(string playerId, DateOnly today, out PlayerProfile updated, out int reward);
    bool TryCompleteMatch(string playerId, string matchId, int gems, int eliminations, out PlayerProfile updated, out int award);
    bool TryPurchaseCosmetic(string playerId, string cosmeticId, out PlayerProfile updated, out string error);
}

public interface ITelemetryRepository
{
    void Track(TelemetryEvent telemetryEvent);
}

public interface ICrashReportRepository
{
    bool Record(string playerId, string fingerprint, string buildVersion, string platform, DateTimeOffset now);
    IReadOnlyList<CrashReportRecord> Snapshot();
}
