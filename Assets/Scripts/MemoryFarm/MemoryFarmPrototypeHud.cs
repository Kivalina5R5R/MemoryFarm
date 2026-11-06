using TMPro;
using UnityEngine;

namespace Deepspace
{
    // Memory Farm wall/floor HUD added on top of the scene panels: the player "ear tag" score strip
    // (Question/Reveal) and the floor watch instruction. Built once (Editor bake or first Play), then only updated.
    [DisallowMultipleComponent]
    public sealed class MemoryFarmPrototypeHud : MonoBehaviour
    {
        [SerializeField] RectTransform scoreStrip;
        [SerializeField] TMP_Text floorWatch, answerInstruction;
        [SerializeField] RectTransform[] tags = new RectTransform[6];
        [SerializeField] TMP_Text[] tagNames = new TMP_Text[6], tagScores = new TMP_Text[6], tagVerdicts = new TMP_Text[6];
        // Outfit Black / Bold, set by the Editor bake so the fonts travel with the scene into builds.
        [SerializeField] TMP_FontAsset themeBlack, themeBold;

        UiThemeKit.Plate watchPlate, answerPlate;
        readonly UiThemeKit.Plate[] tagPlates = new UiThemeKit.Plate[6], tagBands = new UiThemeKit.Plate[6];
        readonly int[] shownScores = { -1, -1, -1, -1, -1, -1 };
        readonly float[] punchAge = { 9, 9, 9, 9, 9, 9 };
        bool reveal;

        public TMP_FontAsset ThemeBlack => themeBlack;
        public TMP_FontAsset ThemeBold => themeBold;
        public void SetFonts(TMP_FontAsset black, TMP_FontAsset bold) { themeBlack = black; themeBold = bold; }
        public bool Built => scoreStrip != null && tags != null && tags.Length == 6 && tags[0] != null;

        public void Build(RectTransform wall, RectTransform floor, TMP_Text template)
        {
            if (wall == null || floor == null || template == null) return;
            MemoryFarmTheme.UseFonts(themeBlack != null ? themeBlack : template.font, themeBold);
            float h = Mathf.Max(1, wall.rect.height), u = h / 2160f, fu = floor.rect.height / (float)FloorDisplaySettings.Height;
            var root = Rect(wall, "Prototype HUD == Memory Farm", 0, 0, 1, 1);
            scoreStrip = Rect(root, "Wall Player Scores", .02f, .012f, .98f, .235f);
            // The old plain-text strip (before the ear tags) is replaced, not kept alongside.
            for (int i = 0; i < 6; i++)
            {
                var old = scoreStrip.Find("Player Score " + (i + 1));
                if (old != null) DestroyImmediate(old.gameObject);
            }
            if (tags == null || tags.Length != 6) tags = new RectTransform[6];
            if (tagNames == null || tagNames.Length != 6) tagNames = new TMP_Text[6];
            if (tagScores == null || tagScores.Length != 6) tagScores = new TMP_Text[6];
            if (tagVerdicts == null || tagVerdicts.Length != 6) tagVerdicts = new TMP_Text[6];
            for (int i = 0; i < 6; i++) BuildTag(i, u);
            scoreStrip.gameObject.SetActive(false);

            // Floor: cream plank sign in the middle of the meadow during Watch.
            var watchSign = MemoryFarmTheme.Sign(floor, "Watch sign", MemoryFarmTheme.Cream, fu, true, false, 34, 12);
            UiThemeKit.Place(watchSign.rect, .16f, .41f, .84f, .59f);
            floorWatch = Text(floor, "Memory Farm == Watch Instruction", .19f, .43f, .81f, .57f, floor.rect.height * .07f);
            MemoryFarmTheme.StyleText(floorWatch, true, MemoryFarmTheme.Ink, 0, default, 3);
            floorWatch.text = "Watch the farmers and remember the sequence";
            // Under the player cursors, so nobody's ring disappears behind the sign.
            var cursors = floor.Find("Panel == Character");
            if (cursors != null)
            {
                int index = cursors.GetSiblingIndex();
                floorWatch.transform.SetSiblingIndex(index);
                watchSign.rect.SetSiblingIndex(index);
            }
            UiThemeKit.PutBehind(watchSign.rect, floorWatch.transform);
            floorWatch.gameObject.SetActive(false);
            watchSign.rect.gameObject.SetActive(false);

            foreach (var text in wall.GetComponentsInChildren<TMP_Text>(true))
                if (text.name == "Text (TMP) == Answer") { answerInstruction = text; break; }
            Bind();
        }

