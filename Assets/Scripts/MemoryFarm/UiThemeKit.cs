using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Deepspace
{
    // Shared by the themed game UIs (Bounty Hunter, Memory Farm, Mirror Path): 9-sliced plates, the Editor bake hooks
    // (saved sprites and outline materials) and small layout / juice helpers. No palette here: each game owns its own.
    // Presentation only; nothing here reads or writes gameplay state.
    public static class UiThemeKit
    {
        // Rounded = painted signs and wood (Bounty Hunter, Memory Farm). Chamfer = cut crystal (Mirror Path).
        public enum Shape { Rounded, Chamfer }

        public sealed class Plate
        {
            public RectTransform rect;
            public Image frame, fill, trim, glow, gloss;
        }

        // ---------------------------------------------------------------- Editor bake hooks
        // Set by the Editor UI bakes only (font, width, colour -> saved outline material), so baked text keeps its outline.
        public static System.Func<TMP_FontAsset, float, Color, Material> EditOutline;

        // Outline without leaking materials: Play uses TMP's instance material, Edit Mode the bake's saved asset.
        public static void ApplyOutline(TMP_Text t, float width, Color color)
        {
            if (t == null) return;
            if (!Application.isPlaying)
            {
                if (width > 0 && EditOutline != null && t.font != null) t.fontSharedMaterial = EditOutline(t.font, width, color);
                return;
            }
            t.outlineColor = color;
            t.outlineWidth = width;
        }

        // ---------------------------------------------------------------- sprites
        const int SpriteSize = 64, SpriteRadius = 22, TrimStroke = 7, SoftEdge = 30;
        public const int Solid = 0, Trim = 1, Glow = 2, Gloss = 3;
        static readonly Dictionary<(Shape, int), Sprite> sprites = new();

        // The Editor bake saves every sprite as a PNG asset and passes it back, so baked plates reference real files.
        public static void UseSprite(Shape shape, int kind, Sprite sprite)
        {
            if (sprite == null) sprites.Remove((shape, kind));
            else sprites[(shape, kind)] = sprite;
        }
        public static void ClearSprites() => sprites.Clear();

        public static Texture2D Texture(Shape shape, int kind, bool readable = true) => kind switch
        {
            Glow => SoftTexture(readable),
            Gloss => GlossTexture(readable),
            _ => ShapeTexture(shape, kind == Trim ? TrimStroke : 0, readable),
        };
        // Glow and gloss do not depend on the shape; the bake saves them once.
        public static bool Shapeless(int kind) => kind == Glow || kind == Gloss;
        public static Vector4 Border(int kind) => kind == Glow ? Vector4.one * (SoftEdge + 1) : kind == Gloss ? Vector4.zero : Vector4.one * (SpriteRadius + 2);

        public static Sprite SpriteFor(Shape shape, int kind) => Get(shape, kind);

        // Outline-only frame (the trim sprite on its own): a thin gilded border with nothing filled in. stroke in canvas units.
        public static Image Ring(RectTransform parent, string name, Shape shape, Color color, float stroke, float inset = 0)
        {
            var rect = Child(parent, name);
            Stretch(rect, inset);
            return Img(rect, Get(shape, Trim), color, TrimStroke / Mathf.Max(.5f, stroke));
        }

        // Re-skins an existing scene Image (timer rails, bar fills) as a 9-sliced plate; radius in its canvas units.
        // Keeps the image's raycastTarget: skinned images can be Button target graphics (mouse clicks in the Editor).
        public static Image Skin(Image image, Shape shape, Color color, float radius)
        {
            if (image == null) return null;
            bool raycast = image.raycastTarget;
            Img(image.rectTransform, Get(shape, Solid), color, SpriteRadius / Mathf.Max(1f, radius));
            image.raycastTarget = raycast;
            return image;
        }

        static Sprite Get(Shape shape, int kind)
        {
            if (Shapeless(kind)) shape = Shape.Rounded;
            if (sprites.TryGetValue((shape, kind), out var sprite) && sprite != null) return sprite;
            var tex = Texture(shape, kind, false);
            tex.name = "Theme " + shape + " " + kind;
            sprite = Sprite.Create(tex, new Rect(0, 0, SpriteSize, SpriteSize), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, Border(kind));
            return sprites[(shape, kind)] = sprite;
        }

        // ---------------------------------------------------------------- plates
        // Frame + inset fill + optional thin inner trim, all 9-sliced so the corners keep their shape at any size.
        // radius is the corner size in the parent's canvas units. glow = soft halo under the frame (starts clear);
        // gloss = diagonal highlight across the fill (glass).
        public static Plate MakePlate(RectTransform parent, string name, Color frame, Color fill, float radius, float border,
            bool trim = false, Color trimColor = default, bool glow = false, Shape shape = Shape.Rounded, bool gloss = false, Color glossColor = default)
        {
            // Root holds no graphic, so the halo (first child) renders under the frame; contents added later render on top.
            var plate = new Plate { rect = Child(parent, name) };
            int order = 0;
            if (glow)
            {
                var g = Child(plate.rect, "Glow");
                Stretch(g, -radius * 1.4f);
                plate.glow = Img(g, Get(shape, Glow), Color.clear, (SoftEdge + 1) / (radius * 1.4f));
                g.SetSiblingIndex(order++);
            }
            var fr = Child(plate.rect, "Frame");
            Stretch(fr, 0);
            fr.SetSiblingIndex(order++);
            plate.frame = Img(fr, Get(shape, Solid), frame, SpriteRadius / Mathf.Max(1f, radius));
            var f = Child(plate.rect, "Fill");
            Stretch(f, border);
            f.SetSiblingIndex(order);
            plate.fill = Img(f, Get(shape, Solid), fill, SpriteRadius / Mathf.Max(1f, radius - border));
            if (gloss)
            {
                var s = Child(f, "Gloss");
                Stretch(s, border * .5f);
                plate.gloss = Img(s, Get(shape, Gloss), glossColor, 1);
                plate.gloss.type = Image.Type.Simple;
            }
            if (trim)
            {
                var t = Child(f, "Trim");
                Stretch(t, border * .7f);
                // Stroke of ~0.45 x border so it stays visible from across the room.
                plate.trim = Img(t, Get(shape, Trim), trimColor, TrimStroke / Mathf.Max(1f, border * .45f));
            }
            return plate;
        }

        public static Plate FindPlate(Transform parent, string name)
        {
            var root = parent != null ? parent.Find(name) as RectTransform : null;
            if (root == null) return null;
            var plate = new Plate { rect = root };
            Find(root, "Frame", out plate.frame);
            Find(root, "Fill", out plate.fill);
            Find(root, "Fill/Trim", out plate.trim);
            Find(root, "Glow", out plate.glow);
            Find(root, "Fill/Gloss", out plate.gloss);
            return plate;
        }
        static void Find(Transform root, string path, out Image image)
        {
            image = null;
            var t = root.Find(path);
            if (t != null) t.TryGetComponent(out image);
        }

        public static void Recolor(Plate plate, Color frame, Color fill)
        {
            if (plate == null) return;
            if (plate.frame != null) plate.frame.color = frame;
            if (plate.fill != null) plate.fill.color = fill;
        }

        // Plates sit just before their text as siblings; this keeps the plate visible exactly when its text is.
        public static void Sync(Plate plate, TMP_Text text)
        {
            if (plate == null || plate.rect == null) return;
            bool on = text != null && text.gameObject.activeSelf && text.transform.parent == plate.rect.parent;
            if (plate.rect.gameObject.activeSelf != on) plate.rect.gameObject.SetActive(on);
            if (on && plate.rect.GetSiblingIndex() > text.transform.GetSiblingIndex()) plate.rect.SetSiblingIndex(text.transform.GetSiblingIndex());
        }

        // A plate placed as the sibling right before `text`, so it renders behind it.
        public static void PutBehind(Transform plate, Transform text)
        {
            if (plate.parent == text.parent && plate.GetSiblingIndex() > text.GetSiblingIndex()) plate.SetSiblingIndex(text.GetSiblingIndex());
        }

        static Image Img(RectTransform rect, Sprite sprite, Color color, float multiplier)
        {
            // TryGetComponent, not GetComponent ?? AddComponent: Unity's Editor fake null would skip the add.
            if (!rect.TryGetComponent(out Image image)) image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite; image.type = Image.Type.Sliced; image.pixelsPerUnitMultiplier = multiplier;
            image.color = color; image.raycastTarget = false; image.enabled = true;
            return image;
        }

        // ---------------------------------------------------------------- scene panels
        // Turns off a panel's own flat graphic (the scenes' white 0.39 overlays) without touching its children.
        public static void Hide(Transform target)
        {
            if (target == null) return;
            foreach (var graphic in target.GetComponents<Graphic>())
                if (!(graphic is TMP_Text)) graphic.enabled = false;
        }

        // A themed panel places its own children; the scenes' old Vertical/Horizontal layout groups would re-stack them.
        public static void NoLayout(Transform target)
        {
            if (target == null) return;
            foreach (var layout in target.GetComponents<LayoutGroup>()) layout.enabled = false;
            foreach (var fitter in target.GetComponents<ContentSizeFitter>()) fitter.enabled = false;
        }

        // Drops the old Deepspace art from an Image / RawImage but keeps the component (raycasts, LED rect tests).
        public static void ClearArt(Transform target)
        {
            if (target == null) return;
            if (target.TryGetComponent(out RawImage raw)) { raw.texture = null; raw.color = Color.clear; }
            if (target.TryGetComponent(out Image image)) { image.sprite = null; image.color = Color.clear; }
        }

        // Flat colour on the panel's own graphic (a GameObject holds one Graphic, so a RawImage is reused, not replaced).
        public static void Paint(Transform target, Color color)
        {
            if (target == null) return;
            if (target.TryGetComponent(out RawImage raw)) { raw.texture = null; raw.color = color; raw.raycastTarget = false; raw.enabled = true; return; }
            if (!target.TryGetComponent(out Image image)) image = target.gameObject.AddComponent<Image>();
            image.sprite = null; image.color = color; image.raycastTarget = false; image.enabled = true;
        }

        // Re-parents and places a scene text inside its sign, centred and auto-sized.
        public static void PlaceText(TMP_Text text, RectTransform parent, float left, float bottom, float right, float top, float size)
        {
            if (text == null) return;
            text.rectTransform.SetParent(parent, false);
            Place(text.rectTransform, left, bottom, right, top);
            text.alignment = TextAlignmentOptions.Center;
            Size(text, size, .5f);
            text.raycastTarget = false;
        }

        // ---------------------------------------------------------------- layout and text
        public static RectTransform Child(RectTransform parent, string name)
        {
            var rect = parent.Find(name) as RectTransform;
            if (rect != null) return rect;
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static TMP_Text Text(RectTransform parent, string name, TMP_FontAsset font)
        {
            var rect = Child(parent, name);
            // TryGetComponent, not GetComponent ?? AddComponent: Unity's Editor fake null would skip the add.
            if (!rect.TryGetComponent(out TMP_Text text)) text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) text.font = font;
            text.raycastTarget = false;
            return text;
        }

        public static void Place(RectTransform rect, float left, float bottom, float right, float top)
        {
            rect.anchorMin = new Vector2(left, bottom); rect.anchorMax = new Vector2(right, top);
            rect.pivot = new Vector2(.5f, .5f); rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one; rect.localRotation = Quaternion.identity;
        }

        // A logo image fitted (aspect kept) inside the box left..right x bottom..top of parent, centred in it.
        public static RawImage Logo(RectTransform parent, string name, Texture2D logo, float left, float bottom, float right, float top)
        {
            var rect = Child(parent, name);
            if (!rect.TryGetComponent(out RawImage image)) image = rect.gameObject.AddComponent<RawImage>();
            image.texture = logo; image.raycastTarget = false; image.color = Color.white;
            float parentAspect = parent.rect.width / Mathf.Max(1f, parent.rect.height), logoAspect = logo.width / (float)Mathf.Max(1, logo.height);
            float w = right - left, h = top - bottom;
            if (w * parentAspect / logoAspect > h) w = h * logoAspect / parentAspect; else h = w * parentAspect / logoAspect;
            float cx = (left + right) / 2, cy = (bottom + top) / 2;
            Place(rect, cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2);
            return image;
        }

        public static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.pivot = new Vector2(.5f, .5f);
            rect.offsetMin = new Vector2(inset, inset); rect.offsetMax = new Vector2(-inset, -inset);
            rect.localScale = Vector3.one; rect.localRotation = Quaternion.identity;
        }

        public static void Size(TMP_Text t, float max, float minFraction = .55f)
        {
            if (t == null) return;
            t.enableAutoSizing = true; t.fontSize = max; t.fontSizeMax = max; t.fontSizeMin = max * minFraction;
            t.textWrappingMode = TextWrappingModes.NoWrap; t.margin = Vector4.zero;
        }

        // Cheap juice curve: 1 at rest, overshoots to 1+amount right after a trigger, settles in `seconds`.
        public static float Punch(float age, float amount, float seconds = .38f)
        {
            if (age >= seconds || age < 0) return 1;
            float u = age / seconds;
            return 1 + amount * Mathf.Sin(u * Mathf.PI) * (1 - u * .5f);
        }

        public static Color Rgb(int hex, float a = 1) => new Color((hex >> 16 & 255) / 255f, (hex >> 8 & 255) / 255f, (hex & 255) / 255f, a);
        public static Color Alpha(Color c, float a) { c.a = a; return c; }
        public static string Paint(string text, Color c) => $"<color=#{ColorUtility.ToHtmlStringRGB(c)}>{text}</color>";

        // WCAG relative luminance: pick whichever of two inks reads better on a coloured band.
        public static float Luminance(Color c) { var l = c.linear; return .2126f * l.r + .7152f * l.g + .0722f * l.b; }
        static float Contrast(float a, float b) => (Mathf.Max(a, b) + .05f) / (Mathf.Min(a, b) + .05f);
        public static bool DarkInkOn(Color band, Color dark, Color light)
        {
            float l = Luminance(band);
            return Contrast(l, Luminance(dark)) >= Contrast(l, Luminance(light));
        }

        // ---------------------------------------------------------------- textures
        static Texture2D ShapeTexture(Shape shape, float stroke, bool readable)
        {
            var tex = New(SpriteSize);
            var px = new Color32[SpriteSize * SpriteSize];
            float half = SpriteSize * .5f, inner = half - SpriteRadius;
            for (int y = 0; y < SpriteSize; y++)
                for (int x = 0; x < SpriteSize; x++)
                {
                    float ax = Mathf.Abs(x + .5f - half), ay = Mathf.Abs(y + .5f - half), d;
                    if (shape == Shape.Chamfer)
                        // Square with 45-degree cut corners (cut = SpriteRadius): distance to the nearer of the two edges.
                        d = Mathf.Max(Mathf.Max(ax, ay) - half, (ax + ay - (2 * half - SpriteRadius)) * .70710678f);
                    else
                    {
                        // Signed distance to a rounded square.
                        float qx = ax - inner, qy = ay - inner;
                        d = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0) - SpriteRadius;
                    }
                    // 1 px antialiasing.
                    float a = Mathf.Clamp01(.5f - d);
                    if (stroke > 0) a *= Mathf.Clamp01(d + stroke + .5f);
                    px[y * SpriteSize + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            return Finish(tex, px, readable);
        }

        static Texture2D SoftTexture(bool readable)
        {
            const int size = SpriteSize, edge = SoftEdge;
            var tex = New(size);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // Distance outward from the plate edge: solid inside (hidden under the frame), fading to 0 at the outer edge.
                    float ox = Mathf.Max(0, Mathf.Max(edge - (x + .5f), x + .5f - (size - edge)));
                    float oy = Mathf.Max(0, Mathf.Max(edge - (y + .5f), y + .5f - (size - edge)));
                    float d = new Vector2(ox, oy).magnitude;
                    float a = Mathf.Pow(1 - Mathf.Clamp01(d / edge), 2);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            return Finish(tex, px, readable);
        }

        // Glass highlight: one soft diagonal band high on the left plus a thin second streak, faded near every edge
        // so the stretched (non-sliced) sprite never paints outside a cut corner.
        static Texture2D GlossTexture(bool readable)
        {
            const int size = SpriteSize;
            var tex = New(size);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + .5f) / size, v = (y + .5f) / size, diag = u - v * .55f;
                    float band = Mathf.Clamp01(1 - Mathf.Abs(diag - .08f) / .11f) + .6f * Mathf.Clamp01(1 - Mathf.Abs(diag - .30f) / .025f);
                    float edge = Mathf.Clamp01(Mathf.Min(Mathf.Min(u, 1 - u), Mathf.Min(v, 1 - v)) / .12f);
                    float a = Mathf.Clamp01(band) * edge * Mathf.Lerp(.35f, 1f, v);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            return Finish(tex, px, readable);
        }

        static Texture2D New(int size) => new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        static Texture2D Finish(Texture2D tex, Color32[] px, bool readable)
        {
            tex.SetPixels32(px);
            tex.Apply(false, !readable);
            return tex;
        }
    }
}
