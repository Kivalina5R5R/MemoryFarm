using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Deepspace
{
    // บาส 2026-10-02: Memory Farm's READY card and its "signal lost" pause card (same layout, different words): a wooden
    // sign over a dim veil with a title, a big countdown / status line, one P1..Pn chip per player (green = on the floor,
    // straw = waiting) and a hint line. Built at runtime on the wall and on the floor; sizes follow the parent's height.
    public sealed class MemoryFarmReadyPanel : MonoBehaviour
    {
        static readonly Color Here = MemoryFarmTheme.Grass, Waiting = MemoryFarmTheme.Straw, Gone = MemoryFarmTheme.Faded;
        TMP_Text title, status, hint;
        RectTransform chipRow;
        readonly List<(Image back, TMP_Text label)> chips = new();

        public static MemoryFarmReadyPanel Create(RectTransform parent, bool wall, TMP_FontAsset fallbackFont)
        {
            var root = Rect(parent, "Panel == Memory Farm Ready", Vector2.zero, Vector2.one);
            var veil = root.gameObject.AddComponent<Image>(); veil.color = new Color(0, 0, 0, .55f); veil.raycastTarget = false;
            var panel = root.gameObject.AddComponent<MemoryFarmReadyPanel>();
            float u = Mathf.Max(1, parent.rect.height) / 2160f;
            var sign = MemoryFarmTheme.Sign(root, "Sign", MemoryFarmTheme.Cream, u, trim: true, glow: true, radius: 60, border: 18);
            sign.rect.anchorMin = wall ? new Vector2(.26f, .16f) : new Vector2(.18f, .12f);
            sign.rect.anchorMax = wall ? new Vector2(.74f, .84f) : new Vector2(.82f, .88f);
            sign.rect.offsetMin = sign.rect.offsetMax = Vector2.zero;
            var board = sign.rect;
            panel.title = Label(board, "Title", fallbackFont, true, new Vector2(.05f, .76f), new Vector2(.95f, .95f), MemoryFarmTheme.BarnRed);
            panel.status = Label(board, "Status", fallbackFont, true, new Vector2(.05f, .46f), new Vector2(.95f, .77f), MemoryFarmTheme.Ink);
            panel.chipRow = Rect(board, "Players", new Vector2(.06f, .22f), new Vector2(.94f, .44f));
            panel.hint = Label(board, "Hint", fallbackFont, false, new Vector2(.05f, .05f), new Vector2(.95f, .2f), MemoryFarmTheme.InkSoft);
            root.gameObject.SetActive(false);
            return panel;
        }

        // present[i] for the first `required` players; gone[i] = dropped out of this session (shown greyed).
        public void Show(string titleText, string statusText, string hintText, bool[] present, bool[] gone, int required)
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            title.text = titleText; status.text = statusText; hint.text = hintText;
            while (chips.Count < required) chips.Add(Chip(chipRow, chips.Count));
            float width = 1f / Mathf.Max(1, required), gap = width * .12f;
            for (int i = 0; i < chips.Count; i++)
            {
                var (back, label) = chips[i];
                bool on = i < required;
                back.gameObject.SetActive(on);
                if (!on) continue;
                var rect = back.rectTransform;
                rect.anchorMin = new Vector2(i * width + gap * .5f, 0); rect.anchorMax = new Vector2((i + 1) * width - gap * .5f, 1);
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                bool isGone = gone != null && i < gone.Length && gone[i];
                back.color = isGone ? Gone : present[i] ? Here : Waiting;
                label.color = MemoryFarmTheme.InkOn(back.color);
            }
        }

        public void Hide() { if (gameObject.activeSelf) gameObject.SetActive(false); }

        (Image, TMP_Text) Chip(RectTransform row, int index)
        {
            var rect = Rect(row, "P" + (index + 1), Vector2.zero, Vector2.one);
            var back = rect.gameObject.AddComponent<Image>();
            back.sprite = UiThemeKit.SpriteFor(UiThemeKit.Shape.Rounded, UiThemeKit.Solid); back.type = Image.Type.Sliced; back.raycastTarget = false;
            var label = Label(rect, "Label", title.font, true, Vector2.zero, Vector2.one, MemoryFarmTheme.Ink);
            label.text = "P" + (index + 1);
            return (back, label);
        }

        static RectTransform Rect(Transform parent, string name, Vector2 min, Vector2 max)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.gameObject.layer = parent.gameObject.layer;
            rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        static TMP_Text Label(Transform parent, string name, TMP_FontAsset fallback, bool heavy, Vector2 min, Vector2 max, Color color)
        {
            var rect = Rect(parent, name, min, max);
            rect.gameObject.AddComponent<CanvasRenderer>();
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (fallback != null) text.font = fallback;
            MemoryFarmTheme.StyleText(text, heavy, color);
            text.fontStyle = FontStyles.Normal; // Thai lines: no forced caps
            text.alignment = TextAlignmentOptions.Center;
            text.enableAutoSizing = true; text.fontSizeMin = 8; text.fontSizeMax = 1000;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            return text;
        }
    }
}