        // One ear tag: dark-wood frame, cream body, player-colour band with the name, big score, verdict stamp underneath.
        void BuildTag(int i, float u)
        {
            var tag = Rect(scoreStrip, "Player Tag " + (i + 1), i / 6f, 0, (i + 1) / 6f, 1);
            var plate = MemoryFarmTheme.Sign(tag, "Tag", MemoryFarmTheme.Cream, u, true, true, 70, 16);
            UiThemeKit.Stretch(plate.rect, 10 * u);
            plate.rect.SetAsFirstSibling();
            if (plate.trim != null) plate.trim.enabled = false; // lit for the leader at runtime
            var band = UiThemeKit.MakePlate(plate.fill.rectTransform, "Band", MemoryFarmTheme.WoodDark, Color.white, 40 * u, 7 * u);
            UiThemeKit.Place(band.rect, .06f, .70f, .94f, .95f);
            tagNames[i] = Text(tag, "Name", .10f, .71f, .90f, .94f, 88 * u);
            tagScores[i] = Text(tag, "Score", .08f, .24f, .92f, .70f, 220 * u);
            MemoryFarmTheme.StyleText(tagScores[i], true, MemoryFarmTheme.Ink);
            tagVerdicts[i] = Text(tag, "Verdict", .08f, .06f, .92f, .25f, 70 * u);
            MemoryFarmTheme.StyleText(tagVerdicts[i], true, MemoryFarmTheme.Grass, 0, default, 6);
            tagVerdicts[i].text = "";
            tags[i] = tag;
        }

        // Finds the plates by name (built or baked); nothing is moved or restyled here.
        public void Bind()
        {
            MemoryFarmTheme.UseFonts(themeBlack, themeBold);
            var floor = floorWatch != null ? floorWatch.transform.parent : null;
            watchPlate = UiThemeKit.FindPlate(floor, "Watch sign");
            answerPlate = answerInstruction != null ? UiThemeKit.FindPlate(answerInstruction.transform.parent, "Answer sign") : null;
            for (int i = 0; i < 6; i++)
            {
                tagPlates[i] = tags != null && i < tags.Length && tags[i] != null ? UiThemeKit.FindPlate(tags[i], "Tag") : null;
                tagBands[i] = tagPlates[i] != null && tagPlates[i].fill != null ? UiThemeKit.FindPlate(tagPlates[i].fill.transform, "Band") : null;
            }
        }

