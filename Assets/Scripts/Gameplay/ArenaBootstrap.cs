using BumperTanks.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using BumperTanks.Frontend;
using BumperTanks.Match;
using BumperTanks.Network;
using System.Collections.Generic;
using System;

namespace BumperTanks.Gameplay
{
    public sealed class ArenaBootstrap : MonoBehaviour
    {
        public const string RealtimeEndpointPlayerPrefsKey = ServiceEndpointSettings.DevelopmentRealtimeOverridePlayerPrefsKey;
        private static ArenaBootstrap current;
        private string realtimeEndpoint;
        private string endpointOverrideError;
        private TankController2D player;
        private MatchDirector match;
        private WebSocketRealtimeSession realtime;
        private readonly Dictionary<int, RemoteTankVisual> remoteTanks = new();
        private readonly Dictionary<int, Transform> onlineGemVisuals = new();
        private readonly Dictionary<int, Transform> onlinePowerUpVisuals = new();
        private readonly Dictionary<int, TankArchetype> onlineArchetypes = new();
        private readonly Dictionary<int, string> onlinePlayerNames = new();
        private readonly HashSet<int> aliveOnlinePlayers = new();
        private string localPlayerId;
        private string realtimeStatus = "Offline";
        private readonly InputSendCadence inputSendCadence = new();
        private int inputTick = -1;
        private int onlinePlayerId = -1;
        private int appliedArenaTick = -1;
        private int onlinePlayerCount;
        private int onlineWinnerPlayerId;
        private int onlineEliminations;
        private int onlineGemCount;
        private int onlineMatchAward;
        private int appliedPickupTick = -1;
        private int appliedShotTick = -1;
        private float onlineRemainingSeconds;
        private string onlinePhase = string.Empty;
        private bool onlineMatch;
        private bool onlineMatchResultReceived;
        private bool showFrontMenu = true;
        private bool showDevelopmentServerSettings;
        private string developmentApiDraft = string.Empty;
        private string developmentServerStatus = string.Empty;
        private bool matchStarted;
        private bool resultAccessibilityShown;
        private AccessibleUiBridge accessibleUi;

        private void Awake()
        {
            current = this;
            var serviceClient = FindFirstObjectByType<GameServiceClient>();
            if (serviceClient == null) serviceClient = new GameObject("Game Service Client").AddComponent<GameServiceClient>();
            realtimeEndpoint = serviceClient.RealtimeEndpoint;
            var endpointSettings = ServiceEndpointSettings.Load();
            developmentApiDraft = PlayerPrefs.GetString(ServiceEndpointSettings.DevelopmentApiOverridePlayerPrefsKey,
                endpointSettings != null ? endpointSettings.ApiBaseUrl : string.Empty);
            var apiOverrideConfigured = !string.IsNullOrWhiteSpace(
                PlayerPrefs.GetString(ServiceEndpointSettings.DevelopmentApiOverridePlayerPrefsKey, string.Empty));
            var configuredEndpoint = PlayerPrefs.GetString(RealtimeEndpointPlayerPrefsKey, string.Empty);
            if (!apiOverrideConfigured && !string.IsNullOrWhiteSpace(configuredEndpoint))
            {
                var isDevelopmentBuild = Application.isEditor || Debug.isDebugBuild;
                if (ServiceEndpointSettings.TryValidateRealtimeOverride(configuredEndpoint, isDevelopmentBuild, out var normalized, out var error))
                    realtimeEndpoint = normalized;
                else if (isDevelopmentBuild)
                {
                    realtimeEndpoint = string.Empty;
                    endpointOverrideError = "Saved realtime endpoint is invalid: " + error;
                }
                else
                {
                    Debug.LogWarning("Ignoring insecure saved realtime endpoint in this release: " + error);
                    PlayerPrefs.DeleteKey(RealtimeEndpointPlayerPrefsKey);
                    PlayerPrefs.Save();
                }
            }
            localPlayerId = ProfileStore.GetOrCreatePlayerId();
            Time.timeScale = 0f;
            match = gameObject.AddComponent<MatchDirector>();
            CreateCamera();
            CreateArenaBounds();
            player = CreateTank("Player Speedy Bumper", new Vector2(0f, -3f), TankArchetype.SpeedyBumper, true, new Color(0.15f, 0.75f, 1f));
            gameObject.AddComponent<GarageOverlay>();
            gameObject.AddComponent<LeaderboardOverlay>();
            accessibleUi = gameObject.AddComponent<AccessibleUiBridge>();
            realtime = gameObject.AddComponent<WebSocketRealtimeSession>();
            realtime.RoomJoined += OnRealtimeRoomJoined;
            realtime.SnapshotReceived += OnRealtimeSnapshot;
            realtime.PickupFrameReceived += OnRealtimePickupFrame;
            realtime.ShotFrameReceived += OnRealtimeShotFrame;
            realtime.MatchReceiptReceived += OnRealtimeMatchReceipt;
            realtime.Error += OnRealtimeError;
            realtime.Disconnected += OnRealtimeDisconnected;
            var heavy = CreateTank("Heavy Dozer Bot", new Vector2(-4f, 3f), TankArchetype.HeavyDozer, false, new Color(1f, 0.55f, 0.15f));
            heavy.gameObject.AddComponent<BotDriver>();
            var spinner = CreateTank("Ricochet Spinner Bot", new Vector2(4f, 3f), TankArchetype.RicochetSpinner, false, new Color(0.9f, 0.25f, 0.8f));
            spinner.gameObject.AddComponent<BotDriver>();
            CreatePowerUp(new Vector2(-2.5f, 0f), PowerUpType.Nitro, new Color(1f, 0.9f, 0.1f));
            CreatePowerUp(new Vector2(0f, 2f), PowerUpType.MegaBullet, new Color(1f, 0.25f, 0.25f));
            CreatePowerUp(new Vector2(2.5f, 0f), PowerUpType.Magnet, new Color(0.3f, 1f, 0.45f));
            for (var i = 0; i < 12; i++)
            {
                var gem = new GameObject("Gem");
                gem.transform.position = new Vector2(UnityEngine.Random.Range(-6f, 6f), UnityEngine.Random.Range(-4f, 4f));
                gem.AddComponent<CircleCollider2D>();
                gem.AddComponent<GemPickup>();
            }
            ShowMainMenuAccessibility();
        }

