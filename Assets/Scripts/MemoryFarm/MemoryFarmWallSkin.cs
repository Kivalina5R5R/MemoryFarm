using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Deepspace
{
    // Dresses the wall's code-built plates in the drawn set from Resources/MemoryFarm/WallUi at start-up. Nothing moves or
    // resizes: the plate's frame shows the picture and its flat fill is switched off. A missing picture leaves that plate as it was.
    public sealed class MemoryFarmWallSkin
    {
        const string ArtFolder = "MemoryFarm/WallUi/";
        const float WallDesignHeight = 2160f;

        // How to draw a plate: which picture, and how many texture pixels make one wall unit at a 2160 px wall.
        readonly struct Look
        {
            public readonly string Piece;
            public readonly float PixelsPerUnit;
            public Look(string piece, float pixelsPerUnit) { Piece = piece; PixelsPerUnit = pixelsPerUnit; }
        }

        // Slices in texture pixels: x is the left and right edges, y the top and bottom. Kept outside the stretching middle.
        static readonly Dictionary<string, Vector2> Slices = new()
        {
            { "board", new Vector2(120, 120) }, { "ribbon", new Vector2(90, 90) }, { "plank", new Vector2(135, 30) },
            { "straw", new Vector2(135, 135) }, { "rail", new Vector2(115, 115) }, { "fill", new Vector2(90, 90) },
            { "tag", new Vector2(150, 150) },
        };

        static readonly Dictionary<string, Look> Plates = new()
        {
            { "Question board", new Look("board", 1.7f) }, { "Set clear poster", new Look("board", 1.2f) },
            { "Result poster", new Look("board", 1.2f) }, { "Sign", new Look("board", 1.2f) },
            { "Set ribbon", new Look("ribbon", 1.67f) }, { "Set clear ribbon", new Look("ribbon", 1.67f) },
            { "Progress plank", new Look("plank", 1.17f) }, { "Result title plank", new Look("plank", 1f) },
            { "Answer sign", new Look("straw", 2.6f) }, { "Tag", new Look("tag", 2.5f) },
        };

        const float TimerPixelsPerUnit = 4.4f;
        // The rail picture's frame on the wall (115 texture px / 4.4); the gauge fits inside it.
        const float TimerInset = 26f;

        readonly Dictionary<string, Sprite> sprites = new();

        // Dresses every known plate under the wall, active or not. The floor is not passed in, so it keeps its plates.
        public void DressPlates(Transform wall, float wallHeight)
        {
            if (wall == null) return;
            float u = wallHeight / WallDesignHeight;
            foreach (var child in wall.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == "Rosette") DressRosette(child);
                else if (Plates.TryGetValue(child.name, out var look)) DressPlate(child, look, u);
            }
        }

        // Timer rail and its gauge. The gauge keeps being tinted by the game.
        public void DressTimer(Image rail, Image gauge, float wallHeight)
        {
            float u = wallHeight / WallDesignHeight;
            var railSprite = SlicedSprite("rail");
            var gaugeSprite = SlicedSprite("fill");
            if (rail == null || railSprite == null) return;
            Show(rail, railSprite, TimerPixelsPerUnit / u, Color.white);
            var sliding = rail.transform.Find("Sliding Area") as RectTransform;
            if (sliding != null) UiThemeKit.Stretch(sliding, TimerInset * u);
            if (gauge != null && gaugeSprite != null) Show(gauge, gaugeSprite, TimerPixelsPerUnit / u, gauge.color);
        }

        // Parade progress seeds: a white seed the game tints.
        public void DressMarkers(IEnumerable<RawImage> markers)
        {
            var seed = Resources.Load<Texture2D>(ArtFolder + "seed");
            if (seed == null) return;
            foreach (var marker in markers)
                if (marker != null) marker.texture = seed;
        }

        void DressPlate(Transform plate, Look look, float u)
        {
            var frame = ChildImage(plate, "Frame");
            var sprite = SlicedSprite(look.Piece);
            if (frame == null || sprite == null) return;
            Show(frame, sprite, look.PixelsPerUnit / u, Color.white);
            HideFlatLayers(plate);
        }

        // The ribbon rosette is a single round picture, and its "1ST" sits on a now-cream button, so it turns to ink.
        void DressRosette(Transform plate)
        {
            var frame = ChildImage(plate, "Frame");
            var art = Resources.Load<Texture2D>(ArtFolder + "rosette");
            if (frame == null || art == null) return;
            frame.sprite = CreateSprite(art, Vector4.zero);
            frame.type = Image.Type.Simple;
            frame.preserveAspect = true;
            frame.color = Color.white;
            HideFlatLayers(plate);
            foreach (var label in plate.GetComponentsInChildren<TMP_Text>(true)) label.color = MemoryFarmTheme.Ink;
        }

        static void HideFlatLayers(Transform plate)
        {
            // Only the graphics: the player band and the texts live under the fill and must stay.
            var fill = ChildImage(plate, "Fill");
            if (fill != null) fill.enabled = false;
            var trim = fill != null ? ChildImage(fill.transform, "Trim") : null;
            if (trim != null) trim.enabled = false;
        }

        static void Show(Image image, Sprite sprite, float pixelsPerUnit, Color color)
        {
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = pixelsPerUnit;
            image.color = color;
        }

        static Image ChildImage(Transform parent, string name)
        {
            var child = parent.Find(name);
            return child != null ? child.GetComponent<Image>() : null;
        }

        Sprite SlicedSprite(string piece)
        {
            if (sprites.TryGetValue(piece, out var cached)) return cached;
            var art = Resources.Load<Texture2D>(ArtFolder + piece);
            var slice = Slices[piece];
            var sprite = art != null ? CreateSprite(art, new Vector4(slice.x, slice.y, slice.x, slice.y)) : null;
            sprites[piece] = sprite;
            return sprite;
        }

        static Sprite CreateSprite(Texture2D art, Vector4 border)
        {
            var whole = new Rect(0, 0, art.width, art.height);
            return Sprite.Create(art, whole, new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect, border);
        }
    }
}
