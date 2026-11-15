using BumperTanks.Gameplay;
using NUnit.Framework;

namespace BumperTanks.Tests
{
    public class OverlayLoadStateTests
    {
        [Test]
        public void BeginClearsPreviousFailureAndMarksLoading()
        {
            var state = new OverlayLoadState();
            state.Fail("network unavailable");

            state.Begin();

            Assert.That(state.Status, Is.EqualTo(OverlayLoadStatus.Loading));
            Assert.That(state.IsLoading, Is.True);
            Assert.That(state.Error, Is.Empty);
            Assert.That(state.HasCompleted, Is.False);
        }

        [Test]
        public void CompleteDistinguishesEmptyDataFromLoadedData()
        {
            var state = new OverlayLoadState();

            state.Complete(isEmpty: true);
            Assert.That(state.Status, Is.EqualTo(OverlayLoadStatus.Empty));
            Assert.That(state.HasCompleted, Is.True);

            state.Complete(isEmpty: false);
            Assert.That(state.Status, Is.EqualTo(OverlayLoadStatus.Loaded));
            Assert.That(state.HasCompleted, Is.True);
        }

        [Test]
        public void FailPreservesUsefulErrorOrUsesFallback()
        {
            var state = new OverlayLoadState();

            state.Fail("request timed out");
            Assert.That(state.Status, Is.EqualTo(OverlayLoadStatus.Failed));
            Assert.That(state.Error, Is.EqualTo("request timed out"));

            state.Fail(" ");
            Assert.That(state.Error, Is.EqualTo("Unable to load data."));
        }
    }
}
