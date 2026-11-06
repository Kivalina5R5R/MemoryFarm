using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Deepspace
{
    // Who plays, and the dropped-player pause.
    //   * There is no start button: the wall plays its menu video to the end, then once someone is on the floor the screens
    //     fade to the player pads. Players are counted at that moment, and anyone who connects while the pads are up gets
    //     a pad too, with the row re-centred. Once the game has gone into the stage nobody new joins until the next game, but
    //     a player who has picked a pad can leave and come back as many times as they like, as the same player.
    //   * Signal lost: a participant with no position for dropoutGraceSeconds pauses the game (farmers stop walking, the
    //     answer timer stops). When every participant is back, 3-2-1 and play resumes where it stopped. If someone is
    //     still missing after dropoutGiveUpSeconds they are dropped from the session and the rest play on.
    public sealed partial class MemoryFarmGameManager
    {
        [Header("Ready / dropped players")]
        [Tooltip("A player with no position for this long pauses the game (tag blips shorter than this are ignored).")]
        [SerializeField, Min(.1f)] float dropoutGraceSeconds = 1.5f;
        [Tooltip("If a lost player is not back after this long, they leave the session and the others play on.")]
        [SerializeField, Min(1f)] float dropoutGiveUpSeconds = 20f;
        const float ResumeCountdownSeconds = 3f;

        readonly bool[] readyPresent = new bool[6], participant = new bool[6], droppedOut = new bool[6];
        readonly float[] missingFor = new float[6], rejoinFor = new float[6];
        bool signalPaused;
        float pausedFor, resumeTimer;
        MemoryFarmReadyPanel wallReady, floorReady;

        // With a live UWB receiver the player count is whoever stands on the floor, not the Inspector.
        bool CountFromFloor => floorPlayers != null && floorPlayers.IsLive;

        void ApplyPlayerCount(int count)
        {
            count = Mathf.Clamp(count, 1, 6);
            if (count == players.Count) return;
            playerCount = count;
            ResolvePlayers();
            foreach (var player in players) player.Reset(PlayerStartLocal(player.Index));
            UpdateScorePanels(false);
        }

        bool PlayerPresent(int index)
        {
            // บาส 2026-10-05 bug fix: inclusive on every edge. Rect.Contains excludes xMax/yMax, and UWB clamps a tag at or
            // past the right/far edge to exactly that value, so a player standing there read as "dropped" and paused the game.
            if (!TryGetInputLocalPoint(index, out var local)) return false;
            var r = FloorRect;
            return local.x >= r.xMin && local.x <= r.xMax && local.y >= r.yMin && local.y <= r.yMax;
        }

        // A tag's UNCLAMPED position inside the floor rect (PlayerPresent clamps onto the floor, so it is true for someone
        // standing beside it). Without UWB (mouse / Inspector count) there is nothing to check.
        bool StandsOnFloor(int index)
        {
            if (floorPlayers == null || !floorPlayers.IsLive) return true;
            if (!floorPlayers.TryGetLocalUnclamped(index, InputArea, out var local)) return false;
            var r = FloorRect;
            return local.x >= r.xMin && local.x <= r.xMax && local.y >= r.yMin && local.y <= r.yMax;
        }

        void EnsureReadyPanels()
        {
            var font = questionText != null ? questionText.font : null;
            if (wallReady == null && cutsceneArea != null)
            {
                wallReady = MemoryFarmReadyPanel.Create(cutsceneArea, true, font);
                wallSkin.DressPlates(wallReady.transform, cutsceneArea.rect.height);
            }
            if (floorReady == null && InputArea != null) floorReady = MemoryFarmReadyPanel.Create(InputArea, false, font);
        }

        void ShowReadyPanels(string title, string status, string hint, bool[] present, bool[] gone, int chips = -1)
        {
            EnsureReadyPanels();
            int required = chips > 0 ? chips : players.Count;
            wallReady?.Show(title, status, hint, present, gone, required);
            floorReady?.Show(title, status, hint, present, gone, required);
        }

        void HideReadyPanels() { wallReady?.Hide(); floorReady?.Hide(); }

        // Long enough that one stray reading from a tag does not open the game.
        const float DetectSeconds = .5f;
        const float MenuFadeSeconds = .5f;
        // A video that cannot play must not hold the menu forever: this long past its length, the game moves on.
        const float VideoGraceSeconds = 2f;
        float detectedFor, menuShownAt;
        bool enteringGame;
        Image wallCurtain, floorCurtain;

        void UpdateAutoStart(float dt)
        {
            if (enteringGame || !CountFromFloor || !MenuVideoDone()) return;
            detectedFor = CountPlayersAtStart() > 0 ? detectedFor + dt : 0f;
            if (detectedFor < DetectSeconds) return;
            detectedFor = 0f;
            StartCoroutine(FadeIntoGame());
        }

        bool MenuVideoDone()
        {
            var video = cutsceneMenuPanel != null ? cutsceneMenuPanel.GetComponentInChildren<MenuIntroVideo>(true) : null;
            if (video == null || video.intro == null) return true;
            return video.IntroFinished || Time.unscaledTime - menuShownAt > (float)video.intro.length + VideoGraceSeconds;
        }

        IEnumerator FadeIntoGame()
        {
            enteringGame = true;
            yield return FadeCurtains(0f, 1f);
            ResetGame();
            yield return FadeCurtains(1f, 0f);
            enteringGame = false;
        }

        IEnumerator FadeCurtains(float from, float to)
        {
            for (float time = 0f; time < MenuFadeSeconds; time += Time.unscaledDeltaTime)
            {
                SetCurtains(Mathf.Lerp(from, to, time / MenuFadeSeconds));
                yield return null;
            }
            SetCurtains(to);
        }

        void SetCurtains(float alpha)
        {
            if (wallCurtain == null) wallCurtain = CreateCurtain(cutsceneArea);
            if (floorCurtain == null) floorCurtain = CreateCurtain(playArea);
            foreach (var curtain in new[] { wallCurtain, floorCurtain })
            {
                if (curtain == null) continue;
                curtain.color = new Color(0f, 0f, 0f, alpha);
                curtain.gameObject.SetActive(alpha > 0f);
                curtain.transform.SetAsLastSibling();   // whatever panel opened meanwhile went last too
            }
        }

        static Image CreateCurtain(RectTransform canvas)
        {
            if (canvas == null) return null;
            var rect = UiThemeKit.Child(canvas, "Fade Curtain");
            UiThemeKit.Stretch(rect, 0);
            if (!rect.TryGetComponent(out Image image)) image = rect.gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        // Reads who is on the floor now; 0 means there is nobody to start a game for.
        int CountPlayersAtStart()
        {
            if (ShouldUseDirectEditorMouse(0)) return 1;
            if (!CountFromFloor) return players.Count;
            floorPlayers.ResetOrder();
            return floorPlayers.SlotCount;
        }

        void StartSession()
        {
            state = GameState.Ready;
            signalPaused = false;
            System.Array.Clear(participant, 0, participant.Length);
            System.Array.Clear(droppedOut, 0, droppedOut.Length);
            System.Array.Clear(missingFor, 0, missingFor.Length);
            System.Array.Clear(rejoinFor, 0, rejoinFor.Length);
            HideQuestion();
            SetStagePauseVisible(false);
            SetSetAndSectionVisible(false);
            ClearParadeViews();
            BeginAfterReady(CountPlayersAtStart());
        }

        // Someone connected while the pads are up, before the game goes into the stage: one more player and one more pad.
        void UpdateLateJoiners()
        {
            if (!PadsActive || pads.Finishing || UsesEditorMouse) return;
            int count = Mathf.Min(floorPlayers.SlotCount, 6);
            if (count <= players.Count) return;

            int firstNew = players.Count;
            playerCount = count;
            for (int i = firstNew; i < count; i++)
            {
                var player = new PlayerState(i, FindDirectRectIn(characterPanel, $"Player {i + 1}") ?? CreatePlayerAnchor(i));
                player.Reset(PlayerStartLocal(i));
                players.Add(player);
                participant[i] = true;
            }
            pads.SetCount(count);
            UpdateScorePanels(false);
            Debug.Log($"[MemoryFarm] {count} players now (joined on the player pads)", this);
        }

        void BeginAfterReady(int count)
        {
            ApplyPlayerCount(count);
            System.Array.Clear(participant, 0, participant.Length);
            for (int i = 0; i < count && i < participant.Length; i++) participant[i] = true;
            HideReadyPanels();
            // บาส 2026-10-22: right after Start (once READY has the count) each player picks their number on a floor pad
            // (shared PlayerPadSelect); when the pads and their particles are done the countdown runs and then the first parade.
            StartPlayerPads(count, () =>
            {
                floorPlayers?.LockRoster();   // into the stage: nobody new joins until the next game
                // Anyone who walked off before the stage starts plays on their return; the others need not wait for them.
                for (int i = 0; i < players.Count && i < participant.Length; i++)
                    if (!PlayerPresent(i)) { participant[i] = false; droppedOut[i] = true; }
                BeginPlaySession();
                ReplayLog.Event("ready", 0f, 0f, null, ReplayLog.Data().Add("players", players.Count));
                StartCountdown();
                if (padsShown) stateTimer = Mathf.Max(stateTimer, afterPadsCountdownSeconds);   // no pads (no UWB): the usual 3-2-1
            });
        }

        [Tooltip("Countdown after the player pads, before the first parade.")]
        [SerializeField, Min(0f)] float afterPadsCountdownSeconds = 3f;
        [Tooltip("Editor with the mouse as P1: show the player pads and let the mouse pick.")]
        [SerializeField] bool playerSelectInEditor = true;
        PlayerPadSelect pads;
        bool padsShown;
        bool PadsActive => pads != null && pads.Active;

        static readonly PlayerPadArt.Look FarmPadLook = new()
        {
            Folder = "MemoryFarm/PlayerSelect",
            NumberInk = new Color(.357f, .239f, .145f),
            BarColor = new Color(.373f, .659f, .31f),
            WellColor = new Color(.937f, .886f, .769f),
        };

        // The pad screen's style (shared by Play and the Editor bake).
        PlayerPadSelect InstallPads()
        {
            if (pads == null && playArea != null)
            {
                var style = new PlayerPadSelect.Style
                {
                    Title = "", Hint = "",   // no heading sign on the floor
                    TitleFont = MemoryFarmTheme.Black, BodyFont = MemoryFarmTheme.Bold, Players = floorPlayers,
                    Ground = Color.clear, WallGround = MemoryFarmTheme.Shade,
                    Plate = MemoryFarmTheme.Cream, Ink = MemoryFarmTheme.Cream, Accent = MemoryFarmTheme.Straw,
                    PadColor = PlayerColor,
                    PadVanished = (pad, k) => { if (farmVfx != null) farmVfx.PadBurst(pad, PlayerColor(k)); },
                    EffectPlaying = () => farmVfx != null && farmVfx.FloorPlaying,
                    StyleText = (t, heading, face) => MemoryFarmTheme.StyleText(t, heading, face == MemoryFarmTheme.Ink ? face : face, .22f, MemoryFarmTheme.WoodDark, heading ? 6 : 3),
                };
                // Flat round pads with the number in the middle and a ring bar that shows while stood on (PlayerPadArt)
                PlayerPadArt.Apply(style, FarmPadLook, () => playArea);
                pads = PlayerPadSelect.Install(this, null, playArea, style);   // floor only
            }
            return pads;
        }

        void StartPlayerPads(int count, System.Action done)
        {
            InstallPads();
            if (pads == null) { done(); return; }
            padsShown = false;                 // Begin calls back at once (false) when there is nothing to pick on
            pads.Begin(count, done, playerSelectInEditor && UsesEditorMouse);
            padsShown = pads.Active;
        }

        // Called every frame of a running session (not READY / Result). True = paused, skip the game update.
        bool UpdateDropouts(float dt)
        {
            // A dropped player back on the floor for dropoutGraceSeconds is a full participant again, with their score and
            // number, however many times they left. "Present" alone is any heard tag (its position is clamped onto the
            // floor), so they must really stand on the floor, and the hold keeps a one-frame blip from re-admitting them.
            for (int i = 0; i < players.Count && i < droppedOut.Length; i++)
            {
                if (!droppedOut[i]) continue;
                rejoinFor[i] = PlayerPresent(i) && StandsOnFloor(i) ? rejoinFor[i] + dt : 0f;
                if (rejoinFor[i] < dropoutGraceSeconds) continue;
                droppedOut[i] = false; participant[i] = true; missingFor[i] = 0f; rejoinFor[i] = 0f;
                ReplayLog.Event("player_rejoined", 0f, 0f, i, ReplayLog.Data().Add("set", currentSet).Add("question", currentSection));
            }
            bool anyLost = false, allBack = true;
            for (int i = 0; i < players.Count && i < participant.Length; i++)
            {
                if (!participant[i]) continue;
                bool here = PlayerPresent(i);
                readyPresent[i] = here;
                missingFor[i] = here ? 0f : missingFor[i] + dt;
                if (missingFor[i] >= dropoutGraceSeconds) anyLost = true;
                if (!here) allBack = false;
            }
            for (int i = 0; i < readyPresent.Length; i++) if (i >= players.Count || !participant[i]) readyPresent[i] = !droppedOut[i] && i < players.Count && PlayerPresent(i);

            if (!signalPaused)
            {
                if (!anyLost) return false;
                signalPaused = true; pausedFor = 0f; resumeTimer = 0f;
                ReplayLog.Event("signal_lost", 2f, 0f, Missing(), ReplayLog.Data().Add("state", state.ToString()).Add("set", currentSet).Add("question", currentSection));
            }

            pausedFor += dt;
            if (!allBack && pausedFor >= dropoutGiveUpSeconds)
            {
                // Give up on whoever has really been gone past the grace; the rest carry on. (Was everyone missing this
                // exact frame, so a one-frame blip at that moment dropped a player who never left.)
                var lost = new System.Collections.Generic.List<int>();
                for (int i = 0; i < participant.Length; i++) if (participant[i] && !readyPresent[i] && missingFor[i] >= dropoutGraceSeconds) lost.Add(i);
                var gone = lost.ToArray();
                foreach (int i in gone) { participant[i] = false; droppedOut[i] = true; }
                if (gone.Length > 0) ReplayLog.Event("player_dropped", 0f, 0f, gone, ReplayLog.Data().Add("seconds", pausedFor).Add("set", currentSet).Add("question", currentSection));
                // บาส 2026-10-05 bug fix: nobody left -> back to the menu, instead of "everyone's back" and playing the
                // remaining sets to an empty floor.
                bool anyoneLeft = false;
                for (int i = 0; i < participant.Length; i++) if (participant[i]) anyoneLeft = true;
                if (!anyoneLeft) { ShowMenu(); return true; }
                allBack = true;
                for (int i = 0; i < participant.Length; i++) if (participant[i] && !readyPresent[i]) allBack = false;
            }

            resumeTimer = allBack ? resumeTimer + dt : 0f;
            if (resumeTimer >= ResumeCountdownSeconds)
            {
                signalPaused = false;
                HideReadyPanels();
                ReplayLog.Event("signal_back", 0f, 2f, null, ReplayLog.Data().Add("pausedSeconds", pausedFor));
                return false;
            }

            int left = Mathf.CeilToInt(Mathf.Max(0f, dropoutGiveUpSeconds - pausedFor));
            string status = allBack ? Mathf.Clamp(Mathf.CeilToInt(ResumeCountdownSeconds - resumeTimer), 1, 3).ToString() : MissingNames();
            string hint = allBack ? "กลับมาครบแล้ว เล่นต่อ" : $"กลับเข้าพื้นที่ภายใน {left} วิ ไม่งั้นเกมจะเล่นต่อโดยไม่มีผู้เล่นที่หลุด";
            ShowReadyPanels(allBack ? "พร้อมเล่นต่อ" : "ผู้เล่นหลุด", status, hint, readyPresent, droppedOut);
            return true;
        }

        int[] Missing()
        {
            var list = new System.Collections.Generic.List<int>();
            for (int i = 0; i < participant.Length; i++) if (participant[i] && !readyPresent[i]) list.Add(i);
            return list.ToArray();
        }

        string MissingNames()
        {
            var text = "";
            foreach (int i in Missing()) text += (text.Length > 0 ? "  " : "") + "P" + (i + 1);
            return text.Length > 0 ? text + " หลุด" : "";
        }
    }
}
