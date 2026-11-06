using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Deepspace
{
    // บาส 2026-10-22: shared "CHOOSE YOUR PLAYER" screen (first built for Boss Slayer, now Deepspace / Memory Farm /
    // VolleyBall too, like the Bounty Hunter 15-min plan's character cards). After a game knows how many play, the floor
    // shows one pad per player; standing on a free pad for HoldSeconds takes it (one player per pad; บาส 2026-10-26: a pick
    // is locked, no switching). A pad pops bigger when stepped on and stays slightly enlarged while stood on; once taken it swells, shrinks to nothing and hands
    // over to the game's effect (Style.PadVanished). The screen waits, with no time limit, until everyone on the floor has a
    // pad, then calls back when the last pad has gone and its effect has played out. Anyone away at that moment gets a free pad in order. The pad number becomes the player number (FloorPlayerInput.SetOrder), so
    // that player's colour, lane, team and score are theirs for the session.
    // Positions come from FloorPlayerInput (normalised floor, y = 0 at the bottom), so every game reads the same;
    // the screen draws its own cursor ring per tag (white until it owns a pad, then that pad's colour). Editor mouse mode:
    // the mouse is P1, players with no position are filled in. Without UWB (and not the Editor mouse) it calls back at
    // once with the order unchanged. The look (colours, fonts, pad places, wall art) comes from each game's Style.
    // บาส 2026-10-22: the games install it floor-only (wall = null); the wall cards stay available for a game that wants them.
    public sealed class PlayerPadSelect : MonoBehaviour
    {
        public sealed class Style
        {
            public string Title = "CHOOSE YOUR PLAYER", Hint = "STAND ON A PAD FOR 2 SECONDS";
            public TMP_FontAsset TitleFont, BodyFont;
            public Color WallGround = new Color(0f, 0f, 0f, .55f);   // behind the wall cards when there is no WallBackground
            public Color Ground = new Color(.04f, .05f, .08f, .96f), Plate = new Color(.1f, .1f, .14f), Ink = Color.white, Accent = new Color(1f, .82f, .3f);
            public Func<int, Color> PadColor = k => Color.white;
            public Func<int, string> PadLabel = k => "P" + (k + 1);
            public Func<int, string> PadCaption;                 // a second line under the label (e.g. the team)
            public Func<int, int, Rect> PadRect;                 // pad k of n, normalised floor rect; null = a centred row
            // Where the players stand.
            public FloorPlayerInput Players;
            public Texture WallBackground, WallLogo;
            public UiThemeKit.Shape Shape = UiThemeKit.Shape.Rounded;
            public Action<TMP_Text, bool, Color> StyleText;      // (text, heading?, face colour); null = font + colour + outline
            // บาส 2026-10-27 (Boss Slayer): art instead of the plates. The Decorate hooks run inside Build, after the plain
            // parts exist, so their objects are baked with the screen; they may re-place "Number" / "State".
            public bool PlainPads;                               // hide each pad's plate (frame / fill / hold bar)
            public Action<RectTransform> DecorateFloor;          // the floor panel, after the pads and cursors are built
            public Action<RectTransform, int> DecoratePad;       // pad k's root
            public Action<RectTransform, int, bool, float> AnimatePad;   // every frame: pad k's root, owned, hold 0..1
            public Sprite CursorRing;                            // replaces the code-drawn ring (and hides the dot)
            public Action<RectTransform, int> PadVanished;       // pad k's root, at the moment it has shrunk to nothing
            public Func<bool> EffectPlaying;                     // the screen stays up while this is true after the last pad
        }

        public float HoldSeconds = 2f;
        public bool Active { get; private set; }
        // Everyone has a pad: the last pads are going away and nobody new can join.
        public bool Finishing { get; private set; }
        public int Count { get; private set; }

        const float StandingScale = 1.04f, PressSmoothing = 18f, StepOnPunch = .18f;
        const float OutroPeakScale = 1.3f, OutroGrowSeconds = .2f, OutroShrinkSeconds = .35f, OutroSeconds = OutroGrowSeconds + OutroShrinkSeconds, NoOutro = -1f;

        bool Live => style != null && style.Players != null && style.Players.IsLive;

        Style style;
        RectTransform wallParent, floorParent, floor, wall, cursorLayer;
        Action done;
        bool editorMouse;
        readonly int[] padOwner = new int[6], playerPad = new int[6], holdPad = new int[6];
        readonly float[] hold = new float[6], punch = new float[6], outro = new float[6], press = new float[6];
        readonly RectTransform[] pads = new RectTransform[6], cards = new RectTransform[6], cursors = new RectTransform[6];
        readonly Image[] padFills = new Image[6], padBars = new Image[6], cardFills = new Image[6], cursorRings = new Image[6];
        readonly TMP_Text[] padStates = new TMP_Text[6], cardStates = new TMP_Text[6];

        public static PlayerPadSelect Install(MonoBehaviour game, RectTransform wall, RectTransform floor, Style style)
        {
            if (!game.TryGetComponent(out PlayerPadSelect pick)) pick = game.gameObject.AddComponent<PlayerPadSelect>();
            pick.wallParent = wall; pick.floorParent = floor; pick.style = style ?? new Style();
            return pick;
        }

        // count players pick; `finished` runs once (at once without UWB unless the Editor mouse plays P1).
        public void Begin(int count, Action finished, bool editorMouseIsP1 = false)
        {
            Count = Mathf.Clamp(count, 1, 6);
            editorMouse = editorMouseIsP1 && Application.isEditor;
            if (!Live && !editorMouse) { finished?.Invoke(); return; }
            done = finished; Active = true; Finishing = false;
            for (int i = 0; i < 6; i++) { padOwner[i] = playerPad[i] = holdPad[i] = -1; hold[i] = 0; punch[i] = 9; outro[i] = NoOutro; press[i] = 1; }
            Build();
            Layout(Count);
            floor.gameObject.SetActive(true); floor.SetAsLastSibling();
            if (wall != null) { wall.gameObject.SetActive(true); wall.SetAsLastSibling(); }
            Refresh();
        }

        public void Cancel() { Active = false; done = null; Hide(); }

        // Someone joined while the pads are up: one more pad, the row re-centred.
        public void SetCount(int count)
        {
            if (!Active) return;
            Count = Mathf.Clamp(count, 1, 6);
            Layout(Count);
            Refresh();
        }

        void Hide()
        {
            if (floor != null) floor.gameObject.SetActive(false);
            if (wall != null) wall.gameObject.SetActive(false);
        }

        void Update()
        {
            if (!Active) return;
            float dt = Time.unscaledDeltaTime;
            for (int k = 0; k < Count; k++) punch[k] += dt;
            AdvanceOutros(dt);
            if (Finishing)
            {
                for (int z = 0; z < Count; z++) ShowCursor(z, Position(z));
                if (OutrosDone && (style.EffectPlaying == null || !style.EffectPlaying())) { End(); return; }
                Refresh();
                return;
            }
            for (int z = 0; z < Count; z++)
            {
                var at = Position(z);
                ShowCursor(z, at);
                if (!at.HasValue) { hold[z] = 0; holdPad[z] = -1; continue; }
                // บาส 2026-10-26: a pick is final; a player who owns a pad can't take another one
                if (playerPad[z] >= 0) { hold[z] = 0; holdPad[z] = -1; continue; }
                int pad = PadAt(at.Value);
                if (pad < 0 || padOwner[pad] >= 0) { hold[z] = 0; holdPad[z] = -1; continue; }
                if (holdPad[z] != pad) { holdPad[z] = pad; hold[z] = 0; punch[pad] = 0; }
                hold[z] += dt;
                if (hold[z] < HoldSeconds) continue;
                padOwner[pad] = z; playerPad[z] = pad; hold[z] = 0; punch[pad] = 0; outro[pad] = 0;
            }
            // Only who is on the floor now counts, so someone who walked off before picking cannot hold everyone else up.
            bool anyone = false, everyone = true;
            for (int z = 0; z < Count; z++)
            {
                if (!Position(z).HasValue) continue;
                anyone = true;
                if (playerPad[z] < 0) everyone = false;
            }
            if (anyone && everyone) Finish();
            Refresh();
        }

        // Normalised floor position of the player now in slot z (UWB), or the Editor mouse for P1.
        Vector2? Position(int z)
        {
            if (editorMouse)
            {
                if (z != 0 || floorParent == null) return null;
                var canvas = floorParent.GetComponentInParent<Canvas>();
                var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                if (Mouse.current == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(floorParent, Mouse.current.position.ReadValue(), cam, out var local)) return null;
                var r = floorParent.rect;
                var n = new Vector2(Mathf.InverseLerp(r.xMin, r.xMax, local.x), Mathf.InverseLerp(r.yMin, r.yMax, local.y));
                return n.x > 0 && n.x < 1 && n.y > 0 && n.y < 1 ? n : (Vector2?)null;
            }
            return style.Players != null ? style.Players.Normalized(z, floorParent) : null;
        }

        int PadAt(Vector2 n)
        {
            for (int k = 0; k < Count; k++)
            {
                if (pads[k] == null) continue;
                var a = pads[k].anchorMin; var b = pads[k].anchorMax;
                if (n.x >= a.x && n.x <= b.x && n.y >= a.y && n.y <= b.y) return k;
            }
            return -1;
        }

        void Finish()
        {
            for (int z = 0; z < Count; z++)   // anyone away gets a free pad in order
            {
                if (playerPad[z] >= 0) continue;
                for (int k = 0; k < Count; k++) if (padOwner[k] < 0) { padOwner[k] = z; playerPad[z] = k; break; }
            }
            for (int k = 0; k < Count; k++) if (outro[k] == NoOutro) outro[k] = 0;
            Finishing = true;
        }

        void End()
        {
            // pad k's player becomes Player k+1 (the pads decide the numbers, not the tag binding), even if they are away
            // right now: they come back to the same number.
            var order = new int[Count];
            for (int k = 0; k < Count; k++) order[k] = padOwner[k] >= 0 ? style.Players.PlayerIdForSlot(padOwner[k]) : -1;
            style.Players.SetOrder(order);
            Debug.Log("[Player pads] players picked their numbers: " + string.Join(",", order));
            Active = false; Finishing = false; Hide();
            var callback = done; done = null;
            callback?.Invoke();
        }

        void AdvanceOutros(float dt)
        {
            for (int k = 0; k < Count; k++)
            {
                if (outro[k] == NoOutro || outro[k] >= OutroSeconds) continue;
                outro[k] += dt;
                if (outro[k] >= OutroSeconds && pads[k] != null) style.PadVanished?.Invoke(pads[k], k);
            }
        }

        bool OutrosDone
        {
            get
            {
                for (int k = 0; k < Count; k++) if (outro[k] < OutroSeconds) return false;
                return true;
            }
        }

        // Stepping onto a free pad pops it bigger once and it stays slightly enlarged while stood on; a taken pad swells, then shrinks to nothing.
        float PadScale(int k, bool owned, float progress)
        {
            if (outro[k] == NoOutro)
            {
                float target = progress > 0 && !owned ? StandingScale : 1;
                press[k] = Mathf.Lerp(press[k], target, 1 - Mathf.Exp(-PressSmoothing * Time.unscaledDeltaTime));
                return press[k] * UiThemeKit.Punch(punch[k], StepOnPunch);
            }
            if (outro[k] < OutroGrowSeconds)
            {
                float grow = outro[k] / OutroGrowSeconds;
                return Mathf.Lerp(press[k], OutroPeakScale, 1 - (1 - grow) * (1 - grow));
            }
            float shrink = Mathf.Clamp01((outro[k] - OutroGrowSeconds) / OutroShrinkSeconds);
            return Mathf.Lerp(OutroPeakScale, 0, shrink * shrink);
        }

        void ShowCursor(int z, Vector2? at)
        {
            if (cursors[z] == null) return;
            cursors[z].gameObject.SetActive(at.HasValue);
            if (!at.HasValue) return;
            cursors[z].anchorMin = cursors[z].anchorMax = at.Value; cursors[z].anchoredPosition = Vector2.zero;
            var col = playerPad[z] >= 0 ? style.PadColor(playerPad[z]) : Color.white;
            if (cursorRings[z] != null) cursorRings[z].color = col;
        }

        void Refresh()
        {
            for (int k = 0; k < Count; k++)
            {
                bool owned = padOwner[k] >= 0;
                float progress = 0; for (int z = 0; z < Count; z++) if (holdPad[z] == k) progress = Mathf.Max(progress, hold[z] / HoldSeconds);
                var col = style.PadColor(k);
                if (padFills[k] != null) padFills[k].color = owned ? Color.Lerp(style.Plate, col, .45f) : style.Plate;
                if (padBars[k] != null) { padBars[k].fillAmount = owned ? 1 : progress; padBars[k].color = col; }
                if (padStates[k] != null) padStates[k].text = owned ? "READY" : progress > 0 ? "HOLD..." : "STAND HERE";
                if (pads[k] != null) pads[k].localScale = Vector3.one * PadScale(k, owned, progress);
                if (pads[k] != null) style.AnimatePad?.Invoke(pads[k], k, owned, progress);
                if (cardFills[k] != null) cardFills[k].color = owned ? Color.Lerp(style.Plate, col, .45f) : style.Plate;
                if (cardStates[k] != null) cardStates[k].text = owned ? "READY" : "...";
                if (cards[k] != null) cards[k].localScale = Vector3.one * UiThemeKit.Punch(punch[k], .12f);
            }
        }

        // ------------------------------------------------------------------ UI (find-or-create by name: Play and bakes)
        const string FloorName = "Panel == Player Select", WallName = "Panel == Player Select Wall";

        void Text(TMP_Text t, bool heading, Color face, TMP_FontAsset font)
        {
            if (t == null) return;
            if (style.StyleText != null) { style.StyleText(t, heading, face); return; }
            if (font != null) t.font = font;
            t.color = face; t.fontStyle = FontStyles.Bold; t.raycastTarget = false;
            UiThemeKit.ApplyOutline(t, .2f, new Color(0, 0, 0, .9f));
        }

        // Builds (or finds) both panels; hidden until Begin. Safe in Edit mode for a bake.
        public void Build()
        {
            if (floorParent == null) return;
            if (floor == null)
            {
                floor = UiThemeKit.Child(floorParent, FloorName);
                UiThemeKit.Stretch(floor, 0);
                UiThemeKit.Paint(floor, style.Ground);
                float fu = Mathf.Max(1, floorParent.rect.height) / (float)FloorDisplaySettings.Height;
                // An empty Title or Hint leaves that line off the floor.
                if (!string.IsNullOrEmpty(style.Title))
                {
                    var heading = UiThemeKit.Text(floor, "Title", style.TitleFont);
                    UiThemeKit.PlaceText(heading, floor, .15f, .82f, .85f, .95f, 110 * fu);
                    Text(heading, true, style.Accent, style.TitleFont); heading.text = style.Title;
                }
                if (!string.IsNullOrEmpty(style.Hint))
                {
                    var hint = UiThemeKit.Text(floor, "Hint", style.BodyFont);
                    UiThemeKit.PlaceText(hint, floor, .15f, .74f, .85f, .82f, 52 * fu);
                    Text(hint, false, style.Ink, style.BodyFont); hint.text = style.Hint;
                }
                for (int k = 0; k < 6; k++)
                {
                    var col = style.PadColor(k);
                    var plate = UiThemeKit.MakePlate(floor, "Player Pad " + (k + 1), col, style.Plate, 26 * fu, 8 * fu, true, style.Accent, true, style.Shape);
                    pads[k] = plate.rect; padFills[k] = plate.fill;
                    var label = UiThemeKit.Text(plate.rect, "Number", style.TitleFont);
                    UiThemeKit.PlaceText(label, plate.rect, .05f, .42f, .95f, .90f, 190 * fu);
                    Text(label, true, col, style.TitleFont); label.text = style.PadLabel(k);
                    if (style.PadCaption != null)
                    {
                        var caption = UiThemeKit.Text(plate.rect, "Caption", style.BodyFont);
                        UiThemeKit.PlaceText(caption, plate.rect, .05f, .33f, .95f, .44f, 40 * fu);
                        Text(caption, false, col, style.BodyFont); caption.text = style.PadCaption(k);
                    }
                    padStates[k] = UiThemeKit.Text(plate.rect, "State", style.BodyFont);
                    UiThemeKit.PlaceText(padStates[k], plate.rect, .05f, .16f, .95f, .32f, 46 * fu);
                    Text(padStates[k], false, style.Ink, style.BodyFont);
                    var bar = UiThemeKit.Child(plate.rect, "Hold Bar");
                    UiThemeKit.Place(bar, .1f, .06f, .9f, .12f);
                    if (!bar.TryGetComponent(out padBars[k])) padBars[k] = bar.gameObject.AddComponent<Image>();
                    padBars[k].sprite = UiThemeKit.SpriteFor(UiThemeKit.Shape.Rounded, UiThemeKit.Solid); padBars[k].type = Image.Type.Filled;
                    padBars[k].fillMethod = Image.FillMethod.Horizontal; padBars[k].fillAmount = 0; padBars[k].raycastTarget = false;
                    if (style.PlainPads)
                        foreach (var part in new[] { plate.frame, plate.fill, plate.trim, plate.glow, padBars[k] }) if (part != null) part.enabled = false;
                    style.DecoratePad?.Invoke(plate.rect, k);
                }
                cursorLayer = UiThemeKit.Child(floor, "Cursors");
                UiThemeKit.Stretch(cursorLayer, 0);
                float size = 130 * fu;
                for (int z = 0; z < 6; z++)
                {
                    var c = UiThemeKit.Child(cursorLayer, "Cursor " + (z + 1));
                    c.sizeDelta = new Vector2(size, size); c.pivot = new Vector2(.5f, .5f);
                    var ring = UiThemeKit.Child(c, "Ring"); UiThemeKit.Stretch(ring, 0);
                    if (!ring.TryGetComponent(out cursorRings[z])) cursorRings[z] = ring.gameObject.AddComponent<Image>();
                    cursorRings[z].sprite = style.CursorRing != null ? style.CursorRing : Circle(true); cursorRings[z].raycastTarget = false;
                    var dot = UiThemeKit.Child(c, "Dot"); UiThemeKit.Place(dot, .36f, .36f, .64f, .64f);
                    if (!dot.TryGetComponent(out Image dotImage)) dotImage = dot.gameObject.AddComponent<Image>();
                    dotImage.sprite = Circle(false); dotImage.raycastTarget = false; dotImage.enabled = style.CursorRing == null;
                    cursors[z] = c; c.gameObject.SetActive(false);
                }
                style.DecorateFloor?.Invoke(floor);
            }
            if (wall == null && wallParent != null)
            {
                wall = UiThemeKit.Child(wallParent, WallName);
                UiThemeKit.Stretch(wall, 0);
                float u = Mathf.Max(1, wallParent.rect.height) / 2160f;
                var bg = UiThemeKit.Child(wall, "Background"); UiThemeKit.Stretch(bg, 0);
                if (!bg.TryGetComponent(out RawImage bgImage)) bgImage = bg.gameObject.AddComponent<RawImage>();
                bgImage.texture = style.WallBackground; bgImage.color = style.WallBackground != null ? Color.white : style.WallGround; bgImage.raycastTarget = false;
                float top = .56f;
                if (style.WallLogo != null)
                {
                    var logo = UiThemeKit.Child(wall, "Logo");
                    float aspect = wallParent.rect.width / Mathf.Max(1f, wallParent.rect.height), h = .40f, w = h * style.WallLogo.width / style.WallLogo.height / Mathf.Max(.01f, aspect);
                    UiThemeKit.Place(logo, .5f - w / 2, .57f, .5f + w / 2, .97f);
                    if (!logo.TryGetComponent(out RawImage logoImage)) logoImage = logo.gameObject.AddComponent<RawImage>();
                    logoImage.texture = style.WallLogo; logoImage.raycastTarget = false;
                }
                else top = .80f;
                var heading = UiThemeKit.Text(wall, "Title", style.TitleFont);
                UiThemeKit.PlaceText(heading, wall, .25f, top - .09f, .75f, top, 120 * u);
                Text(heading, true, style.Accent, style.TitleFont); heading.text = style.Title;
                for (int k = 0; k < 6; k++)
                {
                    var col = style.PadColor(k);
                    var card = UiThemeKit.MakePlate(wall, "Player Card " + (k + 1), col, style.Plate, 30 * u, 8 * u, true, style.Accent, false, style.Shape);
                    cards[k] = card.rect; cardFills[k] = card.fill;
                    var label = UiThemeKit.Text(card.rect, "Number", style.TitleFont);
                    UiThemeKit.PlaceText(label, card.rect, .05f, .38f, .95f, .92f, 150 * u);
                    Text(label, true, col, style.TitleFont); label.text = style.PadLabel(k);
                    cardStates[k] = UiThemeKit.Text(card.rect, "State", style.BodyFont);
                    UiThemeKit.PlaceText(cardStates[k], card.rect, .05f, .1f, .95f, .36f, 70 * u);
                    Text(cardStates[k], false, style.Ink, style.BodyFont);
                }
            }
            if (!Active) Hide();
        }

        static Sprite ring, disc;
        // Cursor art made in code (a soft-edged ring / disc), so the screen needs no scene sprites.
        static Sprite Circle(bool hollow)
        {
            var cached = hollow ? ring : disc;
            if (cached != null) return cached;
            const int n = 128; var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = hollow ? "Pad Cursor Ring" : "Pad Cursor Dot" };
            var px = new Color32[n * n]; float h = n / 2f;
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float d = new Vector2(x + .5f - h, y + .5f - h).magnitude / h;
                float a = Mathf.Clamp01((1 - d) * h); if (hollow) a *= Mathf.Clamp01((d - .78f) * h);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px); tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f), 100);
            if (hollow) ring = sprite; else disc = sprite;
            return sprite;
        }

        public void Layout(int n)
        {
            float gap = .025f, width = Mathf.Min(.19f, (.80f - gap * (n - 1)) / n), total = n * width + (n - 1) * gap, x0 = .5f - total / 2;
            float wallW = Mathf.Min(.12f, (.70f - .012f * (n - 1)) / n), wallTotal = n * wallW + (n - 1) * .012f, wx0 = .5f - wallTotal / 2;
            float wallTop = style.WallLogo != null ? .44f : .66f;
            for (int k = 0; k < 6; k++)
            {
                bool on = k < n;
                if (pads[k] != null)
                {
                    pads[k].gameObject.SetActive(on);
                    if (on)
                    {
                        var r = style.PadRect != null ? style.PadRect(k, n) : new Rect(x0 + k * (width + gap), .25f, width, .43f);
                        UiThemeKit.Place(pads[k], r.xMin, r.yMin, r.xMax, r.yMax);
                    }
                }
                if (cards[k] != null) { cards[k].gameObject.SetActive(on); if (on) UiThemeKit.Place(cards[k], wx0 + k * (wallW + .012f), wallTop - .28f, wx0 + k * (wallW + .012f) + wallW, wallTop); }
            }
        }
    }
}
