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

    public bool TryClaimDailyReward(string playerId, DateOnly today, out PlayerProfile updated, out int reward)
    {
        lock (writeLock)
        {
            var current = GetOrCreate(playerId);
            if (current.LastDailyRewardUtc == today)
            {
                updated = current;
                reward = 0;
                return false;
            }
            var rewards = new[] { 25, 50, 75, 100 };
            reward = rewards[Random.Shared.Next(rewards.Length)];
            updated = current with { BattleCoins = (int)Math.Min(int.MaxValue, (long)current.BattleCoins + reward), LastDailyRewardUtc = today };
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
            award = (int)Math.Min(200L, (long)gems + (long)eliminations * 10);
            updated = current with
            {
                BattleCoins = (int)Math.Min(int.MaxValue, (long)current.BattleCoins + award),
                MatchesPlayed = current.MatchesPlayed == int.MaxValue ? int.MaxValue : current.MatchesPlayed + 1
            };
            profiles[playerId] = updated;
            PersistLocked();
            return true;
        }
    }

    public bool TryPurchaseCosmetic(string playerId, string cosmeticId, out PlayerProfile updated, out string error)
    {
        lock (writeLock)
        {
            var current = GetOrCreate(playerId);
            if (!CosmeticCatalog.TryGet(cosmeticId, out var item))
            {
                updated = current;
                error = "unknown cosmetic";
                return false;
            }
            if (current.OwnedCosmetics.Contains(cosmeticId, StringComparer.Ordinal))
            {
                updated = current;
                error = "cosmetic already owned";
                return false;
            }
            if (current.BattleCoins < item.Price)
            {
                updated = current;
                error = "not enough Battle Coins";
                return false;
            }

            updated = current with
            {
                BattleCoins = current.BattleCoins - item.Price,
                OwnedCosmetics = current.OwnedCosmetics.Append(cosmeticId).ToArray()
            };
            profiles[playerId] = updated;
            PersistLocked();
            error = string.Empty;
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