        private void OnDestroy()
        {
            if (current == this) current = null;
        }

        private void Update()
        {
            UpdateDevelopmentServerStatus();
            UpdateRealtimeInput();
            UpdateRemoteTanks();
            var resultReady = onlineMatch ? onlinePhase == "finished" : match != null && (match.Winner != null || match.Phase == MatchPhase.Complete);
            if (resultAccessibilityShown || !resultReady) return;
            resultAccessibilityShown = true;
            ShowResultAccessibility();
        }

        private System.Collections.IEnumerator TrackArenaStarted()
        {
            yield return null;
            GameServiceClient.Instance?.Track(localPlayerId, "match_started");
        }

        private void OnGUI()
        {
            var safe = GuiSafeAreaLayout.Current;
            if (showFrontMenu)
            {
                var showDevelopmentServerButton = IsDevelopmentBuild;
                var menu = GuiSafeAreaLayout.FrontMenu(safe, showDevelopmentServerButton);
                var panel = menu.Panel;
                GUI.Box(panel, "BUMPER TANKS IO");
                GUI.Label(GuiSafeAreaLayout.Clamp(new Rect(panel.center.x - 140f, panel.y + 45f, 280f, 25f), safe), "Top down arena brawler");
                if (GUI.Button(MenuFrame(panel, -115f, -70f, 230f, 40f), "Play Arena"))
                    BeginArena();
                if (GUI.Button(MenuFrame(panel, -115f, -20f, 110f, 34f), "Garage (G)"))
                {
                    OpenGarageFromMenu();
                }
                if (GUI.Button(MenuFrame(panel, 5f, -20f, 110f, 34f), "Scores (L)"))
                {
                    OpenLeaderboardFromMenu();
                }
                var telemetryStatus = !TelemetryConsentSettings.HasChoice
                    ? "Optional: share gameplay activity from this profile to improve balance."
                    : $"Gameplay activity sharing: {(TelemetryConsentSettings.IsAllowed ? "On" : "Off")}. Change any time.";
                GUI.Label(menu.TelemetryStatus, telemetryStatus);
                if (GUI.Button(menu.AllowButton, "Allow")) SetTelemetryConsent(true);
                if (GUI.Button(menu.DeclineButton, "No thanks")) SetTelemetryConsent(false);
                GUI.Label(menu.CurrencyLabel, $"Battle Coins: {ProfileStore.Load().BattleCoins}");
                if (showDevelopmentServerButton && GUI.Button(menu.DevelopmentServerButton, "Server URL (Dev)"))
                    OpenDevelopmentServerSettings();
                if (showDevelopmentServerSettings)
                {
                    DrawDevelopmentServerSettings(safe);
                    return;
                }
                return;
            }
            GUI.Box(GuiSafeAreaLayout.Clamp(new Rect(safe.x + 4f, safe.y + 4f, 345, 92), safe), onlineMatch ? "BUMPER TANKS IO - Online Arena" : "BUMPER TANKS IO - Offline Arena");
            GUI.Label(GuiSafeAreaLayout.Clamp(new Rect(safe.x + 17f, safe.y + 28f, 320, 24), safe), "Desktop: WASD/arrows, mouse, click release, E, G garage | Mobile: left drag, right release");
            GUI.Label(GuiSafeAreaLayout.Clamp(new Rect(safe.x + 17f, safe.y + 52f, 320, 24), safe), player != null ? $"{player.Archetype}  Health: {player.Health:0}" : "Player eliminated");
            GUI.Label(GuiSafeAreaLayout.Clamp(new Rect(safe.x + 17f, safe.y + 94f, 320, 24), safe), $"Battle Coins: {ProfileStore.Load().BattleCoins}");
            var matchLine = onlineMatch
                ? $"{(string.IsNullOrEmpty(onlinePhase) ? "Matchmaking" : onlinePhase.Replace('_', ' '))}  {Mathf.CeilToInt(onlineRemainingSeconds):0}s  Tanks: {aliveOnlinePlayers.Count}/8"
                : match != null ? $"{match.Phase}  {Mathf.CeilToInt(90f - match.Elapsed):0}s  Tanks: {match.RemainingPlayers}" : string.Empty;
            GUI.Label(GuiSafeAreaLayout.Clamp(new Rect(safe.x + 17f, safe.y + 115f, 320, 24), safe), matchLine);
            var overlayY = safe.yMax - 48f;
            if (GUI.Button(GuiSafeAreaLayout.Clamp(new Rect(safe.x + 4f, overlayY, 112, 44), safe), "Garage")) OpenGarageFromMatch();
            if (GUI.Button(GuiSafeAreaLayout.Clamp(new Rect(safe.x + 124f, overlayY, 112, 44), safe), "Scores")) OpenLeaderboardFromMatch();
            if (player != null)
            {
                var ability = player.GetComponent<TankAbilityController>();
                if (ability != null)
                {
                    GUI.Label(GuiSafeAreaLayout.Clamp(new Rect(safe.x + 17f, safe.y + 136f, 320, 24), safe), ability.ReadyIn <= 0f ? "Ability ready (E)" : $"Ability cooldown: {ability.ReadyIn:0.0}s");
                    if (player.gameObject.activeInHierarchy && match != null && match.Phase != MatchPhase.Complete && match.Winner == null &&
                        GUI.Button(GuiSafeAreaLayout.Clamp(new Rect(safe.xMax - 148f, safe.yMax - 68f, 136f, 60f), safe), ability.ReadyIn <= 0f ? "ABILITY (E)" : $"READY IN {ability.ReadyIn:0.0}s"))
                        ability.Activate();
                }
            }
            if (match != null)
            {
                var gems = player != null && !onlineMatch ? match.GemsFor(player) : 0;
                var eliminations = onlineMatch ? onlineEliminations : player != null ? match.Scores.EliminationsFor(player.GetInstanceID()) : 0;
                GUI.Label(GuiSafeAreaLayout.Clamp(new Rect(safe.x + 17f, safe.y + 157f, 360, 24), safe), onlineMatch ? $"Online Gems: {onlineGemCount}  KOs: {eliminations}" : $"Gems: {gems}  KOs: {eliminations}");
                GUI.Label(GuiSafeAreaLayout.Clamp(new Rect(safe.x + 17f, safe.y + 178f, 320, 24), safe), RealtimeStatusText());
                if (CanOfferRealtimeRetry() && GUI.Button(GuiSafeAreaLayout.Clamp(new Rect(safe.x + 17f, safe.y + 204f, 170f, 44f), safe), "Retry Online"))
                    RetryOnline();
                if (onlineMatch ? onlinePhase == "finished" : match.Winner != null || match.Phase == BumperTanks.Match.MatchPhase.Complete)
                {
                    var label = onlineMatch
                        ? $"Winner: {OnlineWinnerName()}"
                        : match.Winner != null ? $"Winner: {match.Winner.name}" : "Time up";
                    var receivedOnlineReward = onlineMatch && onlineMatchResultReceived;
                    var panel = GuiSafeAreaLayout.Centered(290f, receivedOnlineReward ? 170f : 140f, safe);
                    GUI.Box(panel, label);
                    if (receivedOnlineReward)
                        GUI.Label(GuiSafeAreaLayout.Clamp(new Rect(panel.center.x - 130f, panel.center.y - 20f, 260f, 24f), safe), $"Earned {onlineMatchAward} Battle Coins");
                    if (GUI.Button(GuiSafeAreaLayout.Clamp(new Rect(panel.center.x - 80f, panel.center.y + (receivedOnlineReward ? 20f : 5f), 160f, 36f), safe), "Play Again"))
                    {
                        GameServiceClient.Instance?.Track(localPlayerId, "match_rematch");
                        RestartArena();
                    }
                }
            }
        }

