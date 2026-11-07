using UnityEngine;

namespace BumperTanks.Frontend
{
    /// <summary>Local opt-in preference controlling whether optional gameplay telemetry is sent.</summary>
    public static class TelemetryConsentSettings
    {
        public const string PlayerPrefsKey = "bumper-tanks.telemetry-consent.v1";
        private const int Undecided = 0;
        private const int Allowed = 1;
        private const int Declined = 2;

        public static bool HasChoice => PlayerPrefs.GetInt(PlayerPrefsKey, Undecided) != Undecided;
        public static bool IsAllowed => PlayerPrefs.GetInt(PlayerPrefsKey, Undecided) == Allowed;

        public static void SetAllowed(bool allowed)
        {
            PlayerPrefs.SetInt(PlayerPrefsKey, allowed ? Allowed : Declined);
            PlayerPrefs.Save();
        }
    }
}
