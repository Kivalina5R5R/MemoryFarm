using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Deepspace
{
    // Presses the floor's buttons when a player stands on one for HoldSeconds. A bar fills across the button meanwhile.
    // A player only counts after being seen off the button, so nobody presses a button that appears under their feet,
    // and after a press everyone has to step off first. Enter presses the first button showing, for testing and for
    // the operator; a mouse click still works through the EventSystem.
    public sealed class FloorStandButtons : MonoBehaviour
    {
        private const float HoldSeconds = 2f;
        private const string BarName = "Hold Bar";
        private static readonly Color BarColor = new Color(1f, 1f, 1f, .35f);

        private readonly List<Button> showing = new List<Button>();
        private readonly Dictionary<Button, float> held = new Dictionary<Button, float>();
        private readonly HashSet<(Button button, int slot)> armed = new HashSet<(Button, int)>();
        private RectTransform floor;
        private FloorPlayerInput players;

        public void Configure(RectTransform floorCanvas, FloorPlayerInput floorPlayers)
        {
            floor = floorCanvas;
            players = floorPlayers;
        }

        private void Update()
        {
            if (floor == null || players == null) return;

            floor.GetComponentsInChildren(false, showing);
            armed.RemoveWhere(entry => entry.button == null || !showing.Contains(entry.button));
            foreach (Button button in showing)
            {
                if (button.interactable) StepButton(button);
            }

            if (Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame && showing.Count > 0)
            {
                Press(showing[0]);
            }
        }

        private void StepButton(Button button)
        {
            float time = HasPlayerStanding(button) ? Hold(button) : 0f;
            held[button] = time;
            ShowBar(button, time / HoldSeconds);
            if (time >= HoldSeconds) Press(button);
        }

        private float Hold(Button button)
        {
            held.TryGetValue(button, out float time);
            return time + Time.unscaledDeltaTime;
        }

        private bool HasPlayerStanding(Button button)
        {
            var rect = (RectTransform)button.transform;
            bool standing = false;
            for (int slot = 0; slot < FloorPlayerInput.MaxSlots; slot++)
            {
                if (!players.TryGetWorld(slot, out Vector3 world)) continue;

                if (!rect.rect.Contains(rect.InverseTransformPoint(world))) armed.Add((button, slot));
                else if (armed.Contains((button, slot))) standing = true;
            }

            return standing;
        }

        private void Press(Button button)
        {
            held[button] = 0f;
            ShowBar(button, 0f);
            armed.Clear();
            button.onClick.Invoke();
        }

        private static void ShowBar(Button button, float progress)
        {
            Transform existing = button.transform.Find(BarName);
            if (existing == null && progress <= 0f) return;

            RectTransform bar = existing != null ? (RectTransform)existing : CreateBar(button.transform);
            bar.gameObject.SetActive(progress > 0f);
            bar.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
        }

        private static RectTransform CreateBar(Transform button)
        {
            var bar = new GameObject(BarName, typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(button, false);
            var layout = bar.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;
            var image = bar.GetComponent<Image>();
            image.color = BarColor;
            image.raycastTarget = false;
            var rect = (RectTransform)bar.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(0f, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }
    }
}
