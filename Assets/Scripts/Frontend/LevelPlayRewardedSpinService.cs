using System;
using System.Collections;
using UnityEngine;

#if UNITY_ANDROID || UNITY_IOS
using Unity.Services.LevelPlay;
#endif

namespace BumperTanks.Frontend
{
    [CreateAssetMenu(menuName = "Bumper Tanks/LevelPlay Settings", fileName = "LevelPlaySettings")]
    public sealed class LevelPlaySettings : ScriptableObject
    {
        public string androidAppKey;
        public string androidRewardedAdUnitId;
        public string iosAppKey;
        public string iosRewardedAdUnitId;
        public bool gdprConsent;
        public bool ccpaDoNotSell = true;

        public bool Configured
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return !string.IsNullOrWhiteSpace(androidAppKey) && !string.IsNullOrWhiteSpace(androidRewardedAdUnitId);
#elif UNITY_IOS && !UNITY_EDITOR
                return !string.IsNullOrWhiteSpace(iosAppKey) && !string.IsNullOrWhiteSpace(iosRewardedAdUnitId);
#else
                return false;
#endif
            }
        }
    }

    public sealed class LevelPlayRewardedSpinService : MonoBehaviour
    {
        public static LevelPlayRewardedSpinService Instance { get; private set; }
        public static bool IsConfigured => Resources.Load<LevelPlaySettings>("LevelPlaySettings")?.Configured == true;
        private LevelPlaySettings settings;
#if UNITY_ANDROID || UNITY_IOS
        private LevelPlayRewardedAd rewardedAd;
#endif

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            settings = Resources.Load<LevelPlaySettings>("LevelPlaySettings");
            if (settings == null || !settings.Configured) return;
#if UNITY_ANDROID || UNITY_IOS
            // Privacy choices are applied before SDK initialization. Defaults protect users until configured otherwise.
            LevelPlayPrivacySettings.SetGDPRConsent(settings.gdprConsent);
            LevelPlayPrivacySettings.SetCCPA(settings.ccpaDoNotSell);
            LevelPlay.OnInitSuccess += OnInitialized;
#endif
        }

        private IEnumerator Start()
        {
#if UNITY_ANDROID || UNITY_IOS
            if (settings == null || !settings.Configured) yield break;
            var client = GameServiceClient.Instance;
            while (client != null && !client.IdentityReady && string.IsNullOrEmpty(client.IdentityError)) yield return null;
            if (client == null || !client.IdentityReady) yield break;
            var key = Application.platform == RuntimePlatform.Android ? settings.androidAppKey : settings.iosAppKey;
            LevelPlay.Init(key, ProfileStore.GetServerPlayerId());
#else
            yield break;
#endif
        }

        public void Show(string challengeId, Action<int> onReward, Action<string> onError)
        {
#if UNITY_ANDROID || UNITY_IOS
            if (rewardedAd == null || !rewardedAd.IsAdReady()) { onError?.Invoke("Rewarded video is not ready. Try again shortly."); return; }
            LevelPlay.SetMetaData("LevelPlay_Rewarded_Server_Params", new[] { "challengeId=" + challengeId });
            rewardHandler = onReward;
            errorHandler = onError;
            currentChallengeId = challengeId;
            rewardEventReceived = false;
            rewardedAd.ShowAd();
#else
            onError?.Invoke("Rewarded ads are only available on Android and iOS.");
#endif
        }

#if UNITY_ANDROID || UNITY_IOS
        private Action<int> rewardHandler;
        private Action<string> errorHandler;
        private bool rewardEventReceived;
        private void OnInitialized(LevelPlayConfiguration _)
        {
            var unitId = Application.platform == RuntimePlatform.Android ? settings.androidRewardedAdUnitId : settings.iosRewardedAdUnitId;
            rewardedAd = new LevelPlayRewardedAd(unitId);
            rewardedAd.OnAdLoaded += _ => { };
            rewardedAd.OnAdLoadFailed += _ => { };
            rewardedAd.OnAdRewarded += OnAdRewarded;
            rewardedAd.OnAdClosed += OnAdClosed;
            rewardedAd.LoadAd();
        }
        private void OnAdRewarded(LevelPlayAdInfo _, LevelPlayReward __)
        {
            // The device never grants coins. Only the signed LevelPlay S2S callback can complete this challenge.
            rewardEventReceived = true;
            GameServiceClient.Instance?.PollAdSpinStatus(currentChallengeId, rewardHandler, errorHandler);
        }
        private string currentChallengeId;
        private void OnAdClosed(LevelPlayAdInfo _)
        {
            if (!rewardEventReceived) errorHandler?.Invoke("Ad closed before a reward was confirmed.");
            if (rewardedAd != null) rewardedAd.LoadAd();
        }
#endif

        private void OnDestroy()
        {
#if UNITY_ANDROID || UNITY_IOS
            LevelPlay.OnInitSuccess -= OnInitialized;
            if (rewardedAd != null)
            {
                rewardedAd.OnAdRewarded -= OnAdRewarded;
                rewardedAd.OnAdClosed -= OnAdClosed;
            }
#endif
            if (Instance == this) Instance = null;
        }
    }
}
