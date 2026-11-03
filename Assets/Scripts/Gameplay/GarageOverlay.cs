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
        private string cosmeticsMessage = string.Empty;
        private bool cosmeticPurchasePending;
        private GameServiceClient.CosmeticEntry[] cosmeticCatalog = System.Array.Empty<GameServiceClient.CosmeticEntry>();
        private int page;
        private const float PanelWidth = 218f;
        private const float PanelHeight = 384f;
        private static readonly string[] ColorOptions = { "sky", "coral", "lime" };
        private static readonly string[] TrailOptions = { "none", "neon", "gold" };

        private void Start()
        {
            profile = ProfileStore.Load();
            playerId = ProfileStore.GetOrCreatePlayerId();
            player = FindFirstObjectByType<TankController2D>();
            ApplyColor();
            ApplyTrail();
            player?.GetComponent<TankCosmeticView>()?.Configure(profile.FlagId, profile.BumpSoundId);
            GameServiceClient.Instance?.FetchCosmeticCatalog(items =>
            {
                cosmeticCatalog = items ?? System.Array.Empty<GameServiceClient.CosmeticEntry>();
                if (open && page == 1) ShowAccessibleScreen();
            });
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
            var panel = GuiSafeAreaLayout.Clamp(new Rect(safe.xMax - panelWidth, safe.center.y - PanelHeight * 0.5f, panelWidth, PanelHeight), safe);
            var left = panel.x;
            var top = panel.y;
            var controlWidth = Mathf.Max(0f, panelWidth - 20f);
            GUI.Box(panel, page switch { 0 => "Garage: Paint & Trail", 1 => "Garage: Flags & Sounds", _ => "Garage: Daily Reward" });
            if (page == 0)
            {
                GUI.Label(SafeFrame(left + 10, top + 30, controlWidth, 22, safe), "Tank color");
                ColorButton(left + 10, top + 52, controlWidth, safe);
                GUI.Label(SafeFrame(left + 10, top + 112, controlWidth, 22, safe), "Tank trail");
                TrailButton(left + 10, top + 134, controlWidth, safe);
            }
            else if (page == 1)
            {
                GUI.Label(SafeFrame(left + 10, top + 24, controlWidth, 22, safe), "Tank flag");
                if (GUI.Button(SafeFrame(left + 10, top + 46, controlWidth, 44, safe), $"{FlagName(profile.FlagId)}  |  tap to change")) CycleFlag();
                DrawUnlockButton(left + 10, top + 94, controlWidth, "flag");
                GUI.Label(SafeFrame(left + 10, top + 166, controlWidth, 22, safe), "Bump sound");
                if (GUI.Button(SafeFrame(left + 10, top + 188, controlWidth, 44, safe), $"{SoundName(profile.BumpSoundId)}  |  tap to change")) CycleSound();
                DrawUnlockButton(left + 10, top + 236, controlWidth, "sound");
                if (!string.IsNullOrEmpty(cosmeticsMessage)) GUI.Label(SafeFrame(left + 10, top + 284, controlWidth, 22, safe), cosmeticsMessage);
            }
            else
            {
                GUI.Label(SafeFrame(left + 10, top + 38, controlWidth, 22, safe), "Daily spin: 25, 50, 75, or 100 coins");
                GUI.enabled = !dailyRewardPending;
                if (GUI.Button(SafeFrame(left + 10, top + 66, controlWidth, 44, safe), dailyRewardPending ? "Spinning..." : "Spin daily reward")) RequestDailyReward();
                GUI.enabled = true;
                if (!string.IsNullOrEmpty(dailyRewardMessage)) GUI.Label(SafeFrame(left + 10, top + 118, controlWidth, 38, safe), dailyRewardMessage);
            }

            var footerY = top + 332f;
            if (GUI.Button(SafeFrame(left + 10, footerY, 44, 44, safe), "Prev")) PreviousPage();
            if (GUI.Button(SafeFrame(left + 64, footerY, 90, 44, safe), "Main Menu")) ReturnToMenu();
            if (GUI.Button(SafeFrame(left + 164, footerY, 44, 44, safe), "Next")) NextPage();
        }

        public void CloseGarage() => open = false;

        private void ShowAccessibleScreen()
        {
            if (profile == null) return;
            var safe = GuiSafeAreaLayout.Current;
            var panelWidth = Mathf.Min(PanelWidth, safe.width);
            var panel = GuiSafeAreaLayout.Clamp(new Rect(safe.xMax - panelWidth, safe.center.y - PanelHeight * 0.5f, panelWidth, PanelHeight), safe);
            var left = panel.x;
            var top = panel.y;
            var width = Mathf.Max(0f, panelWidth - 20f);
            var actions = new System.Collections.Generic.List<AccessibleUiAction>();
            if (page == 0)
            {
                actions.Add(new AccessibleUiAction($"Tank color, currently {profile.ColorId}", "Activate to cycle to the next color.", SafeFrame(left + 10, top + 52, width, 44, safe), CycleColor));
                actions.Add(new AccessibleUiAction($"Tank trail, currently {profile.TrailId}", "Activate to cycle to the next trail.", SafeFrame(left + 10, top + 134, width, 44, safe), CycleTrail));
            }
            else if (page == 1)
            {
                actions.Add(new AccessibleUiAction($"Flag, currently {FlagName(profile.FlagId)}", "Activate to cycle through unlocked flags.", SafeFrame(left + 10, top + 46, width, 44, safe), CycleFlag));
                actions.Add(UnlockAction(left + 10, top + 94, width, safe, "flag"));
                actions.Add(new AccessibleUiAction($"Bump sound, currently {SoundName(profile.BumpSoundId)}", "Activate to cycle through unlocked bump sounds.", SafeFrame(left + 10, top + 188, width, 44, safe), CycleSound));
                actions.Add(UnlockAction(left + 10, top + 236, width, safe, "sound"));
            }
            else
            {
                actions.Add(new AccessibleUiAction(dailyRewardPending ? "Daily reward is spinning" : "Spin daily reward", "Available once each day. The reward is added to your Battle Coins.", SafeFrame(left + 10, top + 66, width, 44, safe), RequestDailyReward));
            }
            actions.Add(new AccessibleUiAction("Previous garage page", "Show the previous garage category.", SafeFrame(left + 10, top + 332, 44, 44, safe), PreviousPage));
            actions.Add(new AccessibleUiAction("Back to main menu", "Close the garage and return to the main menu.", SafeFrame(left + 64, top + 332, 90, 44, safe), ReturnToMenu));
            actions.Add(new AccessibleUiAction("Next garage page", "Show the next garage category.", SafeFrame(left + 164, top + 332, 44, 44, safe), NextPage));
            AccessibleUiBridge.Instance?.ShowScreen($"Garage. {PageName(page)}.", actions.ToArray());
        }

        private void PreviousPage() { page = (page + 2) % 3; cosmeticsMessage = string.Empty; ShowAccessibleScreen(); }
        private void NextPage() { page = (page + 1) % 3; cosmeticsMessage = string.Empty; ShowAccessibleScreen(); }

        private void CycleFlag()
        {
            var choices = new System.Collections.Generic.List<string> { "none" };
            foreach (var item in cosmeticCatalog)
                if (item.category == "flag" && IsOwned(item.id)) choices.Add(item.id);
            var index = choices.IndexOf(profile.FlagId);
            profile.FlagId = choices[(index + 1) % choices.Count];
            ProfileStore.Save(profile);
            player?.GetComponent<TankCosmeticView>()?.ApplyFlag(profile.FlagId);
            ShowAccessibleScreen();
        }

        private void CycleSound()
        {
            var choices = new System.Collections.Generic.List<string> { "impact", "silent" };
            foreach (var item in cosmeticCatalog)
                if (item.category == "sound" && IsOwned(item.id)) choices.Add(item.id);
            var index = choices.IndexOf(profile.BumpSoundId);
            profile.BumpSoundId = choices[(index + 1) % choices.Count];
            ProfileStore.Save(profile);
            player?.GetComponent<TankCosmeticView>()?.SetBumpSound(profile.BumpSoundId);
            ShowAccessibleScreen();
        }

        private void DrawUnlockButton(float x, float y, float width, string category)
        {
            var item = NextLockedItem(category);
            var label = cosmeticPurchasePending ? "Purchasing..." : item == null ? $"All {category}s unlocked" : $"Unlock {ItemName(item.id)} ({item.price} coins)";
            var wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && !cosmeticPurchasePending && item != null;
            if (GUI.Button(SafeFrame(x, y, width, 44, GuiSafeAreaLayout.Current), label)) Purchase(item);
            GUI.enabled = wasEnabled;
        }

        private AccessibleUiAction UnlockAction(float x, float y, float width, Rect safe, string category)
        {
            var item = NextLockedItem(category);
            if (item == null)
                return new AccessibleUiAction($"All {category}s unlocked", $"All available {category} cosmetics are unlocked.", SafeFrame(x, y, width, 44, safe), null, false);
            var label = cosmeticPurchasePending ? "Cosmetic purchase pending" : $"Unlock {ItemName(item.id)} for {item.price} Battle Coins";
            return new AccessibleUiAction(label, "Purchase this cosmetic with Battle Coins earned in matches or daily rewards.", SafeFrame(x, y, width, 44, safe), () => Purchase(item));
        }

        private GameServiceClient.CosmeticEntry NextLockedItem(string category)
        {
            foreach (var item in cosmeticCatalog)
                if (item.category == category && !IsOwned(item.id)) return item;
            return null;
        }

        private bool IsOwned(string cosmeticId) =>
            profile.OwnedCosmetics != null && System.Array.IndexOf(profile.OwnedCosmetics, cosmeticId) >= 0;

        private void Purchase(GameServiceClient.CosmeticEntry item)
        {
            if (item == null || cosmeticPurchasePending) return;
            var service = GameServiceClient.Instance;
            if (service == null)
            {
                cosmeticsMessage = "Service unavailable.";
                ShowAccessibleScreen();
                return;
            }

            cosmeticPurchasePending = true;
            cosmeticsMessage = string.Empty;
            ShowAccessibleScreen();
            service.PurchaseCosmetic(playerId, item.id, _ =>
            {
                profile = ProfileStore.Load();
                if (item.category == "flag") profile.FlagId = item.id;
                else if (item.category == "sound") profile.BumpSoundId = item.id;
                ProfileStore.Save(profile);
                player?.GetComponent<TankCosmeticView>()?.Configure(profile.FlagId, profile.BumpSoundId);
                cosmeticPurchasePending = false;
                cosmeticsMessage = $"Unlocked {ItemName(item.id)}.";
                AccessibleUiBridge.Instance?.Announce(cosmeticsMessage);
                ShowAccessibleScreen();
            }, error =>
            {
                cosmeticPurchasePending = false;
                cosmeticsMessage = error.Contains("409") ? "Not enough coins or already unlocked." : "Purchase failed. Try again.";
                AccessibleUiBridge.Instance?.Announce(cosmeticsMessage);
                ShowAccessibleScreen();
            });
        }

        private static string PageName(int currentPage) => currentPage switch
        {
            0 => "Paint and trail",
            1 => "Flags and sounds",
            _ => "Daily reward"
        };

        private static string ItemName(string id) => id switch
        {
            "flag_checkered" => "Checkered Flag",
            "flag_comet" => "Comet Flag",
            "sound_arcade" => "Arcade Bump Sound",
            "sound_laser" => "Laser Bump Sound",
            _ => id
        };

        private static string FlagName(string id) => id switch
        {
            "none" => "No flag",
            "flag_checkered" => "Checkered flag",
            "flag_comet" => "Comet flag",
            _ => id
        };

        private static string SoundName(string id) => id switch
        {
            "impact" => "Impact",
            "silent" => "Silent",
            "sound_arcade" => "Arcade",
            "sound_laser" => "Laser",
            _ => id
        };

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