        public void ShowFrontMenu()
        {
            showFrontMenu = true;
            Time.timeScale = 0f;
            ShowMainMenuAccessibility();
        }

        public static void ConfigureRealtimeEndpoint(string endpoint)
        {
            if (!ServiceEndpointSettings.TryValidateRealtimeOverride(endpoint,
                Application.isEditor || Debug.isDebugBuild, out var normalized, out var error))
                throw new ArgumentException(error, nameof(endpoint));
            PlayerPrefs.SetString(RealtimeEndpointPlayerPrefsKey, normalized);
            PlayerPrefs.Save();
            if (current != null)
            {
                current.realtimeEndpoint = normalized;
                current.endpointOverrideError = null;
            }
        }

        private void BeginArena()
        {
            showFrontMenu = false;
            Time.timeScale = 1f;
            ShowMatchAccessibility();
            if (realtime != null && !realtime.IsConnected && !realtime.IsConnecting)
            {
                var service = GameServiceClient.Instance;
                if (!string.IsNullOrEmpty(endpointOverrideError)) realtimeStatus = "Online unavailable. " + endpointOverrideError;
                else if (service == null || !service.EndpointConfigurationValid) realtimeStatus = "Online unavailable. " + (service != null ? service.EndpointConfigurationError : "Service client unavailable.");
                else if (string.IsNullOrEmpty(realtimeEndpoint)) realtimeStatus = "Online unavailable. Configure a secure realtime endpoint in ServiceEndpointSettings.";
                else
                {
                    realtimeStatus = "Connecting to online matchmaking…";
                    service.ConnectRealtime(realtime, realtimeEndpoint, "Player", player != null ? player.Archetype.ToString() : "SpeedyBumper", OnRealtimeError);
                }
            }
            if (matchStarted) return;
            matchStarted = true;
            StartCoroutine(TrackArenaStarted());
        }

        private void OpenGarageFromMenu()
        {
            showFrontMenu = false;
            SendMessage("CloseLeaderboard", SendMessageOptions.DontRequireReceiver);
            SendMessage("OpenGarage", SendMessageOptions.DontRequireReceiver);
        }

        private void OpenLeaderboardFromMenu()
        {
            showFrontMenu = false;
            SendMessage("CloseGarage", SendMessageOptions.DontRequireReceiver);
            SendMessage("OpenLeaderboard", SendMessageOptions.DontRequireReceiver);
        }

        private void SetTelemetryConsent(bool allowed)
        {
            TelemetryConsentSettings.SetAllowed(allowed);
            accessibleUi?.Announce(allowed ? "Anonymous gameplay sharing enabled." : "Anonymous gameplay sharing disabled.");
            ShowMainMenuAccessibility();
        }

