using BumperTanks.Frontend;
using NUnit.Framework;
using UnityEngine;

namespace BumperTanks.Network.Tests
{
    public sealed class TelemetryConsentSettingsTests
    {
        [SetUp]
        public void SetUp() => PlayerPrefs.DeleteKey(TelemetryConsentSettings.PlayerPrefsKey);

        [TearDown]
        public void TearDown() => PlayerPrefs.DeleteKey(TelemetryConsentSettings.PlayerPrefsKey);

        [Test]
        public void TelemetryIsDisabledUntilThePlayerOptsIn()
        {
            Assert.That(TelemetryConsentSettings.HasChoice, Is.False);
            Assert.That(TelemetryConsentSettings.IsAllowed, Is.False);

            TelemetryConsentSettings.SetAllowed(true);

            Assert.That(TelemetryConsentSettings.HasChoice, Is.True);
            Assert.That(TelemetryConsentSettings.IsAllowed, Is.True);
        }

        [Test]
        public void PlayerCanDeclineAndLaterChangeTheirChoice()
        {
            TelemetryConsentSettings.SetAllowed(false);

            Assert.That(TelemetryConsentSettings.HasChoice, Is.True);
            Assert.That(TelemetryConsentSettings.IsAllowed, Is.False);

            TelemetryConsentSettings.SetAllowed(true);
            Assert.That(TelemetryConsentSettings.IsAllowed, Is.True);
        }
    }
}
