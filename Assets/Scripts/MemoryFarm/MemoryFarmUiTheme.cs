using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Deepspace
{
    // Farm restyle of the scene's own panels (GDD 9.1 "bright, friendly farm"): sky + hills + fence backdrop behind the
    // parade window, wooden signs for the Set banner / question / answer line, a meadow floor with cream answer signs,
    // and a blue-ribbon result poster. Presentation only; game rules and panel names are untouched.
    // Built by the Editor bake (Tools/Deepspace/Memory Farm/Bake UI To Scene) so every sign is a real scene object;
    // an unbaked scene builds the same thing at the start of Play.
    public sealed partial class MemoryFarmGameManager
    {
        enum ChoiceLook { Hidden, Open, Correct, Wrong }

        const string BackdropName = "Farm Backdrop";
        readonly List<UiThemeKit.Plate> choicePlates = new();

        bool FarmUiBaked => WallUi != null && WallUi.Find(BackdropName) != null;
        RectTransform WallUi => cutsceneArea != null ? cutsceneArea.Find("Panel == UI") as RectTransform : null;

        void ConfigureFarmUi()
        {
            if (!TryGetComponent(out prototypeHud)) prototypeHud = gameObject.AddComponent<MemoryFarmPrototypeHud>();
            MemoryFarmTheme.UseFonts(prototypeHud.ThemeBlack, prototypeHud.ThemeBold);
            if (!FarmUiBaked || !prototypeHud.Built)
            {
                BuildFarmUi();
                prototypeHud.Build(cutsceneArea, playArea, questionText);
            }
            else prototypeHud.Bind();
            ApplyMenuArt();
            CacheFarmUi();
            ScatterFloorPieces();
            DressWallUi();
            countdownNumber =cutsceneArea != null ? cutsceneArea.GetComponentInChildren<CountdownNumber>(true) : null;
            if (countdownNumber != null) countdownNumber.Hide();
        }

        readonly MemoryFarmWallSkin wallSkin = new();

        // The drawn wall UI, on baked and freshly built scenes alike, so new art needs no re-bake.
        void DressWallUi()
        {
            if (cutsceneArea == null) return;
            float height = cutsceneArea.rect.height;
            wallSkin.DressPlates(cutsceneArea, height);
            if (answerTimerScrollbar != null && answerTimerScrollbar.TryGetComponent(out Image rail)) wallSkin.DressTimer(rail, answerTimerFill, height);
            wallSkin.DressMarkers(sectionMarkers);
        }

        void ScatterFloorPieces()
        {
            var meadow = playArea != null ? playArea.Find("Meadow") : null;
            var scatter = meadow != null ? meadow.GetComponentInChildren<FloorScatter>(true) : null;
            if (scatter != null) scatter.Scatter();
        }

        // The wall menu is only the video (MenuIntroVideo: intro, then its loop) until someone is detected on the floor. With
        // no clips in Resources/MenuIntro/Memory Farm it adds nothing and the panel keeps its still picture.
        void ApplyMenuArt()
        {
            MenuIntroVideo.Attach(cutsceneMenuPanel, "Memory Farm", true, MemoryFarmTheme.Meadow);
        }

#if UNITY_EDITOR
        // Editor bake only: builds the UI in Edit Mode (models, cursors and game state are not touched).
        public void BakeUi(TMP_FontAsset black, TMP_FontAsset bold)
        {
            ResolveUiReferences();
            if (cutsceneArea == null || playArea == null) throw new System.InvalidOperationException("Wall or floor canvas not found.");
            ResolveChoices();
            ResolveSectionMarkers();
            if (!TryGetComponent(out prototypeHud)) prototypeHud = gameObject.AddComponent<MemoryFarmPrototypeHud>();
            prototypeHud.SetFonts(black, bold);
            MemoryFarmTheme.UseFonts(black, bold);
            BuildFarmUi();
            prototypeHud.Build(cutsceneArea, playArea, questionText);
            CacheFarmUi();
            LayoutResultRows(false);
        }
#endif

        void BuildFarmUi()
        {
            var wall = cutsceneArea;
            float h = Mathf.Max(1, wall.rect.height), u = h / 2160f;
            var ui = WallUi;
            if (ui != null)
            {
                UiThemeKit.Paint(ui, MemoryFarmTheme.Sky);
                BuildBackdrop(ui, u);
            }
            UiThemeKit.Hide(setAndSectionPanel);
            UiThemeKit.Hide(questionPanel);
            var mainDisplay = centerDisplay != null ? centerDisplay.parent as RectTransform : null;
            if (mainDisplay != null && mainDisplay.name == "Panel == MainDisplay") UiThemeKit.Hide(mainDisplay);
            // The window is see-through: the parade walks in front of the hills and fence (the model stage renders
            // on a clear background and switches the colour to white when the first farmer appears).
            if (centerDisplay != null && centerDisplay.TryGetComponent(out RawImage window)) window.color = Color.clear;

            // Set banner: barn-red ribbon across the top.
            if (setText != null && setAndSectionPanel != null)
            {
                Style(setText, setAndSectionPanel, .26f, .885f, .74f, .975f, h * .052f);
                MemoryFarmTheme.StyleText(setText, true, MemoryFarmTheme.Cream, .22f, MemoryFarmTheme.WoodDark, 3);
                Behind(MemoryFarmTheme.Sign(setAndSectionPanel, "Set ribbon", MemoryFarmTheme.BarnRed, u, true, false, 46, 16), setText, .24f, .872f, .76f, .988f);
            }
            // Parade progress: seed dots on a dark plank at the bottom (idle cream, done grass - GDD 9.3).
            if (sectionCountPanel != null && setAndSectionPanel != null)
            {
                UiThemeKit.Place(sectionCountPanel, .30f, .022f, .70f, .088f);
                UiThemeKit.Hide(sectionCountPanel);
                var plank = MemoryFarmTheme.Sign(setAndSectionPanel, "Progress plank", MemoryFarmTheme.Wood, u, false, false, 40, 12);
                UiThemeKit.Place(plank.rect, .285f, .012f, .715f, .098f);
                UiThemeKit.PutBehind(plank.rect, sectionCountPanel);
                var seed = UiThemeKit.SpriteFor(UiThemeKit.Shape.Rounded, UiThemeKit.Solid).texture;
                foreach (var marker in sectionMarkers)
                {
                    marker.texture = seed;
                    marker.rectTransform.sizeDelta = new Vector2(110 * u, 110 * u);
                }
            }

            // Question board, timer rail and the straw answer sign.
            if (questionText != null && questionPanel != null)
            {
                Style(questionText, questionPanel, .15f, .655f, .85f, .835f, h * .075f);
                MemoryFarmTheme.StyleText(questionText, true, MemoryFarmTheme.Ink, 0, default, 2);
                questionText.fontStyle = FontStyles.Normal; // questions are sentence case
                Behind(MemoryFarmTheme.Sign(questionPanel, "Question board", MemoryFarmTheme.Cream, u, true, false, 54, 18), questionText, .13f, .64f, .87f, .85f);
            }
            if (answerTimerScrollbar != null)
            {
                var rail = (RectTransform)answerTimerScrollbar.transform;
                UiThemeKit.Place(rail, .26f, .575f, .74f, .617f);
                float railH = .042f * h;
                if (answerTimerScrollbar.TryGetComponent(out Image railImage)) UiThemeKit.Skin(railImage, UiThemeKit.Shape.Rounded, MemoryFarmTheme.WoodDark, railH * .5f);
                var sliding = rail.Find("Sliding Area") as RectTransform;
                if (sliding != null) UiThemeKit.Stretch(sliding, 12 * u);
                if (answerTimerFill != null) UiThemeKit.Skin(answerTimerFill, UiThemeKit.Shape.Rounded, MemoryFarmTheme.Grass, railH * .5f - 12 * u);
            }
            var answer = FindTMPIn(questionPanel, "Text (TMP) == Answer");
            if (answer != null)
            {
                Style(answer, questionPanel, .32f, .455f, .68f, .545f, h * .055f);
                MemoryFarmTheme.StyleText(answer, true, MemoryFarmTheme.Ink, 0, default, 3);
                Behind(MemoryFarmTheme.Sign(questionPanel, "Answer sign", MemoryFarmTheme.Straw, u, false, false, 40, 14), answer, .30f, .443f, .70f, .557f);
            }
            // The wall's old sci-fi score cards give way to the HUD's ear tags (the manager still writes their numbers).
            var wallScores = FindRectIn(questionPanel, "Panel == Player Score");
            if (wallScores != null) wallScores.gameObject.SetActive(false);

            BuildStagePause(h, u);
            BuildResult(h, u);
            BuildFloor();
            ApplyMenuArt();
        }

        // Sky, drifting-free clouds, two rows of hills and a split-rail fence the parade walks along.
        void BuildBackdrop(RectTransform ui, float u)
        {
            var root = UiThemeKit.Child(ui, BackdropName);
            UiThemeKit.Stretch(root, 0);
            root.SetAsFirstSibling();
            var haze = UiThemeKit.Child(root, "Horizon haze");
            UiThemeKit.Place(haze, 0, .30f, 1, .62f);
            UiThemeKit.Paint(haze, UiThemeKit.Alpha(MemoryFarmTheme.SkyLow, .55f));
            float[,] clouds = { { .03f, .76f, .13f, .84f }, { .15f, .86f, .23f, .92f }, { .79f, .80f, .90f, .88f }, { .88f, .91f, .97f, .96f }, { .60f, .74f, .66f, .79f } };
            for (int i = 0; i < clouds.GetLength(0); i++)
                Blob(root, "Cloud " + (i + 1), MemoryFarmTheme.Cloud, 90 * u, clouds[i, 0], clouds[i, 1], clouds[i, 2], clouds[i, 3]);
            Blob(root, "Far hill left", MemoryFarmTheme.HillFar, 760 * u, -.12f, -.45f, .58f, .44f);
            Blob(root, "Far hill right", MemoryFarmTheme.HillFar, 700 * u, .44f, -.45f, 1.12f, .40f);
            // Fence: two rails and posts, drawn behind the parade window so farmers walk in front of it.
            var fence = UiThemeKit.Child(root, "Fence");
            UiThemeKit.Stretch(fence, 0);
            for (int r = 0; r < 2; r++)
            {
                var rail = UiThemeKit.MakePlate(fence, "Rail " + (r + 1), MemoryFarmTheme.WoodDark, MemoryFarmTheme.Wood, 14 * u, 6 * u);
                UiThemeKit.Place(rail.rect, -.01f, .215f + r * .1f, 1.01f, .245f + r * .1f);
            }
            for (int i = 0; i <= 16; i++)
            {
                float x = i / 16f;
                var post = UiThemeKit.MakePlate(fence, "Post " + (i + 1), MemoryFarmTheme.WoodDark, MemoryFarmTheme.Wood, 14 * u, 6 * u);
                UiThemeKit.Place(post.rect, x - .006f, .17f, x + .006f, .39f);
            }
            Blob(root, "Near hill", MemoryFarmTheme.HillNear, 900 * u, -.06f, -.55f, 1.06f, .20f);
        }

        void BuildStagePause(float h, float u)
        {
            if (stagePausePanel == null) return;
            UiThemeKit.Paint(stagePausePanel, MemoryFarmTheme.Shade);
            var image = stagePausePanel.Find("Image == Stage Pause") as RectTransform ?? stagePausePanel;
            UiThemeKit.NoLayout(stagePausePanel);
            UiThemeKit.NoLayout(image);
            UiThemeKit.ClearArt(image);
            var poster = MemoryFarmTheme.Sign(image, "Set clear poster", MemoryFarmTheme.Cream, u, true, false, 60, 20);
            UiThemeKit.Place(poster.rect, .33f, .30f, .67f, .64f);
            poster.rect.SetAsFirstSibling();
            var ribbon = MemoryFarmTheme.Sign(image, "Set clear ribbon", MemoryFarmTheme.BarnRed, u, true, false, 46, 16);
            UiThemeKit.Place(ribbon.rect, .37f, .58f, .63f, .71f);
            var title = UiThemeKit.Text(image, "Set clear title", MemoryFarmTheme.Black);
            Style(title, image, .39f, .595f, .61f, .695f, h * .07f);
            MemoryFarmTheme.StyleText(title, true, MemoryFarmTheme.Cream, .22f, MemoryFarmTheme.WoodDark, 6);
            title.text = "SET COMPLETE!";
            if (stagePauseCountdownText != null)
            {
                Style(stagePauseCountdownText, image, .36f, .36f, .64f, .54f, h * .085f);
                MemoryFarmTheme.StyleText(stagePauseCountdownText, true, MemoryFarmTheme.Ink, 0, default, 3);
            }
        }

        // Blue-ribbon poster: wood title plank, rosette + winner line, ranked rows with a player-colour stripe.
        void BuildResult(float h, float u)
        {
            if (resultPanel == null) return;
            UiThemeKit.Hide(resultPanel);
            var board = resultPanel.Find("Image == Result Menu") as RectTransform ?? resultPanel;
            UiThemeKit.NoLayout(resultPanel);
            UiThemeKit.NoLayout(board);
            UiThemeKit.Stretch(board, 0);
            UiThemeKit.ClearArt(board);
            UiThemeKit.Paint(board, MemoryFarmTheme.Shade);
            var poster = MemoryFarmTheme.Sign(board, "Result poster", MemoryFarmTheme.Cream, u, true, false, 64, 22);
            UiThemeKit.Place(poster.rect, .29f, .04f, .71f, .80f);
            poster.rect.SetAsFirstSibling();
            var plank = MemoryFarmTheme.Sign(board, "Result title plank", MemoryFarmTheme.Wood, u, true, false, 46, 16);
            UiThemeKit.Place(plank.rect, .34f, .79f, .66f, .96f);
            var title = UiThemeKit.Text(board, "Result title", MemoryFarmTheme.Black);
            Style(title, board, .36f, .805f, .64f, .945f, h * .11f);
            MemoryFarmTheme.StyleText(title, true, MemoryFarmTheme.Straw, .22f, MemoryFarmTheme.WoodDark, 8);
            title.text = "MEMORY FARM";
            float rx = 95 * u / Mathf.Max(1, board.rect.width), ry = 95 * u / h;
            var rosette = UiThemeKit.MakePlate(board, "Rosette", MemoryFarmTheme.Cream, MemoryFarmTheme.Ribbon, 95 * u, 12 * u, true, MemoryFarmTheme.Straw);
            UiThemeKit.Place(rosette.rect, .325f - rx, .715f - ry, .325f + rx, .715f + ry);
            var first = UiThemeKit.Text(rosette.rect, "Place", MemoryFarmTheme.Black);
            UiThemeKit.Stretch(first.rectTransform, 20 * u);
            first.alignment = TextAlignmentOptions.Center;
            UiThemeKit.Size(first, 80 * u, .5f);
            MemoryFarmTheme.StyleText(first, true, MemoryFarmTheme.Cream, 0, default, 0);
            first.text = "1ST";
            if (winnerText != null)
            {
                Style(winnerText, board.parent as RectTransform ?? board, .345f, .665f, .69f, .765f, h * .06f);
                MemoryFarmTheme.StyleText(winnerText, true, MemoryFarmTheme.Ink, 0, default, 2);
            }
            var rowsPanel = ResultRowsPanel;
            if (rowsPanel == null) return;
            UiThemeKit.Place(rowsPanel, .32f, .09f, .68f, .63f);
            UiThemeKit.Hide(rowsPanel);
            UiThemeKit.NoLayout(rowsPanel);
            float rowH = (.54f * h) / 6f * .84f;
            foreach (Transform child in rowsPanel)
            {
                int index = ParseResultPlayerIndex(child.name);
                if (index < 0 || !child.TryGetComponent(out RawImage raw)) continue;
                raw.texture = null; raw.color = Color.clear;
                var row = UiThemeKit.MakePlate((RectTransform)child, "Row", MemoryFarmTheme.WoodDark, MemoryFarmTheme.Cream, rowH * .3f, 8 * u, true, MemoryFarmTheme.StrawDeep);
                UiThemeKit.Stretch(row.rect, 0);
                row.rect.SetAsFirstSibling();
                if (row.trim != null) row.trim.enabled = false;
                var stripe = UiThemeKit.MakePlate(row.fill.rectTransform, "Player color", MemoryFarmTheme.WoodDark, PlayerColor(index), 10 * u, 4 * u);
                UiThemeKit.Place(stripe.rect, .02f, .16f, .045f, .84f);
                foreach (var text in child.GetComponentsInChildren<TMP_Text>(true))
                {
                    bool score = text.name.Contains("Score");
                    UiThemeKit.Place(text.rectTransform, score ? .60f : .08f, 0, score ? .96f : .60f, 1);
                    text.alignment = score ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;
                    UiThemeKit.Size(text, rowH * .9f, .3f);
                    MemoryFarmTheme.StyleText(text, true, score ? MemoryFarmTheme.Ink : Color.Lerp(PlayerColor(index), MemoryFarmTheme.Ink, .45f), 0, default, score ? 0 : 4);
                }
            }
        }

        // Floor: the painted floor picture, cream answer signs, painted Start / Restart / Exit planks.
        void BuildFloor()
        {
            var floor = playArea;
            float fu = Mathf.Max(1, floor.rect.height) / (float)FloorDisplaySettings.Height;
            var meadow = UiThemeKit.Child(floor, "Meadow");
            UiThemeKit.Stretch(meadow, 0);
            meadow.SetAsFirstSibling();
            FarmPicture.ShowFloor(meadow);
            UiThemeKit.Hide(characterPanel);
            UiThemeKit.Hide(answerChoicePanel);
            foreach (var image in choiceImages)
            {
                if (image == null) continue;
                var rect = image.rectTransform;
                var sign = MemoryFarmTheme.Sign(rect, "Choice sign", MemoryFarmTheme.Cream, fu, true, false, 46, 16);
                UiThemeKit.Stretch(sign.rect, 14 * fu);
                sign.rect.SetAsFirstSibling();
                if (sign.trim != null) sign.trim.color = MemoryFarmTheme.StrawDeep;
                image.color = Color.clear;
            }
            foreach (var text in choiceTexts)
            {
                if (text == null) continue;
                UiThemeKit.Place(text.rectTransform, .08f, .30f, .92f, .70f);
                text.alignment = TextAlignmentOptions.Center;
                UiThemeKit.Size(text, 150 * fu, .45f);
                MemoryFarmTheme.StyleText(text, true, MemoryFarmTheme.Ink, 0, default, 2);
            }
            if (gameplayMenuPanel != null)
            {
                UiThemeKit.Hide(gameplayMenuPanel);
                var bg = gameplayMenuPanel.Find("Panel == BG Menu");
                if (bg != null) UiThemeKit.Hide(bg);
                var line = gameplayMenuPanel.Find("Panel == Start");
                if (line != null) UiThemeKit.Hide(line);
            }
            if (gameplayResultPanel != null)
            {
                UiThemeKit.Hide(gameplayResultPanel);
                FloorButton(gameplayResultPanel, "Restart", "RESTART", MemoryFarmTheme.Grass, false);
                FloorButton(gameplayResultPanel, "Exit", "EXIT", MemoryFarmTheme.BarnRed, false);
            }
        }

        // The button keeps its own (now transparent) graphic, so mouse raycasts and the LED rect test are unchanged.
        static void FloorButton(RectTransform panel, string buttonName, string label, Color fill, bool glow)
        {
            Button button = null;
            foreach (var b in panel.GetComponentsInChildren<Button>(true)) if (b.name == buttonName) { button = b; break; }
            if (button == null) return;
            var rect = (RectTransform)button.transform;
            UiThemeKit.ClearArt(rect);
            foreach (Transform child in rect)
                if (child.name.StartsWith("StartArrow")) UiThemeKit.Hide(child);
            float radius = Mathf.Min(rect.rect.height * .35f, 48f), border = Mathf.Max(5f, rect.rect.height * .07f);
            var plate = UiThemeKit.MakePlate(rect, "Theme Plate", MemoryFarmTheme.WoodDark, fill, radius, border, true, MemoryFarmTheme.Straw, glow);
            UiThemeKit.Stretch(plate.rect, 0);
            plate.rect.SetAsFirstSibling();
            TMP_Text text = null;
            foreach (var t in rect.GetComponentsInChildren<TMP_Text>(true)) if (t.transform.parent == rect) { text = t; break; }
            if (text == null) text = UiThemeKit.Text(rect, "Theme Label", MemoryFarmTheme.Black);
            text.text = label;
            UiThemeKit.Stretch(text.rectTransform, border * 1.6f);
            text.alignment = TextAlignmentOptions.Center;
            UiThemeKit.Size(text, rect.rect.height * .5f, .5f);
            MemoryFarmTheme.StyleText(text, true, MemoryFarmTheme.Cream, .24f, MemoryFarmTheme.WoodDark, 6);
        }

        // Built or baked, the animated / recoloured pieces are found by name.
        void CacheFarmUi()
        {
            choicePlates.Clear();
            foreach (var image in choiceImages) choicePlates.Add(image != null ? UiThemeKit.FindPlate(image.transform, "Choice sign") : null);
        }

        void PaintChoice(int index, ChoiceLook look)
        {
            if (index < 0 || index >= choiceImages.Count || choiceImages[index] == null) return;
            var plate = index < choicePlates.Count ? choicePlates[index] : null;
            var text = index < choiceTexts.Count ? choiceTexts[index] : null;
            Color fill = look switch { ChoiceLook.Correct => MemoryFarmTheme.Grass, ChoiceLook.Wrong => UiThemeKit.Alpha(MemoryFarmTheme.Faded, .75f), _ => MemoryFarmTheme.Cream };
            if (plate == null)
            {
                // Unthemed scene: the old flat tint, in farm colours.
                choiceImages[index].color = look == ChoiceLook.Hidden ? Color.clear : UiThemeKit.Alpha(fill, look == ChoiceLook.Open ? .45f : .9f);
                return;
            }
            choiceImages[index].color = Color.clear;
            if (plate.fill != null) plate.fill.color = fill;
            if (plate.frame != null) plate.frame.color = look == ChoiceLook.Correct ? MemoryFarmTheme.StrawDeep : MemoryFarmTheme.WoodDark;
            if (plate.trim != null) plate.trim.enabled = look != ChoiceLook.Wrong;
            if (text != null) text.color = look == ChoiceLook.Correct ? MemoryFarmTheme.Cream : look == ChoiceLook.Wrong ? MemoryFarmTheme.InkSoft : MemoryFarmTheme.Ink;
        }

        // Result rows follow the sorted sibling order (UpdateScorePanel sorts them); the top score rows are lit straw.
        // The rows' own parent ("Panel == Player Score"): the scene's resultScorePanel reference points at the board above it.
        RectTransform ResultRowsPanel => resultPanel != null ? FindRectIn(resultPanel, "Panel == Player Score") : null;

        void LayoutResultRows(bool live)
        {
            var panel = ResultRowsPanel;
            if (panel == null) return;
            var rows = new List<RectTransform>();
            foreach (Transform child in panel)
                if (child.gameObject.activeSelf && ParseResultPlayerIndex(child.name) >= 0) rows.Add((RectTransform)child);
            if (rows.Count == 0) return;
            int top = 0;
            if (live) foreach (var p in players) top = Mathf.Max(top, p.Score);
            // Two or three players get taller rows; the block is centred in the poster either way.
            float pitch = Mathf.Min(.25f, 1f / rows.Count), blockTop = .5f + pitch * rows.Count * .5f;
            for (int i = 0; i < rows.Count; i++)
            {
                float y = blockTop - pitch * i;
                UiThemeKit.Place(rows[i], 0, y - pitch * .84f, 1, y);
                var plate = UiThemeKit.FindPlate(rows[i], "Row");
                if (plate == null) continue;
                bool lead = live && top > 0 && PlayerScore(ParseResultPlayerIndex(rows[i].name)) == top;
                if (plate.fill != null) plate.fill.color = lead ? MemoryFarmTheme.Straw : i % 2 == 0 ? MemoryFarmTheme.Cream : MemoryFarmTheme.CreamEdge;
                if (plate.trim != null) plate.trim.enabled = lead;
            }
        }

        // Menu hint and floor Start breathe; nothing here changes game state.
        // ---------------------------------------------------------------- helpers
        static void Style(TMP_Text text, RectTransform parent, float left, float bottom, float right, float top, float size)
            => UiThemeKit.PlaceText(text, parent, left, bottom, right, top, size);

        static void Behind(UiThemeKit.Plate plate, TMP_Text text, float left, float bottom, float right, float top)
        {
            UiThemeKit.Place(plate.rect, left, bottom, right, top);
            if (text != null) UiThemeKit.PutBehind(plate.rect, text.transform);
        }

        // Frameless rounded shape (cloud, hill): frame and fill the same colour.
        static void Blob(RectTransform parent, string name, Color color, float radius, float left, float bottom, float right, float top)
        {
            var plate = UiThemeKit.MakePlate(parent, name, color, color, radius, 0);
            UiThemeKit.Place(plate.rect, left, bottom, right, top);
        }
    }
}
