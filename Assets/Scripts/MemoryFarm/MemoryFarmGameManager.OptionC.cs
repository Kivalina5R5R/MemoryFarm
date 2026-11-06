using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Deepspace
{
    // บาส 2026-10-21 (แบบ C, web prototype claude.ai/artifact/2dvdkh5nDH1iY6DSDUikHL): Memory Farm stretched toward the
    // 15-minute venue slot without just repeating sets. Five sets: 4 / 5 / 6 farmers as before, then
    //  • set 4 DOUBLE QUESTION: one 6-farmer parade, two questions in a row (two different kinds),
    //  • set 5 FINAL ROUND x2: points x2, and some parades end with a quick re-walk minus one farmer - "who is missing?",
    // a rule card before each of those, and a floor minigame instead of the set break after sets 2 and 4 (CHICKEN CHASE,
    // EGG HUNT) so players move between the long watch-and-stand stretches. Balance Note B1/E values stay as they were
    // (8 s to answer, 4/5/6 farmers, no penalty for a wrong answer). Minigame points go into the same score.
    // Look: the farm's own wooden signs, cream boards, straw and barn red (MemoryFarmTheme), and on the floor the same
    // picture chickens and eggs from ART/Minigame (MemoryFarmFloorStage).
    public sealed partial class MemoryFarmGameManager
    {
        [Header("แบบ C (15 min, 2026-10-21)")]
        [SerializeField] int doubleQuestionSet = 4;
        [SerializeField] int finalSet = 5;
        [SerializeField, Min(1)] int finalScoreMultiplier = 2;
        [Tooltip("Final set: chance a parade ends with a quick re-walk without one farmer and asks who is missing.")]
        [SerializeField, Range(0f, 1f)] float missingQuestionChance = .35f;
        [SerializeField, Min(.2f)] float rewalkSeconds = 1.1f;
        [SerializeField, Min(0f)] float ruleCardSeconds = 4f;
        // บาส 2026-11-02 (C): a third minigame after set 3 - the longest stretch of watch-and-answer drops from ~5 to ~4 min
        [Tooltip("A floor minigame replaces the set break after these sets (1-based), alternating CHICKEN CHASE, EGG HUNT, CHICKEN CHASE...")]
        [SerializeField] int[] minigameAfterSets = { 2, 3, 4 };
        [SerializeField, Min(0f)] float miniIntroSeconds = 5f;
        // บาส 2026-11-02: 40 -> 55 s ("เพิ่มเวลามินิเกม") - the game measured ~14:10, this brings it to ~15 min with more
        // time on the move instead of more watch-and-stand
        [SerializeField, Min(0f)] float miniPlaySeconds = 55f;
        [SerializeField, Min(0f)] float miniResultSeconds = 8f;
        [SerializeField] int miniCatchPoints = 2, miniGoldPoints = 5;

        // ------------------------------------------------------------------ parade twists
        int ruleShownSet, questionInParade, firstQuestionKind = -1, missingIndex = -1;
        bool missingThisParade, rewatching;
        readonly List<ParadeItem> fullParade = new();

        bool DoubleSet => currentSet == doubleQuestionSet;
        bool FinalSet => currentSet == finalSet;
        int ScoreMultiplier => FinalSet ? Mathf.Max(1, finalScoreMultiplier) : 1;
        string RuleTitle(int set) => set == doubleQuestionSet ? "DOUBLE QUESTION" : set == finalSet ? $"FINAL ROUND ×{Mathf.Max(1, finalScoreMultiplier)}" : null;
        string RuleLine(int set) => set == doubleQuestionSet ? "ขบวนเดินครั้งเดียว แต่ถาม 2 ข้อติดกัน · จำให้ครบทั้งขบวน"
            : $"คะแนน ×{Mathf.Max(1, finalScoreMultiplier)} · บางขบวนจะเดินซ้ำโดยขาดไป 1 คน ใครหายไป?";

        void ResetOptionC()
        {
            ruleShownSet = 0; questionInParade = 0; firstQuestionKind = -1; missingThisParade = rewatching = false; fullParade.Clear();
            HideOptionCUi();
        }

        void HideOptionCUi()
        {
            ShowRuleUi(false);
            EndMinigame();
        }

        // StartWatch asks first: a set with a new rule opens with its card (once per set).
        bool RuleCardDue() => currentSection == 1 && ruleCardSeconds > 0 && RuleTitle(currentSet) != null && ruleShownSet != currentSet;

        void StartRuleCard()
        {
            ruleShownSet = currentSet;
            sceneJuice?.Clear();
            if (farmVfx != null) farmVfx.Clear();
            state = GameState.Rule;
            stateTimer = ruleCardSeconds;
            ruleAge = 0;
            ClearParadeViews();
            HideQuestion();
            SetStagePauseVisible(false);
            SetSetAndSectionVisible(false);
            ShowRuleUi(true);
            ReplayLog.Event("rule_card", 0, 4, null, ReplayLog.Data().Add("set", currentSet).Add("rule", RuleTitle(currentSet)).Add("scores", ReplayScores()));
        }

        void UpdateRuleCard(float dt)
        {
            stateTimer -= dt; ruleAge += dt;
            AnimateRuleUi();
            if (stateTimer > 0f) return;
            ShowRuleUi(false);
            StartWatch();
        }

        // After GenerateParade: decide this parade's twist.
        void PrepareParade()
        {
            questionInParade = 0; firstQuestionKind = -1; rewatching = false; missingIndex = -1;
            missingThisParade = FinalSet && parade.Count >= 2 && Random.value < missingQuestionChance;
            if (missingThisParade)
            {
                missingIndex = Random.Range(0, parade.Count);
                // Nobody else may look like the missing farmer, or "who is missing?" has two right answers.
                for (int guard = 0; guard < 60 && LookTaken(parade, missingIndex); guard++)
                    parade[missingIndex] = new ParadeItem { Farmer = Random.Range(0, farmerTypes.Length), Animal = Random.Range(0, animalTypes.Length), Female = Random.value < .5f };
            }
            fullParade.Clear(); fullParade.AddRange(parade);
        }

        static bool SameLook(ParadeItem a, ParadeItem b) => a.Farmer == b.Farmer && a.Animal == b.Animal;
        static bool LookTaken(List<ParadeItem> list, int index)
        {
            for (int i = 0; i < list.Count; i++) if (i != index && SameLook(list[i], list[index])) return true;
            return false;
        }

        // End of the parade in the final set: walk it again, quickly, without the missing farmer.
        bool BeginRewatch()
        {
            if (!missingThisParade || rewatching || missingIndex < 0) return false;
            rewatching = true;
            parade.Clear();
            for (int i = 0; i < fullParade.Count; i++) if (i != missingIndex) parade.Add(fullParade[i]);
            walkPaces.Clear();
            for (int i = 0; i < parade.Count; i++) walkPaces.Add(rewalkSeconds);
            walkingIndex = 0; walkTimer = 0f;
            ClearParadeViews();
            SetupSectionMarkers();
            ShowCurrentFarmer();
            ReplayLog.Event("missing_rewalk", 0, 0, null, ReplayLog.Data().Add("set", currentSet).Add("question", currentSection).Add("farmers", fullParade.Count).Add("missing", missingIndex + 1));
            if (cutsceneArea != null) sceneJuice?.Reward(cutsceneArea.rect.center + Vector2.up * cutsceneArea.rect.height * .2f, "WHO IS MISSING?", MemoryFarmTheme.Straw, true);
            return true;
        }

        // DOUBLE QUESTION: after the first reveal of a parade, ask the second one (different kind, same parade).
        bool SecondQuestionDue()
        {
            if (!DoubleSet || missingThisParade || questionInParade != 0) return false;
            questionInParade = 1;
            return true;
        }

        int NextQuestionKind()
        {
            int kind = Random.Range(0, 4);
            if (questionInParade == 1 && firstQuestionKind >= 0) while (kind == firstQuestionKind) kind = Random.Range(0, 4);
            if (questionInParade == 0) firstQuestionKind = kind;
            return kind;
        }

        string LookName(ParadeItem p) => $"{farmerTypes[p.Farmer].Name}\n{animalTypes[p.Animal].Name}";

        void MakeMissingQuestion()
        {
            int n = Mathf.Max(2, Mathf.Min(4, choiceImages.Count));
            var missing = fullParade[missingIndex];
            var options = new List<ParadeItem> { missing };
            var order = new List<int>(); for (int i = 0; i < fullParade.Count; i++) order.Add(i);
            Shuffle(order);
            foreach (int i in order) { if (options.Count >= n) break; if (!options.Exists(o => SameLook(o, fullParade[i]))) options.Add(fullParade[i]); }
            for (int guard = 0; options.Count < n && guard < 200; guard++)
            {
                var extra = new ParadeItem { Farmer = Random.Range(0, farmerTypes.Length), Animal = Random.Range(0, animalTypes.Length) };
                if (!options.Exists(o => SameLook(o, extra))) options.Add(extra);
            }
            Shuffle(options);
            var choices = new string[options.Count];
            for (int i = 0; i < options.Count; i++) choices[i] = LookName(options[i]);
            question = new QuestionState { Text = QuestionPrefix() + "Who is missing from the parade?", Choices = choices, AnswerIndex = options.FindIndex(o => SameLook(o, missing)), Type = "missing_farmer" };
        }

        // On the question board itself: which of the two (set 4) / that it counts double (set 5).
        string QuestionPrefix() => DoubleSet ? $"Q{questionInParade + 1}/2 · " : FinalSet ? $"×{ScoreMultiplier} · " : "";
        string QuestionLabel() => $"QUESTION {currentSection}/{sectionsPerSet}" + (DoubleSet ? $" · {questionInParade + 1}/2" : "") + (FinalSet ? $" · ×{ScoreMultiplier}" : "");

        // ------------------------------------------------------------------ rule card UI
        RectTransform ruleWall, ruleFloor;
        UiThemeKit.Plate ruleRibbon;
        TMP_Text ruleTitle, ruleLine, ruleKicker, ruleFloorTitle;
        float ruleAge;

        RectTransform OptionCWallParent => WallUi != null ? WallUi : cutsceneArea;

        void ShowRuleUi(bool show)
        {
            if (!show) { if (ruleWall != null) ruleWall.gameObject.SetActive(false); if (ruleFloor != null) ruleFloor.gameObject.SetActive(false); return; }
            var wallParent = OptionCWallParent;
            if (wallParent == null || playArea == null) return;
            float h = Mathf.Max(1, cutsceneArea.rect.height), u = h / 2160f;
            if (ruleWall == null)
            {
                ruleWall = UiThemeKit.Child(wallParent, "Panel == Rule Card");
                UiThemeKit.Stretch(ruleWall, 0);
                UiThemeKit.Paint(ruleWall, MemoryFarmTheme.Shade);
                var poster = MemoryFarmTheme.Sign(ruleWall, "Rule poster", MemoryFarmTheme.Cream, u, true, false, 60, 20);
                UiThemeKit.Place(poster.rect, .27f, .24f, .73f, .66f);
                ruleRibbon = MemoryFarmTheme.Sign(ruleWall, "Rule ribbon", MemoryFarmTheme.BarnRed, u, true, true, 46, 16);
                UiThemeKit.Place(ruleRibbon.rect, .31f, .60f, .69f, .75f);
                ruleTitle = UiThemeKit.Text(ruleRibbon.rect, "Rule title", MemoryFarmTheme.Black);
                Style(ruleTitle, ruleRibbon.rect, .05f, .1f, .95f, .9f, h * .075f);
                MemoryFarmTheme.StyleText(ruleTitle, true, MemoryFarmTheme.Cream, .22f, MemoryFarmTheme.WoodDark, 6);
                ruleKicker = UiThemeKit.Text(ruleWall, "Rule set", MemoryFarmTheme.Bold);
                Style(ruleKicker, ruleWall, .33f, .52f, .67f, .58f, h * .045f);
                MemoryFarmTheme.StyleText(ruleKicker, true, MemoryFarmTheme.InkSoft, 0, default, 4);
                ruleLine = UiThemeKit.Text(ruleWall, "Rule line", MemoryFarmTheme.Bold);
                Style(ruleLine, ruleWall, .30f, .29f, .70f, .50f, h * .055f);
                MemoryFarmTheme.StyleText(ruleLine, false, MemoryFarmTheme.Ink, 0, default, 1);
                ruleLine.textWrappingMode = TextWrappingModes.Normal;

                ruleFloor = UiThemeKit.Child(playArea, "Panel == Rule Card Floor");
                UiThemeKit.Stretch(ruleFloor, 0);
                float fu = Mathf.Max(1, playArea.rect.height) / (float)FloorDisplaySettings.Height;
                var plank = MemoryFarmTheme.Sign(ruleFloor, "Rule floor sign", MemoryFarmTheme.Wood, fu, true, false, 40, 14);
                UiThemeKit.Place(plank.rect, .26f, .40f, .74f, .62f);
                ruleFloorTitle = UiThemeKit.Text(ruleFloor, "Rule floor title", MemoryFarmTheme.Black);
                UiThemeKit.PlaceText(ruleFloorTitle, ruleFloor, .28f, .42f, .72f, .60f, 130 * fu);
                MemoryFarmTheme.StyleText(ruleFloorTitle, true, MemoryFarmTheme.Straw, .24f, MemoryFarmTheme.WoodDark, 8);
            }
            bool final = currentSet == finalSet;
            UiThemeKit.Recolor(ruleRibbon, MemoryFarmTheme.WoodDark, final ? MemoryFarmTheme.StrawDeep : MemoryFarmTheme.BarnRed);
            ruleTitle.text = RuleTitle(currentSet);
            ruleFloorTitle.text = RuleTitle(currentSet);
            ruleKicker.text = $"SET {currentSet}/{(int)setCount}";
            ruleLine.text = RuleLine(currentSet);
            ruleWall.gameObject.SetActive(true); ruleWall.SetAsLastSibling();
            ruleFloor.gameObject.SetActive(true); ruleFloor.SetAsLastSibling();
            AnimateRuleUi();
        }

        void AnimateRuleUi()
        {
            if (ruleRibbon == null) return;
            float s = UiThemeKit.Punch(ruleAge, .28f, .5f);
            ruleRibbon.rect.localScale = Vector3.one * s;
            if (ruleRibbon.glow != null) ruleRibbon.glow.color = UiThemeKit.Alpha(MemoryFarmTheme.Straw, .25f + .35f * (.5f + .5f * Mathf.Sin(ruleAge * 6f)));
            if (ruleFloorTitle != null) ruleFloorTitle.transform.localScale = Vector3.one * s;
        }

        // ------------------------------------------------------------------ floor minigames
        enum MiniKind { ChickenChase, EggHunt }
        sealed class MiniThing { public Vector2 p, v; public bool gold; public float age, life, wander, away; public Transform model; }
        MiniKind miniKind;
        float miniTime;
        bool miniOver, miniWasPlaying;
        float miniSpawn;
        readonly List<MiniThing> miniThings = new();
        readonly int[] miniScore = new int[6];
        readonly float[] miniPunch = { 9, 9, 9, 9, 9, 9 };
        MemoryFarmFloorStage floorStage;

        string MiniTitle => miniKind == MiniKind.ChickenChase ? "CHICKEN CHASE" : "EGG HUNT";
        string MiniRule => miniKind == MiniKind.ChickenChase ? "ไก่หลุดคอก! เดินไปเหยียบจับ ไก่จะวิ่งหนีเมื่อเข้าใกล้ · ไก่ทอง 5 แต้ม"
            : "ไข่โผล่ทีละจุด รีบไปเหยียบก่อนไข่ฟัก · ไข่ทอง 5 แต้ม";
        bool MiniPlaying => state == GameState.Minigame && miniTime >= miniIntroSeconds && miniTime < miniIntroSeconds + miniPlaySeconds;
        Vector2 FloorMeters => new Vector2(Mathf.Max(.1f, arenaWidthMeters), Mathf.Max(.1f, arenaDepthMeters));

        Vector2 ToMeters(Vector2 local) { var r = FloorRect; return new Vector2(Mathf.InverseLerp(r.xMin, r.xMax, local.x) * FloorMeters.x, Mathf.InverseLerp(r.yMin, r.yMax, local.y) * FloorMeters.y); }
        Vector2 ToLocal(Vector2 m) { var r = FloorRect; return new Vector2(Mathf.Lerp(r.xMin, r.xMax, m.x / FloorMeters.x), Mathf.Lerp(r.yMin, r.yMax, m.y / FloorMeters.y)); }

        bool InMinigame(PlayerState p) => p.Cursor != null && p.Cursor.gameObject.activeSelf && (p.Index >= participant.Length || participant[p.Index]);

        // After set `finishedSet` (1-based) ends: true = a minigame took the set break's place.
        bool StartMinigameAfterSet(int finishedSet)
        {
            int slot = minigameAfterSets == null ? -1 : System.Array.IndexOf(minigameAfterSets, finishedSet);
            if (slot < 0 || miniPlaySeconds <= 0) return false;
            StartMinigame(slot % 2 == 0 ? MiniKind.ChickenChase : MiniKind.EggHunt);
            return true;
        }

        void StartMinigame(MiniKind kind)
        {
            sceneJuice?.Clear();
            if (farmVfx != null) farmVfx.Clear();
            state = GameState.Minigame;
            miniKind = kind; miniTime = 0; miniOver = false; miniWasPlaying = false; miniSpawn = 0;
            System.Array.Clear(miniScore, 0, miniScore.Length);
            for (int i = 0; i < miniPunch.Length; i++) miniPunch[i] = 9;
            ClearParadeViews();
            HideQuestion();
            SetStagePauseVisible(false);
            SetSetAndSectionVisible(false);
            ClearMiniThings();
            if (playArea != null)
            {
                if (floorStage == null && !TryGetComponent(out floorStage)) floorStage = gameObject.AddComponent<MemoryFarmFloorStage>();
                floorStage.Open(playArea, FloorMeters);
            }
            if (kind == MiniKind.ChickenChase) for (int i = 0; i < 7; i++) miniThings.Add(NewChicken());
            ShowMiniUi(true);
            ReplayLog.Event("minigame_start", 0, 0, null, ReplayLog.Data().Add("minigame", MiniTitle).Add("afterSet", currentSet - 1).Add("scores", ReplayScores()));
        }

        void EndMinigame()
        {
            ClearMiniThings();
            if (floorStage != null) floorStage.Close();
            ShowMiniUi(false);
        }

        void ClearMiniThings()
        {
            foreach (var t in miniThings) if (t.model != null) Destroy(t.model.gameObject);
            miniThings.Clear();
        }

        // A chicken's place is at its feet and its picture stands above them, so the top keeps a whole chicken of room.
        const float ChickenHeight = .8f, ChickenSideRoom = .45f, ChickenBottomRoom = .1f, ChickenTopRoom = ChickenHeight + .12f;
        Vector2 ChickenAreaMin => new Vector2(ChickenSideRoom, ChickenBottomRoom);
        Vector2 ChickenAreaMax => FloorMeters - new Vector2(ChickenSideRoom, ChickenTopRoom);

        MiniThing NewChicken()
        {
            Vector2 min = ChickenAreaMin, max = ChickenAreaMax; int edge = Random.Range(0, 4);
            var c = new MiniThing
            {
                p = edge == 0 ? new Vector2(min.x, Random.Range(min.y, max.y)) : edge == 1 ? new Vector2(max.x, Random.Range(min.y, max.y))
                    : new Vector2(Random.Range(min.x, max.x), edge == 2 ? min.y : max.y),
                gold = Random.value < .12f,
            };
            c.model = floorStage != null ? floorStage.Spawn("Chicken", ChickenHeight, c.gold ? new Color(1f, .80f, .22f) : (Color?)null) : null;
            return c;
        }

        void UpdateMinigame(float dt)
        {
            miniTime += dt;
            for (int i = 0; i < miniPunch.Length; i++) miniPunch[i] += dt;
            bool playing = MiniPlaying;
            if (playing && !miniWasPlaying)
            {
                if (cutsceneArea != null) sceneJuice?.Reward(cutsceneArea.rect.center, "GO!", MemoryFarmTheme.Straw, true);
                sceneJuice?.Burst(FloorRect.center, MemoryFarmTheme.Straw, 320, SpaceJuice.Transition);
            }
            miniWasPlaying = playing;
            if (playing) StepMiniThings(dt);
            else if (!miniOver && miniTime >= miniIntroSeconds + miniPlaySeconds) FinishMinigamePlay();
            PlaceMiniModels();
            UpdateMiniUi();
            if (miniTime < miniIntroSeconds + miniPlaySeconds + miniResultSeconds) return;
            EndMinigame();
            StartWatch();
        }

        void FinishMinigamePlay()
        {
            miniOver = true;
            ClearMiniThings();
            int best = -1, top = 0;
            foreach (var p in players) if (miniScore[p.Index] > top) { top = miniScore[p.Index]; best = p.Index; }
            // บาส 2026-11-02 bug hunt: 10/10/4 logged P1 alone as winner with margin 6. A tie now lists every top player
            // with margin 0 (+ tie flag), and the celebration uses the neutral straw instead of P1's colour (wall says TIME'S UP).
            var winners = new System.Collections.Generic.List<int>();
            if (top > 0) foreach (var p in players) if (miniScore[p.Index] == top) winners.Add(p.Index);
            bool tie = winners.Count > 1;
            sceneJuice?.Celebrate(best >= 0 && !tie ? PlayerColor(best) : MemoryFarmTheme.Straw);
            UpdateScorePanels(false);
            var mini = new int[players.Count]; for (int i = 0; i < players.Count; i++) mini[i] = miniScore[players[i].Index];
            int second = 0; foreach (int v in mini) if (v != top && v > second) second = v;
            ReplayLog.Event("minigame_end", 5, 4, winners.Count > 0 ? winners.ToArray() : null, ReplayLog.Data().Add("minigame", MiniTitle).Add("miniScores", mini)
                .Add("margin", tie ? 0 : top - second).Add("tie", tie).Add("scores", ReplayScores()));
        }

        void StepMiniThings(float dt)
        {
            var m = FloorMeters;
            if (miniKind == MiniKind.EggHunt)
            {
                miniSpawn -= dt;
                if (miniSpawn <= 0 && miniThings.Count < 6)
                {
                    miniSpawn = .7f;
                    for (int tries = 0; tries < 20; tries++)
                    {
                        var p = new Vector2(Random.Range(.6f, m.x - .6f), Random.Range(.6f, m.y - .6f));
                        bool clear = true;
                        foreach (var pl in players) if (InMinigame(pl) && (ToMeters(pl.Cursor.anchoredPosition) - p).magnitude < 1f) clear = false;
                        foreach (var o in miniThings) if ((o.p - p).magnitude < .9f) clear = false;
                        if (!clear) continue;
                        bool gold = Random.value < .1f;
                        miniThings.Add(new MiniThing { p = p, life = 3.5f, gold = gold, model = floorStage != null ? floorStage.Spawn("Egg", .55f, gold ? new Color(1f, .80f, .22f) : (Color?)null) : null });
                        break;
                    }
                }
            }
            for (int k = miniThings.Count - 1; k >= 0; k--)
            {
                var t = miniThings[k];
                t.age += dt;
                if (t.away > 0) { t.away -= dt; if (t.away <= 0) { var fresh = NewChicken(); t.p = fresh.p; t.gold = fresh.gold; t.v = Vector2.zero; if (t.model != null) Destroy(t.model.gameObject); t.model = fresh.model; } continue; }
                if (miniKind == MiniKind.ChickenChase)
                {
                    // Flee whoever is closest; otherwise peck around. Walls bounce, so a chicken can be cornered.
                    PlayerState near = null; float nd = 99;
                    foreach (var pl in players) { if (!InMinigame(pl)) continue; float d = (ToMeters(pl.Cursor.anchoredPosition) - t.p).magnitude; if (d < nd) { nd = d; near = pl; } }
                    if (near != null && nd < 1.3f) { var from = t.p - ToMeters(near.Cursor.anchoredPosition); t.v = from.normalized * (t.gold ? 1.1f : .9f); }   // a bit slower than a walking player (~1.2 m/s): chase it into a corner or cut it off
                    else { t.wander -= dt; if (t.wander <= 0) { float a = Random.Range(0, Mathf.PI * 2); t.v = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * .4f; t.wander = Random.Range(.8f, 2f); } }
                    t.p += t.v * dt;
                    Vector2 min = ChickenAreaMin, max = ChickenAreaMax;
                    if (t.p.x < min.x || t.p.x > max.x) { t.v.x = -t.v.x; t.p.x = Mathf.Clamp(t.p.x, min.x, max.x); }
                    if (t.p.y < min.y || t.p.y > max.y) { t.v.y = -t.v.y; t.p.y = Mathf.Clamp(t.p.y, min.y, max.y); }
                }
                else if (t.age >= t.life)
                {
                    sceneJuice?.Burst(ToLocal(t.p), MemoryFarmTheme.Cream, 90, SpaceJuice.Respawn);
                    if (t.model != null) Destroy(t.model.gameObject);
                    miniThings.RemoveAt(k);
                    continue;
                }
                float reach = miniKind == MiniKind.ChickenChase ? .45f : .5f;
                foreach (var pl in players)
                {
                    if (!InMinigame(pl) || (ToMeters(pl.Cursor.anchoredPosition) - t.p).magnitude > reach) continue;
                    CatchMiniThing(pl, t);
                    if (miniKind == MiniKind.ChickenChase) { t.away = .6f; if (t.model != null) t.model.gameObject.SetActive(false); }
                    else { if (t.model != null) Destroy(t.model.gameObject); miniThings.RemoveAt(k); }
                    break;
                }
            }
        }

        void CatchMiniThing(PlayerState p, MiniThing t)
        {
            int pts = t.gold ? miniGoldPoints : miniCatchPoints;
            p.Score += pts; miniScore[p.Index] += pts; miniPunch[p.Index] = 0;
            var at = ToLocal(t.p);
            sceneJuice?.Burst(at, t.gold ? MemoryFarmTheme.Straw : PlayerColor(p.Index), t.gold ? 220 : 140, SpaceJuice.Pickup);
            sceneJuice?.Reward(at, "+" + pts, t.gold ? new Color(1f, .87f, .35f) : MemoryFarmTheme.Cream);
            farmVfx?.Answer(p.Cursor, true, p.Index, players.Count, t.gold ? 3 : 1);
            UpdateScorePanels(false);
            ReplayLog.Event("minigame_catch", 2, 2, p.Index, ReplayLog.Data().Add("minigame", MiniTitle).Add("gold", t.gold).Add("points", pts)
                .Add("miniScore", miniScore[p.Index]).Add("timeLeft", Mathf.Max(0, miniIntroSeconds + miniPlaySeconds - miniTime)));
            LogReplayLeadChange(MiniTitle);   // บาส 2026-11-02 bug hunt: minigame points can change the leader too
        }

        void PlaceMiniModels()
        {
            if (floorStage == null) return;
            foreach (var t in miniThings)
            {
                if (t.model == null || t.away > 0) continue;
                if (!t.model.gameObject.activeSelf) t.model.gameObject.SetActive(true);
                if (miniKind == MiniKind.ChickenChase)
                {
                    float speed = t.v.magnitude, hop = speed > .6f ? Mathf.Abs(Mathf.Sin(t.age * 16f)) * .08f : 0;
                    float waddle = speed > .6f ? Mathf.Sin(t.age * 16f) * 6f : 0;
                    // The picture faces right; straight up or down keeps the way it last faced.
                    if (Mathf.Abs(t.v.x) > .05f) MemoryFarmFloorStage.FaceLeft(t.model, t.v.x < 0);
                    floorStage.Place(t.model, t.p, hop, waddle);
                }
                else
                {
                    float k = t.age / Mathf.Max(.1f, t.life), shake = k > .7f ? Mathf.Sin(t.age * 40f) * 12f * (k - .7f) / .3f : 0;
                    floorStage.Place(t.model, t.p, 0, shake);
                }
            }
        }

        // ------------------------------------------------------------------ minigame UI (wall poster + floor sign)
        RectTransform miniWall, miniFloor;
        UiThemeKit.Plate miniRibbon, miniRail;
        TMP_Text miniTitleText, miniKicker, miniRuleText, miniBig, miniFloorText, miniTimeText;
        Image miniRailFill;
        readonly List<(RectTransform root, UiThemeKit.Plate bar, TMP_Text score, TMP_Text tag)> miniBars = new();

        void ShowMiniUi(bool show)
        {
            if (!show) { if (miniWall != null) miniWall.gameObject.SetActive(false); if (miniFloor != null) miniFloor.gameObject.SetActive(false); return; }
            var wallParent = OptionCWallParent;
            if (wallParent == null || playArea == null) return;
            if (miniWall == null) BuildMiniUi(wallParent);
            miniTitleText.text = MiniTitle;
            miniRuleText.text = MiniRule;
            miniWall.gameObject.SetActive(true); miniWall.SetAsLastSibling();
            miniFloor.gameObject.SetActive(true);
            // over the picture stage, under the cursors
            if (characterPanel != null && characterPanel.parent == playArea) miniFloor.SetSiblingIndex(characterPanel.GetSiblingIndex());
            else miniFloor.SetAsLastSibling();
            UpdateMiniUi();
        }

        void BuildMiniUi(RectTransform wallParent)
        {
            float h = Mathf.Max(1, cutsceneArea.rect.height), u = h / 2160f;
            miniWall = UiThemeKit.Child(wallParent, "Panel == Minigame");
            UiThemeKit.Stretch(miniWall, 0);
            var poster = MemoryFarmTheme.Sign(miniWall, "Minigame poster", MemoryFarmTheme.Cream, u, true, false, 60, 20);
            UiThemeKit.Place(poster.rect, .22f, .06f, .78f, .78f);
            miniRibbon = MemoryFarmTheme.Sign(miniWall, "Minigame ribbon", MemoryFarmTheme.BarnRed, u, true, true, 46, 16);
            UiThemeKit.Place(miniRibbon.rect, .30f, .72f, .70f, .88f);
            miniTitleText = UiThemeKit.Text(miniRibbon.rect, "Minigame title", MemoryFarmTheme.Black);
            Style(miniTitleText, miniRibbon.rect, .05f, .1f, .95f, .9f, h * .085f);
            MemoryFarmTheme.StyleText(miniTitleText, true, MemoryFarmTheme.Cream, .22f, MemoryFarmTheme.WoodDark, 6);
            miniKicker = UiThemeKit.Text(miniWall, "Minigame kicker", MemoryFarmTheme.Bold);
            Style(miniKicker, miniWall, .32f, .655f, .68f, .71f, h * .042f);
            MemoryFarmTheme.StyleText(miniKicker, true, MemoryFarmTheme.InkSoft, 0, default, 4);
            miniKicker.text = "FARM MINIGAME";
            miniRuleText = UiThemeKit.Text(miniWall, "Minigame rule", MemoryFarmTheme.Bold);
            Style(miniRuleText, miniWall, .26f, .53f, .74f, .64f, h * .05f);
            MemoryFarmTheme.StyleText(miniRuleText, false, MemoryFarmTheme.Ink, 0, default, 1);
            miniRuleText.textWrappingMode = TextWrappingModes.Normal;
            miniBig = UiThemeKit.Text(miniWall, "Minigame big", MemoryFarmTheme.Black);
            Style(miniBig, miniWall, .32f, .50f, .68f, .64f, h * .09f);
            MemoryFarmTheme.StyleText(miniBig, true, MemoryFarmTheme.BarnRed, 0, default, 6);
            // timer rail, same build as the question rail
            float railH = .042f * h;
            miniRail = UiThemeKit.MakePlate(miniWall, "Minigame rail", MemoryFarmTheme.WoodDark, MemoryFarmTheme.WoodDark, railH * .5f, 0);
            UiThemeKit.Place(miniRail.rect, .30f, .565f, .64f, .607f);
            var fillRect = UiThemeKit.Child(miniRail.rect, "Fill bar");
            miniRailFill = fillRect.gameObject.AddComponent<Image>();
            UiThemeKit.Skin(miniRailFill, UiThemeKit.Shape.Rounded, MemoryFarmTheme.Grass, railH * .5f - 12 * u);
            miniTimeText = UiThemeKit.Text(miniWall, "Minigame seconds", MemoryFarmTheme.Black);
            Style(miniTimeText, miniWall, .645f, .55f, .71f, .62f, h * .05f);
            MemoryFarmTheme.StyleText(miniTimeText, true, MemoryFarmTheme.Ink, 0, default, 2);
            // one column per player: a player-colour bar on a wooden base, ear-tag label underneath
            for (int i = 0; i < 6; i++)
            {
                var col = UiThemeKit.Child(miniWall, "Minigame player " + (i + 1));
                var bar = UiThemeKit.MakePlate(col, "Bar", MemoryFarmTheme.WoodDark, PlayerColor(i), 24 * u, 8 * u, true, MemoryFarmTheme.Straw);
                var score = UiThemeKit.Text(col, "Score", MemoryFarmTheme.Black);
                MemoryFarmTheme.StyleText(score, true, MemoryFarmTheme.Ink, 0, default, 2);
                score.alignment = TextAlignmentOptions.Center; UiThemeKit.Size(score, h * .055f, .5f);
                var tagPlate = UiThemeKit.MakePlate(col, "Tag", MemoryFarmTheme.WoodDark, PlayerColor(i), 20 * u, 6 * u);
                UiThemeKit.Place(tagPlate.rect, .1f, 0, .9f, .16f);
                var tag = UiThemeKit.Text(tagPlate.rect, "Tag text", MemoryFarmTheme.Black);
                UiThemeKit.Stretch(tag.rectTransform, 6 * u); tag.alignment = TextAlignmentOptions.Center; UiThemeKit.Size(tag, h * .038f, .5f);
                MemoryFarmTheme.StyleText(tag, true, MemoryFarmTheme.InkOn(PlayerColor(i)), 0, default, 2);
                tag.text = "P" + (i + 1);
                miniBars.Add((col, bar, score, tag));
            }
            miniFloor = UiThemeKit.Child(playArea, "Panel == Minigame Floor");
            UiThemeKit.Stretch(miniFloor, 0);
            float fu = Mathf.Max(1, playArea.rect.height) / (float)FloorDisplaySettings.Height;
            var plank = MemoryFarmTheme.Sign(miniFloor, "Minigame floor sign", MemoryFarmTheme.Wood, fu, true, false, 40, 14);
            UiThemeKit.Place(plank.rect, .28f, .40f, .72f, .60f);
            miniFloorText = UiThemeKit.Text(miniFloor, "Minigame floor text", MemoryFarmTheme.Black);
            UiThemeKit.PlaceText(miniFloorText, miniFloor, .30f, .42f, .70f, .58f, 130 * fu);
            MemoryFarmTheme.StyleText(miniFloorText, true, MemoryFarmTheme.Straw, .24f, MemoryFarmTheme.WoodDark, 8);
        }

        void UpdateMiniUi()
        {
            if (miniWall == null || !miniWall.gameObject.activeSelf) return;
            float h = Mathf.Max(1, cutsceneArea.rect.height);
            bool intro = miniTime < miniIntroSeconds, playing = MiniPlaying;
            float left = miniIntroSeconds + miniPlaySeconds - miniTime;
            miniRuleText.gameObject.SetActive(intro);
            miniRail.rect.gameObject.SetActive(playing); miniTimeText.gameObject.SetActive(playing);
            miniBig.gameObject.SetActive(!playing);
            miniRibbon.rect.localScale = Vector3.one * UiThemeKit.Punch(miniTime, .28f, .5f);
            if (miniRibbon.glow != null) miniRibbon.glow.color = UiThemeKit.Alpha(MemoryFarmTheme.Straw, .2f + .3f * (.5f + .5f * Mathf.Sin(miniTime * 5f)));
            if (intro)
            {
                miniRuleText.rectTransform.anchorMin = new Vector2(.26f, .53f); miniRuleText.rectTransform.anchorMax = new Vector2(.74f, .64f);
                miniBig.text = Mathf.CeilToInt(miniIntroSeconds - miniTime).ToString();
                UiThemeKit.Place(miniBig.rectTransform, .44f, .42f, .56f, .53f);
                miniBig.color = MemoryFarmTheme.BarnRed;
            }
            else if (!playing)
            {
                UiThemeKit.Place(miniBig.rectTransform, .30f, .52f, .70f, .64f);
                int best = -1, top = 0; bool tie = false;
                foreach (var p in players) { int s = miniScore[p.Index]; if (s > top) { top = s; best = p.Index; tie = false; } else if (s == top && s > 0) tie = true; }
                miniBig.text = best < 0 ? "TIME'S UP!" : tie ? $"TIME'S UP! · {top}" : $"P{best + 1} WINS · +{top}";
                miniBig.color = best >= 0 && !tie ? Color.Lerp(PlayerColor(best), MemoryFarmTheme.Ink, .35f) : MemoryFarmTheme.BarnRed;
            }
            else
            {
                float ratio = Mathf.Clamp01(left / Mathf.Max(.1f, miniPlaySeconds));
                var r = miniRailFill.rectTransform; r.anchorMin = Vector2.zero; r.anchorMax = new Vector2(ratio, 1); r.offsetMin = new Vector2(12, 8) * h / 2160f; r.offsetMax = new Vector2(-12, -8) * h / 2160f;
                miniRailFill.color = ratio > .35f ? MemoryFarmTheme.Grass : MemoryFarmTheme.BarnRed;
                miniTimeText.text = Mathf.CeilToInt(left).ToString();
            }
            // player columns
            var shown = new List<PlayerState>();
            foreach (var p in players) if (p.Index < participant.Length ? participant[p.Index] : true) shown.Add(p);
            int top2 = 1; foreach (var p in shown) top2 = Mathf.Max(top2, miniScore[p.Index]);
            float width = Mathf.Min(.08f, .44f / Mathf.Max(1, shown.Count)), gap = .015f, total = shown.Count * width + (shown.Count - 1) * gap, x0 = .5f - total / 2;
            for (int i = 0; i < miniBars.Count; i++)
            {
                int at = shown.FindIndex(p => p.Index == i);
                var b = miniBars[i];
                b.root.gameObject.SetActive(at >= 0);
                if (at < 0) continue;
                float x = x0 + at * (width + gap);
                UiThemeKit.Place(b.root, x, .10f, x + width, .53f);
                float fill = .16f + .62f * miniScore[i] / top2;
                UiThemeKit.Place(b.bar.rect, .1f, .19f, .9f, .19f + (fill - .16f) + .04f);
                b.score.rectTransform.SetParent(b.root, false);
                UiThemeKit.Place(b.score.rectTransform, -.2f, .23f + (fill - .16f) + .02f, 1.2f, .23f + (fill - .16f) + .2f);
                b.score.text = miniScore[i].ToString();
                b.score.transform.localScale = Vector3.one * UiThemeKit.Punch(miniPunch[i], .35f);
                if (b.bar.trim != null) b.bar.trim.enabled = miniScore[i] == top2 && miniScore[i] > 0;
            }
            if (miniFloor != null)
            {
                miniFloor.gameObject.SetActive(!playing);
                miniFloorText.text = intro ? MiniTitle : "TIME'S UP!";
                miniFloorText.transform.localScale = Vector3.one * UiThemeKit.Punch(intro ? miniTime : miniTime - miniIntroSeconds - miniPlaySeconds, .25f, .5f);
            }
        }
    }
}
