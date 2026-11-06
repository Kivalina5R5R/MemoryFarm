#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deepspace
{
    // Keys for checking the game quickly, in the Editor and development builds only.
    //   M: skip the current question and go on to whatever follows it.
    //   N: skip the rest of the current set and go on to whatever follows it.
    // Between sets (set break, minigame) both keys end that part and start the next set. Skipped questions score nothing.
    public sealed partial class MemoryFarmGameManager
    {
        void UpdateDebugKeys()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.mKey.wasPressedThisFrame) DebugSkip(false);
            else if (keyboard.nKey.wasPressedThisFrame) DebugSkip(true);
        }

        void DebugSkip(bool wholeSet)
        {
            switch (state)
            {
                case GameState.Countdown:
                    if (countdownNumber != null) countdownNumber.Hide();
                    if (wholeSet) DebugSkipSet();
                    else StartWatch();
                    break;
                case GameState.Rule:
                    ShowRuleUi(false);
                    if (wholeSet) DebugSkipSet();
                    else StartWatch();
                    break;
                case GameState.Break:
                    StartWatch();
                    break;
                case GameState.Minigame:
                    EndMinigame();
                    StartWatch();
                    break;
                case GameState.Watch:
                case GameState.Question:
                case GameState.Reveal:
                    if (wholeSet) DebugSkipSet();
                    else AdvancePastQuestion();
                    break;
                default:
                    return;
            }
            Debug.Log($"[Debug keys] {(wholeSet ? "N" : "M")}: now set {currentSet}, question {currentSection}, {state}");
        }

        void DebugSkipSet()
        {
            currentSection = sectionsPerSet;
            AdvancePastQuestion();
        }
    }
}
#endif
