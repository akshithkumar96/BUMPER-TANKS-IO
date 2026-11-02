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
            if (open) GameServiceClient.Instance?.FetchLeaderboard(result => entries = result);
        }

        public void OpenLeaderboard()
        {
            open = true;
            GameServiceClient.Instance?.FetchLeaderboard(result => entries = result);
        }

        private void OnGUI()
        {
            if (!open) return;
            GUI.Box(new Rect(Screen.width - 290, 230, 278, 250), "Leaderboard");
            for (var index = 0; index < Mathf.Min(entries.Length, 5); index++)
            {
                var entry = entries[index];
                var displayId = entry.playerId.Substring(0, Mathf.Min(8, entry.playerId.Length));
                GUI.Label(new Rect(Screen.width - 275, 260 + index * 28, 240, 24), $"{index + 1}. {displayId}  {entry.battleCoins} coins");
            }
            if (entries.Length == 0) GUI.Label(new Rect(Screen.width - 275, 260, 240, 24), "No scores yet");
            if (GUI.Button(new Rect(Screen.width - 275, 418, 248, 28), "Back to main menu"))
            {
                open = false;
                SendMessage("ShowFrontMenu", SendMessageOptions.DontRequireReceiver);
            }
        }

        public void CloseLeaderboard() => open = false;
    }
}
