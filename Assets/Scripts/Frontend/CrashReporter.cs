using System.Collections.Generic;
using UnityEngine;

namespace BumperTanks.Frontend
{
    /// <summary>Opt-in crash reporter. Unity's exception log callback is the only captured log type.</summary>
    public sealed class CrashReporter : MonoBehaviour
    {
        private const int MaxSessionFingerprints = 32;
        private readonly HashSet<string> submittedThisSession = new HashSet<string>();

        private void OnEnable() => Application.logMessageReceived += OnLogMessage;
        private void OnDisable() => Application.logMessageReceived -= OnLogMessage;

        private void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            if (!ShouldCapture(type, TelemetryConsentSettings.IsAllowed)) return;
            var fingerprint = CrashFingerprint.Create(condition, stackTrace);
            if (submittedThisSession.Contains(fingerprint) || submittedThisSession.Count >= MaxSessionFingerprints) return;
            submittedThisSession.Add(fingerprint);
            var client = GameServiceClient.Instance;
            if (client != null) client.SubmitCrashReport(fingerprint, Application.version, Application.platform.ToString());
        }

        public static bool ShouldCapture(LogType type, bool consentAllowed) => type == LogType.Exception && consentAllowed;
    }
}
