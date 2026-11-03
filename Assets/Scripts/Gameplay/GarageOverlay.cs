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
        private bool dailyRewardPending;
        private const float PanelWidth = 218f;

        private void Start()
        {
            profile = ProfileStore.Load();
            playerId = ProfileStore.GetOrCreatePlayerId();
            player = FindFirstObjectByType<TankController2D>();
            ApplyColor();
            ApplyTrail();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.G)) open = !open;
        }

        public void OpenGarage() => open = true;

        private void OnGUI()
        {
            if (!open) return;
            var left = Mathf.Max(8f, Screen.width - PanelWidth - 8f);
            GUI.Box(new Rect(left, 12, PanelWidth, 342), "Garage");
            GUI.Label(new Rect(left + 10, 40, 196, 20), "Choose tank color");
            ColorButton("sky", new Color(0.15f, 0.75f, 1f), left, 62);
            ColorButton("coral", new Color(1f, 0.35f, 0.3f), left, 88);
            ColorButton("lime", new Color(0.35f, 1f, 0.45f), left, 114);
            GUI.Label(new Rect(left + 10, 143, 196, 20), "Tank trail");
            TrailButton("none", left, 164);
            TrailButton("neon", left, 190);
            TrailButton("gold", left, 216);
            GUI.Label(new Rect(left + 10, 245, 196, 20), "Daily spin: 25 / 50 / 75 / 100");
            GUI.enabled = !dailyRewardPending;
            if (GUI.Button(new Rect(left + 10, 266, 198, 26), dailyRewardPending ? "Spinning..." : "Spin daily reward"))
            {
                dailyRewardPending = true;
                dailyRewardMessage = string.Empty;
                var client = GameServiceClient.Instance;
                if (client == null)
                {
                    dailyRewardPending = false;
                    dailyRewardMessage = "Service unavailable. Try again later.";
                }
                else
                {
                    client.ClaimDailyReward(playerId, reward =>
                    {
                        profile = ProfileStore.Load();
                        profile.DailySpinDay = System.DateTime.UtcNow.DayOfYear;
                        ProfileStore.Save(profile);
                        dailyRewardPending = false;
                        dailyRewardMessage = $"Won {reward} Battle Coins!";
                    }, _ =>
                    {
                        dailyRewardPending = false;
                        dailyRewardMessage = "Unavailable. Try again later.";
                    });
                }
            }
            GUI.enabled = true;
            if (!string.IsNullOrEmpty(dailyRewardMessage)) GUI.Label(new Rect(left + 10, 294, 198, 20), dailyRewardMessage);
            if (GUI.Button(new Rect(left + 10, 318, 198, 24), "Back to main menu"))
            {
                open = false;
                SendMessage("ShowFrontMenu", SendMessageOptions.DontRequireReceiver);
            }
        }

        public void CloseGarage() => open = false;

        private void ColorButton(string id, Color color, float left, float y)
        {
            var label = profile.ColorId == id ? $"* {id}" : id;
            var previous = GUI.color;
            GUI.color = color;
            if (GUI.Button(new Rect(left + 10, y, 198, 26), label))
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

        private void TrailButton(string id, float left, float y)
        {
            var label = profile.TrailId == id ? $"* {id}" : id;
            if (!GUI.Button(new Rect(left + 10, y, 198, 26), label)) return;
            profile.TrailId = id;
            ProfileStore.Save(profile);
            ApplyTrail();
        }

        private void ApplyTrail()
        {
            if (player == null || !player.IsPlayer) return;
            var trail = player.GetComponent<TrailRenderer>();
            if (trail == null) trail = player.gameObject.AddComponent<TrailRenderer>();
            trail.time = 0.65f;
            trail.startWidth = 0.34f;
            trail.endWidth = 0.02f;
            trail.numCapVertices = 4;
            trail.material = new Material(Shader.Find("Sprites/Default"));
            trail.sortingOrder = -1;
            var color = profile.TrailId switch
            {
                "neon" => new Color(0.1f, 1f, 0.9f),
                "gold" => new Color(1f, 0.72f, 0.15f),
                _ => Color.white
            };
            trail.startColor = color;
            trail.endColor = new Color(color.r, color.g, color.b, 0f);
            trail.emitting = profile.TrailId != "none";
            trail.enabled = profile.TrailId != "none";
        }
    }
}
