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
        }

        private System.Collections.IEnumerator TrackArenaStarted()
        {
            yield return null;
            GameServiceClient.Instance?.Track(localPlayerId, "match_started");
        }

        private void OnGUI()
        {
            if (showFrontMenu)
            {
                GUI.Box(new Rect(Screen.width * 0.5f - 170f, Screen.height * 0.5f - 120f, 340f, 240f), "BUMPER TANKS IO");
                GUI.Label(new Rect(Screen.width * 0.5f - 140f, Screen.height * 0.5f - 75f, 280f, 25f), "Top down arena brawler");
                if (GUI.Button(new Rect(Screen.width * 0.5f - 115f, Screen.height * 0.5f - 30f, 230f, 40f), "Play Arena"))
                {
                    showFrontMenu = false;
                    Time.timeScale = 1f;
                    StartCoroutine(TrackArenaStarted());
                }
                if (GUI.Button(new Rect(Screen.width * 0.5f - 115f, Screen.height * 0.5f + 20f, 110f, 34f), "Garage (G)"))
                {
                    showFrontMenu = false;
                    SendMessage("CloseLeaderboard", SendMessageOptions.DontRequireReceiver);
                    SendMessage("OpenGarage", SendMessageOptions.DontRequireReceiver);
                }
                if (GUI.Button(new Rect(Screen.width * 0.5f + 5f, Screen.height * 0.5f + 20f, 110f, 34f), "Scores (L)"))
                {
                    showFrontMenu = false;
                    SendMessage("CloseGarage", SendMessageOptions.DontRequireReceiver);
                    SendMessage("OpenLeaderboard", SendMessageOptions.DontRequireReceiver);
                }
                GUI.Label(new Rect(Screen.width * 0.5f - 140f, Screen.height * 0.5f + 68f, 280f, 24f), $"Battle Coins: {ProfileStore.Load().BattleCoins}");
                return;
            }
            GUI.Box(new Rect(12, 12, 345, 92), "BUMPER TANKS IO - Offline Arena");
            GUI.Label(new Rect(25, 40, 320, 24), "Desktop: WASD/arrows, mouse, click release, E, G garage | Mobile: left drag, right release");
            GUI.Label(new Rect(25, 66, 320, 24), player != null ? $"{player.Archetype}  Health: {player.Health:0}" : "Player eliminated");
            GUI.Label(new Rect(25, 108, 320, 24), $"Battle Coins: {ProfileStore.Load().BattleCoins}");
            if (player != null)
            {
                var ability = player.GetComponent<TankAbilityController>();
                if (ability != null)
                {
                    GUI.Label(new Rect(25, 129, 320, 24), ability.ReadyIn <= 0f ? "Ability ready (E)" : $"Ability cooldown: {ability.ReadyIn:0.0}s");
                    if (player.gameObject.activeInHierarchy && match != null && match.Phase != MatchPhase.Complete && match.Winner == null &&
                        GUI.Button(new Rect(Mathf.Max(12f, Screen.width - 148f), Screen.height - 72f, 136f, 60f), ability.ReadyIn <= 0f ? "ABILITY (E)" : $"READY IN {ability.ReadyIn:0.0}s"))
                        ability.Activate();
                }
            }
            if (match != null)
            {
                var gems = player != null ? match.GemsFor(player) : 0;
                var eliminations = player != null ? match.Scores.EliminationsFor(player.GetInstanceID()) : 0;
                GUI.Label(new Rect(25, 87, 320, 24), $"{match.Phase}  {Mathf.CeilToInt(90f - match.Elapsed):0}s  Tanks: {match.RemainingPlayers}  Gems: {gems}  KOs: {eliminations}");
                if (match.Winner != null || match.Phase == BumperTanks.Match.MatchPhase.Complete)
                {
                    var label = match.Winner != null ? $"Winner: {match.Winner.name}" : "Time up";
                    GUI.Box(new Rect(Screen.width * 0.5f - 145f, Screen.height * 0.5f - 70f, 290f, 140f), label);
                    if (GUI.Button(new Rect(Screen.width * 0.5f - 80f, Screen.height * 0.5f + 5f, 160f, 36f), "Play Again"))
                    {
                        if (player != null) GameServiceClient.Instance.SubmitMatch(localPlayerId, match.MatchId, match.GemsFor(player), match.Scores.EliminationsFor(player.GetInstanceID()));
                        GameServiceClient.Instance?.Track(localPlayerId, "match_rematch");
                        Time.timeScale = 1f;
                        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                    }
                }
            }
        }

        public void ShowFrontMenu()
        {
            showFrontMenu = true;
            Time.timeScale = 0f;
        }

        private static void CreateCamera()
        {
            var cameraObject = new GameObject("Main Camera");
            var camera = cameraObject.AddComponent<Camera>();
            cameraObject.tag = "MainCamera";
            camera.orthographic = true;
            camera.orthographicSize = 7f;
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            camera.backgroundColor = new Color(0.035f, 0.06f, 0.1f);
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

        private static void CreatePowerUp(Vector2 position, PowerUpType type, Color color)
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
            return controller;
        }
    }
}
