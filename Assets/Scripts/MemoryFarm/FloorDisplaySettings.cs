using UnityEngine;

namespace Deepspace
{
    // Pixel size of the floor canvas, matching the 2944 x 1408 LED floor output.
    public static class FloorDisplaySettings
    {
        public const int Width = 2944;
        public const int Height = 1408;
        public static Vector2 Size => new Vector2(Width, Height);
        public static Rect LayoutRect => new Rect(-Width * .5f, -Height * .5f, Width, Height);
    }
}