        private void OpenGarageFromMatch()
        {
            Time.timeScale = 0f;
            SendMessage("CloseLeaderboard", SendMessageOptions.DontRequireReceiver);
            SendMessage("OpenGarage", SendMessageOptions.DontRequireReceiver);
        }

        private void OpenLeaderboardFromMatch()
        {
            Time.timeScale = 0f;
            SendMessage("CloseGarage", SendMessageOptions.DontRequireReceiver);
            SendMessage("OpenLeaderboard", SendMessageOptions.DontRequireReceiver);
        }

        private void RestartArena()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        private void ShowMainMenuAccessibility()
        {
            var showDevelopmentServerButton = IsDevelopmentBuild;
            var menu = GuiSafeAreaLayout.FrontMenu(GuiSafeAreaLayout.Current, showDevelopmentServerButton);
            var panel = menu.Panel;
            var actions = new List<AccessibleUiAction>
            {
                new("Play Arena", "Start the local arena match. Telemetry choice is optional.", MenuFrame(panel, -115f, -70f, 230f, 40f), BeginArena),
                new("Garage", "Change tank color or trail and claim the daily reward.", MenuFrame(panel, -115f, -20f, 110f, 34f), OpenGarageFromMenu),
                new("Leaderboard", "View Battle Coins rankings.", MenuFrame(panel, 5f, -20f, 110f, 34f), OpenLeaderboardFromMenu),
                new("Telemetry sharing choice", !TelemetryConsentSettings.HasChoice
                    ? "Optional. Choose whether to share gameplay activity from this profile to improve balance. You can play without choosing."
                    : $"Current choice: {(TelemetryConsentSettings.IsAllowed ? "on" : "off")}. You can change it any time.",
                    menu.TelemetryStatus, null, false),
                new("Allow gameplay sharing", "Send gameplay activity linked to this game profile to help improve balance. No name or email is sent.", menu.AllowButton, () => SetTelemetryConsent(true)),
                new("No thanks", "Keep optional gameplay telemetry disabled. You can change this choice from the main menu.", menu.DeclineButton, () => SetTelemetryConsent(false))
            };
            if (showDevelopmentServerButton)
                actions.Add(new AccessibleUiAction("Development server URL", "Set the API URL for a backend on your local network. Only available in Development builds.",
                    menu.DevelopmentServerButton, OpenDevelopmentServerSettings));
            accessibleUi?.ShowScreen("Bumper Tanks main menu", actions.ToArray());
        }

        private bool IsDevelopmentBuild => Application.isEditor || Debug.isDebugBuild;

        private void OpenDevelopmentServerSettings()
        {
            if (!IsDevelopmentBuild) return;
            showDevelopmentServerSettings = true;
            var service = GameServiceClient.Instance;
            if (string.IsNullOrWhiteSpace(developmentApiDraft) && service != null && service.ApiBaseUrl != "http://127.0.0.1:5188")
                developmentApiDraft = service.ApiBaseUrl;
            developmentServerStatus = service == null ? "Service client is unavailable." :
                service.IdentityReady ? "Online identity is ready for this API." :
                !string.IsNullOrEmpty(service.IdentityError) ? "Identity setup failed: " + service.IdentityError :
                "Enter a reachable API URL, then save to retry identity setup.";
            ShowDevelopmentServerAccessibility();
        }

        private void DrawDevelopmentServerSettings(Rect safe)
        {
            var frames = GuiSafeAreaLayout.DevelopmentServerSettings(safe);
            GUI.Box(frames.Panel, "Development server");
            GUI.Label(frames.HelpText, "Use the PC's LAN API URL on the same Wi-Fi, e.g. http://192.168.1.20:5188");
            developmentApiDraft = GUI.TextField(frames.ApiUrlField, developmentApiDraft ?? string.Empty, 256);
            GUI.Label(frames.StatusText, developmentServerStatus);
            if (GUI.Button(frames.SaveButton, "Save & Retry")) SaveDevelopmentServerSettings();
            if (GUI.Button(frames.CloseButton, "Done"))
            {
                showDevelopmentServerSettings = false;
                ShowMainMenuAccessibility();
            }
        }

        private void SaveDevelopmentServerSettings()
        {
            var service = GameServiceClient.Instance;
            if (service == null)
            {
                developmentServerStatus = "Service client is unavailable.";
                return;
            }
            if (!service.ConfigureDevelopmentApiBaseUrl(developmentApiDraft, out var error))
            {
                developmentServerStatus = error;
                ShowDevelopmentServerAccessibility();
                return;
            }

            realtimeEndpoint = service.RealtimeEndpoint;
            endpointOverrideError = null;
            developmentApiDraft = service.ApiBaseUrl;
            developmentServerStatus = "Saved. Creating online identity…";
            ShowDevelopmentServerAccessibility();
        }

        private void UpdateDevelopmentServerStatus()
        {
            if (!showDevelopmentServerSettings) return;
            var service = GameServiceClient.Instance;
            if (service == null) return;
            if (service.IdentityReady) developmentServerStatus = "Online identity is ready for this API.";
            else if (!string.IsNullOrEmpty(service.IdentityError)) developmentServerStatus = "Identity setup failed: " + service.IdentityError;
        }

        private void ShowDevelopmentServerAccessibility()
        {
            var frames = GuiSafeAreaLayout.DevelopmentServerSettings(GuiSafeAreaLayout.Current);
            accessibleUi?.ShowScreen("Development server URL", new AccessibleUiAction[]
            {
                new("API base URL", "Type the PC's API URL, for example http://192.168.1.20:5188. The realtime WebSocket URL is derived automatically.", frames.ApiUrlField, null, false),
                new("Save and retry identity", developmentServerStatus, frames.SaveButton, SaveDevelopmentServerSettings),
                new("Done", "Close server settings and return to the main menu.", frames.CloseButton, () =>
                {
                    showDevelopmentServerSettings = false;
                    ShowMainMenuAccessibility();
                })
            });
        }

