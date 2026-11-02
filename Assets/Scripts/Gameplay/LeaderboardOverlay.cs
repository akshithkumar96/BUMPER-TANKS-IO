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
            GUI.Box(new Rect(Screen.width - 270, 230, 258, 210), "Leaderboard");
            for (var index = 0; index < Mathf.Min(entries.Length, 5); index++)
            {
                var entry = entries[index];
                var displayId = entry.playerId.Substring(0, Mathf.Min(8, entry.playerId.Length));
                GUI.Label(new Rect(Screen.width - 255, 260 + index * 28, 230, 24), $"{index + 1}. {displayId}  {entry.battleCoins} coins");
            }
            if (entries.Length == 0) GUI.Label(new Rect(Screen.width - 255, 260, 220, 24), "No scores yet");
        }
    }
}
