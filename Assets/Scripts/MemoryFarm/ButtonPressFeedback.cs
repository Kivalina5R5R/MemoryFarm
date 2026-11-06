using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Deepspace
{
    // Attach to any Button: whichever way it is pressed (mouse, a player standing on it, Enter) it fires the same
    // onClick, so this one hook covers them all. A short scale punch that returns to the original scale on its own.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    public sealed class ButtonPressFeedback : MonoBehaviour
    {
        [SerializeField] float punchScale = 0.15f;
        [SerializeField] float punchSeconds = 0.25f;

        Button button;
        Vector3 baseScale;
        Coroutine punch;

        // Lets an external idle scale pulse step aside while the punch runs.
        public System.Action OnPunchStart;
        public System.Action OnPunchComplete;

        void Awake()
        {
            button = GetComponent<Button>();
            baseScale = transform.localScale;
            button.onClick.AddListener(PlayPunch);
        }

        // For code that replaces button.onClick wholesale after Awake already hooked the punch. No-op before Awake.
        public void Rehook()
        {
            if (button == null) return;
            button.onClick.RemoveListener(PlayPunch);
            button.onClick.AddListener(PlayPunch);
        }

        void OnDisable()
        {
            if (punch == null) return;
            StopCoroutine(punch);
            punch = null;
            transform.localScale = baseScale;
        }

        void OnDestroy()
        {
            if (button != null) button.onClick.RemoveListener(PlayPunch);
        }

        void PlayPunch()
        {
            if (!isActiveAndEnabled) return;
            if (punch != null) StopCoroutine(punch);
            OnPunchStart?.Invoke();
            punch = StartCoroutine(Punch());
        }

        IEnumerator Punch()
        {
            float seconds = Mathf.Max(0.01f, punchSeconds);
            for (float time = 0f; time < seconds; time += Time.unscaledDeltaTime)
            {
                float t = time / seconds;
                transform.localScale = baseScale * (1f + punchScale * Mathf.Sin(t * Mathf.PI * 3f) * (1f - t));
                yield return null;
            }

            transform.localScale = baseScale;
            punch = null;
            OnPunchComplete?.Invoke();
        }
    }
}
