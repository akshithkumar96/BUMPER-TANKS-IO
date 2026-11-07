using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace BumperTanks.Frontend
{
    public sealed class GameServiceClient : MonoBehaviour
    {
        [SerializeField] private string baseUrl = "http://127.0.0.1:5188";

        public static GameServiceClient Instance { get; private set; }
        public bool IdentityReady { get; private set; }
        public string IdentityError { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            StartCoroutine(InitializeIdentity());
        }

        public void ConnectRealtime(BumperTanks.Network.WebSocketRealtimeSession session, string url, string displayName, string archetype)
        {
            StartCoroutine(ConnectRealtimeWhenReady(session, url, displayName, archetype));
        }

        private IEnumerator ConnectRealtimeWhenReady(BumperTanks.Network.WebSocketRealtimeSession session, string url, string displayName, string archetype)
        {
            while (!IdentityReady && string.IsNullOrEmpty(IdentityError)) yield return null;
            if (!IdentityReady) { Debug.LogWarning("Realtime identity unavailable: " + IdentityError); yield break; }
            session.Connect(url, ProfileStore.GetServerPlayerId(), displayName, archetype, ProfileStore.GetAccessToken());
        }

        private IEnumerator InitializeIdentity()
        {
            var existingId = ProfileStore.GetServerPlayerId();
            var existingToken = ProfileStore.GetAccessToken();
            if (!string.IsNullOrEmpty(existingId) && !string.IsNullOrEmpty(existingToken))
            {
                IdentityReady = true;
                yield break;
            }

            using var request = new UnityWebRequest(baseUrl.TrimEnd('/') + "/v1/identity/anonymous", UnityWebRequest.kHttpVerbPOST);
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
            IdentityReady = true;
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

        public void Track(string playerId, string eventName)
        {
            StartCoroutine(PostJson("/v1/telemetry", JsonUtility.ToJson(new Telemetry(playerId, eventName)), null));
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
            while (!IdentityReady && string.IsNullOrEmpty(IdentityError)) yield return null;
            if (!IdentityReady) { onError?.Invoke("Identity unavailable: " + IdentityError); yield break; }
            json = json.Replace(ProfileStore.GetOrCreatePlayerId(), ProfileStore.GetServerPlayerId());
            using var request = new UnityWebRequest(baseUrl.TrimEnd('/') + path, UnityWebRequest.kHttpVerbPOST);
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

        private IEnumerator GetJson(string path, Action<string> onSuccess, Action<string> onError)
        {
            using var request = UnityWebRequest.Get(baseUrl.TrimEnd('/') + path);
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
        private sealed class LeaderboardPayload { public LeaderboardEntry[] entries; }

        [Serializable]
        private sealed class MatchCompleteResponse { public ServerProfile profile; }

        [Serializable]
        private sealed class DailyRewardResponse { public ServerProfile profile; public int award; }

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
