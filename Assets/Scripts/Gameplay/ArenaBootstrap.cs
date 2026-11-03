using BumperTanks.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using BumperTanks.Frontend;
using BumperTanks.Match;

namespace BumperTanks.Gameplay
{
    public sealed class ArenaBootstrap : MonoBehaviour
    {
        private TankController2D player;
        private MatchDirector match;
        private string localPlayerId;
        private bool showFrontMenu = true;
        private bool matchStarted;
        private bool resultAccessibilityShown;
        private AccessibleUiBridge accessibleUi;

        private void Awake()
        {
            localPlayerId = ProfileStore.GetOrCreatePlayerId();
            Time.timeScale = 0f;
            if (FindFirstObjectByType<GameServiceClient>() == null) new GameObject("Game Service Client").AddComponent<GameServiceClient>();
            match = gameObject.AddComponent<MatchDirector>();
            CreateCamera();
            CreateArenaBounds();
            player = CreateTank("Player Speedy Bumper", new Vector2(0f, -3f), TankArchetype.SpeedyBumper, true, new Color(0.15f, 0.75f, 1f));
            gameObject.AddComponent<GarageOverlay>();
            gameObject.AddComponent<LeaderboardOverlay>();
            accessibleUi = gameObject.AddComponent<AccessibleUiBridge>();
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

        private void Update()
        {
            if (resultAccessibilityShown || match == null || (match.Winner == null && match.Phase != MatchPhase.Complete)) return;
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
                var panel = GuiSafeAreaLayout.Centered(340f, 240f, safe);
                GUI.Box(panel, "BUMPER TANKS IO");
                GUI.Label(GuiSafeAreaLayout.Clamp(new Rect(panel.center.x - 140f, panel.y + 45f, 280f, 25f), safe), "Top down arena brawler");
                if (GUI.Button(MenuFrame(panel, -115f, -30f, 230f, 40f), "Play Arena"))
                    BeginArena();
                if (GUI.Button(MenuFrame(panel, -115f, 20f, 110f, 34f), "Garage (G)"))
                {
                    OpenGarageFromMenu();
                }
                if (GUI.Button(MenuFrame(panel, 5f, 20f, 110f, 34f), "Scores (L)"))
                {
                    OpenLeaderboardFromMenu();
                }
                GUI.Label(GuiSafeAreaLayout.Clamp(new Rect(panel.center.x - 140f, panel.y + 188f, 280f, 24f), safe), $"Battle Coins: {ProfileStore.Load().BattleCoins}");
                return;
            }
            GUI.Box(GuiSafeAreaLayout.Clamp(new Rect(safe.x + 4f, safe.y + 4f, 345, 92), safe), "BUMPER TANKS IO - Offline Arena");
            GUI.Label(GuiSafeAreaLayout.Clamp(new Rect(safe.x + 17f, safe.y + 28f, 320, 24), safe), "Desktop: WASD/arrows, mouse, click release, E, G garage | Mobile: left drag, right release");
            GUI.Label(GuiSafeAreaLayout.Clamp(new Rect(safe.x + 17f, safe.y + 52f, 320, 24), safe), player != null ? $"{player.Archetype}  Health: {player.Health:0}" : "Player eliminated");
            GUI.Label(GuiSafeAreaLayout.Clamp(new Rect(safe.x + 17f, safe.y + 94f, 320, 24), safe), $"Battle Coins: {ProfileStore.Load().BattleCoins}");
            GUI.Label(GuiSafeAreaLayout.Clamp(new Rect(safe.x + 17f, safe.y + 115f, 320, 24), safe), match != null ? $"{match.Phase}  {Mathf.CeilToInt(90f - match.Elapsed):0}s  Tanks: {match.RemainingPlayers}" : string.Empty);
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
                var gems = player != null ? match.GemsFor(player) : 0;
                var eliminations = player != null ? match.Scores.EliminationsFor(player.GetInstanceID()) : 0;
                GUI.Label(GuiSafeAreaLayout.Clamp(new Rect(safe.x + 17f, safe.y + 157f, 320, 24), safe), $"Gems: {gems}  KOs: {eliminations}");
                if (match.Winner != null || match.Phase == BumperTanks.Match.MatchPhase.Complete)
                {
                    var label = match.Winner != null ? $"Winner: {match.Winner.name}" : "Time up";
                    var panel = GuiSafeAreaLayout.Centered(290f, 140f, safe);
                    GUI.Box(panel, label);
                    if (GUI.Button(GuiSafeAreaLayout.Clamp(new Rect(panel.center.x - 80f, panel.center.y + 5f, 160f, 36f), safe), "Play Again"))
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

        private void BeginArena()
        {
            showFrontMenu = false;
            Time.timeScale = 1f;
            ShowMatchAccessibility();
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
            var panel = GuiSafeAreaLayout.Centered(340f, 240f, GuiSafeAreaLayout.Current);
            accessibleUi?.ShowScreen("Bumper Tanks main menu", new AccessibleUiAction[]
            {
                new("Play Arena", "Start the local arena match.", MenuFrame(panel, -115f, -30f, 230f, 40f), BeginArena),
                new("Garage", "Change tank color or trail and claim the daily reward.", MenuFrame(panel, -115f, 20f, 110f, 34f), OpenGarageFromMenu),
                new("Leaderboard", "View Battle Coins rankings.", MenuFrame(panel, 5f, 20f, 110f, 34f), OpenLeaderboardFromMenu)
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
            accessibleUi?.ShowScreen("Match controls. Drag the left side to steer; drag and release the right side to aim and fire.", actions.ToArray());
        }

        private void ShowResultAccessibility()
        {
            var label = match.Winner != null ? $"Winner: {match.Winner.name}" : "Match complete";
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
