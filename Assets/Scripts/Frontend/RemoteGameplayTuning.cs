using System;
using BumperTanks.Core;
using UnityEngine;

namespace BumperTanks.Frontend
{
    [Serializable]
    public sealed class RemoteGameplayTuningResponse
    {
        public int revision;
        public float speedMultiplier;
        public float projectileCooldownMultiplier;
        public float knockbackMultiplier;
    }

    /// <summary>Applies server tuning and keeps a seven-day offline fallback.</summary>
    public static class RemoteGameplayTuning
    {
        private const string Prefix = "bumper-tanks.gameplay-tuning.";
        private const long CacheLifetimeTicks = TimeSpan.TicksPerDay * 7;
        private static bool initialized;
        private static string etag = string.Empty;
        public static string ETag { get { EnsureInitialized(); return etag; } }

        public static void LoadCached()
        {
            EnsureInitialized();
        }

        public static bool TryApplyResponse(string json, string responseEtag)
        {
            if (!TryParseResponse(json, out var response) ||
                !GameplayTuning.TryApply(response.speedMultiplier, response.projectileCooldownMultiplier, response.knockbackMultiplier)) return false;

            etag = string.IsNullOrWhiteSpace(responseEtag) ? string.Empty : responseEtag;
            PlayerPrefs.SetString(Prefix + "etag", etag);
            PlayerPrefs.SetFloat(Prefix + "speed", response.speedMultiplier);
            PlayerPrefs.SetFloat(Prefix + "cooldown", response.projectileCooldownMultiplier);
            PlayerPrefs.SetFloat(Prefix + "knockback", response.knockbackMultiplier);
            PlayerPrefs.SetString(Prefix + "savedUtcTicks", DateTime.UtcNow.Ticks.ToString());
            PlayerPrefs.Save();
            initialized = true;
            return true;
        }

        public static bool TryParseResponse(string json, out RemoteGameplayTuningResponse response)
        {
            response = null;
            try { response = JsonUtility.FromJson<RemoteGameplayTuningResponse>(json); }
            catch (ArgumentException) { return false; }
            return response != null && response.revision == GameplayTuningSettingsRevision &&
                   GameplayTuning.IsValid(response.speedMultiplier, response.projectileCooldownMultiplier, response.knockbackMultiplier);
        }

        public static void UseCachedAfterNotModified()
        {
            EnsureInitialized();
        }

        public static bool IsCacheFresh(long savedUtcTicks, long nowUtcTicks)
        {
            return savedUtcTicks > 0 && nowUtcTicks >= savedUtcTicks && nowUtcTicks - savedUtcTicks <= CacheLifetimeTicks;
        }

        public static bool IsCachedTuningUsable(long savedUtcTicks, long nowUtcTicks,
            float speedMultiplier, float projectileCooldownMultiplier, float knockbackMultiplier)
        {
            return IsCacheFresh(savedUtcTicks, nowUtcTicks) &&
                   GameplayTuning.IsValid(speedMultiplier, projectileCooldownMultiplier, knockbackMultiplier);
        }

        private const int GameplayTuningSettingsRevision = 1;

        private static void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;
            GameplayTuning.Reset();
            var nowTicks = DateTime.UtcNow.Ticks;
            if (!long.TryParse(PlayerPrefs.GetString(Prefix + "savedUtcTicks", string.Empty), out var savedTicks) ||
                !IsCacheFresh(savedTicks, nowTicks))
            {
                etag = string.Empty;
                return;
            }

            var speed = PlayerPrefs.GetFloat(Prefix + "speed", GameplayTuning.DefaultSpeedMultiplier);
            var cooldown = PlayerPrefs.GetFloat(Prefix + "cooldown", GameplayTuning.DefaultProjectileCooldownMultiplier);
            var knockback = PlayerPrefs.GetFloat(Prefix + "knockback", GameplayTuning.DefaultKnockbackMultiplier);
            if (!IsCachedTuningUsable(savedTicks, nowTicks, speed, cooldown, knockback) ||
                !GameplayTuning.TryApply(speed, cooldown, knockback))
            {
                etag = string.Empty;
                return;
            }
            etag = PlayerPrefs.GetString(Prefix + "etag", string.Empty);
        }
    }
}
