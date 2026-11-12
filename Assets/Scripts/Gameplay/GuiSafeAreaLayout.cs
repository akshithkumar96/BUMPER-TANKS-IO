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

        public static FrontMenuFrames FrontMenu(Rect safeArea, bool includeDevelopmentServerButton = false)
        {
            var compact = safeArea.width <= 500f || safeArea.height <= 500f;
            var panel = Centered(340f, includeDevelopmentServerButton ? 388f : compact ? 340f : 300f, safeArea);
            return new FrontMenuFrames(panel,
                Clamp(new Rect(panel.center.x - 145f, panel.center.y + 12f, Mathf.Min(290f, safeArea.width - 32f), 44f), safeArea),
                Clamp(new Rect(panel.center.x - 115f, panel.center.y + 64f, 110f, 44f), safeArea),
                Clamp(new Rect(panel.center.x + 5f, panel.center.y + 64f, 110f, 44f), safeArea),
                Clamp(new Rect(panel.center.x - 140f, panel.center.y + 116f, 280f, 24f), safeArea),
                includeDevelopmentServerButton
                    ? Clamp(new Rect(panel.center.x - 115f, panel.center.y + 148f, 230f, 44f), safeArea)
                    : default);
        }

        public static DevelopmentServerSettingsFrames DevelopmentServerSettings(Rect safeArea)
        {
            var panel = Centered(360f, 260f, safeArea);
            var horizontalPadding = Mathf.Min(12f, panel.width * 0.04f);
            var contentWidth = Mathf.Max(0f, panel.width - horizontalPadding * 2f);
            var buttonGap = Mathf.Min(8f, contentWidth * 0.03f);
            var buttonWidth = Mathf.Max(0f, (contentWidth - buttonGap) * 0.5f);
            var left = panel.x + horizontalPadding;
            return new DevelopmentServerSettingsFrames(panel,
                Clamp(new Rect(left, panel.y + 34f, contentWidth, 40f), panel),
                Clamp(new Rect(left, panel.y + 80f, contentWidth, 44f), panel),
                Clamp(new Rect(left, panel.y + 132f, contentWidth, 54f), panel),
                Clamp(new Rect(left, panel.y + 200f, buttonWidth, 44f), panel),
                Clamp(new Rect(left + buttonWidth + buttonGap, panel.y + 200f, buttonWidth, 44f), panel));
        }
    }

    public readonly struct FrontMenuFrames
    {
        public FrontMenuFrames(Rect panel, Rect telemetryStatus, Rect allowButton, Rect declineButton, Rect currencyLabel, Rect developmentServerButton)
        {
            Panel = panel;
            TelemetryStatus = telemetryStatus;
            AllowButton = allowButton;
            DeclineButton = declineButton;
            CurrencyLabel = currencyLabel;
            DevelopmentServerButton = developmentServerButton;
        }

        public Rect Panel { get; }
        public Rect TelemetryStatus { get; }
        public Rect AllowButton { get; }
        public Rect DeclineButton { get; }
        public Rect CurrencyLabel { get; }
        public Rect DevelopmentServerButton { get; }
    }

    public readonly struct DevelopmentServerSettingsFrames
    {
        public DevelopmentServerSettingsFrames(Rect panel, Rect helpText, Rect apiUrlField, Rect statusText, Rect saveButton, Rect closeButton)
        {
            Panel = panel;
            HelpText = helpText;
            ApiUrlField = apiUrlField;
            StatusText = statusText;
            SaveButton = saveButton;
            CloseButton = closeButton;
        }

        public Rect Panel { get; }
        public Rect HelpText { get; }
        public Rect ApiUrlField { get; }
        public Rect StatusText { get; }
        public Rect SaveButton { get; }
        public Rect CloseButton { get; }
    }
}
