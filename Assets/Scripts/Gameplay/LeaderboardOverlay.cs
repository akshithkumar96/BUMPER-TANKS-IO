using BumperTanks.Frontend;
using UnityEngine;

namespace BumperTanks.Gameplay
{
    public sealed class LeaderboardOverlay : MonoBehaviour
    {
        private bool open;
        private GameServiceClient.LeaderboardEntry[] entries = System.Array.Empty<GameServiceClient.LeaderboardEntry>();

        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.L)) return;
            open = !open;
            if (open)
            {
                FetchEntries();
                ShowAccessibleScreen();
            }
            else SendMessage("RefreshCurrentAccessibility", SendMessageOptions.DontRequireReceiver);
        }

        public void OpenLeaderboard()
        {
            open = true;
            FetchEntries();
            ShowAccessibleScreen();
        }

        private void OnGUI()
        {
            if (!open) return;
            var safe = GuiSafeAreaLayout.Current;
            var panelWidth = Mathf.Min(278f, safe.width);
            var panel = GuiSafeAreaLayout.Clamp(new Rect(safe.xMax - panelWidth, safe.center.y - 125f, panelWidth, 250f), safe);
            var left = panel.x;
            var top = panel.y;
            GUI.Box(panel, "Leaderboard");
            for (var index = 0; index < Mathf.Min(entries.Length, 5); index++)
            {
                var entry = entries[index];
                GUI.Label(SafeFrame(left + 15, top + 40 + index * 30, panelWidth - 30, 26, safe), $"Rank {index + 1}: {entry.battleCoins} Battle Coins");
            }
            if (entries.Length == 0) GUI.Label(SafeFrame(left + 15, top + 40, panelWidth - 30, 26, safe), "No scores yet");
            if (GUI.Button(SafeFrame(left + 15, top + 190, panelWidth - 30, 44, safe), "Back to main menu"))
            {
                open = false;
                SendMessage("ShowFrontMenu", SendMessageOptions.DontRequireReceiver);
            }
        }

        public void CloseLeaderboard() => open = false;

        private void FetchEntries()
        {
            GameServiceClient.Instance?.FetchLeaderboard(result =>
            {
                entries = result;
                if (open) ShowAccessibleScreen();
            });
        }

        private void ShowAccessibleScreen()
        {
            var safe = GuiSafeAreaLayout.Current;
            var panelWidth = Mathf.Min(278f, safe.width);
            var panel = GuiSafeAreaLayout.Clamp(new Rect(safe.xMax - panelWidth, safe.center.y - 125f, panelWidth, 250f), safe);
            var left = panel.x;
            var top = panel.y;
            var actions = new System.Collections.Generic.List<AccessibleUiAction>();
            for (var index = 0; index < Mathf.Min(entries.Length, 5); index++)
            {
                var entry = entries[index];
                var frame = SafeFrame(left + 15, top + 40 + index * 30, panelWidth - 30, 26, safe);
                actions.Add(new AccessibleUiAction($"Rank {index + 1}: {entry.battleCoins} Battle Coins", "Leaderboard score.", frame, null, false));
            }
            actions.Add(new AccessibleUiAction("Back to main menu", "Close the leaderboard and return to the main menu.",
                SafeFrame(left + 15, top + 190, panelWidth - 30, 44, safe), () =>
                {
                    open = false;
                    SendMessage("ShowFrontMenu", SendMessageOptions.DontRequireReceiver);
                }));
            AccessibleUiBridge.Instance?.ShowScreen("Leaderboard", actions.ToArray());
        }

        private static Rect SafeFrame(float x, float y, float width, float height, Rect safe) =>
            GuiSafeAreaLayout.Clamp(new Rect(x, y, Mathf.Max(0f, width), height), safe);
    }
}
