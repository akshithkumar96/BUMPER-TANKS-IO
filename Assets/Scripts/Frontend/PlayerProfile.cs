using System;
using UnityEngine;

namespace BumperTanks.Frontend
{
    [Serializable]
    public sealed class PlayerProfile
    {
        public int BattleCoins;
        public string ColorId = "sky";
        public string TrailId = "none";
        public int DailySpinDay = -1;
    }

    public static class ProfileStore
    {
        private const string ProfileKey = "bumper-tanks.profile.v1";

        public static PlayerProfile Load()
        {
            var stored = PlayerPrefs.GetString(ProfileKey, string.Empty);
            return string.IsNullOrWhiteSpace(stored) ? new PlayerProfile() : JsonUtility.FromJson<PlayerProfile>(stored) ?? new PlayerProfile();
        }

        public static void Save(PlayerProfile profile)
        {
            PlayerPrefs.SetString(ProfileKey, JsonUtility.ToJson(profile));
            PlayerPrefs.Save();
        }
    }
}
