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
        private const string PlayerIdKey = "bumper-tanks.player-id.v1";

        public static string GetOrCreatePlayerId()
        {
            var playerId = PlayerPrefs.GetString(PlayerIdKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(playerId)) return playerId;
            playerId = Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString(PlayerIdKey, playerId);
            PlayerPrefs.Save();
            return playerId;
        }

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
