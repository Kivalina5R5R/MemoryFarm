using System;
using System.Collections.Generic;
using Fortal.Players;
using Fortal.UWB;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Deepspace
{
    public sealed partial class MemoryFarmGameManager : MonoBehaviour
    {
        ArcadeJuice sceneJuice;
        enum GameState { Countdown, Watch, Question, Reveal, Break, Result, Ready, Rule, Minigame }   // Rule / Minigame: แบบ C (.OptionC.cs)
        enum SessionRounds
        {
            [InspectorName("3 Round")] Three = 3,
            [InspectorName("5 Round")] Five = 5,
            [InspectorName("7 Round")] Seven = 7
        }

        [Serializable]
        struct NamedColor
        {
            public string Name;
            public Color Color;

            public NamedColor(string name, Color color)
            {
                Name = name;
                Color = color;
            }
        }

        struct ParadeItem
        {
            public int Farmer;
            public int Animal;
            // Looks only: no question asks about it, and it stays the same on a re-walk.
            public bool Female;
        }

        sealed class QuestionState
        {
            public string Text;
            public string[] Choices;
            public int AnswerIndex;
            public string Type;
        }

        sealed class PlayerState
        {
            public readonly int Index;
            public readonly RectTransform Cursor;
            public Vector2 Velocity;
            public int Score;
            public int Streak;
            public int AnswerIndex = -1;
            public bool ScoredThisQuestion;
            public int HeldZone = -1;      // last zone actually seen this question (survives a short tag dropout)
            public float HeldZoneTime;
            public int StableZone = -1;    // last zone seen OUTSIDE a divider band (บาส 2026-10-06, balance note A2)
            public bool Absent;            // tag never seen during this question: sits it out, streak kept (note A1)
            public float AnswerSince = float.MaxValue;   // question seconds when the current answer was stepped into (fast bonus)
            public int LastRealZone = -1;  // last zone actually READ from tracking this question (not the held one) - A/B

            public PlayerState(int index, RectTransform cursor)
            {
                Index = index;
                Cursor = cursor;
            }

            public void Reset(Vector2 localPosition)
            {
                Score = 0;
                Streak = 0;
                AnswerIndex = -1;
                HeldZone = -1;
                StableZone = -1;
                Absent = false;
                ScoredThisQuestion = false;
                Velocity = Vector2.zero;
                Cursor.anchoredPosition = localPosition;
                Cursor.gameObject.SetActive(true);
            }
        }

        [Header("Input")]
        [SerializeField] UWBPlayerSpawner playerSpawner;
        [Tooltip("Editor with UWB Simulation only: the mouse plays P1 and moves that player's marker.")]
        [SerializeField] bool useMouseInEditor = true;
        [Tooltip("A player whose tag drops out keeps their last answer zone this long, so a UWB blip on the grading frame isn't a MISSED.")]
        // บาส 2026-10-06 (balance note A1): 1 s, was .5 s - the zone seen within the last second before time-up counts.
        [SerializeField, Min(0f)] float answerDropoutGraceSeconds = 1f;

        [Header("Scene")]
        [SerializeField] RectTransform playArea;
        [SerializeField] RectTransform cutsceneArea;
        [SerializeField] RectTransform cutsceneMenuPanel;
        [SerializeField] RectTransform gameplayMenuPanel;
        [SerializeField] RectTransform characterPanel;
        [SerializeField] RectTransform gameplayUiPanel;
        [SerializeField] RectTransform centerDisplay;
        [SerializeField] RectTransform farmerPanel;
        [SerializeField] RectTransform setAndSectionPanel;
        [SerializeField] RectTransform sectionCountPanel;
        [SerializeField] RectTransform questionPanel;
        [SerializeField] RectTransform answerChoicePanel;
        [SerializeField] RectTransform gameplayScorePanel;
        [SerializeField] RectTransform stagePausePanel;
        [SerializeField] RectTransform resultPanel;
        [SerializeField] RectTransform gameplayResultPanel;
        [SerializeField] RectTransform resultScorePanel;
        [SerializeField] RectTransform gameplayResultScorePanel;
        [SerializeField] TMP_Text setText;
        [SerializeField] TMP_Text questionText;
        [SerializeField] TMP_Text stagePauseCountdownText;
        [SerializeField] TMP_Text winnerText;
        [SerializeField] Scrollbar answerTimerScrollbar;
        [SerializeField] Image answerTimerFill;
        [SerializeField] RawImage farmerPrefab;
        [SerializeField] RawImage animalPrefab;
        [SerializeField, Range(1, 6)] int playerCount = 4;

        [Header("Memory Farm")]
        [SerializeField] float arenaWidthMeters = 11f;
        [SerializeField] float arenaDepthMeters = 5f;
        // บาส 2026-10-21 (แบบ C): 5 sets, the last two keep 6 farmers and add a twist instead (.OptionC.cs).
        [SerializeField, InspectorName("Session")] SessionRounds setCount = SessionRounds.Five;
        [SerializeField] int sectionsPerSet = 5;
        [SerializeField] int[] farmersPerSet = { 4, 5, 6, 6, 6 };
        [SerializeField] float countdownSeconds = 3f;
        // บาส 2026-10-02: each farmer crosses at its own random pace (was a fixed 1.1 s) - some dash, some dawdle.
        [Tooltip("Fastest a farmer crosses the window, seconds.")]
        [SerializeField, Min(.2f)] float farmerWalkMinSeconds = 1f;
        [Tooltip("Slowest a farmer crosses the window, seconds.")]
        [SerializeField, Min(.2f)] float farmerWalkMaxSeconds = 5f;
        [Tooltip("The farmers' route on the wall: out of the barn, down the road and off the left edge.")]
        [SerializeField] FarmerWalkPath farmerWalkPath = new();
        float currentWalkSeconds = 3f;
        [SerializeField] float answerSeconds = 8f;
        [SerializeField] float revealSeconds = 2.2f;
        [SerializeField] float setBreakSeconds = 5f;
        [SerializeField] int scorePerCorrect = 10;
        [SerializeField] int maxStreakBonus = 5;
        // บาส 2026-11-02 (A + B, "ไม่ให้ผู้เล่นเบื่อกับเกมหลักที่เป็นแค่เกมตอบคำถาม"): the 8 s answer wait was dead time once
        // everyone stood on an answer.
        //   A: stepping into the right answer within fastAnswerSeconds earns fastAnswerBonus (x the final-set multiplier).
        //      Already standing on it when the question appears does not count - you have to move to it.
        //   B: once every player on the floor has walked into an answer and nobody has moved for allLockedHoldSeconds (and
        //      the fast window has passed, so the question was readable), the reveal comes at once.
        [Tooltip("A: seconds from the question appearing in which stepping into the right answer earns the fast bonus.")]
        [SerializeField, Min(0f)] float fastAnswerSeconds = 3f;
        [SerializeField, Min(0)] int fastAnswerBonus = 1;
        // บาส 2026-11-04: B is OFF - the question always runs its full answerSeconds and is graded on where each player stands
        // when the time is up ("รอเวลาหมดแล้วจะเช็คคำตอบที่ผู้เล่นยืน"): ending as soon as someone stood on the right answer
        // cut the others' thinking time and gave away the answer. Players may change their mind until the bar runs out.
        [Tooltip("B (off since 2026-11-04): end the question early once every player on the floor has walked into an answer and held it. Off = always wait the full answer time and grade where players stand at the end.")]
        [SerializeField] bool revealWhenAllLocked;
        [Tooltip("B: reveal early once every player on the floor has held an answer this long (only with revealWhenAllLocked).")]
        [SerializeField, Min(0f)] float allLockedHoldSeconds = 1.5f;
        float lastAnswerChange;
        bool firstAnswerRead;
        float lastAnswerRead = -999f;

        readonly NamedColor[] farmerTypes =
        {
            new("Red", new Color(1f, 0.302f, 0.369f, 1f)),
            new("Yellow", new Color(1f, 0.824f, 0.239f, 1f)),
            new("Blue", new Color(0.353f, 0.651f, 1f, 1f)),
            new("Green", new Color(0.31f, 0.878f, 0.541f, 1f)),
            new("Purple", new Color(0.706f, 0.361f, 1f, 1f)),
        };

        readonly NamedColor[] animalTypes =
        {
            new("Pig", new Color(1f, 0.69f, 0.784f, 1f)),
            new("Dog", new Color(0.847f, 0.627f, 0.353f, 1f)),
            new("Chicken", new Color(1f, 0.878f, 0.4f, 1f)),
            new("Cow", new Color(0.902f, 0.902f, 0.902f, 1f)),
            new("Cat", new Color(0.788f, 0.627f, 1f, 1f)),
        };

        readonly List<PlayerState> players = new();
        readonly List<ParadeItem> parade = new();
        readonly List<RawImage> choiceImages = new();
        readonly List<TMP_Text> choiceTexts = new();
        readonly List<RawImage> sectionMarkers = new();
        readonly List<RectTransform> scorePanels = new();

        MemoryFarmPrototypeHud prototypeHud;
        MemoryFarmModelStage modelStage;
        CountdownNumber countdownNumber;
        GameState state;
        QuestionState question;
        int currentSet;
        int currentSection;
        int walkingIndex;
        float stateTimer;
        float walkTimer;
        bool graded;
        bool gameStarted;
        string stagePauseCountdownTemplate;
        string winnerTemplate;

        RectTransform InputArea => characterPanel != null ? characterPanel : playArea;
        Rect FloorRect => InputArea != null ? InputArea.rect : FloorDisplaySettings.LayoutRect;

        MemoryFarmVfx farmVfx;

        const string ReplayGame = "MemoryFarm";
        static readonly string[] ReplayZoneStates = { "zone0", "zone1", "zone2", "zone3", "zone4", "zone5" };
        readonly ReplayLeadTracker replayLead = new ReplayLeadTracker();
        readonly List<ReplayPlayer> replayFrame = new List<ReplayPlayer>(6);
        int[] replayLastZone = new int[0];
        float[] replaySwitchTimeLeft = new float[0];

        void OnDisable() { if (ReplayLog.CurrentGame == ReplayGame) ReplayLog.EndSession("unloaded"); sceneJuice?.Clear(); if (farmVfx != null) farmVfx.Clear(); }

        void Start()
        {
            AudioListener listenerToKeep = null;
            foreach (var root in gameObject.scene.GetRootGameObjects())
                foreach (var listener in root.GetComponentsInChildren<AudioListener>(true))
                    if (listener.isActiveAndEnabled)
                    {
                        if (listenerToKeep == null) listenerToKeep = listener;
                        else listener.enabled = false;
                    }
            ResolveSceneReferences();
            farmVfx = GetComponent<MemoryFarmVfx>() ?? gameObject.AddComponent<MemoryFarmVfx>();
            farmVfx.Configure(InputArea, cutsceneArea);
            sceneJuice=gameObject.AddComponent<ArcadeJuice>();
            sceneJuice.Initialize(InputArea,cutsceneArea,questionText!=null?questionText.font:null);
            BindInput();
            if (!TryGetComponent(out FloorStandButtons standButtons)) standButtons = gameObject.AddComponent<FloorStandButtons>();
            standButtons.Configure(playArea, floorPlayers);
            // บาส 2026-10-02: result screen holds 10 s, then SKIP -> Restart / Exit (lower) - see ResultChoiceFlow.
            ResultChoiceFlow.Install(gameplayResultPanel, 220f);
            ShowMenu();
        }

        void Update()
        {
            if (Time.timeScale <= 0f) return;
            if (InputArea == null) return;
            TintPlayerMarkers();
            if (!gameStarted)
            {
                UpdateAutoStart(Time.unscaledDeltaTime);
                return;
            }

            UpdateLateJoiners();
            float dt = Time.deltaTime;
            UpdatePlayers(dt);
            // Signal lost: freeze the round (farmers, timers) until the players are back - MemoryFarmGameManager.Ready.
            if (state != GameState.Ready && state != GameState.Result && UpdateDropouts(dt)) { if (gameStarted) UpdateHud(); return; } // not after everyone dropped -> menu

            if (PadsActive) return;   // the player pads (PlayerPadSelect) run themselves; the READY state waits under them
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            UpdateDebugKeys();
#endif
            switch (state)
            {
                case GameState.Countdown:
                    UpdateCountdown(dt);
                    break;
                case GameState.Watch:
                    UpdateWatch(dt);
                    break;
                case GameState.Question:
                    UpdateQuestion(dt);
                    break;
                case GameState.Reveal:
                    UpdateReveal(dt);
                    break;
                case GameState.Break:
                    UpdateBreak(dt);
                    break;
                case GameState.Rule:
                    UpdateRuleCard(dt);
                    break;
                case GameState.Minigame:
                    UpdateMinigame(dt);
                    break;
            }

            UpdateHud();
            if (ReplayLog.ShouldSampleFrame()) WriteReplayFrame();
        }

        void ResolveSceneReferences()
        {
            ResolveUiReferences();
            if (centerDisplay != null && centerDisplay.GetComponent<RectMask2D>() == null)
                centerDisplay.gameObject.AddComponent<RectMask2D>();
            if (farmerPanel != null) farmerPanel.gameObject.SetActive(false);

            ResolvePrefabReferences();
            ConfigureFarmModelDisplay();
            ResolveChoices();
            ResolveSectionMarkers();
            ResolveScorePanels();
            ResolvePlayers();
            WireResultButtons(resultPanel);
            WireResultButtons(gameplayResultPanel);
            ConfigureFarmUi();
        }

        // Wall/floor UI references only: shared by Play and the Editor UI bake (which must not touch models or cursors).
        void ResolveUiReferences()
        {
            playArea = playArea != null ? playArea : FindRect("Canvas == Gameplay");
            cutsceneArea = cutsceneArea != null ? cutsceneArea : FindRect("Canvas == Cutscene");
            cutsceneMenuPanel = cutsceneMenuPanel != null ? cutsceneMenuPanel : FindDirectRectIn(cutsceneArea, "Panel == Menu") ?? FindRectIn(cutsceneArea, "Panel == Menu");
            gameplayMenuPanel = gameplayMenuPanel != null ? gameplayMenuPanel : FindDirectRectIn(playArea, "Panel == Menu") ?? FindRectIn(playArea, "Panel == Menu");
            characterPanel = characterPanel != null ? characterPanel : FindRectIn(playArea, "Panel == Character") ?? FindRect("Panel == Character");
            gameplayUiPanel = gameplayUiPanel != null ? gameplayUiPanel : FindRectIn(playArea, "Panel == UI") ?? FindRect("Panel == UI");
            centerDisplay = centerDisplay != null ? centerDisplay : FindRectIn(cutsceneArea, "RawImage == CenterDisplay") ?? FindRect("RawImage == CenterDisplay");
            farmerPanel = farmerPanel != null ? farmerPanel : FindRectIn(cutsceneArea, "Panel == Farmer") ?? FindRect("Panel == Farmer");
            setAndSectionPanel = setAndSectionPanel != null ? setAndSectionPanel : FindRectIn(cutsceneArea, "Panel == Set and Section") ?? FindRect("Panel == Set and Section");
            sectionCountPanel = sectionCountPanel != null ? sectionCountPanel : FindRectIn(cutsceneArea, "Panel == Section Count") ?? FindRect("Panel == Section Count");
            questionPanel = questionPanel != null ? questionPanel : FindRectIn(cutsceneArea, "Panel == Questions") ?? FindRect("Panel == Questions");
            answerChoicePanel = answerChoicePanel != null ? answerChoicePanel : FindRectIn(playArea, "Panel == Answer Choice") ?? FindRect("Panel == Answer Choice");
            gameplayScorePanel = gameplayScorePanel != null ? gameplayScorePanel : FindRectIn(gameplayUiPanel, "Panel == Player Score") ?? FindRect("Panel == Player Score");
            stagePausePanel = stagePausePanel != null ? stagePausePanel : FindRectIn(cutsceneArea, "Panel == Stage Pause") ?? FindRect("Panel == Stage Pause");
            resultPanel = resultPanel != null ? resultPanel : FindDirectRectIn(cutsceneArea, "Panel == Result") ?? FindRectIn(cutsceneArea, "Panel == Result") ?? FindRect("Panel == Result");
            gameplayResultPanel = gameplayResultPanel != null ? gameplayResultPanel : FindDirectRectIn(playArea, "Panel == Result") ?? FindRectIn(playArea, "Panel == Result");
            resultScorePanel = resultScorePanel != null ? resultScorePanel : FindRectIn(resultPanel, "Panel == Player Score");
            gameplayResultScorePanel = gameplayResultScorePanel != null ? gameplayResultScorePanel : FindRectIn(gameplayResultPanel, "Panel == Player Score");
            setText = setText != null ? setText : FindTMP("Text == Set") ?? FindTMPIn(cutsceneArea, "Text == Set");
            questionText = questionText != null ? questionText : FindTMP("Text == Question") ?? FindTMPIn(questionPanel, "Text == Question");
            stagePauseCountdownText = stagePauseCountdownText != null ? stagePauseCountdownText : FindTMPIn(stagePausePanel, "Stage Countdown");
            if (stagePauseCountdownText != null && string.IsNullOrWhiteSpace(stagePauseCountdownTemplate))
                stagePauseCountdownTemplate = stagePauseCountdownText.text;
            winnerText = winnerText != null ? winnerText : FindTMP("Text (TMP) == Winner") ?? FindTMPIn(resultPanel, "Winner");
            if (winnerText != null && string.IsNullOrWhiteSpace(winnerTemplate))
                winnerTemplate = winnerText.text;
            answerTimerScrollbar = answerTimerScrollbar != null ? answerTimerScrollbar : FindScrollbar("Answer Count");
            answerTimerFill = answerTimerFill != null ? answerTimerFill : FindImageIn(answerTimerScrollbar != null ? answerTimerScrollbar.transform : null, "Fill") ?? FindImageIn(answerTimerScrollbar != null ? answerTimerScrollbar.transform : null, "Handle");
            if (answerTimerScrollbar != null) answerTimerScrollbar.direction = Scrollbar.Direction.LeftToRight;
        }

        void ResolvePrefabReferences()
        {
            if (farmerPrefab == null)
            {
                foreach (var prefab in Resources.FindObjectsOfTypeAll<RawImage>())
                {
                    if (prefab == null || prefab.gameObject.scene.IsValid()) continue;
                    if (prefab.name.Contains("Farmer"))
                    {
                        farmerPrefab = prefab;
                        break;
                    }
                }
            }

            if (animalPrefab == null)
            {
                foreach (var prefab in Resources.FindObjectsOfTypeAll<RawImage>())
                {
                    if (prefab == null || prefab.gameObject.scene.IsValid()) continue;
                    if (prefab.name.Contains("Animal"))
                    {
                        animalPrefab = prefab;
                        break;
                    }
                }
            }
        }

        // Both of this scene's own cameras default to culling mask "Everything." Wall-only things are
        // tagged WallLayer, so the floor camera (Camera.main) needs an explicit exclusion.
        void ConfigureFarmModelDisplay()
        {
            if (Camera.main != null) Camera.main.cullingMask &= ~(1 << Visual3DFollower.WallLayer);
        }

        void ResolvePlayers()
        {
            players.Clear();
            playerCount = Mathf.Clamp(playerCount, 1, 6);
            for (int i = 0; i < 6; i++)
            {
                var cursor = FindDirectRectIn(characterPanel, $"Player {i + 1}") ?? CreatePlayerAnchor(i);
                cursor.gameObject.SetActive(i < playerCount);
                if (i >= playerCount) continue;
                players.Add(new PlayerState(i, cursor));
            }
        }

        // An invisible point that follows the player: effects and score pops are placed from it. What players see is
        // their own marker on the UWB player prefab (TintPlayerMarkers).
        RectTransform CreatePlayerAnchor(int index)
        {
            var anchor = new GameObject($"Player {index + 1}", typeof(RectTransform)).GetComponent<RectTransform>();
            anchor.SetParent(characterPanel, false);
            anchor.anchorMin = anchor.anchorMax = anchor.pivot = new Vector2(.5f, .5f);
            anchor.sizeDelta = Vector2.zero;
            return anchor;
        }

        void TintPlayerMarkers()
        {
            if (floorPlayers == null) return;
            for (int slot = 0; slot < FloorPlayerInput.MaxSlots; slot++) floorPlayers.TintMarker(slot, PlayerColor(slot));
        }

        void ResolveChoices()
        {
            choiceImages.Clear();
            choiceTexts.Clear();
            for (int i = 1; i <= 6; i++)
            {
                var choice = FindRectIn(answerChoicePanel, $"RawImage == Choice {i}") ?? FindRect($"RawImage == Choice {i}");
                if (choice == null || !choice.TryGetComponent(out RawImage image)) continue;
                choiceImages.Add(image);
                choiceTexts.Add(
                    FindTMPIn(choice, "Text") ??
                    FindTMPIn(choice, $"Choice {i}") ??
                    FindTMPIn(answerChoicePanel, $"Choice {i}") ??
                    FindTMP($"Choice {i}"));
            }
        }

        void ResolveSectionMarkers()
        {
            sectionMarkers.Clear();
            if (sectionCountPanel == null) return;
            for (int i = 1; i <= 6; i++)
            {
                var marker = FindDirectRectIn(sectionCountPanel, i.ToString()) ?? FindRectIn(sectionCountPanel, i.ToString());
                if (marker != null && marker.TryGetComponent(out RawImage image)) sectionMarkers.Add(image);
            }
        }

        void ResolveScorePanels()
        {
            scorePanels.Clear();
            AddScorePanel(gameplayScorePanel);
            AddScorePanel(resultScorePanel);
            AddScorePanel(gameplayResultScorePanel);
            foreach (var rect in Resources.FindObjectsOfTypeAll<RectTransform>())
            {
                if (rect != null && rect.gameObject.scene.IsValid() && rect.name == "Panel == Player Score") AddScorePanel(rect);
            }
        }

        FloorPlayerInput floorPlayers;

        void BindInput()
        {
            if (playerSpawner == null) playerSpawner = FindFirstObjectByType<UWBPlayerSpawner>(FindObjectsInactive.Include);
            floorPlayers = new FloorPlayerInput(playerSpawner, FindFirstObjectByType<UWBManager>(FindObjectsInactive.Include));
        }

        void ResetGame()
        {
            ClearParadeViews();
            gameStarted = true;
            SetMenuVisible(false);
            foreach (var player in players) player.Reset(PlayerStartLocal(player.Index));
            currentSet = 1;
            currentSection = 1;
            question = null;
            ResetOptionC();
            if (stagePausePanel != null) stagePausePanel.gameObject.SetActive(false);
            if (resultPanel != null) resultPanel.gameObject.SetActive(false);
            if (gameplayResultPanel != null) gameplayResultPanel.gameObject.SetActive(false);
            pads?.Cancel();
            StartSession();
            UpdateScorePanels(false);
            // The replay session opens once the player pads are done (BeginPlaySession), not here.
        }

        void BeginPlaySession()
        {
            replayLead.Reset();
            replayLastZone = new int[players.Count];
            replaySwitchTimeLeft = new float[players.Count];
            ReplayLog.BeginSession(ReplayGame, players.Count, ReplayLog.Data().Add("sets", (int)setCount).Add("questionsPerSet", sectionsPerSet)
                .Add("farmersPerSet", farmersPerSet ?? new int[0]).Add("answerSeconds", answerSeconds));
        }

        void ShowMenu()
        {
            pads?.Cancel();
            if (countdownNumber != null) countdownNumber.Hide();
            floorPlayers?.ResetOrder(); // back at the menu: the next group gets Player 1.. in the order they step on
            menuShownAt = Time.unscaledTime;
            detectedFor = 0f;
            sceneJuice?.Clear();
            if (farmVfx != null) farmVfx.Clear();
            if (gameStarted) ReplayLog.EndSession("aborted");
            gameStarted = false;
            signalPaused = false; HideReadyPanels();
            prototypeHud?.ShowState("Menu", "", players.Count);
            foreach (var player in players) player.Cursor.gameObject.SetActive(false);
            ClearParadeViews();
            HideQuestion();
            HideOptionCUi();
            SetStagePauseVisible(false);
            SetSetAndSectionVisible(false);
            if (resultPanel != null) resultPanel.gameObject.SetActive(false);
            if (gameplayResultPanel != null) gameplayResultPanel.gameObject.SetActive(false);
            SetMenuVisible(true);
        }

        void SetMenuVisible(bool visible)
        {
            SetPanelVisible(cutsceneMenuPanel, visible);
            SetPanelVisible(gameplayMenuPanel, visible);
        }

        static void SetPanelVisible(RectTransform panel, bool visible)
        {
            if (panel == null) return;
            panel.gameObject.SetActive(visible);
            if (visible) panel.SetAsLastSibling();
        }

        void WireResultButtons(RectTransform panel)
        {
            if (panel == null) return;

            foreach (var button in panel.GetComponentsInChildren<Button>(true))
            {
                if (button == null) continue;
                if (button.name == "Restart") { button.onClick = new Button.ButtonClickedEvent(); button.onClick.AddListener(RestartGame); }
                else if (button.name == "Exit") { button.onClick = new Button.ButtonClickedEvent(); button.onClick.AddListener(ExitGame); }
            }
        }

        bool AnyoneToPlay()
        {
            if (CountPlayersAtStart() > 0) return true;
            Debug.Log("[MemoryFarm] Start ignored: no player is on the floor", this);
            return false;
        }

        public void RestartToMenu()
        {
            ShowMenu();
        }

        // บาส 2026-10-06 (balance note A5): Restart on the result screen starts the next game straight away (was: back to
        // the menu, then press Start again). It goes through READY, whose 3-2-1 runs on its own once the players are on
        // the floor, so the next group's count is still re-read. Arrival order is kept (no ResetArrivalOrder).
        public void RestartGame()
        {
            if (!AnyoneToPlay()) return;
            sceneJuice?.Clear();
            if (farmVfx != null) farmVfx.Clear();
            if (gameStarted && ReplayLog.CurrentGame == ReplayGame) ReplayLog.EndSession("aborted"); // no-op after a completed session
            ResetGame();
        }

        // บาส 2026-10-02: Result's Exit button was themed but never wired. Same behaviour as Bounty Hunter's
        // ExitGame (stop Play Mode in the Editor, quit in a build); OnDisable still closes the replay session.
        public void ExitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void StartCountdown()
        {
            if (farmVfx != null) farmVfx.Clear();
            HideReadyPanels();
            state = GameState.Countdown;
            stateTimer = countdownSeconds;
            HideQuestion();
            SetStagePauseVisible(false);
            // Only the dropping number shows during the count; the banner comes back with the first walk.
            SetSetAndSectionVisible(false);
            ClearParadeViews();
            SetTimerBar(1f);
        }

        void StartWatch()
        {
            if (RuleCardDue()) { StartRuleCard(); return; }   // แบบ C: sets 4 and 5 open with their rule card
            sceneJuice?.Clear();
            if (farmVfx != null) farmVfx.Clear();
            state = GameState.Watch;
            walkTimer = 0f;
            walkingIndex = 0;
            graded = false;
            GenerateParade();
            PrepareParade();
            HideQuestion();
            SetStagePauseVisible(false);
            SetSetAndSectionVisible(true);
            SetupSectionMarkers();
            ShowCurrentFarmer();
            if (currentSection == 1) ReplayLog.Event("round_start", 0f, 0f, null, ReplayLog.Data().Add("set", currentSet).Add("scores", ReplayScores()));
        }

        void StartQuestion()
        {
            if (farmVfx != null) farmVfx.Clear();
            state = GameState.Question;
            stateTimer = answerSeconds;
            graded = false;
            ClearParadeViews();
            MakeQuestion();
            foreach (var player in players) player.AnswerIndex = -1;
            SetStagePauseVisible(false);
            SetSetAndSectionVisible(false);
            if (questionPanel != null) questionPanel.gameObject.SetActive(true);
            if (answerChoicePanel != null) answerChoicePanel.gameObject.SetActive(true);
            ApplyQuestionToUi();
            foreach (var player in players) { player.ScoredThisQuestion = false; player.HeldZone = -1; player.StableZone = -1; player.Absent = false; player.AnswerSince = float.MaxValue; player.LastRealZone = -1; }
            lastAnswerChange = 0f; firstAnswerRead = true;
            SetTimerBar(1f);
            for (int i = 0; i < replayLastZone.Length; i++) { replayLastZone[i] = -1; replaySwitchTimeLeft[i] = float.MaxValue; }
            if (question != null) ReplayLog.Event("question_start", 0f, 0f, null, ReplayLog.Data().Add("set", currentSet).Add("question", currentSection)
                .Add("questionType", question.Type).Add("answerIndex", question.AnswerIndex).Add("choices", question.Choices.Length).Add("ofParade", questionInParade + 1).Add("multiplier", ScoreMultiplier));
        }

        void UpdateCountdown(float dt)
        {
            if (countdownNumber != null) countdownNumber.Show(stateTimer);
            stateTimer -= dt;
            if (stateTimer > 0f) return;

            if (countdownNumber != null) countdownNumber.Hide();
            StartWatch();
        }

        void UpdateWatch(float dt)
        {
            if (parade.Count == 0)
            {
                StartQuestion();
                return;
            }

            walkTimer += dt;
            MoveCurrentFarmer(Mathf.Clamp01(walkTimer / Mathf.Max(0.05f, currentWalkSeconds)));
            if (walkTimer < currentWalkSeconds) return;

            MarkSectionProgress(walkingIndex);
            walkingIndex++;
            walkTimer = 0f;
            ClearParadeViews();
            if (walkingIndex >= parade.Count) { if (!BeginRewatch()) StartQuestion(); }   // final set: maybe a re-walk minus one
            else ShowCurrentFarmer();
        }

        void UpdateQuestion(float dt)
        {
            if (Time.timeScale <= 0f) return;
            stateTimer -= dt;
            SetTimerBar(Mathf.Clamp01(stateTimer / Mathf.Max(0.05f, answerSeconds)));
            UpdatePlayerAnswers();
            TrackReplayZoneChanges();
            if (stateTimer > 0f && EveryoneLockedIn()) stateTimer = 0f;   // B
            if (stateTimer > 0f) return;

            GradeAnswers();
            state = GameState.Reveal;
            stateTimer = revealSeconds;
        }

        void UpdateReveal(float dt)
        {
            stateTimer -= dt;
            if (stateTimer > 0f) return;
            if (SecondQuestionDue()) { StartQuestion(); return; }   // DOUBLE QUESTION: same parade, second question
            AdvancePastQuestion();
        }

        // The current question is over: the next question, what follows the set, or the result.
        void AdvancePastQuestion()
        {
            if (currentSection >= sectionsPerSet) ReplayLog.Event("round_end", 0f, 0f, null, ReplayLog.Data().Add("set", currentSet).Add("scores", ReplayScores()));
            if (currentSection < sectionsPerSet)
            {
                currentSection++;
                StartWatch();
            }
            else if (currentSet < (int)setCount)
            {
                currentSet++;
                currentSection = 1;
                if (!StartMinigameAfterSet(currentSet - 1)) StartStagePause();   // แบบ C: a floor minigame after sets 2 and 4
            }
            else
            {
                ShowResult();
            }
        }

        void UpdateBreak(float dt)
        {
            stateTimer -= dt;
            UpdateStagePauseText();
            if (stateTimer <= 0f) StartWatch();
        }

        void StartStagePause()
        {
            state = GameState.Break;
            stateTimer = setBreakSeconds;
            ClearParadeViews();
            HideQuestion();
            SetSetAndSectionVisible(false);
            SetStagePauseVisible(true);
            UpdateStagePauseText();
        }

        void GenerateParade()
        {
            parade.Clear();
            int len = FarmersThisSet();
            for (int i = 0; i < len; i++)
            {
                parade.Add(new ParadeItem
                {
                    Farmer = UnityEngine.Random.Range(0, farmerTypes.Length),
                    Animal = UnityEngine.Random.Range(0, animalTypes.Length),
                    Female = UnityEngine.Random.value < .5f,
                });
            }
            ShuffleWalkPaces(len);
        }

        // บาส 2026-10-02: independent random paces could still come out slow-to-fast and read as the game speeding
        // up. Each question now spreads its paces over the whole 1-5 s range (one per equal slice) and lays them out as
        // a zig-zag: the slower half and the faster half alternate (random order inside each half, random which half
        // starts), so the order can never climb or fall twice in a row.
        readonly List<float> walkPaces = new();
        void ShuffleWalkPaces(int count)
        {
            walkPaces.Clear();
            float min = Mathf.Min(farmerWalkMinSeconds, farmerWalkMaxSeconds), max = Mathf.Max(farmerWalkMinSeconds, farmerWalkMaxSeconds);
            var fast = new List<float>(); var slow = new List<float>();
            for (int i = 0; i < count; i++)
                (i < count / 2 ? fast : slow).Add(Mathf.Lerp(min, max, (i + UnityEngine.Random.Range(.15f, .85f)) / Mathf.Max(1, count)));
            Shuffle(fast); Shuffle(slow);
            // An odd count leaves the bigger half one longer, so that half has to start.
            bool slowFirst = slow.Count > fast.Count || (slow.Count == fast.Count && UnityEngine.Random.value < .5f);
            for (int i = 0; i < count; i++)
            {
                var half = (i % 2 == 0) == slowFirst ? slow : fast;
                walkPaces.Add(half[i / 2]);
            }
        }

        static void Shuffle(List<float> list)
        {
            for (int i = list.Count - 1; i > 0; i--) { int j = UnityEngine.Random.Range(0, i + 1); (list[i], list[j]) = (list[j], list[i]); }
        }

        bool PacesLookMixed()
        {
            for (int i = 2; i < walkPaces.Count; i++)
                if (Mathf.Sign(walkPaces[i] - walkPaces[i - 1]) == Mathf.Sign(walkPaces[i - 1] - walkPaces[i - 2])) return false;
            return true;
        }

        void MakeQuestion()
        {
            if (missingThisParade) { MakeMissingQuestion(); return; }
            int kind = NextQuestionKind();   // DOUBLE QUESTION never asks the same kind twice
            string text;
            string correct;
            string[] pool;
            string type;

            if (kind == 0)
            {
                int i = UnityEngine.Random.Range(0, parade.Count);
                text = $"What animal did the {Ordinal(i)} farmer carry?";
                correct = animalTypes[parade[i].Animal].Name;
                pool = Names(animalTypes);
                type = "animal_at_position";
            }
            else if (kind == 1)
            {
                int i = UnityEngine.Random.Range(0, parade.Count);
                int farmer = parade[i].Farmer;
                int firstIndex = parade.FindIndex(x => x.Farmer == farmer);
                text = $"What did the first {farmerTypes[farmer].Name} farmer carry?";
                correct = animalTypes[parade[firstIndex].Animal].Name;
                pool = Names(animalTypes);
                type = "first_color_farmer_animal";
            }
            else if (kind == 2)
            {
                int animal = UnityEngine.Random.Range(0, animalTypes.Length);
                int count = 0;
                foreach (var item in parade)
                    if (item.Animal == animal) count++;
                text = $"How many {animalTypes[animal].Name}s were in this parade?";
                correct = count.ToString();
                pool = CountChoices(count, parade.Count, Mathf.Max(2, Mathf.Min(4, choiceImages.Count)));
                type = "animal_count";
            }
            else
            {
                int i = UnityEngine.Random.Range(0, parade.Count);
                text = $"What shirt color did the {Ordinal(i)} farmer wear?";
                correct = farmerTypes[parade[i].Farmer].Name;
                pool = Names(farmerTypes);
                type = "shirt_color_at_position";
            }

            var choices = ShuffleTake(pool, correct, Mathf.Max(2, Mathf.Min(4, choiceImages.Count)));
            question = new QuestionState { Text = QuestionPrefix() + text, Choices = choices, AnswerIndex = Array.IndexOf(choices, correct), Type = type };
        }

        void ApplyQuestionToUi()
        {
            if (questionText != null) questionText.text = question != null ? question.Text : string.Empty;
            for (int i = 0; i < choiceImages.Count; i++)
            {
                bool active = question != null && i < question.Choices.Length;
                choiceImages[i].gameObject.SetActive(active);
                if (choiceTexts.Count > i && choiceTexts[i] != null) choiceTexts[i].text = active ? question.Choices[i] : string.Empty;
                PaintChoice(i, active ? ChoiceLook.Open : ChoiceLook.Hidden);
            }
        }

        void UpdatePlayerAnswers()
        {
            if (question == null)
            {
                foreach (var player in players) player.AnswerIndex = -1;
                return;
            }

            // บาส 2026-10-02: grading used to read tracking on that one frame only - a stale pose right as the
            // timer hit 0 turned 8 s of standing on the right sign into MISSED. Hold the last seen zone briefly.
            float elapsed = answerSeconds - stateTimer;
            // บาส 2026-11-02 review: a "walk" is judged on zones actually READ from tracking, never on AnswerIndex - a tag
            // blip set AnswerIndex to -1 (UpdatePlayers) and the held zone put it back, which counted as stepping in every
            // frame: someone standing still got the fast bonus / ended the question early. The first real read of a
            // question - and the first read after a pause (nothing read for a while: the moves made meanwhile were made with
            // the clock stopped) - is where people stand, not a step.
            bool rebase = firstAnswerRead || Time.unscaledTime - lastAnswerRead > .5f;
            if (state == GameState.Question) lastAnswerRead = Time.unscaledTime;
            foreach (var player in players)
            {
                int real = ChoiceIndexForPlayer(player), zone = real;
                if (zone >= 0) { player.HeldZone = zone; player.HeldZoneTime = Time.time; player.Absent = false; }
                else if (player.HeldZone >= 0 && Time.time - player.HeldZoneTime <= answerDropoutGraceSeconds) zone = player.HeldZone;
                if (real >= 0 && state == GameState.Question)
                {
                    if (!rebase && player.LastRealZone >= 0 && real != player.LastRealZone)
                    {
                        player.AnswerSince = elapsed;   // A: stepped into this answer now
                        lastAnswerChange = elapsed;     // B: someone moved
                    }
                    player.LastRealZone = real;
                }
                player.AnswerIndex = zone;
            }
            if (state == GameState.Question) firstAnswerRead = false;
        }

        // B: every player on the floor (not dropped) has WALKED INTO an answer since the question appeared, nobody moved for
        // allLockedHoldSeconds, and the fast-answer window is over. Walked into, because the whole floor is answer columns:
        // just standing where the parade left you is not a choice - the first version revealed at 3 s while people were
        // still thinking (MemoryFarmAnswerFlowCheck case 3). Someone who never moves keeps the full answer time.
        bool EveryoneLockedIn()
        {
            if (!revealWhenAllLocked || allLockedHoldSeconds <= 0f || question == null) return false;
            float elapsed = answerSeconds - stateTimer;
            if (elapsed < fastAnswerSeconds || elapsed - lastAnswerChange < allLockedHoldSeconds) return false;
            int onFloor = 0;
            for (int i = 0; i < players.Count; i++)
            {
                if (i < droppedOut.Length && droppedOut[i]) continue;
                if (!PlayerPresent(i)) continue;
                onFloor++;
                if (players[i].AnswerIndex < 0 || players[i].AnswerSince == float.MaxValue) return false;
            }
            return onFloor > 0;
        }

        void GradeAnswers()
        {
            if (Time.timeScale <= 0f) return;
            if (graded || question == null) return;
            graded = true;
            UpdatePlayerAnswers();
            // บาส 2026-10-06 (balance note A1): never seen during the whole question = a device problem, not the player's
            // fault -> sits this one out: no points, streak NOT reset (HeldZone stays -1 only if no zone was ever seen).
            foreach (var player in players) player.Absent = player.HeldZone < 0;
            int replayCorrect = 0, replaySolo = -1;
            for (int pi = 0; pi < players.Count; pi++)
            {
                var player = players[pi];
                if (player.ScoredThisQuestion)
                {
                    continue;
                }
                if (player.Absent) continue;
                // บาส 2026-11-02 bug hunt: a dropped player is out of the quiz too (minigames already skip them), until
                // UpdateDropouts re-admits them - no points, streak kept, same as absent.
                if (pi < droppedOut.Length && droppedOut[pi]) continue;
                if (player.AnswerIndex == question.AnswerIndex)
                {
                    AwardCorrect(player);
                    player.ScoredThisQuestion = true;
                    farmVfx?.Answer(player.Cursor, true, player.Index, players.Count, player.Streak);
                    replayCorrect++; replaySolo = player.Index;
                    if (player.Streak == 3 || player.Streak == 5) ReplayLog.Event("streak", 3f, 3f, player.Index, ReplayLog.Data().Add("streak", player.Streak).Add("set", currentSet).Add("question", currentSection));
                    if (pi < replaySwitchTimeLeft.Length && replaySwitchTimeLeft[pi] <= 1f) ReplayLog.Event("late_switch_correct", 3f, 2f, player.Index, ReplayLog.Data().Add("switchSecondsBeforeEnd", Mathf.Max(0f, replaySwitchTimeLeft[pi])).Add("set", currentSet).Add("question", currentSection));
                }
                else
                {
                    if (player.Streak >= 3) ReplayLog.Event("streak_broken", 2f, 2f, player.Index, ReplayLog.Data().Add("lostStreak", player.Streak).Add("answered", player.AnswerIndex >= 0).Add("set", currentSet).Add("question", currentSection));
                    player.Streak = 0;
                    if (player.AnswerIndex >= 0)
                    {
                        farmVfx?.Answer(player.Cursor, false, player.Index, players.Count, 0);
                        if(player.Cursor!=null)sceneJuice?.Impact(sceneJuice.Local(player.Cursor),new Color(1f,.18f,.25f));
                    }
                }
            }
            UpdateScorePanels(false);
            LogReplayGrading(replayCorrect, replaySolo);
        }

        void AwardCorrect(PlayerState player)
        {
            player.Streak++;
            if (player.Cursor != null && (player.Streak == 3 || player.Streak == 5))
                sceneJuice?.Burst(sceneJuice.Local(player.Cursor), new Color(1f,.8f,.2f), player.Streak == 5 ? 300 : 210, SpaceJuice.Transition);
            if(player.Cursor!=null)sceneJuice?.Impact(sceneJuice.Local(player.Cursor),new Color(.3f,1f,.5f));
            bool fast = fastAnswerBonus > 0 && player.AnswerSince <= fastAnswerSeconds;   // A (บาส 2026-11-02)
            int gained=(scorePerCorrect + Mathf.Min(maxStreakBonus, Mathf.Max(0, player.Streak - 1) * 2) + (fast ? fastAnswerBonus : 0))*ScoreMultiplier; // final set x2
            player.Score += gained;
            if(player.Cursor!=null)sceneJuice?.Reward(sceneJuice.Local(player.Cursor),"+"+gained+(ScoreMultiplier>1?" ×"+ScoreMultiplier:"")+(player.Streak>1?"  STREAK "+player.Streak:"")+(fast?"  FAST":""),new Color(1f,.87f,.35f));
        }

        void ShowCurrentFarmer()
        {
            if(centerDisplay==null || walkingIndex<0 || walkingIndex>=parade.Count)return;
            var item=parade[walkingIndex];
            currentWalkSeconds=walkingIndex<walkPaces.Count?walkPaces[walkingIndex]:UnityEngine.Random.Range(Mathf.Min(farmerWalkMinSeconds,farmerWalkMaxSeconds),Mathf.Max(farmerWalkMinSeconds,farmerWalkMaxSeconds));
            if(modelStage==null)modelStage=centerDisplay.GetComponent<MemoryFarmModelStage>() ?? centerDisplay.gameObject.AddComponent<MemoryFarmModelStage>();
            modelStage.Show(animalTypes[item.Animal].Name,item.Female,farmerTypes[item.Farmer].Color,centerDisplay,farmerWalkPath);
        }

        void MoveCurrentFarmer(float t)
        {
            if(modelStage!=null)modelStage.SetProgress(t);
        }

        void ClearParadeViews()
        {
            if(modelStage!=null)modelStage.Clear();
        }

        void SetupSectionMarkers()
        {
            int count = parade.Count > 0 ? parade.Count : FarmersThisSet();   // the final set's re-walk is one short
            // Centred for this set's farmer count (4, 5 or 6), on even slots of a 6-farmer row, so the dots always sit
            // in the middle of their plank instead of starting from the left like the old layout group did.
            if (sectionCountPanel != null)
                foreach (var layout in sectionCountPanel.GetComponents<LayoutGroup>()) layout.enabled = false;
            float step = 1f / Mathf.Max(6, count), start = .5f - step * Mathf.Min(count, sectionMarkers.Count) * .5f;
            for (int i = 0; i < sectionMarkers.Count; i++)
            {
                bool active = i < count;
                sectionMarkers[i].gameObject.SetActive(active);
                if (!active) continue;
                sectionMarkers[i].color = UiThemeKit.Alpha(MemoryFarmTheme.Cream, .35f);
                var r = sectionMarkers[i].rectTransform;
                r.anchorMin = r.anchorMax = new Vector2(start + step * (i + .5f), .5f);
                r.pivot = new Vector2(.5f, .5f);
                r.anchoredPosition = Vector2.zero;
            }
        }

        void MarkSectionProgress(int index)
        {
            if (index < 0 || index >= sectionMarkers.Count) return;
            sectionMarkers[index].color = MemoryFarmTheme.Grass;
        }

        void HideQuestion()
        {
            if (questionPanel != null) questionPanel.gameObject.SetActive(false);
            if (answerChoicePanel != null) answerChoicePanel.gameObject.SetActive(false);
            SetTimerBar(1f);
        }

        void SetSetAndSectionVisible(bool visible)
        {
            if (setAndSectionPanel != null) setAndSectionPanel.gameObject.SetActive(visible);
        }

        void SetStagePauseVisible(bool visible)
        {
            if (stagePausePanel == null) return;
            stagePausePanel.gameObject.SetActive(visible);
            if (visible) stagePausePanel.SetAsLastSibling();
        }

        void UpdateStagePauseText()
        {
            if (stagePauseCountdownText == null) return;
            string template = string.IsNullOrWhiteSpace(stagePauseCountdownTemplate)
                ? "Next Stage In : N"
                : stagePauseCountdownTemplate;
            // Only the standalone "N" placeholder: a plain Replace("N") also hit the N of "Next" ("5ext Set In : 5").
            stagePauseCountdownText.text = System.Text.RegularExpressions.Regex.Replace(template, @"\bN\b", Mathf.CeilToInt(Mathf.Max(0f, stateTimer)).ToString());
        }

        void ShowResult()
        {
            sceneJuice?.Clear();
            HideOptionCUi();
            state = GameState.Result;
            signalPaused = false; HideReadyPanels();
            ClearParadeViews();
            HideQuestion();
            SetStagePauseVisible(false);
            SetSetAndSectionVisible(false);
            if (resultPanel != null)
            {
                resultPanel.gameObject.SetActive(true);
                resultPanel.SetAsLastSibling();
            }
            if (gameplayResultPanel != null)
            {
                gameplayResultPanel.gameObject.SetActive(true);
                gameplayResultPanel.SetAsLastSibling();
            }
            var winner = Winner();
            if (winnerText != null) winnerText.text = ResultWinnerLine(winner);
            UpdateScorePanels(true);
            LayoutResultRows(true);
            int replayTop = 0;
            foreach (var player in players) if (winner != null && player.Score == winner.Score) replayTop++;
            ReplayLog.EndSession("completed", winner != null && replayTop == 1 ? new[] { winner.Index } : new int[0], ReplayScores());
            farmVfx?.Celebrate();
            sceneJuice?.Celebrate(new Color(.55f,1f,.35f,.6f));
        }

        string ResultWinnerLine(PlayerState winner)
        {
            if (winner == null) return "Best Memory : Draw - Points : 00";

            var tied = new List<string>();
            foreach (var player in players)
                if (player.Score == winner.Score) tied.Add($"Player {player.Index + 1}");
            if (tied.Count > 1)
                return $"Best Memory : Draw ({string.Join(", ", tied)}) - Points : {winner.Score:00}";

            string template = string.IsNullOrWhiteSpace(winnerTemplate)
                ? "Best Memory : Player N - Points : 00"
                : winnerTemplate;
            string result = template.Replace("Player N", $"Player {winner.Index + 1}");
            result = result.Replace("Points : 00", $"Points : {winner.Score:00}");
            return result;
        }

        void UpdateHud()
        {
            if (prototypeHud != null)
            {
                prototypeHud.ShowState(state.ToString(), question != null ? question.Choices[question.AnswerIndex] : "", players.Count);
                for (int i = 0; i < players.Count; i++)
                {
                    var player = players[i];
                    bool away = player.Absent || (i < droppedOut.Length && droppedOut[i]);
                    string verdict = state == GameState.Reveal && question != null
                        ? (away ? "ABSENT" : player.AnswerIndex == question.AnswerIndex ? "CORRECT" : "INCORRECT") : "";
                    prototypeHud.SetPlayer(i, player.Score, PlayerColor(player.Index), verdict);
                }
            }
            if (setText != null)
            {
                string suffix = state switch
                {
                    GameState.Watch => rewatching ? $"Who is missing? Watch again! ({Mathf.Min(walkingIndex + 1, parade.Count)}/{parade.Count})" : $"Memorize the sequence!! ({Mathf.Min(walkingIndex + 1, FarmersThisSet())}/{FarmersThisSet()})",
                    GameState.Question => "Choose your answer",
                    // บาส 2026-11-02 bug hunt: "who is missing" choices are "Red\nPig" (2 lines on the answer cards);
                    // the ribbon is one line, so the label goes in with a space instead of the line break
                    GameState.Reveal => question != null ? $"Answer: {question.Choices[question.AnswerIndex].Replace('\n', ' ')}" : "Reveal",
                    GameState.Break => $"NEXT SET IN {Mathf.CeilToInt(stateTimer)}",
                    GameState.Result => "RESULT",
                    _ => string.Empty,
                };
                setText.text = $"SET {currentSet}/{(int)setCount} - {QuestionLabel()} - {suffix}";
            }

            if (state == GameState.Reveal && question != null)
            {
                UpdateScorePanels(false);
                for (int i = 0; i < choiceImages.Count; i++)
                {
                    if (!choiceImages[i].gameObject.activeSelf) continue;
                    PaintChoice(i, i == question.AnswerIndex ? ChoiceLook.Correct : ChoiceLook.Wrong);
                }
            }
        }

        int[] ReplayScores()
        {
            var scores = new int[players.Count];
            for (int i = 0; i < players.Count; i++) scores[i] = players[i].Score;
            return scores;
        }

        bool ReplayPlayerActive(PlayerState player) => player.Cursor != null && player.Cursor.gameObject.activeSelf;

        void TrackReplayZoneChanges()
        {
            if (!ReplayLog.Active) return;
            for (int i = 0; i < players.Count && i < replayLastZone.Length; i++)
            {
                int zone = players[i].AnswerIndex;
                if (zone < 0 || zone == replayLastZone[i]) continue;
                if (replayLastZone[i] >= 0) replaySwitchTimeLeft[i] = stateTimer;
                replayLastZone[i] = zone;
            }
        }

        void LogReplayGrading(int correct, int soloPlayer)
        {
            if (!ReplayLog.Active) return;
            int active = 0;
            foreach (var player in players) if (ReplayPlayerActive(player) && !player.Absent) active++; // absent players didn't answer
            // S only with 3+ players (บาส 2026-09-29): with 2, "only one right" is just an ordinary 1-vs-1 split.
            if (correct == 1 && active >= 2) ReplayLog.Event("solo_correct", 3f, 3f, soloPlayer, ReplayLog.Data().Add("set", currentSet).Add("question", currentSection).Add("active", active));
            // บาส 2026-10-06: A only with 2+ answering players - one player getting one question wrong isn't a highlight.
            if (correct == 0 && active >= 1) ReplayLog.Event("all_wrong", 2f, 3f, null, ReplayLog.Data().Add("set", currentSet).Add("question", currentSection).Add("active", active));
            LogReplayLeadChange(null);
        }

        // บาส 2026-11-02 bug hunt: minigame catches move Score too, so they share this path with grading -
        // the lead change is logged when it happens (minigame = its title then, extra field), not at the next question
        void LogReplayLeadChange(string minigame)
        {
            if (!ReplayLog.Active) return;
            var scores = ReplayScores();
            if (replayLead.Update(scores, out int leader, out int previous))
            {
                bool final = currentSet >= (int)setCount && currentSection >= sectionsPerSet;
                var data = ReplayLog.Data().Add("previous", players[previous].Index).Add("scores", scores)
                    .Add("set", currentSet).Add("question", currentSection).Add("final", final);   // schema 2: the fact the old S tier stood for
                if (minigame != null) data.Add("minigame", minigame);
                ReplayLog.Event("lead_change", 3f, 2f, players[leader].Index, data);
            }
        }

        void WriteReplayFrame()
        {
            var r = FloorRect;
            replayFrame.Clear();
            foreach (var player in players)
            {
                bool on = ReplayPlayerActive(player);
                Vector2 pos = player.Cursor != null ? player.Cursor.anchoredPosition : Vector2.zero;
                string st = null;
                if (question != null && state == GameState.Reveal) st = player.AnswerIndex == question.AnswerIndex ? "correct" : player.Absent ? "absent" : player.AnswerIndex >= 0 ? "wrong" : "noanswer";
                else if (state == GameState.Question && player.AnswerIndex >= 0 && player.AnswerIndex < ReplayZoneStates.Length) st = ReplayZoneStates[player.AnswerIndex];
                replayFrame.Add(new ReplayPlayer(player.Index, on, Mathf.InverseLerp(r.xMin, r.xMax, pos.x), Mathf.InverseLerp(r.yMin, r.yMax, pos.y), player.Score, st));
            }
            var data = ReplayLog.Data().Add("set", currentSet).Add("question", currentSection);
            if (state == GameState.Question || state == GameState.Countdown || state == GameState.Break || state == GameState.Reveal) data.Add("timeLeft", Mathf.Max(0f, stateTimer));
            if (state == GameState.Watch) data.Add("farmer", walkingIndex).Add("farmers", parade.Count);
            ReplayLog.WriteFrame(state.ToString(), replayFrame, data);
        }

        void SetTimerBar(float ratio)
        {
            ratio = Mathf.Clamp01(ratio);
            RectTransform barRect = null;
            if (answerTimerScrollbar != null)
            {
                answerTimerScrollbar.direction = Scrollbar.Direction.LeftToRight;
                answerTimerScrollbar.size = ratio;
                answerTimerScrollbar.value = 0f;
                barRect = answerTimerScrollbar.handleRect;
            }
            Color color = ratio > 0.35f ? MemoryFarmTheme.Grass : MemoryFarmTheme.BarnRed;
            if (answerTimerFill != null)
            {
                answerTimerFill.color = color;
                barRect = answerTimerFill.rectTransform;
            }
            else if (answerTimerScrollbar != null && answerTimerScrollbar.targetGraphic != null) answerTimerScrollbar.targetGraphic.color = color;
            if (barRect == null) return;

            barRect.anchorMin = new Vector2(0f, 0f);
            barRect.anchorMax = new Vector2(ratio, 1f);
            barRect.offsetMin = Vector2.zero;
            barRect.offsetMax = Vector2.zero;
            barRect.pivot = new Vector2(0f, 0.5f);
        }

        void UpdatePlayers(float dt)
        {
            if (Time.timeScale <= 0f) return;
            foreach (var player in players)
            {
                if (!TryGetInputLocalPoint(player.Index, out var target))
                {
                    player.Cursor.gameObject.SetActive(false);
                    player.Velocity = Vector2.zero;
                    if (state == GameState.Question) player.AnswerIndex = -1;
                    continue;
                }
                player.Cursor.gameObject.SetActive(true);
                target.x = Mathf.Clamp(target.x, FloorRect.xMin + 40f, FloorRect.xMax - 40f);
                target.y = Mathf.Clamp(target.y, FloorRect.yMin + 40f, FloorRect.yMax - 40f);
                player.Cursor.anchoredPosition = target;
                player.Velocity = Vector2.zero;
            }
        }

        bool TryGetInputLocalPoint(int zoneIndex, out Vector2 localPoint)
        {
            if (ShouldUseDirectEditorMouse(zoneIndex))
            {
                localPoint = default;
                var mouse = UnityEngine.InputSystem.Mouse.current;
                if (mouse == null) return false;
                var editorCanvas = InputArea.GetComponentInParent<Canvas>();
                var editorCamera = editorCanvas != null && editorCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? editorCanvas.worldCamera : null;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(InputArea, mouse.position.ReadValue(), editorCamera, out localPoint);
                floorPlayers.MoveInSimulation(zoneIndex, InputArea.TransformPoint(localPoint));
                return true;
            }
            if (UsesEditorMouse)
            {
                localPoint = default;
                return false;
            }

            localPoint = default;
            return floorPlayers != null && floorPlayers.TryGetLocal(zoneIndex, InputArea, out localPoint);
        }

        bool UsesEditorMouse => Application.isEditor && useMouseInEditor && floorPlayers != null && floorPlayers.IsSimulation;

        bool ShouldUseDirectEditorMouse(int zoneIndex) => UsesEditorMouse && zoneIndex == 0;

        int ChoiceIndexForPlayer(PlayerState player)
        {
            if (player == null || player.Cursor == null || InputArea == null) return -1;
            if (!TryGetInputLocalPoint(player.Index, out _)) return -1;
            Vector2 local = player.Cursor.anchoredPosition;
            int activeChoices = ActiveChoiceCount();
            if (activeChoices > 0)
            {
                float t = Mathf.InverseLerp(FloorRect.xMin, FloorRect.xMax, local.x);
                int zone = Mathf.Clamp(Mathf.FloorToInt(t * activeChoices), 0, activeChoices - 1);
                return ApplyDividerBand(player, t, zone, activeChoices);
            }

            for (int i = 0; i < choiceImages.Count; i++)
            {
                if (choiceImages[i] == null || !choiceImages[i].gameObject.activeInHierarchy) continue;
                Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(InputArea, choiceImages[i].rectTransform);
                bool insideRect =
                    local.x >= bounds.min.x && local.x <= bounds.max.x &&
                    local.y >= bounds.min.y && local.y <= bounds.max.y;
                if (insideRect) return i;

                bool insideColumn = local.x >= bounds.min.x && local.x <= bounds.max.x;
                if (insideColumn) return i;
            }
            return -1;
        }

        int ActiveChoiceCount()
        {
            int count = 0;
            for (int i = 0; i < choiceImages.Count; i++)
            {
                if (choiceImages[i] != null && choiceImages[i].gameObject.activeInHierarchy) count++;
            }
            return count;
        }

        int GetZoneIndexForScreenPoint(Vector2 point)
        {
            if (players.Count == 0 || InputArea == null) return 0;
            var canvas = InputArea.GetComponentInParent<Canvas>();
            var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(InputArea, point, camera, out var local);
            int nearestIndex = 0;
            float nearestDistance = float.MaxValue;
            for (int i = 0; i < players.Count; i++)
            {
                float distance = (players[i].Cursor.anchoredPosition - local).sqrMagnitude;
                if (distance >= nearestDistance) continue;
                nearestDistance = distance;
                nearestIndex = players[i].Index;
            }
            return nearestIndex;
        }

        void UpdateScorePanels(bool sortResult)
        {
            foreach (var panel in scorePanels)
            {
                if (panel == null) continue;
                bool isResultPanel = (resultPanel != null && panel.IsChildOf(resultPanel))
                    || (gameplayResultPanel != null && panel.IsChildOf(gameplayResultPanel));
                UpdateScorePanel(panel, isResultPanel, sortResult && isResultPanel);
            }
        }

        void UpdateScorePanel(RectTransform panel, bool isResultPanel, bool sort)
        {
            var entries = new List<ScoreEntry>();
            var handled = new HashSet<Transform>();
            foreach (var rawImage in panel.GetComponentsInChildren<RawImage>(true))
            {
                int playerIndex = ParseResultPlayerIndex(rawImage.name);
                if (playerIndex < 0) continue;
                bool active = playerIndex < players.Count;
                rawImage.gameObject.SetActive(active);
                if (!active) continue;
                int score = PlayerScore(playerIndex);
                if (isResultPanel) SetPlayerLabelText(rawImage.transform, playerIndex);
                SetScoreText(rawImage.transform, score, isResultPanel);
                handled.Add(rawImage.transform);
                entries.Add(new ScoreEntry(rawImage.rectTransform, score, playerIndex));
            }

            foreach (var playerText in panel.GetComponentsInChildren<TMP_Text>(true))
            {
                int playerIndex = ParsePlayerLabelIndex(playerText.text);
                if (playerIndex < 0 || playerIndex >= players.Count) continue;

                Transform root = playerText.transform;
                while (root != null && root.parent != panel) root = root.parent;
                if (root == null || handled.Contains(root)) continue;

                int score = PlayerScore(playerIndex);
                if (isResultPanel) SetPlayerLabelText(root, playerIndex);
                SetScoreText(root, score, isResultPanel);
                handled.Add(root);
                if (root.TryGetComponent(out RectTransform rect)) entries.Add(new ScoreEntry(rect, score, playerIndex));
            }

            if (!sort) return;
            entries.Sort((a, b) => b.Score != a.Score ? b.Score.CompareTo(a.Score) : a.PlayerIndex.CompareTo(b.PlayerIndex));
            for (int i = 0; i < entries.Count; i++) entries[i].Rect.SetSiblingIndex(i);
        }

        int FarmersThisSet()
        {
            if (farmersPerSet == null || farmersPerSet.Length == 0) return 4;
            int index = Mathf.Clamp(currentSet - 1, 0, farmersPerSet.Length - 1);
            return Mathf.Clamp(farmersPerSet[index], 1, 6);
        }

        Vector2 PlayerStartLocal(int playerIndex)
        {
            var r = FloorRect;
            int count = Mathf.Max(1, players.Count);
            float t = count == 1 ? 0.5f : (playerIndex + 1f) / (count + 1f);
            return new Vector2(Mathf.Lerp(r.xMin + 120f, r.xMax - 120f, t), r.yMin + r.height * 0.25f);
        }

        PlayerState Winner()
        {
            PlayerState winner = null;
            foreach (var player in players)
                if (winner == null || player.Score > winner.Score) winner = player;
            return winner;
        }

        int PlayerScore(int playerIndex)
        {
            foreach (var player in players)
                if (player.Index == playerIndex) return player.Score;
            return 0;
        }

        void AddScorePanel(RectTransform panel)
        {
            if (panel != null && !scorePanels.Contains(panel)) scorePanels.Add(panel);
        }

        static string[] Names(NamedColor[] values)
        {
            var names = new string[values.Length];
            for (int i = 0; i < values.Length; i++) names[i] = values[i].Name;
            return names;
        }

        static string Ordinal(int index)
        {
            int value = index + 1;
            int lastTwo = value % 100;
            if (lastTwo == 11 || lastTwo == 12 || lastTwo == 13) return value + "th";

            int last = value % 10;
            if (last == 1) return value + "st";
            if (last == 2) return value + "nd";
            if (last == 3) return value + "rd";
            return value + "th";
        }

        static string[] ShuffleTake(string[] pool, string correct, int count)
        {
            var rest = new List<string>();
            foreach (var item in pool)
                if (item != correct) rest.Add(item);
            Shuffle(rest);
            var output = new List<string> { correct };
            for (int i = 0; i < rest.Count && output.Count < count; i++) output.Add(rest[i]);
            Shuffle(output);
            return output.ToArray();
        }

        static void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        static void SetPlayerLabelText(Transform root, int playerIndex)
        {
            var playerText = FindTMPIn(root, $"Text (TMP) == Player {playerIndex + 1}") ?? FindTMPIn(root, $"Player {playerIndex + 1}");
            if (playerText != null) playerText.text = $"Player {playerIndex + 1}";
        }

        static void SetScoreText(Transform root, int score, bool resultFormat)
        {
            var scoreText = FindTMPIn(root, "Text (TMP) == Score") ?? FindTMPIn(root, "Player Score") ?? FindTMPIn(root, "Score");
            if (scoreText != null) scoreText.text = score.ToString(resultFormat ? "0000" : "00");
        }

        static RectTransform FindRect(string name)
        {
            foreach (var rect in Resources.FindObjectsOfTypeAll<RectTransform>())
                if (rect != null && rect.gameObject.scene.IsValid() && rect.name == name) return rect;
            return null;
        }

        // Same fixed 6-color palette BossSlayerGameManager's own PlayerColor
        // uses — kept identical rather than inventing a separate one, so a
        // given player index reads as the same color across every game in
        // this installation.
        static Color PlayerColor(int index)
        {
            Color[] colors = { Color.cyan, Color.green, Color.yellow, new(1f, 0.35f, 1f), new(1f, 0.45f, 0.65f), new(0.6f, 0.25f, 0.9f) };
            return colors[Mathf.Clamp(index, 0, colors.Length - 1)];
        }

        static RectTransform FindRectIn(Transform root, string name)
        {
            if (root == null) return null;
            foreach (var rect in root.GetComponentsInChildren<RectTransform>(true))
                if (rect.name == name || rect.name.Contains(name)) return rect;
            return null;
        }

        static RectTransform FindDirectRectIn(Transform root, string name)
        {
            if (root == null) return null;
            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child.name == name && child.TryGetComponent(out RectTransform rect)) return rect;
            }
            return null;
        }

        static TMP_Text FindTMP(string name)
        {
            foreach (var text in Resources.FindObjectsOfTypeAll<TMP_Text>())
                if (text != null && text.gameObject.scene.IsValid() && text.name == name) return text;
            return null;
        }

        static TMP_Text FindTMPIn(Transform root, string name)
        {
            if (root == null) return null;
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
                if (text.name == name || text.name.Contains(name)) return text;
            return null;
        }

        static Scrollbar FindScrollbar(string name)
        {
            foreach (var scrollbar in Resources.FindObjectsOfTypeAll<Scrollbar>())
                if (scrollbar != null && scrollbar.gameObject.scene.IsValid() && (scrollbar.name == name || scrollbar.name.Contains(name))) return scrollbar;
            return null;
        }

        static Image FindImageIn(Transform root, string name)
        {
            if (root == null) return null;
            foreach (var image in root.GetComponentsInChildren<Image>(true))
                if (image.name == name || image.name.Contains(name)) return image;
            return null;
        }

        static int ParseResultPlayerIndex(string objectName)
        {
            const string prefix = "RawImage == Player";
            if (string.IsNullOrEmpty(objectName) || !objectName.StartsWith(prefix)) return -1;
            return int.TryParse(objectName.Substring(prefix.Length), out int value) ? value - 1 : -1;
        }

        static int ParsePlayerLabelIndex(string text)
        {
            const string prefix = "Player ";
            if (string.IsNullOrEmpty(text) || !text.StartsWith(prefix)) return -1;

            int start = prefix.Length;
            int end = start;
            while (end < text.Length && char.IsDigit(text[end])) end++;
            if (end == start) return -1;

            return int.TryParse(text.Substring(start, end - start), out int value) ? value - 1 : -1;
        }

        readonly struct ScoreEntry
        {
            public readonly RectTransform Rect;
            public readonly int Score;
            public readonly int PlayerIndex;

            public ScoreEntry(RectTransform rect, int score, int playerIndex)
            {
                Rect = rect;
                Score = score;
                PlayerIndex = playerIndex;
            }
        }
    }
}
