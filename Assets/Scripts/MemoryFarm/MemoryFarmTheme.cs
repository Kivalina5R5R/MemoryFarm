using TMPro;
using UnityEngine;

namespace Deepspace
{
    // Memory Farm's "bright, friendly farm" palette (GDD 9.1): daytime sky and rolling hills on the wall, a mowed meadow
    // on the floor, painted wooden signs with cream boards. Presentation only; nothing here reads or writes game state.
    public static class MemoryFarmTheme
    {
        // Picked for an older post-processing setup with heavy saturation and exposure, which SampleSceneProfile no longer has
        // (it is now bloom, vignette and tonemapping only): greens / straw / red are about half saturation and the inks dark.
        // Backdrop: sky, two hill greens and a darker meadow for the floor so the bright player cursors stay readable on it.
        public static readonly Color Sky = Rgb(0x9FCFE6), SkyLow = Rgb(0xC9E3EE), Cloud = Rgb(0xFFFFFF, .92f),
            HillFar = Rgb(0x89A475), HillNear = Rgb(0x68895A), Meadow = Rgb(0x445A3B), MeadowStripe = Rgb(0x4C6442);
        // Signs: wood frames, cream boards, straw and barn-red accents, brown ink.
        public static readonly Color Wood = Rgb(0x6A4A33), WoodDark = Rgb(0x2B1D14), Cream = Rgb(0xFFF6E0), CreamEdge = Rgb(0xEBDDBE),
            Straw = Rgb(0xD8BD6A), StrawDeep = Rgb(0xB8923E), BarnRed = Rgb(0x9A4034), Ink = Rgb(0x1E140D), InkSoft = Rgb(0x4E3E31),
            Grass = Rgb(0x6B925C), Wrong = Rgb(0x9E4A3C), Faded = Rgb(0xA8A397), Ribbon = Rgb(0x415C88), Shade = Rgb(0x1F3A12, .45f);

        // Outfit Black (headlines, numbers) and Outfit Bold (labels); the bake stores them on MemoryFarmPrototypeHud.
        public static TMP_FontAsset Black, Bold;
        public static void UseFonts(TMP_FontAsset black, TMP_FontAsset bold)
        {
            if (black != null) Black = black;
            if (bold != null) Bold = bold;
            if (Bold == null) Bold = Black;
        }

        public static Color Rgb(int hex, float a = 1) => UiThemeKit.Rgb(hex, a);

        // Sign lettering: heavy font, caps via style (stored text untouched, so label parsing still works).
        public static void StyleText(TMP_Text t, bool heavy, Color face, float outline = 0, Color outlineColor = default, float spacing = 0)
        {
            if (t == null) return;
            var font = heavy ? Black : Bold;
            if (font != null && t.font != font) t.font = font;
            t.fontStyle = FontStyles.UpperCase;
            t.color = face;
            t.characterSpacing = spacing;
            t.raycastTarget = false;
            UiThemeKit.ApplyOutline(t, outline, outlineColor);
        }

        // Name on a player-colour band: whichever of brown ink / cream reads better.
        public static Color InkOn(Color band) => UiThemeKit.DarkInkOn(band, Ink, Cream) ? Ink : Cream;

        // Wooden sign: dark-wood frame, board fill, optional straw trim. radius / border in wall units (2160 px tall wall).
        public static UiThemeKit.Plate Sign(RectTransform parent, string name, Color fill, float u, bool trim = false, bool glow = false, float radius = 40, float border = 14)
            => UiThemeKit.MakePlate(parent, name, WoodDark, fill, radius * u, border * u, trim, Straw, glow);
    }
}
