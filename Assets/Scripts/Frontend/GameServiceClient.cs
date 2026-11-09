using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace BumperTanks.Frontend
{
    public sealed class GameServiceClient : MonoBehaviour
    {
        public static GameServiceClient Instance { get; private set; }
        public bool IdentityReady { get; private set; }
        public string IdentityError { get; private set; }
        public bool EndpointConfigurationValid { get; private set; }
        public string EndpointConfigurationError { get; private set; }
        public string ApiBaseUrl { get; private set; }
        public string RealtimeEndpoint { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            RemoteGameplayTuning.LoadCached();
            var settings = ServiceEndpointSettings.Load();
            var isDevelopmentBuild = Application.isEditor || Debug.isDebugBuild;
            if (ServiceEndpointSettings.TryResolve(settings != null ? settings.ApiBaseUrl : string.Empty,
                settings != null ? settings.RealtimeWebSocketUrl : string.Empty, isDevelopmentBuild,
                out var endpoints, out var endpointError))
            {
                ApiBaseUrl = endpoints.ApiBaseUrl;
                RealtimeEndpoint = endpoints.RealtimeWebSocketUrl;
                EndpointConfigurationValid = true;
            }
            else
            {
                EndpointConfigurationError = endpointError;
                IdentityError = endpointError;
                Debug.LogError(endpointError);
            }
#if UNITY_ANDROID || UNITY_IOS
            if (GetComponent<LevelPlayRewardedSpinService>() == null) gameObject.AddComponent<LevelPlayRewardedSpinService>();
#endif
            if (GetComponent<CrashReporter>() == null) gameObject.AddComponent<CrashReporter>();
            if (EndpointConfigurationValid) StartCoroutine(InitializeIdentity());
        }

        public void ConnectRealtime(BumperTanks.Network.WebSocketRealtimeSession session, string url, string displayName, string archetype, Action<string> onError = null)
        {
            StartCoroutine(ConnectRealtimeWhenReady(session, url, displayName, archetype, onError));
        }

        private IEnumerator ConnectRealtimeWhenReady(BumperTanks.Network.WebSocketRealtimeSession session, string url, string displayName, string archetype, Action<string> onError)
        {
            if (!EndpointConfigurationValid)
            {
                var error = "Online matchmaking unavailable: " + EndpointConfigurationError;
                Debug.LogWarning(error);
                onError?.Invoke(error);
                yield break;
            }
            while (!IdentityReady && string.IsNullOrEmpty(IdentityError)) yield return null;
            if (!IdentityReady)
            {
                var error = "Realtime identity unavailable: " + IdentityError;
                Debug.LogWarning(error);
                onError?.Invoke(error);
                yield break;
            }
            yield return FetchGameplayTuning();
            session.Connect(url, ProfileStore.GetServerPlayerId(), displayName, archetype, ProfileStore.GetAccessToken());
        }

        private IEnumerator InitializeIdentity()
        {
            if (!EndpointConfigurationValid) yield break;
            var existingId = ProfileStore.GetServerPlayerId();
            var existingToken = ProfileStore.GetAccessToken();
            if (!string.IsNullOrEmpty(existingId) && !string.IsNullOrEmpty(existingToken))
            {
                yield return FetchGameplayTuning();
                IdentityReady = true;
                yield break;
            }

            using var request = new UnityWebRequest(ApiBaseUrl + "/v1/identity/anonymous", UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(Array.Empty<byte>());
            request.downloadHandler = new DownloadHandlerBuffer();
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                IdentityError = request.error;
                yield break;
            }
            var identity = JsonUtility.FromJson<AnonymousIdentity>(request.downloadHandler.text);
            if (identity == null || string.IsNullOrEmpty(identity.playerId) || string.IsNullOrEmpty(identity.accessToken))
            {
                IdentityError = "Invalid identity response.";
                yield break;
            }
            ProfileStore.SaveServerIdentity(identity.playerId, identity.accessToken);
            yield return FetchGameplayTuning();
            IdentityReady = true;
        }

        private IEnumerator FetchGameplayTuning()
        {
            if (!EndpointConfigurationValid) yield break;
            using var request = UnityWebRequest.Get(ApiBaseUrl + "/v1/gameplay/tuning");
            var previousEtag = RemoteGameplayTuning.ETag;
            if (!string.IsNullOrEmpty(previousEtag)) request.SetRequestHeader("If-None-Match", previousEtag);
            yield return request.SendWebRequest();
            if (request.responseCode == 304)
            {
                RemoteGameplayTuning.UseCachedAfterNotModified();
                yield break;
            }
            if (request.result == UnityWebRequest.Result.Success)
                RemoteGameplayTuning.TryApplyResponse(request.downloadHandler.text, request.GetResponseHeader("ETag"));
        }

        public void SubmitMatch(string playerId, string matchId, int gems, int eliminations, Action<string> onError = null)
        {
            StartCoroutine(PostJsonResponse("/v1/matches/complete", JsonUtility.ToJson(new MatchResult(playerId, matchId, gems, eliminations)), response =>
            {
                var result = JsonUtility.FromJson<MatchCompleteResponse>(response);
                if (result?.profile != null) SetServerProfile(result.profile);
            }, onError));
        }

        public void ClaimDailyReward(string playerId, Action<int> onSuccess = null, Action<string> onError = null)
        {
            StartCoroutine(PostJsonResponse("/v1/rewards/daily", JsonUtility.ToJson(new DailyReward(playerId)), response =>
            {
                var result = JsonUtility.FromJson<DailyRewardResponse>(response);
                if (result?.profile != null) SetServerProfile(result.profile);
                onSuccess?.Invoke(result != null ? result.award : 0);
            }, onError));
        }

        public bool CanWatchAdBonusSpin => EndpointConfigurationValid && LevelPlayRewardedSpinService.IsConfigured;

        public void WatchAdBonusSpin(Action<int> onSuccess, Action<string> onError)
        {
            StartCoroutine(CreateAdSpinChallenge(challenge => LevelPlayRewardedSpinService.Instance.Show(challenge, onSuccess, onError), onError));
        }

        private IEnumerator CreateAdSpinChallenge(Action<string> onSuccess, Action<string> onError)
        {
            var requestBody = JsonUtility.ToJson(new DailyReward(ProfileStore.GetServerPlayerId()));
            yield return PostJsonResponse("/v1/rewards/ad/challenge", requestBody, response =>
            {
                var challenge = JsonUtility.FromJson<AdSpinChallengeResponse>(response);
                if (challenge == null || string.IsNullOrEmpty(challenge.challengeId)) onError?.Invoke("Invalid ad challenge response.");
                else onSuccess?.Invoke(challenge.challengeId);
            }, onError);
        }

        public void PollAdSpinStatus(string challengeId, Action<int> onComplete, Action<string> onError = null)
        {
            StartCoroutine(PollAdSpin(challengeId, onComplete, onError));
        }

        private IEnumerator PollAdSpin(string challengeId, Action<int> onComplete, Action<string> onError)
        {
            var deadline = Time.realtimeSinceStartup + 90f;
            while (Time.realtimeSinceStartup < deadline)
            {
                var done = false;
                yield return GetJson("/v1/rewards/ad/status/" + challengeId, response =>
                {
                    var status = JsonUtility.FromJson<AdSpinStatusResponse>(response);
                    if (status != null && status.completed)
                    {
                        if (status.profile != null) SetServerProfile(status.profile);
                        onComplete?.Invoke(status.award);
                        done = true;
                    }
                }, error => { onError?.Invoke(error); done = true; });
                if (done) yield break;
                yield return new WaitForSecondsRealtime(2f);
            }
            onError?.Invoke("Reward verification is still pending. Reopen the garage to check again.");
        }

        public void Track(string playerId, string eventName)
        {
            if (!TelemetryConsentSettings.IsAllowed) return;
            StartCoroutine(PostJson("/v1/telemetry", JsonUtility.ToJson(new Telemetry(playerId, eventName)), null));
        }

        public void SubmitCrashReport(string fingerprint, string buildVersion, string platform)
        {
            if (!TelemetryConsentSettings.IsAllowed || !EndpointConfigurationValid || !IdentityReady) return;
            StartCoroutine(PostCrashReport(fingerprint, buildVersion, platform));
        }

        private IEnumerator PostCrashReport(string fingerprint, string buildVersion, string platform)
        {
            // Consent may be revoked after the Unity log callback queues this coroutine.
            if (!TelemetryConsentSettings.IsAllowed || !IdentityReady) yield break;
            var body = new CrashReport(ProfileStore.GetServerPlayerId(), fingerprint, buildVersion, platform);
            yield return PostJsonResponse("/v1/crash-reports", JsonUtility.ToJson(body), null, null);
        }

        public void FetchLeaderboard(Action<LeaderboardEntry[]> onComplete, Action<string> onError = null)
        {
            StartCoroutine(GetJson("/v1/leaderboard", json => onComplete?.Invoke(JsonUtility.FromJson<LeaderboardPayload>(json)?.entries ?? Array.Empty<LeaderboardEntry>()), onError));
        }

        public void FetchCosmeticCatalog(Action<CosmeticEntry[]> onComplete, Action<string> onError = null)
        {
            StartCoroutine(GetJson("/v1/cosmetics", json =>
                onComplete?.Invoke(JsonUtility.FromJson<CosmeticCatalogPayload>(json)?.items ?? Array.Empty<CosmeticEntry>()), onError));
        }

        public void PurchaseCosmetic(string playerId, string cosmeticId, Action<ServerProfile> onSuccess = null, Action<string> onError = null)
        {
            StartCoroutine(PostJsonResponse("/v1/cosmetics/purchase", JsonUtility.ToJson(new CosmeticPurchase(playerId, cosmeticId)), response =>
            {
                var result = JsonUtility.FromJson<CosmeticPurchaseResponse>(response);
                if (result?.profile != null)
                {
                    SetServerProfile(result.profile);
                    onSuccess?.Invoke(result.profile);
                }
                else onError?.Invoke("Invalid cosmetic purchase response.");
            }, onError));
        }

        private IEnumerator PostJson(string path, string json, Action<string> onError) => PostJsonResponse(path, json, null, onError);

        private IEnumerator PostJsonResponse(string path, string json, Action<string> onSuccess, Action<string> onError)
        {
            if (!EndpointConfigurationValid) { onError?.Invoke(EndpointConfigurationError); yield break; }
            while (!IdentityReady && string.IsNullOrEmpty(IdentityError)) yield return null;
            if (!IdentityReady) { onError?.Invoke("Identity unavailable: " + IdentityError); yield break; }
            json = json.Replace(ProfileStore.GetOrCreatePlayerId(), ProfileStore.GetServerPlayerId());
            using var request = new UnityWebRequest(ApiBaseUrl + path, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + ProfileStore.GetAccessToken());
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) onError?.Invoke(request.error);
            else onSuccess?.Invoke(request.downloadHandler.text);
        }

        private static void SetServerProfile(ServerProfile serverProfile)
        {
            var profile = ProfileStore.Load();
            profile.BattleCoins = serverProfile.battleCoins;
            if (serverProfile.ownedCosmetics != null) profile.OwnedCosmetics = serverProfile.ownedCosmetics;
            ProfileStore.Save(profile);
        }

        public void ApplyServerProfile(int battleCoins, string[] ownedCosmetics)
        {
            var profile = ProfileStore.Load();
            profile.BattleCoins = battleCoins;
            if (ownedCosmetics != null) profile.OwnedCosmetics = ownedCosmetics;
            ProfileStore.Save(profile);
        }

        private IEnumerator GetJson(string path, Action<string> onSuccess, Action<string> onError)
        {
            if (!EndpointConfigurationValid) { onError?.Invoke(EndpointConfigurationError); yield break; }
            using var request = UnityWebRequest.Get(ApiBaseUrl + path);
            if (!string.IsNullOrEmpty(ProfileStore.GetAccessToken())) request.SetRequestHeader("Authorization", "Bearer " + ProfileStore.GetAccessToken());
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) onError?.Invoke(request.error);
            else onSuccess?.Invoke(request.downloadHandler.text);
        }

        [Serializable]
        private sealed class AnonymousIdentity { public string playerId; public string accessToken; }

        [Serializable]
        private sealed class MatchResult
        {
            public string PlayerId;
            public string MatchId;
            public int Gems;
            public int Eliminations;
            public MatchResult(string playerId, string matchId, int gems, int eliminations) => (PlayerId, MatchId, Gems, Eliminations) = (playerId, matchId, gems, eliminations);
        }

        [Serializable]
        private sealed class DailyReward
        {
            public string PlayerId;
            public DailyReward(string playerId) => PlayerId = playerId;
        }

        [Serializable]
        private sealed class Telemetry
        {
            public string PlayerId;
            public string Name;
            public Telemetry(string playerId, string name) => (PlayerId, Name) = (playerId, name);
        }

        [Serializable]
        private sealed class CrashReport
        {
            public string PlayerId;
            public string Fingerprint;
            public string BuildVersion;
            public string Platform;
            public CrashReport(string playerId, string fingerprint, string buildVersion, string platform) =>
                (PlayerId, Fingerprint, BuildVersion, Platform) = (playerId, fingerprint, buildVersion, platform);
        }

        [Serializable]
        private sealed class LeaderboardPayload { public LeaderboardEntry[] entries; }

        [Serializable]
        private sealed class MatchCompleteResponse { public ServerProfile profile; }

        [Serializable]
        private sealed class DailyRewardResponse { public ServerProfile profile; public int award; }

        [Serializable] private sealed class AdSpinChallengeResponse { public string challengeId; }
        [Serializable] private sealed class AdSpinStatusResponse { public bool completed; public int award; public ServerProfile profile; }

        [Serializable]
        public sealed class ServerProfile { public int battleCoins; public string[] ownedCosmetics; }
        [Serializable]
        public sealed class CosmeticEntry { public string id; public string category; public int price; }
        [Serializable]
        private sealed class CosmeticPurchase { public string PlayerId; public string CosmeticId; public CosmeticPurchase(string playerId, string cosmeticId) => (PlayerId, CosmeticId) = (playerId, cosmeticId); }
        [Serializable]
        private sealed class CosmeticCatalogPayload { public CosmeticEntry[] items; }
        [Serializable]
        private sealed class CosmeticPurchaseResponse { public ServerProfile profile; public string cosmeticId; }

        [Serializable]
        public sealed class LeaderboardEntry { public string playerId; public int battleCoins; public int matchesPlayed; }
    }
}
