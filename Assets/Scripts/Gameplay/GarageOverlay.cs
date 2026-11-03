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
        private static readonly string[] ColorOptions = { "sky", "coral", "lime" };
        private static readonly string[] TrailOptions = { "none", "neon", "gold" };

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
            if (!Input.GetKeyDown(KeyCode.G)) return;
            open = !open;
            if (open) ShowAccessibleScreen();
            else SendMessage("RefreshCurrentAccessibility", SendMessageOptions.DontRequireReceiver);
        }

        public void OpenGarage()
        {
            open = true;
            ShowAccessibleScreen();
        }

        private void OnGUI()
        {
            if (!open) return;
            var safe = GuiSafeAreaLayout.Current;
            var panelWidth = Mathf.Min(PanelWidth, safe.width);
            var panel = GuiSafeAreaLayout.Clamp(new Rect(safe.xMax - panelWidth, safe.center.y - 154f, panelWidth, 308f), safe);
            var left = panel.x;
            var top = panel.y;
            var controlWidth = Mathf.Max(0f, panelWidth - 20f);
            GUI.Box(panel, "Garage");
            GUI.Label(SafeFrame(left + 10, top + 18, controlWidth, 22, safe), "Tank color");
            ColorButton(left + 10, top + 40, controlWidth, safe);
            GUI.Label(SafeFrame(left + 10, top + 86, controlWidth, 22, safe), "Tank trail");
            TrailButton(left + 10, top + 108, controlWidth, safe);
            GUI.Label(SafeFrame(left + 10, top + 154, controlWidth, 22, safe), "Daily spin: 25, 50, 75, or 100");
            GUI.enabled = !dailyRewardPending;
            if (GUI.Button(SafeFrame(left + 10, top + 176, controlWidth, 44, safe), dailyRewardPending ? "Spinning..." : "Spin daily reward"))
                RequestDailyReward();
            GUI.enabled = true;
            if (!string.IsNullOrEmpty(dailyRewardMessage)) GUI.Label(SafeFrame(left + 10, top + 222, controlWidth, 24, safe), dailyRewardMessage);
            if (GUI.Button(SafeFrame(left + 10, top + 254, controlWidth, 44, safe), "Back to main menu"))
                ReturnToMenu();
        }

        public void CloseGarage() => open = false;

        private void ShowAccessibleScreen()
        {
            if (profile == null) return;
            var safe = GuiSafeAreaLayout.Current;
            var panelWidth = Mathf.Min(PanelWidth, safe.width);
            var panel = GuiSafeAreaLayout.Clamp(new Rect(safe.xMax - panelWidth, safe.center.y - 154f, panelWidth, 308f), safe);
            var left = panel.x;
            var top = panel.y;
            var width = Mathf.Max(0f, panelWidth - 20f);
            AccessibleUiBridge.Instance?.ShowScreen("Garage", new AccessibleUiAction[]
            {
                new($"Tank color, currently {profile.ColorId}", "Activate to cycle to the next color.", SafeFrame(left + 10, top + 40, width, 44, safe), CycleColor),
                new($"Tank trail, currently {profile.TrailId}", "Activate to cycle to the next trail.", SafeFrame(left + 10, top + 108, width, 44, safe), CycleTrail),
                new(dailyRewardPending ? "Daily reward is spinning" : "Spin daily reward", "Available once each day. The reward is added to your Battle Coins.", SafeFrame(left + 10, top + 176, width, 44, safe), RequestDailyReward),
                new("Back to main menu", "Close the garage and return to the main menu.", SafeFrame(left + 10, top + 254, width, 44, safe), ReturnToMenu)
            });
        }

        private void CycleColor()
        {
            var index = System.Array.IndexOf(ColorOptions, profile.ColorId);
            if (index < 0) index = 0;
            profile.ColorId = ColorOptions[(index + 1) % ColorOptions.Length];
            ProfileStore.Save(profile);
            ApplyColor();
            ShowAccessibleScreen();
        }

        private void CycleTrail()
        {
            var index = System.Array.IndexOf(TrailOptions, profile.TrailId);
            if (index < 0) index = 0;
            profile.TrailId = TrailOptions[(index + 1) % TrailOptions.Length];
            ProfileStore.Save(profile);
            ApplyTrail();
            ShowAccessibleScreen();
        }

        private void RequestDailyReward()
        {
            if (dailyRewardPending) return;
            dailyRewardPending = true;
            dailyRewardMessage = string.Empty;
            ShowAccessibleScreen();
            var client = GameServiceClient.Instance;
            if (client == null)
            {
                dailyRewardPending = false;
                dailyRewardMessage = "Service unavailable. Try again later.";
                ShowAccessibleScreen();
                return;
            }

            client.ClaimDailyReward(playerId, reward =>
            {
                profile = ProfileStore.Load();
                profile.DailySpinDay = System.DateTime.UtcNow.DayOfYear;
                ProfileStore.Save(profile);
                dailyRewardPending = false;
                dailyRewardMessage = $"Won {reward} Battle Coins!";
                AccessibleUiBridge.Instance?.Announce(dailyRewardMessage);
                ShowAccessibleScreen();
            }, _ =>
            {
                dailyRewardPending = false;
                dailyRewardMessage = "Unavailable. Try again later.";
                AccessibleUiBridge.Instance?.Announce(dailyRewardMessage);
                ShowAccessibleScreen();
            });
        }

        private void ReturnToMenu()
        {
            open = false;
            SendMessage("ShowFrontMenu", SendMessageOptions.DontRequireReceiver);
        }

        private void ColorButton(float x, float y, float width, Rect safe)
        {
            var options = ColorOptions;
            var index = System.Array.IndexOf(options, profile.ColorId);
            if (index < 0) index = 0;
            var color = profile.ColorId switch
            {
                "coral" => new Color(1f, 0.35f, 0.3f),
                "lime" => new Color(0.35f, 1f, 0.45f),
                _ => new Color(0.15f, 0.75f, 1f)
            };
            var oldBackground = GUI.backgroundColor;
            var oldContent = GUI.contentColor;
            GUI.backgroundColor = color;
            GUI.contentColor = Color.black;
            var label = $"{profile.ColorId}  |  tap to change";
            if (GUI.Button(SafeFrame(x, y, width, 44, safe), label))
            {
                CycleColor();
            }
            GUI.backgroundColor = oldBackground;
            GUI.contentColor = oldContent;
        }

        private void TrailButton(float x, float y, float width, Rect safe)
        {
            var options = TrailOptions;
            var index = System.Array.IndexOf(options, profile.TrailId);
            if (index < 0) index = 0;
            if (!GUI.Button(SafeFrame(x, y, width, 44, safe), $"{profile.TrailId} trail  |  tap to change")) return;
            CycleTrail();
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

        private void ApplyTrail()
        {
            if (player == null || !player.IsPlayer) return;
            var trail = player.GetComponent<TrailRenderer>();
            if (trail == null && profile.TrailId == "none") return;
            if (trail == null) trail = player.gameObject.AddComponent<TrailRenderer>();
            trail.time = 0.65f;
            trail.startWidth = 0.34f;
            trail.endWidth = 0.02f;
            trail.numCapVertices = 4;
            if (trail.sharedMaterial == null)
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader != null) trail.material = new Material(shader);
            }
            trail.sortingOrder = -1;
            var color = profile.TrailId switch
            {
                "neon" => new Color(0.1f, 1f, 0.9f),
                "gold" => new Color(1f, 0.72f, 0.15f),
                _ => Color.white
            };
            trail.startColor = color;
            trail.endColor = new Color(color.r, color.g, color.b, 0f);
            if (profile.TrailId == "none") trail.Clear();
            trail.emitting = profile.TrailId != "none";
            trail.enabled = profile.TrailId != "none";
        }

        private static Rect SafeFrame(float x, float y, float width, float height, Rect safe) =>
            GuiSafeAreaLayout.Clamp(new Rect(x, y, width, height), safe);
    }
}
