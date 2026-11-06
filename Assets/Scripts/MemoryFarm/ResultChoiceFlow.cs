using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Deepspace
{
    // บาส 2026-10-02: one result-screen flow for every game. When the floor result panel opens, the summary stays up
    // for holdSeconds with no buttons (nobody steps on Restart by accident while reading the scores), then a SKIP
    // button appears; SKIP swaps itself for the Restart / Exit choice. Restart and Exit sit buttonDrop lower than
    // where the scene put them. SKIP is a runtime copy of Restart, so it keeps that game's theme, stand-to-press bridge
    // and press punch without any scene edits.
    [DisallowMultipleComponent]
    public sealed class ResultChoiceFlow : MonoBehaviour
    {
        [SerializeField, Min(0f)] float holdSeconds = 10f;
        [SerializeField] float buttonDrop = 220f;

        Button restart, exit, skip;
        float openedAt;
        bool choosing, dropped, droppedByLayout;

        // Play Mode only: the Editor UI bake also runs ResolveSceneReferences, and must not move scene buttons.
        public static ResultChoiceFlow Install(RectTransform panel, float buttonDrop)
        {
            if (!Application.isPlaying || panel == null) return null;
            if (!panel.TryGetComponent(out ResultChoiceFlow flow)) flow = panel.gameObject.AddComponent<ResultChoiceFlow>();
            flow.buttonDrop = buttonDrop;
            flow.Resolve();
            return flow;
        }

        // Shared "leave the game" for result screens whose game has no ExitGame of its own.
        public static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void Resolve()
        {
            foreach (var button in GetComponentsInChildren<Button>(true))
            {
                if (button.name == "Restart") restart = button;
                else if (button.name == "Exit") exit = button;
                else if (button.name == "Skip") skip = button;
            }
        }

        void OnEnable()
        {
            if (restart == null && exit == null) Resolve();
            DropButtonsOnce();
            openedAt = Time.unscaledTime;
            choosing = false;
            SetChoiceVisible(false);
            if (skip != null) skip.gameObject.SetActive(false);
        }

        void Update()
        {
            if (choosing || (skip != null && skip.gameObject.activeSelf)) return;
            if (Time.unscaledTime - openedAt < holdSeconds) return;
            var button = EnsureSkip();
            if (button != null) button.gameObject.SetActive(true);
            else { choosing = true; SetChoiceVisible(true); } // no template to copy: fall back to the plain choice
        }

        void OnSkip()
        {
            choosing = true;
            if (skip != null) skip.gameObject.SetActive(false);
            SetChoiceVisible(true);
        }

        void SetChoiceVisible(bool visible)
        {
            if (restart != null) restart.gameObject.SetActive(visible);
            if (exit != null) exit.gameObject.SetActive(visible);
        }

        void DropButtonsOnce()
        {
            if (dropped) return;
            dropped = true;
            // Five scenes lay Restart / Exit out with a HorizontalLayoutGroup on the panel, which would put any
            // anchoredPosition change straight back: move its padding instead (top + / bottom - shifts the row down
            // whatever the vertical alignment).
            var parent = (restart != null ? restart.transform : exit != null ? exit.transform : null)?.parent as RectTransform;
            if (parent != null && parent.TryGetComponent(out HorizontalOrVerticalLayoutGroup group) && group.enabled)
            {
                var p = group.padding;
                int drop = Mathf.RoundToInt(buttonDrop);
                group.padding = new RectOffset(p.left, p.right, p.top + drop, p.bottom - drop);
                LayoutRebuilder.MarkLayoutForRebuild(parent);
                droppedByLayout = true;
                return;
            }
            if (restart != null) ((RectTransform)restart.transform).anchoredPosition += Vector2.down * buttonDrop;
            if (exit != null) ((RectTransform)exit.transform).anchoredPosition += Vector2.down * buttonDrop;
        }

        // Built on first use (not at Install) so the copy picks up whatever the game's UI theme did to Restart.
        Button EnsureSkip()
        {
            if (skip != null) return skip;
            var template = restart != null ? restart : exit;
            if (template == null) return null;

            var copy = Instantiate(template.gameObject, template.transform.parent);
            copy.name = "Skip";
            copy.SetActive(false);
            var rect = (RectTransform)copy.transform;
            // Out of the panel's layout group: alone in an upper-left row it would sit exactly where Restart then
            // appears, so the foot on SKIP would be standing on Restart.
            if (!copy.TryGetComponent(out LayoutElement element)) element = copy.AddComponent<LayoutElement>();
            element.ignoreLayout = true;
            rect.localScale = Vector3.one; // the template may be mid scale-pulse (Deepspace) when copied
            if (copy.TryGetComponent(out CanvasGroup group)) group.alpha = 1f;
            // Centred between Restart and Exit, on their (lowered) row. Hidden layout children keep their last laid-out
            // spot, which is from before the padding moved.
            if (restart != null && exit != null)
                rect.localPosition = (restart.transform.localPosition + exit.transform.localPosition) * .5f + (droppedByLayout ? Vector3.down * buttonDrop : Vector3.zero);

            skip = copy.GetComponent<Button>();
            skip.onClick = new Button.ButtonClickedEvent(); // drop Restart's own calls (scene-persistent ones included)
            if (copy.TryGetComponent(out ButtonPressFeedback feedback)) feedback.Rehook();
            skip.onClick.AddListener(OnSkip);

            foreach (var label in copy.GetComponentsInChildren<TMP_Text>(true)) label.text = "SKIP";
            foreach (var label in copy.GetComponentsInChildren<Text>(true)) label.text = "SKIP";
            return skip;
        }
    }
}
