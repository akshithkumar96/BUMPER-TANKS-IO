using BumperTanks.Frontend;
using NUnit.Framework;

namespace BumperTanks.Tests
{
    public sealed class ServiceEndpointSettingsTests
    {
        [Test]
        public void DevelopmentBuildDefaultsToLocalApiAndDerivesWebSocketUrl()
        {
            var valid = ServiceEndpointSettings.TryResolve(string.Empty, string.Empty, true, out var endpoints, out var error);

            Assert.That(valid, Is.True, error);
            Assert.That(endpoints.ApiBaseUrl, Is.EqualTo("http://127.0.0.1:5188"));
            Assert.That(endpoints.RealtimeWebSocketUrl, Is.EqualTo("ws://127.0.0.1:5188/v1/realtime"));
        }

        [Test]
        public void ReleaseBuildRequiresHttpsAndDerivesWssEndpoint()
        {
            var valid = ServiceEndpointSettings.TryResolve("https://api.example.com", string.Empty, false, out var endpoints, out var error);

            Assert.That(valid, Is.True, error);
            Assert.That(endpoints.ApiBaseUrl, Is.EqualTo("https://api.example.com"));
            Assert.That(endpoints.RealtimeWebSocketUrl, Is.EqualTo("wss://api.example.com/v1/realtime"));
        }

        [Test]
        public void ResourcesContainsEndpointConfigurationAsset()
        {
            var settings = ServiceEndpointSettings.Load();

            Assert.That(settings, Is.Not.Null);
            Assert.That(settings.ApiBaseUrl, Is.Empty);
            Assert.That(settings.RealtimeWebSocketUrl, Is.Empty);
        }

        [TestCase("", "", "release builds must reject missing URLs")]
        [TestCase("http://api.example.com", "", "release builds must reject plain HTTP")]
        [TestCase("https://api.example.com", "ws://api.example.com/v1/realtime", "release builds must reject plain WS")]
        [TestCase("https://127.0.0.1:8443", "", "release builds must reject loopback hosts")]
        public void ReleaseBuildRejectsMissingOrInsecureEndpoints(string api, string realtime, string assertion)
        {
            var valid = ServiceEndpointSettings.TryResolve(api, realtime, false, out _, out var error);

            Assert.That(valid, Is.False, assertion);
            Assert.That(error, Is.Not.Empty);
        }

        [Test]
        public void DerivedWebSocketUrlPreservesApiPathAndCustomPort()
        {
            var valid = ServiceEndpointSettings.TryResolve("http://dev.example.test:8081/game/", string.Empty, true, out var endpoints, out var error);

            Assert.That(valid, Is.True, error);
            Assert.That(endpoints.RealtimeWebSocketUrl, Is.EqualTo("ws://dev.example.test:8081/game/v1/realtime"));
        }

        [Test]
        public void RealtimeOverrideRejectsInsecureReleaseUrl()
        {
            var valid = ServiceEndpointSettings.TryValidateRealtimeOverride("ws://api.example.com/v1/realtime", false, out _, out var error);

            Assert.That(valid, Is.False);
            Assert.That(error, Does.Contain("wss://"));
        }

        [Test]
        public void DevelopmentApiOverrideNormalizesUrlAndDerivesRealtimeEndpoint()
        {
            var valid = ServiceEndpointSettings.TryValidateDevelopmentApiOverride(
                " http://192.168.1.20:5188/game/ ", true, out var endpoints, out var error);

            Assert.That(valid, Is.True, error);
            Assert.That(endpoints.ApiBaseUrl, Is.EqualTo("http://192.168.1.20:5188/game"));
            Assert.That(endpoints.RealtimeWebSocketUrl, Is.EqualTo("ws://192.168.1.20:5188/game/v1/realtime"));
        }

        [TestCase("")]
        [TestCase("ftp://192.168.1.20:5188")]
        [TestCase("http://user:pass@192.168.1.20:5188")]
        [TestCase("http://192.168.1.20:5188?mode=dev")]
        [TestCase("http://192.168.1.20:5188#fragment")]
        public void DevelopmentApiOverrideRejectsInvalidUrls(string candidate)
        {
            var valid = ServiceEndpointSettings.TryValidateDevelopmentApiOverride(candidate, true, out _, out var error);

            Assert.That(valid, Is.False);
            Assert.That(error, Is.Not.Empty);
        }

        [Test]
        public void ReleaseBuildCannotUseDevelopmentApiOverride()
        {
            var valid = ServiceEndpointSettings.TryValidateDevelopmentApiOverride(
                "http://192.168.1.20:5188", false, out _, out var error);

            Assert.That(valid, Is.False);
            Assert.That(error, Does.Contain("Development"));
        }
    }
}
