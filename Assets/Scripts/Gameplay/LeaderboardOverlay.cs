using BumperTanks.Frontend;
using UnityEngine;

namespace BumperTanks.Gameplay
{
    public sealed class LeaderboardOverlay : MonoBehaviour
    {
        private bool open;
        private GameServiceClient.LeaderboardEntry[] entries = System.Array.Empty<GameServiceClient.LeaderboardEntry>();
        private readonly OverlayLoadState entriesLoad = new();

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
            if (entriesLoad.Status == OverlayLoadStatus.Loading)
                GUI.Label(SafeFrame(left + 15, top + 40, panelWidth - 30, 26, safe), "Loading leaderboard...");
            else if (entriesLoad.Status == OverlayLoadStatus.Failed)
            {
                GUI.Label(SafeFrame(left + 15, top + 40, panelWidth - 30, 30, safe), "Leaderboard unavailable.");
                if (GUI.Button(SafeFrame(left + 15, top + 76, panelWidth - 30, 44, safe), "Retry leaderboard")) FetchEntries();
            }
            else if (entriesLoad.Status == OverlayLoadStatus.Empty)
                GUI.Label(SafeFrame(left + 15, top + 40, panelWidth - 30, 26, safe), "No scores yet");
            else if (entriesLoad.Status == OverlayLoadStatus.Loaded)
            {
                for (var index = 0; index < Mathf.Min(entries.Length, 5); index++)
                {
                    var entry = entries[index];
                    GUI.Label(SafeFrame(left + 15, top + 40 + index * 30, panelWidth - 30, 26, safe), $"Rank {index + 1}: {entry.battleCoins} Battle Coins");
                }
            }
            if (GUI.Button(SafeFrame(left + 15, top + 190, panelWidth - 30, 44, safe), "Back to main menu"))
            {
                open = false;
                SendMessage("ShowFrontMenu", SendMessageOptions.DontRequireReceiver);
            }
        }

        public void CloseLeaderboard() => open = false;

        private void FetchEntries()
        {
            if (entriesLoad.IsLoading) return;
            entriesLoad.Begin();
            entries = System.Array.Empty<GameServiceClient.LeaderboardEntry>();
            var service = GameServiceClient.Instance;
            if (service == null)
            {
                entriesLoad.Fail("Online leaderboard is unavailable.");
                if (open) ShowAccessibleScreen();
                return;
            }

            if (open) ShowAccessibleScreen();
            service.FetchLeaderboard(result =>
            {
                entries = result ?? System.Array.Empty<GameServiceClient.LeaderboardEntry>();
                entriesLoad.Complete(entries.Length == 0);
                if (open) ShowAccessibleScreen();
            }, error =>
            {
                entriesLoad.Fail(error);
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
            for (var index = 0; entriesLoad.Status == OverlayLoadStatus.Loaded && index < Mathf.Min(entries.Length, 5); index++)
            {
                var entry = entries[index];
                var frame = SafeFrame(left + 15, top + 40 + index * 30, panelWidth - 30, 26, safe);
                actions.Add(new AccessibleUiAction($"Rank {index + 1}: {entry.battleCoins} Battle Coins", "Leaderboard score.", frame, null, false));
            }
            var statusDescription = entriesLoad.Status switch
            {
                OverlayLoadStatus.Loading => "Loading leaderboard.",
                OverlayLoadStatus.Empty => "No scores yet.",
                OverlayLoadStatus.Failed => "Leaderboard unavailable. Retry is available.",
                _ => string.Empty
            };
            if (entriesLoad.Status == OverlayLoadStatus.Failed)
                actions.Add(new AccessibleUiAction("Retry leaderboard", "Try loading scores again.",
                    SafeFrame(left + 15, top + 76, panelWidth - 30, 44, safe), FetchEntries));
            actions.Add(new AccessibleUiAction("Back to main menu", "Close the leaderboard and return to the main menu.",
                SafeFrame(left + 15, top + 190, panelWidth - 30, 44, safe), () =>
                {
                    open = false;
                    SendMessage("ShowFrontMenu", SendMessageOptions.DontRequireReceiver);
                }));
            AccessibleUiBridge.Instance?.ShowScreen("Leaderboard. " + statusDescription, actions.ToArray());
        }

        private static Rect SafeFrame(float x, float y, float width, float height, Rect safe) =>
            GuiSafeAreaLayout.Clamp(new Rect(x, y, Mathf.Max(0f, width), height), safe);
    }
}