        // count lays out the tags.
        public void ShowState(string phase, string answer, int count)
        {
            if (scoreStrip == null || floorWatch == null) return;
            bool question = phase == "Question";
            reveal = phase == "Reveal";
            // The answer sign is for the reveal only: while answering, the timer bar and the floor signs say it all.
            if (answerInstruction != null)
            {
                answerInstruction.gameObject.SetActive(reveal);
                answerInstruction.text = $"ANSWER: {answer}";
                answerInstruction.color = MemoryFarmTheme.Cream;
                UiThemeKit.Sync(answerPlate, answerInstruction);
                if (answerPlate != null && answerPlate.fill != null) answerPlate.fill.color = MemoryFarmTheme.Grass;
                // Dressed in a picture (MemoryFarmWallSkin) the flat fill is off, so the reveal's green tints the picture instead.
                if (answerPlate != null && answerPlate.fill != null && !answerPlate.fill.enabled) answerPlate.frame.color = MemoryFarmTheme.Grass;
            }
            scoreStrip.gameObject.SetActive(question || reveal);
            floorWatch.gameObject.SetActive(phase == "Watch");
            UiThemeKit.Sync(watchPlate, floorWatch);
            count = Mathf.Clamp(count, 1, 6);
            // Tags share the strip evenly; a few players get wider tags, capped so two players do not stretch edge to edge.
            float width = Mathf.Min(1f / count, .2f), start = .5f - width * count * .5f;
            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i] == null) continue;
                bool active = i < count;
                tags[i].gameObject.SetActive(active);
                if (active) UiThemeKit.Place(tags[i], start + width * i, 0, start + width * (i + 1), 1);
            }
        }

        public void SetPlayer(int index, int score, Color color, string verdict)
        {
            if (tags == null || index < 0 || index >= tags.Length || tags[index] == null) return;
            if (tagNames[index] != null)
            {
                tagNames[index].text = $"Player {index + 1}";
                tagNames[index].color = MemoryFarmTheme.InkOn(color);
            }
            if (tagBands[index] != null && tagBands[index].fill != null) tagBands[index].fill.color = color;
            if (tagScores[index] != null) tagScores[index].text = score.ToString();
            if (tagVerdicts[index] != null)
            {
                bool right = verdict == "CORRECT";
                // บาส 2026-11-02 bug hunt: an absent player (no signal / dropped) sat the question out - neutral tag, not a red MISSED.
                bool absent = verdict == "ABSENT";
                tagVerdicts[index].text = string.IsNullOrEmpty(verdict) ? "" : right ? "CORRECT" : absent ? "ABSENT" : "MISSED";
                tagVerdicts[index].color = right ? MemoryFarmTheme.Grass : absent ? MemoryFarmTheme.Faded : MemoryFarmTheme.Wrong;
            }
            if (shownScores[index] >= 0 && score > shownScores[index]) punchAge[index] = 0;
            shownScores[index] = score;
        }

        // Score punch on every point; the leader's tag gets a straw trim and halo while scores are showing.
        void Update()
        {
            float dt = Time.unscaledDeltaTime, pulse = .5f + .5f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f * 1.1f);
            int top = 0;
            foreach (var s in shownScores) top = Mathf.Max(top, s);
            for (int i = 0; i < tags.Length; i++)
            {
                var plate = tagPlates[i];
                if (plate == null || tags[i] == null || !tags[i].gameObject.activeInHierarchy) continue;
                punchAge[i] += dt;
                if (tagScores[i] != null) tagScores[i].transform.localScale = Vector3.one * UiThemeKit.Punch(punchAge[i], .3f);
                float flash = Mathf.Clamp01(1 - punchAge[i] / .4f);
                bool lead = top > 0 && shownScores[i] == top;
                if (plate.trim != null) { plate.trim.enabled = lead; plate.trim.color = Color.Lerp(MemoryFarmTheme.StrawDeep, MemoryFarmTheme.Straw, pulse); }
                if (plate.glow != null) plate.glow.color = UiThemeKit.Alpha(MemoryFarmTheme.Straw, Mathf.Max(lead ? .2f + .3f * pulse : 0, flash * .8f));
                if (plate.fill != null) plate.fill.color = Color.Lerp(MemoryFarmTheme.Cream, MemoryFarmTheme.Straw, flash * .6f);
            }
            if (answerInstruction != null)
                answerInstruction.transform.localScale = Vector3.one * (reveal && answerInstruction.gameObject.activeInHierarchy ? 1 + .04f * pulse : 1);
        }

        static RectTransform Rect(Transform parent, string name, float left, float bottom, float right, float top)
        {
            var r = UiThemeKit.Child((RectTransform)parent, name);
            UiThemeKit.Place(r, left, bottom, right, top);
            return r;
        }

        static TMP_Text Text(Transform parent, string name, float left, float bottom, float right, float top, float size)
        {
            var t = UiThemeKit.Text((RectTransform)parent, name, MemoryFarmTheme.Black);
            UiThemeKit.Place(t.rectTransform, left, bottom, right, top);
            t.alignment = TextAlignmentOptions.Center;
            UiThemeKit.Size(t, Mathf.Max(24f, size), .5f);
            return t;
        }
    }
}