        public void RefreshCurrentAccessibility()
        {
            if (showFrontMenu) ShowMainMenuAccessibility();
            else ShowMatchAccessibility();
        }

        private void ShowMatchAccessibility()
        {
            var safe = GuiSafeAreaLayout.Current;
            var y = safe.yMax - 48f;
            var actions = new System.Collections.Generic.List<AccessibleUiAction>
            {
                new("Open Garage", "Pause the match and change cosmetics or claim the daily reward.", GuiSafeAreaLayout.Clamp(new Rect(safe.x + 4f, y, 112f, 44f), safe), OpenGarageFromMatch),
                new("Open Scores", "Pause the match and view Battle Coins rankings.", GuiSafeAreaLayout.Clamp(new Rect(safe.x + 124f, y, 112f, 44f), safe), OpenLeaderboardFromMatch)
            };
            if (player != null && player.gameObject.activeInHierarchy)
            {
                var ability = player.GetComponent<TankAbilityController>();
                if (ability != null)
                {
                    var frame = GuiSafeAreaLayout.Clamp(new Rect(safe.xMax - 148f, safe.yMax - 68f, 136f, 60f), safe);
                    actions.Add(new AccessibleUiAction("Activate tank ability", "Use the archetype ability. It may be on cooldown.", frame, () => ability.Activate()));
                }
            }
            if (CanOfferRealtimeRetry())
                actions.Add(new AccessibleUiAction("Retry Online", "Reconnect to your online match. Match state can be restored for 30 seconds after a connection loss.",
                    GuiSafeAreaLayout.Clamp(new Rect(safe.x + 17f, safe.y + 204f, 170f, 44f), safe), RetryOnline));
            accessibleUi?.ShowScreen($"Match controls. Drag the left side to steer; drag and release the right side to aim and fire. {RealtimeStatusText()}", actions.ToArray());
        }

        private bool CanOfferRealtimeRetry()
        {
            var service = GameServiceClient.Instance;
            return onlineMatch && realtime != null && !realtime.IsConnected && !realtime.IsConnecting &&
                service != null && service.IdentityReady && !string.IsNullOrWhiteSpace(realtimeEndpoint) &&
                Uri.TryCreate(realtimeEndpoint, UriKind.Absolute, out var endpoint) &&
                (endpoint.Scheme == "ws" || endpoint.Scheme == "wss");
        }

        private void RetryOnline()
        {
            if (!CanOfferRealtimeRetry()) return;
            realtimeStatus = "Retrying online connection…";
            realtime.Reconnect();
            ShowMatchAccessibility();
        }

        private void UpdateRealtimeInput()
        {
            if (player == null || realtime == null || !realtime.IsConnected || onlinePlayerId < 0) return;
            if (!inputSendCadence.TryConsume(Time.realtimeSinceStartup, realtime.TickRate)) return;
            realtime.SubmitInput(player.ConsumeNetworkInput(++inputTick,
                Time.timeScale > 0f && player.gameObject.activeInHierarchy));
        }

        private void OnRealtimeRoomJoined(RealtimeRoomJoined room)
        {
            onlinePlayerId = room.PlayerId;
            inputTick = -1;
            inputSendCadence.Reset();
            appliedArenaTick = -1;
            appliedPickupTick = -1;
            onlineGemCount = 0;
            ClearPickupVisuals(onlineGemVisuals);
            ClearPickupVisuals(onlinePowerUpVisuals);
            onlinePlayerCount = room.Players != null ? room.Players.Length : 0;
            realtimeStatus = $"Queued · {onlinePlayerCount}/8 players";
            foreach (var view in remoteTanks.Values) Destroy(view.Transform.gameObject);
            remoteTanks.Clear();
            onlineArchetypes.Clear();
            onlinePlayerNames.Clear();
            aliveOnlinePlayers.Clear();
            if (room.Players != null)
                foreach (var participant in room.Players)
                {
                    aliveOnlinePlayers.Add(participant.playerId);
                    onlinePlayerNames[participant.playerId] = participant.displayName;
                    if (Enum.TryParse(participant.archetype, out TankArchetype archetype))
                        onlineArchetypes[participant.playerId] = archetype;
                }
            onlinePhase = string.Empty;
            onlineWinnerPlayerId = 0;
            onlineMatchAward = 0;
            onlineMatchResultReceived = false;
            if (match != null) match.enabled = false;
            foreach (var tank in FindObjectsByType<TankController2D>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (tank == player) continue;
                tank.gameObject.SetActive(false);
            }
            foreach (var pickup in FindObjectsByType<GemPickup>(FindObjectsSortMode.None)) Destroy(pickup.gameObject);
            foreach (var pickup in FindObjectsByType<PowerUpPickup>(FindObjectsSortMode.None)) Destroy(pickup.gameObject);
            onlineMatch = true;
            ShowMatchAccessibility();
        }

