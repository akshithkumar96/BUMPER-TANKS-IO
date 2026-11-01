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

        public void SubmitMatch(string playerId, int gems, int eliminations, Action<string> onError = null)
        {
            StartCoroutine(PostJson("/v1/matches/complete", JsonUtility.ToJson(new MatchResult(playerId, gems, eliminations)), onError));
        }

        public void ClaimDailyReward(string playerId, Action onSuccess = null, Action<string> onError = null)
        {
            StartCoroutine(PostJson("/v1/rewards/daily", JsonUtility.ToJson(new DailyReward(playerId)), onSuccess, onError));
        }

        public void Track(string playerId, string eventName)
        {
            StartCoroutine(PostJson("/v1/telemetry", JsonUtility.ToJson(new Telemetry(playerId, eventName)), null));
        }

        private IEnumerator PostJson(string path, string json, Action<string> onError) => PostJson(path, json, null, onError);

        private IEnumerator PostJson(string path, string json, Action onSuccess, Action<string> onError)
        {
            using var request = new UnityWebRequest(baseUrl.TrimEnd('/') + path, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) onError?.Invoke(request.error);
            else onSuccess?.Invoke();
        }

        [Serializable]
        private sealed class MatchResult
        {
            public string PlayerId;
            public int Gems;
            public int Eliminations;
            public MatchResult(string playerId, int gems, int eliminations) => (PlayerId, Gems, Eliminations) = (playerId, gems, eliminations);
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
    }
}
