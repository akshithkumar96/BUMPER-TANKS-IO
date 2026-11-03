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
    }
}
