using UnityEngine;

namespace BumperTanks.Gameplay
{
    /// <summary>Converts Unity's bottom-left safe area to top-left IMGUI coordinates.</summary>
    public static class GuiSafeAreaLayout
    {
        public static Rect Current => ToGuiRect(Screen.safeArea, Screen.height, 8f);

        public static Rect ToGuiRect(Rect screenSafeArea, float screenHeight, float padding = 0f)
        {
            var gui = new Rect(
                screenSafeArea.xMin + padding,
                screenHeight - screenSafeArea.yMax + padding,
                Mathf.Max(0f, screenSafeArea.width - padding * 2f),
                Mathf.Max(0f, screenSafeArea.height - padding * 2f));
            return gui;
        }

        public static Rect Clamp(Rect frame, Rect bounds)
        {
            frame.width = Mathf.Min(frame.width, bounds.width);
            frame.height = Mathf.Min(frame.height, bounds.height);
            frame.x = Mathf.Clamp(frame.x, bounds.xMin, bounds.xMax - frame.width);
            frame.y = Mathf.Clamp(frame.y, bounds.yMin, bounds.yMax - frame.height);
            return frame;
        }

        public static Rect Centered(float width, float height, Rect bounds)
        {
            return Clamp(new Rect(bounds.center.x - width * 0.5f, bounds.center.y - height * 0.5f, width, height), bounds);
        }

        public static FrontMenuFrames FrontMenu(Rect safeArea)
        {
            var compact = safeArea.width <= 500f || safeArea.height <= 500f;
            var panel = Centered(340f, compact ? 340f : 300f, safeArea);
            return new FrontMenuFrames(panel,
                Clamp(new Rect(panel.center.x - 145f, panel.center.y + 12f, Mathf.Min(290f, safeArea.width - 32f), 44f), safeArea),
                Clamp(new Rect(panel.center.x - 115f, panel.center.y + 64f, 110f, 44f), safeArea),
                Clamp(new Rect(panel.center.x + 5f, panel.center.y + 64f, 110f, 44f), safeArea),
                Clamp(new Rect(panel.center.x - 140f, panel.center.y + 120f, 280f, 24f), safeArea));
        }
    }

    public readonly struct FrontMenuFrames
    {
        public FrontMenuFrames(Rect panel, Rect telemetryStatus, Rect allowButton, Rect declineButton, Rect currencyLabel)
        {
            Panel = panel;
            TelemetryStatus = telemetryStatus;
            AllowButton = allowButton;
            DeclineButton = declineButton;
            CurrencyLabel = currencyLabel;
        }

        public Rect Panel { get; }
        public Rect TelemetryStatus { get; }
        public Rect AllowButton { get; }
        public Rect DeclineButton { get; }
        public Rect CurrencyLabel { get; }
    }
}
