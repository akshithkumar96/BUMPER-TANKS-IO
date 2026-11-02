using System.Collections.Concurrent;
using System.Text.Json;

public sealed class ProfileRepository
{
    private readonly ConcurrentDictionary<string, PlayerProfile> profiles;
    private readonly HashSet<string> completedMatches = new();
    private readonly string path;
    private readonly Lock writeLock = new();

    public ProfileRepository(IConfiguration configuration)
    {
        path = configuration["Storage:ProfilePath"] ?? Path.Combine(AppContext.BaseDirectory, "data", "profiles.json");
        if (File.Exists(path))
        {
            var snapshot = JsonSerializer.Deserialize<ProfileSnapshot>(File.ReadAllText(path)) ?? new ProfileSnapshot();
            profiles = new ConcurrentDictionary<string, PlayerProfile>(snapshot.Profiles);
            completedMatches.UnionWith(snapshot.CompletedMatches);
        }
        else profiles = new ConcurrentDictionary<string, PlayerProfile>();
    }

    public PlayerProfile GetOrCreate(string playerId) => profiles.GetOrAdd(playerId, static id => new PlayerProfile(id, 0, 0, DateOnly.MinValue));
    public IEnumerable<PlayerProfile> All => profiles.Values;

    public void Save(PlayerProfile profile)
    {
        profiles[profile.PlayerId] = profile;
        lock (writeLock) PersistLocked();
    }

    public bool TryClaimDailyReward(string playerId, DateOnly today, int reward, out PlayerProfile updated)
    {
        lock (writeLock)
        {
            var current = GetOrCreate(playerId);
            if (current.LastDailyRewardUtc == today)
            {
                updated = current;
                return false;
            }
            updated = current with { BattleCoins = current.BattleCoins + reward, LastDailyRewardUtc = today };
            profiles[playerId] = updated;
            PersistLocked();
            return true;
        }
    }

    public bool TryCompleteMatch(string playerId, string matchId, int gems, int eliminations, out PlayerProfile updated, out int award)
    {
        lock (writeLock)
        {
            var current = GetOrCreate(playerId);
            var key = $"{playerId}:{matchId}";
            if (!completedMatches.Add(key))
            {
                updated = current;
                award = 0;
                return false;
            }
            award = Math.Min(200, gems + eliminations * 10);
            updated = current with { BattleCoins = current.BattleCoins + award, MatchesPlayed = current.MatchesPlayed + 1 };
            profiles[playerId] = updated;
            PersistLocked();
            return true;
        }
    }

    private void PersistLocked()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new ProfileSnapshot
        {
            Profiles = profiles.ToDictionary(pair => pair.Key, pair => pair.Value),
            CompletedMatches = completedMatches
        }));
        File.Move(temporary, path, true);
    }
}

public sealed class ProfileSnapshot
{
    public Dictionary<string, PlayerProfile> Profiles { get; set; } = new();
    public HashSet<string> CompletedMatches { get; set; } = new();
}