        private void OnRealtimeSnapshot(TankSnapshot snapshot)
        {
            if (snapshot.Tick > appliedArenaTick)
            {
                appliedArenaTick = snapshot.Tick;
                if (!string.IsNullOrEmpty(snapshot.Phase))
                {
                    onlinePhase = snapshot.Phase;
                    onlineRemainingSeconds = Mathf.Max(0f, snapshot.RemainingSeconds);
                    onlineWinnerPlayerId = snapshot.WinnerPlayerId;
                    realtimeStatus = $"Online · {snapshot.Phase.Replace('_', ' ')} · {Mathf.CeilToInt(onlineRemainingSeconds)}s";
                }
                if (onlineMatch && match != null && !string.IsNullOrEmpty(snapshot.Phase))
                    match.ApplyRealtimeState(snapshot.ElapsedSeconds, snapshot.ArenaSize);
            }
            if (snapshot.Alive) aliveOnlinePlayers.Add(snapshot.PlayerId);
            else aliveOnlinePlayers.Remove(snapshot.PlayerId);
            if (snapshot.PlayerId == onlinePlayerId)
            {
                onlineEliminations = snapshot.Eliminations;
                onlineGemCount = snapshot.Gems;
                player?.SyncRealtimePowerUpState(snapshot.NitroActive, snapshot.MegaBulletActive, snapshot.MagnetActive);
                player?.ReconcileNetworkState(snapshot, 0.2f);
                return;
            }

            if (Enum.TryParse(snapshot.Archetype, out TankArchetype snapshotArchetype))
                onlineArchetypes[snapshot.PlayerId] = snapshotArchetype;
            if (!onlinePlayerNames.ContainsKey(snapshot.PlayerId))
                onlinePlayerNames[snapshot.PlayerId] = snapshot.PlayerId < 0
                    ? $"Fill Bot {Mathf.Abs(snapshot.PlayerId)}"
                    : $"Player {snapshot.PlayerId}";

            if (!remoteTanks.TryGetValue(snapshot.PlayerId, out var view))
            {
                view = CreateRemoteTank(snapshot.PlayerId);
                remoteTanks.Add(snapshot.PlayerId, view);
                onlinePlayerCount = Mathf.Max(onlinePlayerCount, remoteTanks.Count + 1);
            }
            view.Interpolator.Push(snapshot);
            view.ReceivedAt = Time.unscaledTime;
            view.LastSeenTick = snapshot.Tick;
            view.Health = snapshot.Alive ? snapshot.Health : 0f;
            view.Shielded = snapshot.Shielded;
            view.Whirling = snapshot.Whirling;
            view.NitroActive = snapshot.NitroActive;
            view.MegaBulletActive = snapshot.MegaBulletActive;
            view.MagnetActive = snapshot.MagnetActive;
        }

        private void OnRealtimeShotFrame(RealtimeShotFrame frame)
        {
            if (!onlineMatch || frame.Tick <= appliedShotTick) return;
            appliedShotTick = frame.Tick;
            foreach (var shot in frame.Traces)
            {
                var visual = new GameObject("Online Shot Trace");
                var line = visual.AddComponent<LineRenderer>();
                line.positionCount = 2;
                line.useWorldSpace = true;
                line.startWidth = 0.065f;
                line.endWidth = 0.025f;
                line.material = new Material(Shader.Find("Sprites/Default"));
                var color = shot.ShieldBlocked ? new Color(1f, 0.35f, 0.15f, 0.95f) :
                    shot.Deflected ? new Color(0.4f, 1f, 0.45f, 0.95f) :
                    shot.Type == "RicochetLaser" ? new Color(0.95f, 0.2f, 0.9f, 0.9f) : new Color(0.25f, 0.85f, 1f, 0.9f);
                line.startColor = color;
                line.endColor = new Color(color.r, color.g, color.b, 0.1f);
                line.sortingOrder = 20;
                line.SetPosition(0, new Vector3(shot.Start.x, shot.Start.y, -0.1f));
                line.SetPosition(1, new Vector3(shot.End.x, shot.End.y, -0.1f));
                Destroy(visual, 0.14f);
                Destroy(line.material, 0.16f);
            }
        }

        private void OnRealtimePickupFrame(RealtimePickupFrame frame)
        {
            if (!onlineMatch || frame.Tick < appliedPickupTick) return;
            appliedPickupTick = frame.Tick;
            ReconcilePickupVisuals(onlineGemVisuals, frame.Gems, item => item.PickupId, item => item.Position,
                item => CreatePickupVisual($"Online Gem {item.PickupId}", item.Position,
                    item.Value > 1 ? new Color(1f, 0.78f, 0.2f) : new Color(0.2f, 0.95f, 1f), item.Value > 1 ? 0.42f : 0.34f));
            ReconcilePickupVisuals(onlinePowerUpVisuals, frame.PowerUps, item => item.PickupId, item => item.Position,
                item => CreatePickupVisual($"Online {item.Type} {item.PickupId}", item.Position, PowerUpColor(item.Type), 0.42f));
        }

        private void OnRealtimeMatchReceipt(RealtimeMatchReceipt receipt)
        {
            if (receipt == null || receipt.profile == null ||
                !string.Equals(receipt.playerId, ProfileStore.GetServerPlayerId(), StringComparison.Ordinal)) return;
            GameServiceClient.Instance?.ApplyServerProfile(receipt.profile.battleCoins, receipt.profile.ownedCosmetics);
            onlineMatchAward = receipt.award;
            onlineMatchResultReceived = true;
            ShowResultAccessibility();
        }

        private void ReconcilePickupVisuals<T>(Dictionary<int, Transform> visuals, T[] items, Func<T, int> idSelector,
            Func<T, Vector2> positionSelector, Func<T, Transform> create)
        {
            var available = new HashSet<int>();
            foreach (var item in items)
            {
                var id = idSelector(item);
                available.Add(id);
                if (!visuals.TryGetValue(id, out var visual) || visual == null)
                {
                    visual = create(item);
                    visuals[id] = visual;
                }
                visual.position = positionSelector(item);
            }
            List<int> removed = null;
            foreach (var item in visuals)
            {
                if (available.Contains(item.Key)) continue;
                removed ??= new List<int>();
                removed.Add(item.Key);
            }
            if (removed == null) return;
            foreach (var id in removed)
            {
                if (visuals[id] != null) Destroy(visuals[id].gameObject);
                visuals.Remove(id);
            }
        }

