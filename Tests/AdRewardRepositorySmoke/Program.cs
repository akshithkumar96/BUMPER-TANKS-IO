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

Console.WriteLine("Reward event binding and replay persistence verified");
