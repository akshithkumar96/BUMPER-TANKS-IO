using System;
using UnityEngine;
#if UNITY_EDITOR || UNITY_ANDROID || UNITY_IOS
using UnityEngine.Accessibility;
#endif

namespace BumperTanks.Gameplay
{
    public readonly struct AccessibleUiAction
    {
        public AccessibleUiAction(string label, string hint, Rect guiFrame, Action activate, bool isInteractive = true)
        {
            Label = label;
            Hint = hint;
            GuiFrame = guiFrame;
            Activate = activate;
            IsInteractive = isInteractive;
        }

        public string Label { get; }
        public string Hint { get; }
        public Rect GuiFrame { get; }
        public Action Activate { get; }
        public bool IsInteractive { get; }
    }

    public static class AccessibleUiFrames
    {
        public static Rect ToScreen(Rect guiFrame, float screenHeight) =>
            new(guiFrame.x, screenHeight - guiFrame.y - guiFrame.height, guiFrame.width, guiFrame.height);
    }

    public sealed class AccessibleUiBridge : MonoBehaviour
    {
        public static AccessibleUiBridge Instance { get; private set; }
        private static bool assistiveMatchControlsVisible;
        public static bool AssistiveMatchControlsVisible
        {
            get
            {
#if UNITY_EDITOR || UNITY_ANDROID || UNITY_IOS
                return assistiveMatchControlsVisible && Application.isMobilePlatform && AssistiveSupport.isScreenReaderEnabled;
#else
                return false;
#endif
            }
        }

        private TankController2D accessiblePlayer;

#if UNITY_EDITOR || UNITY_ANDROID || UNITY_IOS
        private AccessibilityHierarchy hierarchy;
        private string currentTitle;
        private AccessibleUiAction[] currentActions = Array.Empty<AccessibleUiAction>();
#endif

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
#if UNITY_EDITOR || UNITY_ANDROID || UNITY_IOS
            hierarchy = new AccessibilityHierarchy();
            AssistiveSupport.screenReaderStatusChanged += OnScreenReaderStatusChanged;
#endif
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
#if UNITY_EDITOR || UNITY_ANDROID || UNITY_IOS
            AssistiveSupport.screenReaderStatusChanged -= OnScreenReaderStatusChanged;
            if (AssistiveSupport.activeHierarchy == hierarchy) AssistiveSupport.activeHierarchy = null;
#endif
        }

        public void ShowScreen(string title, params AccessibleUiAction[] actions)
        {
            assistiveMatchControlsVisible = title != null && title.StartsWith("Match controls", StringComparison.Ordinal);
            accessiblePlayer = assistiveMatchControlsVisible ? FindAccessiblePlayer() : null;
#if UNITY_EDITOR || UNITY_ANDROID || UNITY_IOS
            currentTitle = title ?? string.Empty;
            var combined = new System.Collections.Generic.List<AccessibleUiAction>(actions ?? Array.Empty<AccessibleUiAction>());
            if (assistiveMatchControlsVisible && accessiblePlayer != null)
            {
                AddAssistiveMatchActions(combined, accessiblePlayer);
            }
            currentActions = combined.ToArray();
            if (Application.isMobilePlatform && AssistiveSupport.isScreenReaderEnabled) RebuildHierarchy();
#endif
        }

        private static TankController2D FindAccessiblePlayer()
        {
            var tanks = FindObjectsByType<TankController2D>(FindObjectsSortMode.None);
            foreach (var tank in tanks)
                if (tank != null && tank.IsPlayer) return tank;
            return null;
        }

