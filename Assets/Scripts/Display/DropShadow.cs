using UnityEngine;

namespace Fortal.Display
{
    // A soft drop shadow under a picture lying on the floor (a bush or bin, a thief, a passer-by): the
    // same picture in see-through black, just under it and pushed a little toward the bottom right of
    // the floor. Sits on a child of the picture so it follows its sway, flip and creeping, but keeps
    // the same world offset however the picture is turned. It follows an animated picture frame by
    // frame, and shows only while the picture does, as see-through as the picture is.
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class DropShadow : MonoBehaviour
    {
        // Lowered a touch so it never draws through the picture.
        private const float Drop = 0.002f;

        private SpriteRenderer shadow;
        private SpriteRenderer caster;
        private Vector3 worldOffset;
        private Color color;

        // offset is in world X/Z (Vector2.y is world Z).
        public static DropShadow Add(SpriteRenderer caster, Vector2 offset, Color color)
        {
            var shadowObject = new GameObject("Shadow", typeof(SpriteRenderer));
            shadowObject.transform.SetParent(caster.transform, false);
            var shadow = shadowObject.AddComponent<DropShadow>();
            shadow.caster = caster;
            shadow.shadow = shadowObject.GetComponent<SpriteRenderer>();
            shadow.shadow.sortingOrder = caster.sortingOrder - 1;
            shadow.color = color;
            shadow.worldOffset = new Vector3(offset.x, -Drop, offset.y);
            shadow.LateUpdate();
            return shadow;
        }

        private void LateUpdate()
        {
            if (caster == null) return;

            transform.position = caster.transform.position + worldOffset;
            shadow.sprite = caster.sprite;
            shadow.flipX = caster.flipX;
            shadow.flipY = caster.flipY;
            shadow.enabled = caster.enabled;
            Color faded = color;
            faded.a *= caster.color.a;
            shadow.color = faded;
        }
    }
}
