using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Deepspace
{
    // Art pads for the CHOOSE YOUR PLAYER screen (PlayerPadSelect): a flat round disc with the number in the middle. A ring
    // bar inside the disc shows only while someone stands on the pad, and fills as they hold. The pictures are in
    // Resources/<Folder>: Core, HoldRing and HoldCap (one round bar end, drawn at the top of the ring), plus Cursor.
    // Built through PlayerPadSelect's Decorate hooks.
    public static class PlayerPadArt
    {
        public sealed class Look
        {
            // Under Resources, e.g. "MemoryFarm/PlayerSelect".
            public string Folder;
            // Pad width as a floor fraction (most; shrinks for 5-6).
            public float Size = .175f;
            // Floor height of the pads' centres.
            public float CenterY = .5f;
            public Color NumberInk = Color.black;
            public Color BarColor = Color.green;
            // The empty part of the ring, behind the bar.
            public Color WellColor = Color.gray;
            // The number inside the disc (left, bottom, right, top).
            public Vector4 NumberBox = new(.2f, .2f, .8f, .8f);
            // The largest number size, in floor units; it shrinks to fit the box on smaller pads.
            public float NumberSize = 300f;
        }

        const float Gap = .028f;
        const string Round = "Sigil", Well = "Hold Well", Bar = "Hold Ring", StartCap = "Hold Start", EndCap = "Hold End";

        public static void Apply(PlayerPadSelect.Style style, Look look, Func<RectTransform> floor)
        {
            style.PlainPads = true;
            style.PadLabel = k => (k + 1).ToString();
            style.CursorRing = Resources.Load<Sprite>(look.Folder + "/Cursor");
            style.PadRect = (k, n) => PadRect(look, floor?.Invoke(), k, n);
            style.DecoratePad = (pad, k) => DecoratePad(look, pad);
            style.AnimatePad = (pad, k, owned, progress) => Animate(pad, owned, progress);
        }

        // Pad width (floor fraction) for n pads: look.Size, smaller when n of them wouldn't fit in a row.
        static float SizeFor(Look look, int n) => Mathf.Min(look.Size, (.86f - Gap * (n - 1)) / Mathf.Max(1, n));

        // A disc, square in floor pixels.
        static Rect PadRect(Look look, RectTransform floor, int k, int n)
        {
            float aspect = floor != null && floor.rect.height > 0 ? floor.rect.width / floor.rect.height : FloorDisplaySettings.Width / (float)FloorDisplaySettings.Height;
            float size = SizeFor(look, n);
            float height = size * aspect;
            float centerX = .5f - (n * size + (n - 1) * Gap) / 2 + k * (size + Gap) + size / 2;
            return new Rect(centerX - size / 2, look.CenterY - height / 2, size, height);
        }

        static void DecoratePad(Look look, RectTransform pad)
        {
            var round = UiThemeKit.Child(pad, Round);
            round.SetSiblingIndex(0);
            UiThemeKit.Stretch(round, 0);
            UiThemeKit.Stretch(Raw(look, round, "Core", "Core", Color.white).rectTransform, 0);
            UiThemeKit.Stretch(Raw(look, round, Well, "HoldRing", look.WellColor).rectTransform, 0);

            var barRect = UiThemeKit.Child(round, Bar);
            UiThemeKit.Stretch(barRect, 0);
            if (!barRect.TryGetComponent(out Image bar)) bar = barRect.gameObject.AddComponent<Image>();
            bar.sprite = Resources.Load<Sprite>(look.Folder + "/HoldRing");
            bar.type = Image.Type.Filled;
            bar.fillMethod = Image.FillMethod.Radial360;
            bar.fillOrigin = (int)Image.Origin360.Top;
            bar.fillClockwise = true;
            bar.fillAmount = 0;
            bar.color = look.BarColor;
            bar.raycastTarget = false;

            UiThemeKit.Stretch(Raw(look, round, StartCap, "HoldCap", look.BarColor).rectTransform, 0);
            UiThemeKit.Stretch(Raw(look, round, EndCap, "HoldCap", look.BarColor).rectTransform, 0);

            int order = 0;
            foreach (var part in new[] { "Core", Well, Bar, StartCap, EndCap }) round.Find(part).SetSiblingIndex(order++);

            // Build looks for "Number" on the pad itself, so in a baked scene it has just made a fresh one there: it
            // replaces the copy moved in here last time.
            Adopt(pad, "Number", round, look.NumberBox);
            if (round.Find("Number") is Transform number && number.TryGetComponent(out TMP_Text text))
            {
                text.color = look.NumberInk;
                UiThemeKit.ApplyOutline(text, 0f, Color.clear);
                UiThemeKit.Size(text, look.NumberSize, .2f);
            }

            // These pads carry no state wording.
            Drop(pad.Find("State"));
            Animate(pad, false, 0);
        }

        // The disc's centre in the floor's world space and its radius. Read from the unscaled layout, because the pad has
        // shrunk to nothing by the time its burst plays.
        public static (Vector3 center, float radius) RoundPiece(RectTransform pad)
        {
            var round = pad.Find(Round) as RectTransform ?? pad;
            Vector3 inPad = round == pad ? Vector3.zero : round.localPosition + (Vector3)round.rect.center;
            return (pad.parent.TransformPoint(pad.localPosition + inPad), round.rect.width * .5f);
        }

        // Free: only the disc and number. Stood on: the ring shows and fills clockwise from the top. Taken: full ring.
        static void Animate(RectTransform pad, bool owned, float progress)
        {
            if (!(pad.Find(Round) is RectTransform round)) return;
            float amount = owned ? 1f : Mathf.Clamp01(progress);
            bool showing = amount > 0f;
            SetShown(round, Well, showing);
            SetShown(round, Bar, showing);
            SetShown(round, StartCap, showing);
            SetShown(round, EndCap, showing);
            if (!showing) return;

            if (round.Find(Bar) is Transform bar && bar.TryGetComponent(out Image image)) image.fillAmount = amount;
            if (round.Find(EndCap) is Transform end) end.localRotation = Quaternion.Euler(0f, 0f, -360f * amount);
        }

        static void SetShown(RectTransform round, string part, bool shown)
        {
            var child = round.Find(part);
            if (child != null && child.gameObject.activeSelf != shown) child.gameObject.SetActive(shown);
        }

        static void Adopt(RectTransform pad, string name, RectTransform into, Vector4 box)
        {
            var part = pad.Find(name) as RectTransform;
            if (part == null) return;
            Drop(into.Find(name));
            part.SetParent(into, false);
            UiThemeKit.Place(part, box.x, box.y, box.z, box.w);
            part.SetAsLastSibling();
        }

        static RawImage Raw(Look look, RectTransform parent, string name, string art, Color tint)
        {
            var rect = UiThemeKit.Child(parent, name);
            if (!rect.TryGetComponent(out RawImage image)) image = rect.gameObject.AddComponent<RawImage>();
            image.texture = Resources.Load<Texture2D>(look.Folder + "/" + art);
            image.color = tint;
            image.raycastTarget = false;
            return image;
        }

        static void Drop(Transform old)
        {
            if (old == null) return;
            // Gone from Find at once (Destroy only lands at the end of the frame).
            old.SetParent(null, false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(old.gameObject); else UnityEngine.Object.DestroyImmediate(old.gameObject);
        }
    }
}
