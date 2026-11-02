using System.Collections.Concurrent;
using System.Text.Json;

public sealed class ProfileRepository
{
    private readonly ConcurrentDictionary<string, PlayerProfile> profiles;
    private readonly string path;
    private readonly Lock writeLock = new();

    public ProfileRepository(IConfiguration configuration)
    {
        path = configuration["Storage:ProfilePath"] ?? Path.Combine(AppContext.BaseDirectory, "data", "profiles.json");
        profiles = File.Exists(path)
            ? new ConcurrentDictionary<string, PlayerProfile>(JsonSerializer.Deserialize<Dictionary<string, PlayerProfile>>(File.ReadAllText(path)) ?? [])
            : new ConcurrentDictionary<string, PlayerProfile>();
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

    private void PersistLocked()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(profiles));
        File.Move(temporary, path, true);
    }
}
