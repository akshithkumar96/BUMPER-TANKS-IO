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

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void SubmitMatch(string playerId, string matchId, int gems, int eliminations, Action<string> onError = null)
        {
            StartCoroutine(PostJsonResponse("/v1/matches/complete", JsonUtility.ToJson(new MatchResult(playerId, matchId, gems, eliminations)), response =>
            {
                var result = JsonUtility.FromJson<MatchCompleteResponse>(response);
                if (result?.profile != null) SetBattleCoins(result.profile.battleCoins);
            }, onError));
        }

        public void ClaimDailyReward(string playerId, Action<int> onSuccess = null, Action<string> onError = null)
        {
            StartCoroutine(PostJsonResponse("/v1/rewards/daily", JsonUtility.ToJson(new DailyReward(playerId)), response =>
            {
                var result = JsonUtility.FromJson<DailyRewardResponse>(response);
                if (result?.profile != null) SetBattleCoins(result.profile.battleCoins);
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

        private IEnumerator PostJson(string path, string json, Action<string> onError) => PostJsonResponse(path, json, null, onError);

        private IEnumerator PostJsonResponse(string path, string json, Action<string> onSuccess, Action<string> onError)
        {
            using var request = new UnityWebRequest(baseUrl.TrimEnd('/') + path, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) onError?.Invoke(request.error);
            else onSuccess?.Invoke(request.downloadHandler.text);
        }

        private static void SetBattleCoins(int battleCoins)
        {
            var profile = ProfileStore.Load();
            profile.BattleCoins = battleCoins;
            ProfileStore.Save(profile);
        }

        private IEnumerator GetJson(string path, Action<string> onSuccess, Action<string> onError)
        {
            using var request = UnityWebRequest.Get(baseUrl.TrimEnd('/') + path);
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) onError?.Invoke(request.error);
            else onSuccess?.Invoke(request.downloadHandler.text);
        }

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
        private sealed class ServerProfile { public int battleCoins; }

        [Serializable]
        public sealed class LeaderboardEntry { public string playerId; public int battleCoins; public int matchesPlayed; }
    }
}
