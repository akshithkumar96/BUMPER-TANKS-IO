using BumperTanks.Frontend;
using UnityEngine;

namespace BumperTanks.Gameplay
{
    public sealed class GarageOverlay : MonoBehaviour
    {
        private bool open;
        private PlayerProfile profile;
        private TankController2D player;

        private void Start()
        {
            profile = ProfileStore.Load();
            player = FindFirstObjectByType<TankController2D>();
            ApplyColor();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.G)) open = !open;
        }

        private void OnGUI()
        {
            if (!open) return;
            GUI.Box(new Rect(Screen.width - 210, 12, 198, 172), "Garage");
            GUI.Label(new Rect(Screen.width - 195, 42, 170, 20), "Choose tank color");
            ColorButton("sky", new Color(0.15f, 0.75f, 1f), 70);
            ColorButton("coral", new Color(1f, 0.35f, 0.3f), 105);
            ColorButton("lime", new Color(0.35f, 1f, 0.45f), 140);
        }

        private void ColorButton(string id, Color color, float y)
        {
            var label = profile.ColorId == id ? $"✓ {id}" : id;
            var previous = GUI.color;
            GUI.color = color;
            if (GUI.Button(new Rect(Screen.width - 190, y, 156, 26), label))
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
