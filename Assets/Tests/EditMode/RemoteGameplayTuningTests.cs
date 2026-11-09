using BumperTanks.Core;
using BumperTanks.Frontend;
using NUnit.Framework;

namespace BumperTanks.Tests
{
    public sealed class RemoteGameplayTuningTests
    {
        [TearDown]
        public void ResetTuning() => GameplayTuning.Reset();

        [Test]
        public void AppliesOnlySupportedRevisionAndBoundedValues()
        {
            Assert.That(RemoteGameplayTuning.TryParseResponse(
                "{\"revision\":1,\"speedMultiplier\":1.1,\"projectileCooldownMultiplier\":0.9,\"knockbackMultiplier\":1.2}",
                out var response), Is.True);
            Assert.That(GameplayTuning.TryApply(response.speedMultiplier, response.projectileCooldownMultiplier, response.knockbackMultiplier), Is.True);
            Assert.That(GameplayTuning.SpeedMultiplier, Is.EqualTo(1.1f).Within(0.001f));
            Assert.That(GameplayTuning.ProjectileCooldownMultiplier, Is.EqualTo(0.9f).Within(0.001f));
            Assert.That(GameplayTuning.KnockbackMultiplier, Is.EqualTo(1.2f).Within(0.001f));

            Assert.That(RemoteGameplayTuning.TryParseResponse(
                "{\"revision\":2,\"speedMultiplier\":1,\"projectileCooldownMultiplier\":1,\"knockbackMultiplier\":1}",
                out _), Is.False);
            Assert.That(RemoteGameplayTuning.TryParseResponse(
                "{\"revision\":1,\"speedMultiplier\":9,\"projectileCooldownMultiplier\":1,\"knockbackMultiplier\":1}",
                out _), Is.False);
            Assert.That(GameplayTuning.TryApply(0.5f, 1f, 1f), Is.False);
            Assert.That(GameplayTuning.SpeedMultiplier, Is.EqualTo(1.1f).Within(0.001f));
        }

        [Test]
        public void ExpiredOrFutureCacheTimestampFallsBackToDefaults()
        {
            const long day = System.TimeSpan.TicksPerDay;
            Assert.That(RemoteGameplayTuning.IsCacheFresh(100 * day, 107 * day), Is.True);
            Assert.That(RemoteGameplayTuning.IsCacheFresh(100 * day, 107 * day + 1), Is.False);
            Assert.That(RemoteGameplayTuning.IsCacheFresh(101 * day, 100 * day), Is.False);
            Assert.That(RemoteGameplayTuning.IsCacheFresh(0, 100 * day), Is.False);
            Assert.That(RemoteGameplayTuning.IsCachedTuningUsable(100 * day, 101 * day, 1f, 1f, 1f), Is.True);
            Assert.That(RemoteGameplayTuning.IsCachedTuningUsable(100 * day, 101 * day, 9f, 1f, 1f), Is.False);
            Assert.That(RemoteGameplayTuning.IsCachedTuningUsable(100 * day, 101 * day, float.NaN, 1f, 1f), Is.False);
            Assert.That(RemoteGameplayTuning.IsCachedTuningUsable(100 * day, 108 * day, 1f, 1f, 1f), Is.False);
        }
    }
}
