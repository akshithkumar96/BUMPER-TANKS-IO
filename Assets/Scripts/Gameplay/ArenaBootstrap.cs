using BumperTanks.Core;
using UnityEngine;

namespace BumperTanks.Gameplay
{
    public sealed class ArenaBootstrap : MonoBehaviour
    {
        private TankController2D player;

        private void Awake()
        {
            gameObject.AddComponent<MatchDirector>();
            CreateCamera();
            CreateArenaBounds();
            player = CreateTank("Player Speedy Bumper", new Vector2(0f, -3f), TankArchetype.SpeedyBumper, true, new Color(0.15f, 0.75f, 1f));
            var heavy = CreateTank("Heavy Dozer Bot", new Vector2(-4f, 3f), TankArchetype.HeavyDozer, false, new Color(1f, 0.55f, 0.15f));
            heavy.gameObject.AddComponent<BotDriver>();
            var spinner = CreateTank("Ricochet Spinner Bot", new Vector2(4f, 3f), TankArchetype.RicochetSpinner, false, new Color(0.9f, 0.25f, 0.8f));
            spinner.gameObject.AddComponent<BotDriver>();
        }

        private void OnGUI()
        {
            GUI.Box(new Rect(12, 12, 345, 92), "BUMPER TANKS IO - Offline Arena");
            GUI.Label(new Rect(25, 40, 320, 24), "Desktop: WASD/arrows, mouse, click release, E | Mobile: left drag, right release");
            GUI.Label(new Rect(25, 66, 320, 24), player != null ? $"{player.Archetype}  Health: {player.Health:0}" : "Player eliminated");
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
