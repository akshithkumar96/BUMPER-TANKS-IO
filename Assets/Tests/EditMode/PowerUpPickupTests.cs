using BumperTanks.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace BumperTanks.Tests
{
    public class PowerUpPickupTests
    {
        [Test]
        public void PickupCanOnlyBeClaimedOnceBeforeDeferredDestruction()
        {
            var pickupObject = new GameObject("Power-up pickup test");
            try
            {
                var pickup = pickupObject.AddComponent<PowerUpPickup>();

                Assert.That(pickup.TryConsume(), Is.True);
                Assert.That(pickup.IsCollected, Is.True);
                Assert.That(pickup.TryConsume(), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(pickupObject);
            }
        }
    }
}
