using UnityEngine;

namespace Fortal.Display
{
    // Where the drawn part of a sprite lies, from its tight outline, leaving out its transparent
    // padding: a box in the sprite's own units, round its pivot. Pictures differ in how much empty
    // margin they have (square bush art, a wide canvas), so each is measured.
    public static class SpriteExtents
    {
        public static Rect Visible(Sprite sprite)
        {
            Vector2[] vertices = sprite.vertices;
            if (vertices == null || vertices.Length == 0) return new Rect(sprite.bounds.min, sprite.bounds.size);

            Vector2 min = vertices[0];
            Vector2 max = vertices[0];
            foreach (Vector2 vertex in vertices)
            {
                min = Vector2.Min(min, vertex);
                max = Vector2.Max(max, vertex);
            }

            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
    }
}
