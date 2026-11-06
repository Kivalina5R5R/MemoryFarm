using TMPro;
using UnityEngine;

namespace Deepspace
{
    // A big number that drops onto the wall at each second of the countdown: it falls from big, shrinking faster and faster
    // and fading in, squashes as it lands and springs back, holds, then fades. The font, colour and size are on the text.
    [RequireComponent(typeof(TMP_Text))]
    public sealed class CountdownNumber : MonoBehaviour
    {
        private const float BounceEnd = .5f, FadeFrom = .8f;

        [Tooltip("How big a number starts, high above the screen.")]
        [SerializeField, Min(1f)] private float dropFromScale = 3f;
        [Tooltip("How much of each second the fall takes.")]
        [SerializeField, Range(.05f, .6f)] private float fallPart = .25f;
        [Tooltip("How much a number squashes on landing.")]
        [SerializeField, Range(0f, .4f)] private float landSquash = .15f;

        private TMP_Text number;

        private TMP_Text Number => number != null ? number : number = GetComponent<TMP_Text>();

        // The whole second that secondsLeft is in picks the number and how far through its drop it is.
        public void Show(float secondsLeft)
        {
            int whole = Mathf.CeilToInt(secondsLeft);
            if (whole < 1)
            {
                Hide();
                return;
            }

            if (!Number.enabled)
            {
                Number.enabled = true;
                transform.SetAsLastSibling();
            }

            Number.text = whole.ToString();
            Pose(1f - (secondsLeft - (whole - 1)));
        }

        public void Hide()
        {
            Number.enabled = false;
        }

        private void Pose(float t)
        {
            Vector3 scale = Vector3.one;
            float alpha = 1f;
            if (t < fallPart)
            {
                float u = t / fallPart;
                scale *= Mathf.Lerp(dropFromScale, 1f, u * u);
                alpha = Mathf.Clamp01(u * 3f);
            }
            else if (t < BounceEnd)
            {
                float u = (t - fallPart) / (BounceEnd - fallPart);
                float spring = Mathf.Cos(u * Mathf.PI * 2.5f) * (1f - u) * (1f - u) * landSquash;
                scale = new Vector3(1f + spring, 1f - spring, 1f);
            }
            else if (t > FadeFrom)
            {
                alpha = 1f - (t - FadeFrom) / (1f - FadeFrom);
            }

            transform.localScale = scale;
            Number.alpha = alpha;
        }
    }
}
