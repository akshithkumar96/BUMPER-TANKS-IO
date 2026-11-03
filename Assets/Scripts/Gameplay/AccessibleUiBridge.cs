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
#if UNITY_EDITOR || UNITY_ANDROID || UNITY_IOS
            currentTitle = title ?? string.Empty;
            currentActions = actions ?? Array.Empty<AccessibleUiAction>();
            if (Application.isMobilePlatform && AssistiveSupport.isScreenReaderEnabled) RebuildHierarchy();
#endif
        }

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
