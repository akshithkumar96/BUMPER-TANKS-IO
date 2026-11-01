using UnityEngine;

namespace BumperTanks.Gameplay
{
    public static class RuntimeSprite
    {
        private static Sprite whiteSprite;

        public static Sprite White
        {
            get
            {
                if (whiteSprite != null) return whiteSprite;
                whiteSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
                return whiteSprite;
            }
        }
    }
}
