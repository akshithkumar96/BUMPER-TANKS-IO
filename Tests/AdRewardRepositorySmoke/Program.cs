using Microsoft.Extensions.Configuration;

var profilePath = Path.Combine(Path.GetTempPath(), "bumper-ad-reward-smoke", Guid.NewGuid().ToString("N"), "profiles.json");
var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:ProfilePath"] = profilePath })
    .Build();
var repository = new ProfileRepository(configuration);
var playerId = Guid.NewGuid().ToString("N");
var today = DateOnly.FromDateTime(DateTime.UtcNow);
var challengeA = repository.CreateAdSpinChallenge(playerId, today);
var signedEventTime = DateTimeOffset.UtcNow;
if (!repository.TryCompleteAdSpin(challengeA.ChallengeId, "levelplay-event-A", playerId, signedEventTime, out var firstProfile, out var award, out _))
    throw new InvalidOperationException("Initial signed ad event was rejected.");

var challengeB = repository.CreateAdSpinChallenge(playerId, today.AddDays(1));
var mutatedCallbackTime = DateTimeOffset.UtcNow.AddSeconds(1);
if (repository.TryCompleteAdSpin(challengeB.ChallengeId, "levelplay-event-A", playerId, mutatedCallbackTime, out var replayProfile, out _, out _))
    throw new InvalidOperationException("A valid event ID was rebound to a second challenge.");
if (replayProfile.BattleCoins != firstProfile.BattleCoins || replayProfile.BattleCoins != award)
    throw new InvalidOperationException("Rebinding the event changed the player's balance.");

var restartedRepository = new ProfileRepository(configuration);
if (!restartedRepository.TryCompleteAdSpin(challengeA.ChallengeId, "levelplay-event-A", playerId, signedEventTime, out var persistedProfile, out var persistedAward, out _) || persistedAward != award)
    throw new InvalidOperationException("An identical persisted callback was not acknowledged idempotently.");
if (restartedRepository.TryCompleteAdSpin(challengeB.ChallengeId, "levelplay-event-A", playerId, mutatedCallbackTime, out var afterRestart, out _, out _))
    throw new InvalidOperationException("Restart lost the event-to-challenge binding.");
if (afterRestart.BattleCoins != firstProfile.BattleCoins || persistedProfile.BattleCoins != firstProfile.BattleCoins)
    throw new InvalidOperationException("A replay changed the persisted balance.");

var matchId = Guid.NewGuid().ToString("N");
try
{
    repository.CreateMatchBonusChallenge(playerId, matchId);
    throw new InvalidOperationException("A match without a finalized reward was eligible for doubling.");
}
catch (InvalidOperationException exception) when (exception.Message.Contains("not eligible", StringComparison.Ordinal)) { }

if (!repository.TryCompleteMatch(playerId, matchId, gems: 12, eliminations: 1, out var matchProfile, out var matchAward) || matchAward != 22)
    throw new InvalidOperationException("The base match reward was not finalized as expected.");
var matchChallenge = repository.CreateMatchBonusChallenge(playerId, matchId);
if (!matchChallenge.ChallengeId.StartsWith("mb", StringComparison.Ordinal) || matchChallenge.ChallengeId.Length != 32 || matchChallenge.Award != matchAward)
    throw new InvalidOperationException("The match bonus challenge was not bound to the finalized match reward.");
if (repository.CreateMatchBonusChallenge(playerId, matchId).ChallengeId != matchChallenge.ChallengeId)
    throw new InvalidOperationException("Retrying challenge creation created a second match reward offer.");
if (repository.GetMatchBonusChallenge(matchChallenge.ChallengeId, Guid.NewGuid().ToString("N")) != null)
    throw new InvalidOperationException("A different player could inspect the match bonus challenge.");

if (repository.TryCompleteMatchBonus(matchChallenge.ChallengeId, "levelplay-event-A", playerId, DateTimeOffset.UtcNow,
        out var crossRewardProfile, out _, out _) || crossRewardProfile.BattleCoins != matchProfile.BattleCoins)
    throw new InvalidOperationException("A daily-spin LevelPlay event was reused to grant a match bonus.");

var matchEventTime = DateTimeOffset.UtcNow;
if (!repository.TryCompleteMatchBonus(matchChallenge.ChallengeId, "levelplay-match-event-1", playerId, matchEventTime,
        out var doubledProfile, out var doubledAward, out _) || doubledAward != matchAward ||
    doubledProfile.BattleCoins != matchProfile.BattleCoins + matchAward)
    throw new InvalidOperationException("The verified match bonus did not grant exactly one additional match award.");
if (!repository.TryCompleteMatchBonus(matchChallenge.ChallengeId, "levelplay-match-event-1", playerId, matchEventTime,
        out var duplicateMatchProfile, out var duplicateMatchAward, out var duplicateMatchEvent) || !duplicateMatchEvent ||
    duplicateMatchAward != matchAward || duplicateMatchProfile.BattleCoins != doubledProfile.BattleCoins)
    throw new InvalidOperationException("A repeated LevelPlay callback changed the doubled match balance.");
if (repository.TryCompleteMatchBonus(matchChallenge.ChallengeId, "levelplay-match-event-2", playerId, DateTimeOffset.UtcNow,
        out var secondBonusProfile, out _, out _) || secondBonusProfile.BattleCoins != doubledProfile.BattleCoins)
    throw new InvalidOperationException("A second LevelPlay event doubled the same match twice.");
try
{
    repository.CreateMatchBonusChallenge(playerId, matchId);
    throw new InvalidOperationException("A completed match bonus challenge was issued again.");
}
catch (InvalidOperationException exception) when (exception.Message.Contains("already claimed", StringComparison.Ordinal)) { }

var spinAfterMatchBonus = repository.CreateAdSpinChallenge(playerId, today.AddDays(2));
if (spinAfterMatchBonus.ChallengeId == matchChallenge.ChallengeId)
    throw new InvalidOperationException("Match bonus and daily spin challenge identifiers were conflated.");

var restartedWithMatchBonus = new ProfileRepository(configuration);
if (!restartedWithMatchBonus.TryCompleteMatchBonus(matchChallenge.ChallengeId, "levelplay-match-event-1", playerId, matchEventTime,
        out var persistedMatchProfile, out var persistedMatchAward, out var persistedMatchDuplicate) || !persistedMatchDuplicate ||
    persistedMatchAward != matchAward || persistedMatchProfile.BattleCoins != doubledProfile.BattleCoins)
    throw new InvalidOperationException("A persisted match bonus callback was not acknowledged idempotently.");

Console.WriteLine("Daily-spin isolation, match reward binding, one-time doubled payout, and callback replay persistence verified");
