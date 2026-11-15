using System.Collections.Concurrent;
using System.Text.Json;

public sealed class ProfileRepository : IProfileRepository
{
    private readonly ConcurrentDictionary<string, PlayerProfile> profiles;
    private readonly HashSet<string> completedMatches = new();
    private readonly Dictionary<string, int> completedMatchAwards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AdSpinChallenge> adSpinChallenges = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MatchBonusChallenge> matchBonusChallenges = new(StringComparer.Ordinal);
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
            foreach (var (key, award) in snapshot.CompletedMatchAwards ?? new()) completedMatchAwards[key] = award;
            foreach (var challenge in snapshot.AdSpinChallenges ?? new()) adSpinChallenges[challenge.ChallengeId] = challenge;
            foreach (var challenge in snapshot.MatchBonusChallenges ?? new()) matchBonusChallenges[challenge.ChallengeId] = challenge;
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
            if (duplicate && binding == new RewardedAdEvent(playerId, challengeId, "daily_spin") && challenge is { Completed: true } && challenge.PlayerId == playerId)
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

    public MatchBonusChallenge CreateMatchBonusChallenge(string playerId, string matchId)
    {
        lock (writeLock)
        {
            var matchKey = MatchKey(playerId, matchId);
            if (!completedMatchAwards.TryGetValue(matchKey, out var matchAward) || matchAward <= 0)
                throw new InvalidOperationException("match reward is not eligible for doubling");
            var current = GetOrCreate(playerId);
            if ((long)current.BattleCoins + matchAward > int.MaxValue)
                throw new InvalidOperationException("battle coin balance cannot accept the bonus");

            var existing = matchBonusChallenges.Values.FirstOrDefault(item => item.PlayerId == playerId && item.MatchId == matchId);
            if (existing is { Completed: true }) throw new InvalidOperationException("match reward bonus already claimed");
            var now = DateTimeOffset.UtcNow;
            if (existing is { ExpiresUtc: var expires } && expires > now) return existing;

            var challenge = existing == null
                ? NewMatchBonusChallenge(playerId, matchId, matchAward, now)
                : existing with { CreatedUtc = now, ExpiresUtc = now.AddDays(7) };
            if (existing == null) matchBonusChallenges[challenge.ChallengeId] = challenge;
            else matchBonusChallenges[existing.ChallengeId] = challenge;
            try { PersistLocked(); }
            catch
            {
                if (existing == null) matchBonusChallenges.Remove(challenge.ChallengeId);
                else matchBonusChallenges[existing.ChallengeId] = existing;
                throw;
            }
            return challenge;
        }
    }

    public bool TryCompleteMatchBonus(string challengeId, string eventId, string playerId, DateTimeOffset callbackAt,
        out PlayerProfile updated, out int award, out bool duplicate)
    {
        lock (writeLock)
        {
            duplicate = rewardedAdEvents.TryGetValue(eventId, out var binding);
            var challenge = matchBonusChallenges.GetValueOrDefault(challengeId);
            var hasCurrentProfile = profiles.TryGetValue(playerId, out var current);
            current ??= new PlayerProfile(playerId, 0, 0, DateOnly.MinValue);
            if (duplicate && binding == new RewardedAdEvent(playerId, challengeId, "match_bonus") &&
                challenge is { Completed: true } && challenge.PlayerId == playerId)
            {
                updated = current;
                award = challenge.Award;
                return true;
            }
            if (duplicate)
            {
                updated = current;
                award = 0;
                return false;
            }

            updated = current;
            award = 0;
            if (challenge == null || challenge.Completed || challenge.PlayerId != playerId ||
                challenge.ExpiresUtc < DateTimeOffset.UtcNow || callbackAt < challenge.CreatedUtc.AddMinutes(-1) ||
                callbackAt > challenge.ExpiresUtc ||
                !completedMatchAwards.TryGetValue(MatchKey(playerId, challenge.MatchId), out var matchAward) ||
                matchAward != challenge.Award || (long)current.BattleCoins + challenge.Award > int.MaxValue)
                return false;

            award = challenge.Award;
            updated = current with { BattleCoins = current.BattleCoins + award };
            var previousProfile = hasCurrentProfile ? current : null;
            var completed = challenge with { Completed = true, EventId = eventId, Timestamp = callbackAt.ToString("yyyyMMddHHmm") };
            profiles[playerId] = updated;
            rewardedAdEvents[eventId] = new RewardedAdEvent(playerId, challengeId, "match_bonus");
            matchBonusChallenges[challengeId] = completed;
            try { PersistLocked(); }
            catch
            {
                if (previousProfile == null) profiles.TryRemove(playerId, out _); else profiles[playerId] = previousProfile;
                rewardedAdEvents.Remove(eventId);
                matchBonusChallenges[challengeId] = challenge;
                throw;
            }
            return true;
        }
    }

