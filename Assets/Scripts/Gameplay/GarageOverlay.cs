using BumperTanks.Frontend;
using UnityEngine;

namespace BumperTanks.Gameplay
{
    public sealed class GarageOverlay : MonoBehaviour
    {
        private bool open;
        private PlayerProfile profile;
        private TankController2D player;
        private string playerId;
        private string dailyRewardMessage = string.Empty;

        private void Start()
        {
            profile = ProfileStore.Load();
            playerId = ProfileStore.GetOrCreatePlayerId();
            player = FindFirstObjectByType<TankController2D>();
            ApplyColor();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.G)) open = !open;
        }

        public void OpenGarage() => open = true;

        private void OnGUI()
        {
            if (!open) return;
            GUI.Box(new Rect(Screen.width - 230, 12, 218, 246), "Garage");
            GUI.Label(new Rect(Screen.width - 210, 42, 180, 20), "Choose tank color");
            ColorButton("sky", new Color(0.15f, 0.75f, 1f), 70);
            ColorButton("coral", new Color(1f, 0.35f, 0.3f), 105);
            ColorButton("lime", new Color(0.35f, 1f, 0.45f), 140);
            if (GUI.Button(new Rect(Screen.width - 210, 175, 178, 28), "Claim daily 50 coins"))
            {
                GameServiceClient.Instance?.ClaimDailyReward(playerId, () =>
                {
                    profile = ProfileStore.Load();
                    profile.DailySpinDay = System.DateTime.UtcNow.DayOfYear;
                    ProfileStore.Save(profile);
                    dailyRewardMessage = "Daily reward claimed.";
                }, _ => dailyRewardMessage = "Reward unavailable. Try again later.");
            }
            if (!string.IsNullOrEmpty(dailyRewardMessage)) GUI.Label(new Rect(Screen.width - 210, 203, 178, 20), dailyRewardMessage);
            if (GUI.Button(new Rect(Screen.width - 210, 229, 178, 28), "Back to main menu"))
            {
                open = false;
                SendMessage("ShowFrontMenu", SendMessageOptions.DontRequireReceiver);
            }
        }

        public void CloseGarage() => open = false;

        private void ColorButton(string id, Color color, float y)
        {
            var label = profile.ColorId == id ? $"✓ {id}" : id;
            var previous = GUI.color;
            GUI.color = color;
            if (GUI.Button(new Rect(Screen.width - 210, y, 178, 26), label))
            {
                profile.ColorId = id;
                ProfileStore.Save(profile);
                ApplyColor();
            }
            GUI.color = previous;
        }

        private void ApplyColor()
        {
            if (player == null || !player.IsPlayer) return;
            var color = profile.ColorId switch
            {
                "coral" => new Color(1f, 0.35f, 0.3f),
                "lime" => new Color(0.35f, 1f, 0.45f),
                _ => new Color(0.15f, 0.75f, 1f)
            };
            player.GetComponent<SpriteRenderer>().color = color;
        }
    }
}