        private static Transform CreatePickupVisual(string name, Vector2 position, Color color, float size)
        {
            var pickup = new GameObject(name);
            pickup.transform.position = position;
            pickup.transform.localScale = Vector3.one * size;
            var sprite = pickup.AddComponent<SpriteRenderer>();
            sprite.sprite = RuntimeSprite.White;
            sprite.color = color;
            sprite.sortingOrder = 2;
            return pickup.transform;
        }

        private static Color PowerUpColor(PowerUpType type) => type switch
        {
            PowerUpType.Nitro => new Color(1f, 0.9f, 0.1f),
            PowerUpType.MegaBullet => new Color(1f, 0.25f, 0.25f),
            _ => new Color(0.3f, 1f, 0.45f)
        };

        private void ClearPickupVisuals(Dictionary<int, Transform> visuals)
        {
            foreach (var item in visuals.Values)
                if (item != null) Destroy(item.gameObject);
            visuals.Clear();
        }

        private RemoteTankVisual CreateRemoteTank(int playerId)
        {
            var tank = new GameObject(playerId < 0 ? $"Online Fill Bot {Mathf.Abs(playerId)}" : $"Online Player {playerId}");
            var archetype = onlineArchetypes.TryGetValue(playerId, out var selected) ? selected : TankArchetype.SpeedyBumper;
            var palette = new[]
            {
                new Color(1f, 0.48f, 0.22f), new Color(0.92f, 0.28f, 0.7f),
                new Color(0.55f, 0.9f, 0.32f), new Color(0.98f, 0.78f, 0.2f),
                new Color(0.55f, 0.55f, 1f), new Color(0.35f, 0.9f, 0.8f), new Color(0.85f, 0.55f, 1f)
            };
            tank.transform.localScale = archetype == TankArchetype.HeavyDozer
                ? new Vector3(1.35f, 1.55f, 1f)
                : new Vector3(1.1f, 1.4f, 1f);
            var sprite = tank.AddComponent<SpriteRenderer>();
            sprite.sprite = RuntimeSprite.White;
            sprite.color = archetype switch
            {
                TankArchetype.HeavyDozer => new Color(1f, 0.55f, 0.15f),
                TankArchetype.RicochetSpinner => new Color(0.9f, 0.25f, 0.8f),
                _ => palette[Mathf.Abs(playerId) % palette.Length]
            };
            sprite.sortingOrder = 1;
            return new RemoteTankVisual(tank.transform, sprite, sprite.color);
        }

        private void UpdateRemoteTanks()
        {
            List<int> stalePlayers = null;
            foreach (var item in remoteTanks)
            {
                var view = item.Value;
                if (appliedArenaTick - view.LastSeenTick > MatchmakingPolicy.SimulationTicksPerSecond * 2)
                {
                    stalePlayers ??= new List<int>();
                    stalePlayers.Add(item.Key);
                    continue;
                }
                var alpha = Mathf.Clamp01((Time.unscaledTime - view.ReceivedAt) * MatchmakingPolicy.SimulationTicksPerSecond);
                var position = view.Interpolator.Position(alpha);
                view.Transform.position = position;
                view.Sprite.color = view.Shielded ? Color.cyan : view.Whirling ? Color.magenta : view.NitroActive ? Color.yellow : view.MegaBulletActive ? new Color(1f, 0.25f, 0.25f) : view.MagnetActive ? new Color(0.3f, 1f, 0.45f) : view.BaseColor;
                var velocity = view.Interpolator.Velocity(alpha);
                if (velocity.sqrMagnitude > 0.01f) view.Transform.up = velocity.normalized;
                if (view.Transform.gameObject.activeSelf != (view.Health > 0f))
                    view.Transform.gameObject.SetActive(view.Health > 0f);
            }
            if (stalePlayers == null) return;
            foreach (var playerId in stalePlayers)
            {
                Destroy(remoteTanks[playerId].Transform.gameObject);
                remoteTanks.Remove(playerId);
                onlineArchetypes.Remove(playerId);
                onlinePlayerNames.Remove(playerId);
                aliveOnlinePlayers.Remove(playerId);
            }
        }

        private void OnRealtimeError(string error)
        {
            var statusError = error;
            const string closePrefix = "Realtime WebSocket closed (";
            if (!string.IsNullOrEmpty(error) && error.StartsWith(closePrefix, StringComparison.Ordinal))
            {
                var closeDetails = error.Substring(closePrefix.Length);
                var closeCodeEnd = closeDetails.IndexOf(')');
                statusError = closeCodeEnd >= 0
                    ? "Realtime WS " + closeDetails.Substring(0, closeCodeEnd) + closeDetails.Substring(closeCodeEnd + 1)
                    : "Realtime WS " + closeDetails;
            }
            realtimeStatus = onlineMatch
                ? statusError.StartsWith("Realtime WS ", StringComparison.Ordinal) ? statusError : $"Online issue: {statusError}"
                : $"Offline fallback · {statusError}";
            ShowMatchAccessibility();
        }

        private void OnRealtimeDisconnected()
        {
            realtimeStatus = onlineMatch ? "Online connection lost · reconnecting" : "Offline";
            ShowMatchAccessibility();
        }