    public MatchBonusChallenge? GetMatchBonusChallenge(string challengeId, string playerId)
    {
        lock (writeLock)
            return matchBonusChallenges.TryGetValue(challengeId, out var challenge) && challenge.PlayerId == playerId
                ? challenge : null;
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
            var hadProfile = profiles.TryGetValue(playerId, out var previousProfile);
            var current = GetOrCreate(playerId);
            var key = $"{playerId}:{matchId}";
            if (!completedMatches.Add(key))
            {
                updated = current;
                award = 0;
                return false;
            }
            award = (int)Math.Min(200L, (long)gems + (long)eliminations * 10);
            completedMatchAwards[key] = award;
            updated = current with
            {
                BattleCoins = (int)Math.Min(int.MaxValue, (long)current.BattleCoins + award),
                MatchesPlayed = current.MatchesPlayed == int.MaxValue ? int.MaxValue : current.MatchesPlayed + 1
            };
            profiles[playerId] = updated;
            try
            {
                PersistLocked();
            }
            catch
            {
                if (hadProfile) profiles[playerId] = previousProfile!;
                else profiles.TryRemove(playerId, out _);
                completedMatches.Remove(key);
                completedMatchAwards.Remove(key);
                throw;
            }
            return true;
        }
    }

    public CompletedMatchResult? GetCompletedMatch(string playerId, string matchId)
    {
        lock (writeLock)
        {
            var key = MatchKey(playerId, matchId);
            if (!completedMatches.Contains(key) || !profiles.TryGetValue(playerId, out var profile)) return null;
            completedMatchAwards.TryGetValue(key, out var award);
            return new CompletedMatchResult(profile, award);
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
            CompletedMatchAwards = completedMatchAwards,
            AdSpinChallenges = adSpinChallenges.Values.ToList(),
            MatchBonusChallenges = matchBonusChallenges.Values.ToList(),
            RewardedAdEvents = rewardedAdEvents
        }));
        File.Move(temporary, path, true);
    }

    private static string MatchKey(string playerId, string matchId) => $"{playerId}:{matchId}";

    private static MatchBonusChallenge NewMatchBonusChallenge(string playerId, string matchId, int award, DateTimeOffset now) =>
        new("mb" + Guid.NewGuid().ToString("N")[..30], playerId, matchId, now, now.AddDays(7), false, award, string.Empty, string.Empty);
}

public sealed class ProfileSnapshot
{
    public Dictionary<string, PlayerProfile> Profiles { get; set; } = new();
    public HashSet<string> CompletedMatches { get; set; } = new();
    public Dictionary<string, int> CompletedMatchAwards { get; set; } = new(StringComparer.Ordinal);
    public List<AdSpinChallenge>? AdSpinChallenges { get; set; } = new();
    public List<MatchBonusChallenge>? MatchBonusChallenges { get; set; } = new();
    public Dictionary<string, RewardedAdEvent>? RewardedAdEvents { get; set; } = new(StringComparer.Ordinal);
}

public sealed record AdSpinChallenge(string ChallengeId, string PlayerId, DateOnly Day, DateTimeOffset CreatedUtc, DateTimeOffset ExpiresUtc, bool Completed, int Award, string EventId, string Timestamp);
public sealed record MatchBonusChallenge(string ChallengeId, string PlayerId, string MatchId, DateTimeOffset CreatedUtc, DateTimeOffset ExpiresUtc, bool Completed, int Award, string EventId, string Timestamp);
public sealed record RewardedAdEvent(string PlayerId, string ChallengeId, string ChallengeType = "daily_spin");
