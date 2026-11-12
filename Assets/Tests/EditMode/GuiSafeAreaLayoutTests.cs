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

        [TestCase(390f, 844f)]
        [TestCase(320f, 568f)]
        [TestCase(1280f, 720f)]
        public void DevelopmentServerButtonFitsMainMenu(float width, float height)
        {
            var safe = GuiSafeAreaLayout.ToGuiRect(new Rect(0f, 0f, width, height), height, 8f);
            var menu = GuiSafeAreaLayout.FrontMenu(safe, true);

            Assert.That(menu.DevelopmentServerButton.width, Is.GreaterThanOrEqualTo(200f));
            Assert.That(menu.DevelopmentServerButton.height, Is.EqualTo(44f));
            Assert.That(menu.DevelopmentServerButton.xMin, Is.GreaterThanOrEqualTo(menu.Panel.xMin));
            Assert.That(menu.DevelopmentServerButton.xMax, Is.LessThanOrEqualTo(menu.Panel.xMax));
            Assert.That(menu.CurrencyLabel.yMax, Is.LessThanOrEqualTo(menu.DevelopmentServerButton.yMin));
            Assert.That(menu.DevelopmentServerButton.yMax, Is.LessThanOrEqualTo(menu.Panel.yMax));
        }

        [Test]
        public void StandardMainMenuDoesNotExposeDevelopmentServerButton()
        {
            var menu = GuiSafeAreaLayout.FrontMenu(new Rect(8f, 8f, 374f, 828f));

            Assert.That(menu.DevelopmentServerButton, Is.EqualTo(default(Rect)));
        }

        [TestCase(390f, 844f)]
        [TestCase(320f, 568f)]
        [TestCase(1280f, 720f)]
        public void DevelopmentServerFormFitsSafeAreaAndKeepsTouchTargets(float width, float height)
        {
            var safe = GuiSafeAreaLayout.ToGuiRect(new Rect(0f, 0f, width, height), height, 8f);
            var form = GuiSafeAreaLayout.DevelopmentServerSettings(safe);

            AssertInside(form.Panel, safe);
            AssertInside(form.HelpText, form.Panel);
            AssertInside(form.ApiUrlField, form.Panel);
            AssertInside(form.StatusText, form.Panel);
            AssertInside(form.SaveButton, form.Panel);
            AssertInside(form.CloseButton, form.Panel);
            Assert.That(form.ApiUrlField.height, Is.EqualTo(44f));
            Assert.That(form.SaveButton.height, Is.EqualTo(44f));
            Assert.That(form.CloseButton.height, Is.EqualTo(44f));
            Assert.That(form.HelpText.yMax, Is.LessThanOrEqualTo(form.ApiUrlField.yMin));
            Assert.That(form.ApiUrlField.yMax, Is.LessThanOrEqualTo(form.StatusText.yMin));
            Assert.That(form.StatusText.yMax, Is.LessThanOrEqualTo(form.SaveButton.yMin));
        }

        private static void AssertInside(Rect frame, Rect bounds)
        {
            Assert.That(frame.xMin, Is.GreaterThanOrEqualTo(bounds.xMin));
            Assert.That(frame.yMin, Is.GreaterThanOrEqualTo(bounds.yMin));
            Assert.That(frame.xMax, Is.LessThanOrEqualTo(bounds.xMax));
            Assert.That(frame.yMax, Is.LessThanOrEqualTo(bounds.yMax));
        }
    }
}
