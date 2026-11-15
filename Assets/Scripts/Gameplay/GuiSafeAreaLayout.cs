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
            var panel = Centered(340f, includeDevelopmentServerButton ? 500f : 400f, safeArea);
            const float archetypeButtonGap = 6f;
            var archetypeButtonWidth = Mathf.Min(100f, (panel.width - 24f - archetypeButtonGap * 2f) / 3f);
            var archetypeRowWidth = archetypeButtonWidth * 3f + archetypeButtonGap * 2f;
            var archetypeStartX = panel.center.x - archetypeRowWidth * 0.5f;
            var archetypeButtons = new[]
            {
                Clamp(new Rect(archetypeStartX, panel.center.y - 20f, archetypeButtonWidth, 44f), panel),
                Clamp(new Rect(archetypeStartX + archetypeButtonWidth + archetypeButtonGap, panel.center.y - 20f, archetypeButtonWidth, 44f), panel),
                Clamp(new Rect(archetypeStartX + (archetypeButtonWidth + archetypeButtonGap) * 2f, panel.center.y - 20f, archetypeButtonWidth, 44f), panel)
            };
            return new FrontMenuFrames(panel,
                Clamp(new Rect(panel.center.x - 115f, panel.center.y - 70f, 230f, 40f), panel),
                archetypeButtons,
                Clamp(new Rect(panel.center.x - 115f, panel.center.y + 30f, 110f, 34f), panel),
                Clamp(new Rect(panel.center.x + 5f, panel.center.y + 30f, 110f, 34f), panel),
                Clamp(new Rect(panel.center.x - 145f, panel.center.y + 68f, Mathf.Min(290f, panel.width - 16f), 44f), panel),
                Clamp(new Rect(panel.center.x - 115f, panel.center.y + 120f, 110f, 44f), panel),
                Clamp(new Rect(panel.center.x + 5f, panel.center.y + 120f, 110f, 44f), panel),
                Clamp(new Rect(panel.center.x - 140f, panel.center.y + 166f, 280f, 24f), panel),
                includeDevelopmentServerButton
                    ? Clamp(new Rect(panel.center.x - 115f, panel.center.y + 202f, 230f, 44f), panel)
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
        public FrontMenuFrames(Rect panel, Rect playButton, Rect[] archetypeButtons, Rect garageButton, Rect leaderboardButton,
            Rect telemetryStatus, Rect allowButton, Rect declineButton, Rect currencyLabel, Rect developmentServerButton)
        {
            Panel = panel;
            PlayButton = playButton;
            ArchetypeButtons = archetypeButtons;
            GarageButton = garageButton;
            LeaderboardButton = leaderboardButton;
            TelemetryStatus = telemetryStatus;
            AllowButton = allowButton;
            DeclineButton = declineButton;
            CurrencyLabel = currencyLabel;
            DevelopmentServerButton = developmentServerButton;
        }

        public Rect Panel { get; }
        public Rect PlayButton { get; }
        public Rect[] ArchetypeButtons { get; }
        public Rect GarageButton { get; }
        public Rect LeaderboardButton { get; }
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