        private void OnGUI()
        {
#if UNITY_EDITOR || UNITY_ANDROID || UNITY_IOS
            if (!AssistiveMatchControlsVisible || accessiblePlayer == null || !Application.isMobilePlatform ||
                !AssistiveSupport.isScreenReaderEnabled) return;

            var safe = Screen.safeArea;
            var size = Mathf.Clamp(Screen.width * 0.14f, 48f, 68f);
            var gap = size * 0.12f;
            var left = safe.xMin + gap;
            var bottom = Screen.height - safe.yMin - size - gap;
            var centerX = left + size + gap;
            var centerY = bottom - size - gap;
            DrawControl("◀", new Rect(left, centerY, size, size), () => accessiblePlayer.SetAccessibleSteering(Vector2.left));
            DrawControl("▲", new Rect(centerX, centerY - size - gap, size, size), () => accessiblePlayer.SetAccessibleSteering(Vector2.up));
            DrawControl("▶", new Rect(centerX + size + gap, centerY, size, size), () => accessiblePlayer.SetAccessibleSteering(Vector2.right));
            DrawControl("▼", new Rect(centerX, centerY, size, size), () => accessiblePlayer.SetAccessibleSteering(Vector2.down));

            var fireLeft = safe.xMax - size * 2f - gap * 2f;
            var fireTop = bottom - size * 2f - gap;
            DrawControl("Fire ↑", new Rect(fireLeft, fireTop, size, size), () => accessiblePlayer.AccessibleAimAndFire(Vector2.up));
            DrawControl("Fire →", new Rect(fireLeft + size + gap, fireTop, size, size), () => accessiblePlayer.AccessibleAimAndFire(Vector2.right));
            DrawControl("Fire ←", new Rect(fireLeft, fireTop + size + gap, size, size), () => accessiblePlayer.AccessibleAimAndFire(Vector2.left));
            DrawControl("Fire ↓", new Rect(fireLeft + size + gap, fireTop + size + gap, size, size), () => accessiblePlayer.AccessibleAimAndFire(Vector2.down));
#endif
        }

#if UNITY_EDITOR || UNITY_ANDROID || UNITY_IOS
        private void AddAssistiveMatchActions(System.Collections.Generic.List<AccessibleUiAction> actions, TankController2D tank)
        {
            var safe = Screen.safeArea;
            var size = Mathf.Clamp(Screen.width * 0.14f, 48f, 68f);
            var gap = size * 0.12f;
            var left = safe.xMin + gap;
            var bottom = Screen.height - safe.yMin - size - gap;
            var centerX = left + size + gap;
            var centerY = bottom - size - gap;
            actions.Add(new AccessibleUiAction("Move left", "Move left briefly. Activate again to continue.", new Rect(left, centerY, size, size), () => tank.SetAccessibleSteering(Vector2.left)));
            actions.Add(new AccessibleUiAction("Move up", "Move up briefly. Activate again to continue.", new Rect(centerX, centerY - size - gap, size, size), () => tank.SetAccessibleSteering(Vector2.up)));
            actions.Add(new AccessibleUiAction("Move right", "Move right briefly. Activate again to continue.", new Rect(centerX + size + gap, centerY, size, size), () => tank.SetAccessibleSteering(Vector2.right)));
            actions.Add(new AccessibleUiAction("Move down", "Move down briefly. Activate again to continue.", new Rect(centerX, centerY, size, size), () => tank.SetAccessibleSteering(Vector2.down)));
            var fireLeft = safe.xMax - size * 2f - gap * 2f;
            var fireTop = bottom - size * 2f - gap;
            actions.Add(new AccessibleUiAction("Aim up and fire", "Aim upward and fire once.", new Rect(fireLeft, fireTop, size, size), () => tank.AccessibleAimAndFire(Vector2.up)));
            actions.Add(new AccessibleUiAction("Aim right and fire", "Aim right and fire once.", new Rect(fireLeft + size + gap, fireTop, size, size), () => tank.AccessibleAimAndFire(Vector2.right)));
            actions.Add(new AccessibleUiAction("Aim left and fire", "Aim left and fire once.", new Rect(fireLeft, fireTop + size + gap, size, size), () => tank.AccessibleAimAndFire(Vector2.left)));
            actions.Add(new AccessibleUiAction("Aim down and fire", "Aim downward and fire once.", new Rect(fireLeft + size + gap, fireTop + size + gap, size, size), () => tank.AccessibleAimAndFire(Vector2.down)));
        }

        private static void DrawControl(string label, Rect frame, Action activate)
        {
            var previous = GUI.color;
            GUI.color = new Color(0.08f, 0.13f, 0.2f, 0.88f);
            if (GUI.Button(frame, label)) activate?.Invoke();
            GUI.color = previous;
        }
#endif

        public void Announce(string message)
        {
#if UNITY_EDITOR || UNITY_ANDROID || UNITY_IOS
            if (Application.isMobilePlatform && AssistiveSupport.isScreenReaderEnabled)
                AssistiveSupport.notificationDispatcher?.SendAnnouncement(message);
#endif
        }

#if UNITY_EDITOR || UNITY_ANDROID || UNITY_IOS
        private void OnScreenReaderStatusChanged(bool enabled)
        {
            if (enabled && Application.isMobilePlatform) RebuildHierarchy();
            else if (AssistiveSupport.activeHierarchy == hierarchy) AssistiveSupport.activeHierarchy = null;
        }

        private void RebuildHierarchy()
        {
            hierarchy.Clear();
            var heading = hierarchy.AddNode(currentTitle);
            heading.role = AccessibilityRole.Header;
            heading.frameGetter = () => new Rect(0f, 0f, Screen.width, 48f);
            AccessibilityNode firstAction = null;
            foreach (var action in currentActions)
            {
                var node = hierarchy.AddNode(action.Label, heading);
                node.role = action.IsInteractive ? AccessibilityRole.Button : AccessibilityRole.StaticText;
                node.hint = action.Hint;
                var frame = action.GuiFrame;
                node.frameGetter = () => AccessibleUiFrames.ToScreen(frame, Screen.height);
                if (action.IsInteractive) node.invoked += () =>
                {
                    action.Activate?.Invoke();
                    return true;
                };
                if (firstAction == null) firstAction = node;
            }
            AssistiveSupport.activeHierarchy = hierarchy;
            AssistiveSupport.notificationDispatcher?.SendScreenChanged(firstAction ?? heading);
        }
#endif
    }
}
