using BumperTanks.Frontend;
using BumperTanks.Gameplay;
using NUnit.Framework;

namespace BumperTanks.Tests
{
    public class ProfileTests
    {
        [Test]
        public void DailySpinRewardChoicesAreShownToPlayers()
        {
            Assert.That(DailySpin.RewardOptions, Is.EqualTo(new[] { 25, 50, 75, 100 }));
        }

        [Test]
        public void PlayerIdIsAnAppScopedIdentifier()
        {
            var playerId = ProfileStore.GetOrCreatePlayerId();
            Assert.That(playerId, Does.Match("^[a-f0-9]{32}$"));
            Assert.That(ProfileStore.GetOrCreatePlayerId(), Is.EqualTo(playerId));
        }

        [Test]
        public void TrailCosmeticChoiceSurvivesProfileSerialization()
        {
            var profile = new PlayerProfile { TrailId = "neon" };
            var restored = UnityEngine.JsonUtility.FromJson<PlayerProfile>(UnityEngine.JsonUtility.ToJson(profile));

            Assert.That(restored.TrailId, Is.EqualTo("neon"));
        }

        [Test]
        public void FlagAndBumpSoundChoicesSurviveProfileSerialization()
        {
            var profile = new PlayerProfile { FlagId = "flag_checkered", BumpSoundId = "sound_arcade", OwnedCosmetics = new[] { "flag_checkered", "sound_arcade" } };
            var restored = UnityEngine.JsonUtility.FromJson<PlayerProfile>(UnityEngine.JsonUtility.ToJson(profile));

            Assert.That(restored.FlagId, Is.EqualTo("flag_checkered"));
            Assert.That(restored.BumpSoundId, Is.EqualTo("sound_arcade"));
            Assert.That(restored.OwnedCosmetics, Is.EqualTo(new[] { "flag_checkered", "sound_arcade" }));
        }

        [Test]
        public void ClearingServerIdentityPreservesLocalPlayerAndCosmetics()
        {
            var originalProfile = ProfileStore.Load();
            var originalServerId = ProfileStore.GetServerPlayerId();
            var originalToken = ProfileStore.GetAccessToken();
            var localId = ProfileStore.GetOrCreatePlayerId();
            try
            {
                ProfileStore.Save(new PlayerProfile
                {
                    BattleCoins = 275,
                    ColorId = "coral",
                    TrailId = "neon",
                    FlagId = "flag_checkered",
                    BumpSoundId = "sound_arcade",
                    OwnedCosmetics = new[] { "flag_checkered", "sound_arcade" }
                });
                ProfileStore.SaveServerIdentity("old-host-player", "old-host-token");

                ProfileStore.ClearServerIdentity();

                Assert.That(ProfileStore.GetServerPlayerId(), Is.Empty);
                Assert.That(ProfileStore.GetAccessToken(), Is.Empty);
                Assert.That(ProfileStore.GetOrCreatePlayerId(), Is.EqualTo(localId));
                var profile = ProfileStore.Load();
                Assert.That(profile.BattleCoins, Is.EqualTo(275));
                Assert.That(profile.ColorId, Is.EqualTo("coral"));
                Assert.That(profile.TrailId, Is.EqualTo("neon"));
                Assert.That(profile.FlagId, Is.EqualTo("flag_checkered"));
                Assert.That(profile.BumpSoundId, Is.EqualTo("sound_arcade"));
                Assert.That(profile.OwnedCosmetics, Is.EqualTo(new[] { "flag_checkered", "sound_arcade" }));
            }
            finally
            {
                ProfileStore.Save(originalProfile);
                ProfileStore.SaveServerIdentity(originalServerId, originalToken);
            }
        }

        [Test]
        public void AccessibleFramesConvertImmediateGuiCoordinatesToScreenCoordinates()
        {
            var frame = AccessibleUiFrames.ToScreen(new UnityEngine.Rect(12f, 20f, 112f, 44f), 640f);

            Assert.That(frame, Is.EqualTo(new UnityEngine.Rect(12f, 576f, 112f, 44f)));
        }
    }
}
