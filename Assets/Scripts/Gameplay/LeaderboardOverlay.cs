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
            var panelWidth = Mathf.Min(278f, Screen.width - 16f);
            var left = Mathf.Max(8f, Screen.width - panelWidth - 12f);
            var top = Mathf.Max(8f, (Screen.height - 250f) * 0.5f);
            GUI.Box(new Rect(left, top, panelWidth, 250), "Leaderboard");
            for (var index = 0; index < Mathf.Min(entries.Length, 5); index++)
            {
                var entry = entries[index];
                GUI.Label(new Rect(left + 15, top + 40 + index * 30, panelWidth - 30, 26), $"Rank {index + 1}: {entry.battleCoins} Battle Coins");
            }
            if (entries.Length == 0) GUI.Label(new Rect(left + 15, top + 40, panelWidth - 30, 26), "No scores yet");
            if (GUI.Button(new Rect(left + 15, top + 190, panelWidth - 30, 44), "Back to main menu"))
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
            var panelWidth = Mathf.Min(278f, Screen.width - 16f);
            var left = Mathf.Max(8f, Screen.width - panelWidth - 12f);
            var top = Mathf.Max(8f, (Screen.height - 250f) * 0.5f);
            var actions = new System.Collections.Generic.List<AccessibleUiAction>();
            for (var index = 0; index < Mathf.Min(entries.Length, 5); index++)
            {
                var entry = entries[index];
                var frame = new Rect(left + 15, top + 40 + index * 30, panelWidth - 30, 26);
                actions.Add(new AccessibleUiAction($"Rank {index + 1}: {entry.battleCoins} Battle Coins", "Leaderboard score.", frame, null, false));
            }
            actions.Add(new AccessibleUiAction("Back to main menu", "Close the leaderboard and return to the main menu.",
                new Rect(left + 15, top + 190, panelWidth - 30, 44), () =>
                {
                    open = false;
                    SendMessage("ShowFrontMenu", SendMessageOptions.DontRequireReceiver);
                }));
            AccessibleUiBridge.Instance?.ShowScreen("Leaderboard", actions.ToArray());
        }
    }
}