        private string OnlineWinnerName()
        {
            if (onlineWinnerPlayerId == onlinePlayerId) return "You";
            if (onlineWinnerPlayerId == 0) return "Match complete";
            if (onlinePlayerNames.TryGetValue(onlineWinnerPlayerId, out var displayName)) return displayName;
            return onlineWinnerPlayerId < 0
                ? $"Fill Bot {Mathf.Abs(onlineWinnerPlayerId)}"
                : $"Player {onlineWinnerPlayerId}";
        }

        private string RealtimeStatusText()
        {
            if (!string.IsNullOrEmpty(realtimeStatus)) return realtimeStatus;
            return realtime != null && realtime.IsConnecting ? "Connecting to matchmaking…" : "Offline";
        }

        private sealed class RemoteTankVisual
        {
            public RemoteTankVisual(Transform transform, SpriteRenderer sprite, Color baseColor)
            {
                Transform = transform;
                Sprite = sprite;
                BaseColor = baseColor;
                Interpolator = new SnapshotInterpolator();
            }

            public Transform Transform { get; }
            public SpriteRenderer Sprite { get; }
            public Color BaseColor { get; }
            public SnapshotInterpolator Interpolator { get; }
            public float ReceivedAt { get; set; }
            public int LastSeenTick { get; set; }
            public float Health { get; set; } = 100f;
            public bool Shielded { get; set; }
            public bool Whirling { get; set; }
            public bool NitroActive { get; set; }
            public bool MegaBulletActive { get; set; }
            public bool MagnetActive { get; set; }
        }

        private void ShowResultAccessibility()
        {
            var label = onlineMatch
                ? $"Winner: {OnlineWinnerName()}" + (onlineMatchResultReceived ? $". Earned {onlineMatchAward} Battle Coins." : string.Empty)
                : match != null && match.Winner != null ? $"Winner: {match.Winner.name}" : "Match complete";
            var safe = GuiSafeAreaLayout.Current;
            var panel = GuiSafeAreaLayout.Centered(290f, 140f, safe);
            var frame = GuiSafeAreaLayout.Clamp(new Rect(panel.center.x - 80f, panel.center.y + 5f, 160f, 36f), safe);
            accessibleUi?.ShowScreen(label, new AccessibleUiAction[]
            {
                new("Play Again", "Start a fresh arena match.", frame, RestartArena)
            });
        }

        private static Rect MenuFrame(Rect panel, float offsetX, float offsetY, float width, float height) =>
            GuiSafeAreaLayout.Clamp(new Rect(panel.center.x + offsetX, panel.center.y + offsetY, width, height), GuiSafeAreaLayout.Current);

        private static void CreateCamera()
        {
            var cameraObject = new GameObject("Main Camera");
            var camera = cameraObject.AddComponent<Camera>();
            cameraObject.tag = "MainCamera";
            camera.orthographic = true;
            camera.orthographicSize = 7f;
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            camera.backgroundColor = new Color(0.035f, 0.06f, 0.1f);
            cameraObject.AddComponent<AudioListener>();
        }

        private static void CreateArenaBounds()
        {
            var field = new GameObject("Arena Floor");
            field.AddComponent<ArenaField>();
            CreateWall(new Vector2(0f, 6f), new Vector2(16f, 0.4f));
            CreateWall(new Vector2(0f, -6f), new Vector2(16f, 0.4f));
            CreateWall(new Vector2(8f, 0f), new Vector2(0.4f, 12f));
            CreateWall(new Vector2(-8f, 0f), new Vector2(0.4f, 12f));
        }

        private static void CreateWall(Vector2 position, Vector2 size)
        {
            var wall = new GameObject("Arena Wall");
            wall.transform.position = position;
            wall.transform.localScale = size;
            var sprite = wall.AddComponent<SpriteRenderer>();
            sprite.sprite = RuntimeSprite.White;
            sprite.color = new Color(0.18f, 0.27f, 0.37f);
            wall.AddComponent<BoxCollider2D>();
        }

        public static TankController2D CreateBot(string name, Vector2 position)
        {
            var options = new[] { TankArchetype.SpeedyBumper, TankArchetype.HeavyDozer, TankArchetype.RicochetSpinner };
            var archetype = options[UnityEngine.Random.Range(0, options.Length)];
            return CreateTank(name, position, archetype, false, new Color(0.85f, 0.35f, 0.45f));
        }

        public static void CreatePowerUp(Vector2 position, PowerUpType type, Color color)
        {
            var pickup = new GameObject(type.ToString());
            pickup.transform.position = position;
            pickup.AddComponent<CircleCollider2D>();
            pickup.AddComponent<PowerUpPickup>().Configure(type, color);
        }

        private static TankController2D CreateTank(string name, Vector2 position, TankArchetype archetype, bool isPlayer, Color color)
        {
            var tank = new GameObject(name);
            tank.transform.position = position;
            tank.transform.localScale = new Vector3(1.1f, 1.4f, 1f);
            var sprite = tank.AddComponent<SpriteRenderer>();
            sprite.sprite = RuntimeSprite.White;
            sprite.color = color;
            var collider = tank.AddComponent<CircleCollider2D>();
            collider.radius = 0.48f;
            var controller = tank.AddComponent<TankController2D>();
            controller.Configure(archetype, isPlayer);
            tank.AddComponent<TankAbilityController>();
            var cosmetics = tank.AddComponent<TankCosmeticView>();
            if (isPlayer)
            {
                var profile = ProfileStore.Load();
                cosmetics.Configure(profile.FlagId, profile.BumpSoundId);
            }
            return controller;
        }
    }
}
