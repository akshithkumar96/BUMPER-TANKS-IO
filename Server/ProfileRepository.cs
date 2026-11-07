using System.Collections.Concurrent;
using System.Text.Json;

public sealed class ProfileRepository
{
    private readonly ConcurrentDictionary<string, PlayerProfile> profiles;
    private readonly HashSet<string> completedMatches = new();
    private readonly Dictionary<string, AdSpinChallenge> adSpinChallenges = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RewardedAdEvent> rewardedAdEvents = new(StringComparer.Ordinal);
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
            foreach (var challenge in snapshot.AdSpinChallenges ?? new()) adSpinChallenges[challenge.ChallengeId] = challenge;
            foreach (var (eventId, binding) in snapshot.RewardedAdEvents ?? new()) rewardedAdEvents[eventId] = binding;
        }
        else profiles = new ConcurrentDictionary<string, PlayerProfile>();
    }

    public PlayerProfile GetOrCreate(string playerId) => profiles.GetOrAdd(playerId, static id => new PlayerProfile(id, 0, 0, DateOnly.MinValue));
    public IEnumerable<PlayerProfile> All => profiles.Values;

    public AdSpinChallenge CreateAdSpinChallenge(string playerId, DateOnly today)
    {
        lock (writeLock)
        {
            var existing = adSpinChallenges.Values.FirstOrDefault(item => item.PlayerId == playerId && item.ExpiresUtc > DateTimeOffset.UtcNow && !item.Completed);
            if (existing != null) return existing;
            if (adSpinChallenges.Values.Any(item => item.PlayerId == playerId && item.Day == today && item.Completed))
                throw new InvalidOperationException("bonus spin already claimed today");
            var createdUtc = DateTimeOffset.UtcNow;
            var challenge = new AdSpinChallenge(Guid.NewGuid().ToString("N"), playerId, today, createdUtc, createdUtc.AddDays(9), false, 0, string.Empty, string.Empty);
            adSpinChallenges[challenge.ChallengeId] = challenge;
            try { PersistLocked(); }
            catch { adSpinChallenges.Remove(challenge.ChallengeId); throw; }
            return challenge;
        }
    }

    public bool TryCompleteAdSpin(string challengeId, string eventId, string playerId, DateTimeOffset callbackAt, out PlayerProfile updated, out int award, out bool duplicate)
    {
        lock (writeLock)
        {
            duplicate = rewardedAdEvents.TryGetValue(eventId, out var binding);
            var challenge = adSpinChallenges.GetValueOrDefault(challengeId);
            var hasCurrentProfile = profiles.TryGetValue(playerId, out var current);
            current ??= new PlayerProfile(playerId, 0, 0, DateOnly.MinValue);
            if (duplicate && binding == new RewardedAdEvent(playerId, challengeId) && challenge is { Completed: true } && challenge.PlayerId == playerId)
            {
                updated = current; award = challenge.Award; return true;
            }
            if (duplicate) { updated = current; award = 0; return false; }
            updated = current; award = 0;
            if (challenge == null || challenge.Completed || challenge.PlayerId != playerId || challenge.ExpiresUtc < DateTimeOffset.UtcNow || callbackAt < challenge.CreatedUtc.AddMinutes(-1) || callbackAt > challenge.ExpiresUtc) return false;
            award = new[] { 25, 50, 75, 100 }[Random.Shared.Next(4)];
            updated = updated with { BattleCoins = (int)Math.Min(int.MaxValue, (long)updated.BattleCoins + award) };
            var previousProfile = hasCurrentProfile ? current : null;
            var completed = challenge with { Day = DateOnly.FromDateTime(callbackAt.UtcDateTime), Completed = true, Award = award, EventId = eventId, Timestamp = callbackAt.ToString("yyyyMMddHHmm") };
            profiles[playerId] = updated;
            rewardedAdEvents[eventId] = new RewardedAdEvent(playerId, challengeId);
            adSpinChallenges[challengeId] = completed;
            try { PersistLocked(); }
            catch
            {
                if (previousProfile == null) profiles.TryRemove(playerId, out _); else profiles[playerId] = previousProfile;
                rewardedAdEvents.Remove(eventId);
                adSpinChallenges[challengeId] = challenge;
                throw;
            }
            return true;
        }
    }

    public AdSpinChallenge? GetAdSpinChallenge(string challengeId, string playerId)
    {
        lock (writeLock) return adSpinChallenges.TryGetValue(challengeId, out var challenge) && challenge.PlayerId == playerId ? challenge : null;
    }

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
            CompletedMatches = completedMatches,
            AdSpinChallenges = adSpinChallenges.Values.ToList(),
            RewardedAdEvents = rewardedAdEvents
        }));
        File.Move(temporary, path, true);
    }
}

public sealed class ProfileSnapshot
{
    public Dictionary<string, PlayerProfile> Profiles { get; set; } = new();
    public HashSet<string> CompletedMatches { get; set; } = new();
    public List<AdSpinChallenge>? AdSpinChallenges { get; set; } = new();
    public Dictionary<string, RewardedAdEvent>? RewardedAdEvents { get; set; } = new(StringComparer.Ordinal);
}

public sealed record AdSpinChallenge(string ChallengeId, string PlayerId, DateOnly Day, DateTimeOffset CreatedUtc, DateTimeOffset ExpiresUtc, bool Completed, int Award, string EventId, string Timestamp);
public sealed record RewardedAdEvent(string PlayerId, string ChallengeId);
