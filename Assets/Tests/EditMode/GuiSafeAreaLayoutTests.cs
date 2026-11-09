using BumperTanks.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace BumperTanks.Tests
{
    public class GuiSafeAreaLayoutTests
    {
        [Test]
        public void ConvertsBottomLeftSafeAreaToTopLeftGuiCoordinates()
        {
            var gui = GuiSafeAreaLayout.ToGuiRect(new Rect(30f, 40f, 900f, 1800f), 1920f, 8f);

            Assert.That(gui, Is.EqualTo(new Rect(38f, 88f, 884f, 1784f)));
        }

        [TestCase(320f, 568f)]
        [TestCase(240f, 280f)]
        [TestCase(900f, 180f)]
        public void CenteredPanelsStayInsideNarrowOrShortSafeAreas(float width, float height)
        {
            var safe = new Rect(24f, 32f, width, height);
            var panel = GuiSafeAreaLayout.Centered(340f, 240f, safe);

            Assert.That(panel.xMin, Is.GreaterThanOrEqualTo(safe.xMin));
            Assert.That(panel.yMin, Is.GreaterThanOrEqualTo(safe.yMin));
            Assert.That(panel.xMax, Is.LessThanOrEqualTo(safe.xMax));
            Assert.That(panel.yMax, Is.LessThanOrEqualTo(safe.yMax));
            Assert.That(panel.width, Is.EqualTo(Mathf.Min(340f, width)));
            Assert.That(panel.height, Is.EqualTo(Mathf.Min(240f, height)));
        }

        [Test]
        public void ClampedActionFrameNeverLeavesSafeArea()
        {
            var safe = new Rect(20f, 40f, 300f, 400f);
            var frame = GuiSafeAreaLayout.Clamp(new Rect(280f, 420f, 100f, 60f), safe);

            Assert.That(frame.xMax, Is.EqualTo(safe.xMax));
            Assert.That(frame.yMax, Is.EqualTo(safe.yMax));
        }

        [TestCase(390f, 844f)]
        [TestCase(320f, 568f)]
        [TestCase(1280f, 720f)]
        public void FrontMenuConsentTextAndTouchTargetsFitWithoutOverlap(float width, float height)
        {
            var safe = GuiSafeAreaLayout.ToGuiRect(new Rect(0f, 0f, width, height), height, 8f);
            var menu = GuiSafeAreaLayout.FrontMenu(safe);

            Assert.That(menu.TelemetryStatus.yMin, Is.GreaterThanOrEqualTo(menu.Panel.yMin));
            Assert.That(menu.TelemetryStatus.yMax, Is.LessThanOrEqualTo(menu.AllowButton.yMin));
            Assert.That(menu.AllowButton.yMax, Is.LessThanOrEqualTo(menu.CurrencyLabel.yMin));
            Assert.That(menu.DeclineButton.yMin, Is.EqualTo(menu.AllowButton.yMin));
            Assert.That(menu.AllowButton.height, Is.EqualTo(44f));
            Assert.That(menu.DeclineButton.height, Is.EqualTo(44f));
            Assert.That(menu.CurrencyLabel.yMax, Is.LessThanOrEqualTo(menu.Panel.yMax));
            Assert.That(menu.TelemetryStatus.xMin, Is.GreaterThanOrEqualTo(safe.xMin));
            Assert.That(menu.TelemetryStatus.xMax, Is.LessThanOrEqualTo(safe.xMax));
        }
    }
}
