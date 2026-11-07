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
        public string FlagId = "none";
        public string BumpSoundId = "impact";
        public string[] OwnedCosmetics = Array.Empty<string>();
        public int DailySpinDay = -1;
    }

    public static class ProfileStore
    {
        private const string ProfileKey = "bumper-tanks.profile.v1";
        private const string PlayerIdKey = "bumper-tanks.player-id.v1";
        private const string ServerPlayerIdKey = "bumper-tanks.server-player-id.v1";
        private const string AccessTokenKey = "bumper-tanks.access-token.v1";

        public static string GetOrCreatePlayerId()
        {
            var playerId = PlayerPrefs.GetString(PlayerIdKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(playerId)) return playerId;
            playerId = Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString(PlayerIdKey, playerId);
            PlayerPrefs.Save();
            return playerId;
        }

        public static string GetServerPlayerId() => PlayerPrefs.GetString(ServerPlayerIdKey, string.Empty);
        public static string GetAccessToken() => PlayerPrefs.GetString(AccessTokenKey, string.Empty);

        public static void SaveServerIdentity(string playerId, string accessToken)
        {
            PlayerPrefs.SetString(ServerPlayerIdKey, playerId ?? string.Empty);
            PlayerPrefs.SetString(AccessTokenKey, accessToken ?? string.Empty);
            PlayerPrefs.Save();
        }

        public static PlayerProfile Load()
        {
            var stored = PlayerPrefs.GetString(ProfileKey, string.Empty);
            var profile = string.IsNullOrWhiteSpace(stored) ? new PlayerProfile() : JsonUtility.FromJson<PlayerProfile>(stored) ?? new PlayerProfile();
            profile.OwnedCosmetics ??= Array.Empty<string>();
            return profile;
        }

        public static void Save(PlayerProfile profile)
        {
            PlayerPrefs.SetString(ProfileKey, JsonUtility.ToJson(profile));
            PlayerPrefs.Save();
        }
    }
}
