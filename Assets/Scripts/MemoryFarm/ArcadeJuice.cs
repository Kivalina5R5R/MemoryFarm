using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Deepspace
{
    // Shared presentation only. Every game supplies its own accepted gameplay events and clock.
    public sealed class ArcadeJuice : MonoBehaviour
    {
        sealed class Popup { public TMP_Text text; public Vector2 origin; public Color tint; public float age; }
        readonly Popup[] popups = new Popup[32];
        readonly GameObject[] roots = new GameObject[2];
        readonly DeepspaceJuiceGraphic[] effects = new DeepspaceJuiceGraphic[2];
        readonly RectTransform[] areas = new RectTransform[2];
        readonly int[] next = new int[2];
        static RectTransform MakeRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }
        TMP_FontAsset font;
        BossUltimateImpactFrame impact;
        public bool ImpactPlaying => impact != null && impact.Playing;
        public void Impact(Vector2 point, Color tint, bool major = false, bool wall = false)
        {
            if (roots[0] == null || roots[1] == null) return;
            if (impact == null)
            {
                impact = gameObject.AddComponent<BossUltimateImpactFrame>();
                impact.ManualPlayback = true;
                impact.Initialize((RectTransform)roots[1].transform, (RectTransform)roots[0].transform, null);
            }
            int i = wall ? 1 : 0; var r = areas[i].rect;
            var focus = new Vector2(Mathf.InverseLerp(r.xMin,r.xMax,point.x), Mathf.InverseLerp(r.yMin,r.yMax,point.y));
            Burst(point, tint, major ? 240 : 125, SpaceJuice.Kill, wall);
            if (!impact.CanPlay(major)) return;
            impact.Wall.UseCustomTint = impact.Floor.UseCustomTint = true;
            impact.Wall.CustomTint = impact.Floor.CustomTint = tint;
            impact.Play(wall ? focus : new Vector2(focus.x,.5f), false, 1, major, wall ? new Vector2(.5f,.5f) : focus);
        }
        public bool ManualClock;
        // Opt-in per game (defaults keep the shared look): a shared outline/underlay material for reward text,
        // and upward stacking so back-to-back rewards at the same spot do not print on top of each other.
        public Material PopupMaterial;
        public bool StackPopups;
        public float ClockDelta { get; private set; }
        public int ActivePopups { get { int count = 0; foreach (var p in popups) if (p != null && p.text.gameObject.activeSelf) count++; return count; } }
        public DeepspaceJuiceGraphic Effect(bool wall) => effects[wall ? 1 : 0];
        public void Initialize(RectTransform floor, RectTransform wall, TMP_FontAsset labelFont = null)
        {
            font = labelFont; areas[0] = floor; areas[1] = wall;
            for (int i = 0; i < 2; i++)
            {
                if (areas[i] == null || roots[i] != null) continue;
                var go = new GameObject("Canvas == Arcade reward " + i, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                go.transform.SetParent(transform, false); go.layer = Visual3DFollower.UILayer; roots[i] = go;
                var canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.targetDisplay = areas[i].GetComponentInParent<Canvas>().rootCanvas.targetDisplay; canvas.sortingOrder = 90;
                var scaler = go.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = areas[i].rect.size; scaler.matchWidthOrHeight = .5f;
                var rect = MakeRect(go.transform, "Reward sparks");
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.sizeDelta = Vector2.zero;
                var graphic = rect.gameObject.AddComponent<DeepspaceJuiceGraphic>(); graphic.raycastTarget = false;
                graphic.ManualPlayback = true; effects[i] = graphic;
            }
            foreach (var root in gameObject.scene.GetRootGameObjects())
                foreach (var label in root.GetComponentsInChildren<TMP_Text>(true))
                {
                    string n = label.name;
                    if (n.IndexOf("Score", StringComparison.OrdinalIgnoreCase) < 0 && n.IndexOf("Countdown", StringComparison.OrdinalIgnoreCase) < 0 && n.IndexOf("Rally", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    // The dropping countdown number poses its own scale; a pulse would overwrite it every frame.
                    if (label.GetComponent<CountdownNumber>() != null) continue;
                    if (label.GetComponent<ArcadeNumberPulse>() != null) continue;
                    label.gameObject.AddComponent<ArcadeNumberPulse>().Owner = this;
                }
        }
        public void Burst(Vector2 point, Color tint, float radius, SpaceJuice kind = SpaceJuice.Kill, bool wall = false)
        {
            int i = wall ? 1 : 0; if (effects[i] == null || areas[i] == null) return;
            var size = areas[i].rect.size;
            effects[i].Emit(kind, point - areas[i].rect.center, tint, radius, new Rect(-size * .5f, size));
        }
        public void Travel(Vector2 start, Vector2 end, Color tint, int count = 5, bool wall = false)
        {
            int i = wall ? 1 : 0; if (effects[i] == null || areas[i] == null) return;
            var r = areas[i].rect; var clip = new Rect(-r.size * .5f, r.size);
            for (int n = 0; n < Mathf.Min(count, 12); n++)
                effects[i].Travel(start - r.center, end - r.center, tint, wall ? 24 : 12, clip, n * .04f);
        }
        public void Reward(Vector2 point, string text, Color tint, bool wall = false)
        {
            int display = wall ? 1 : 0; if (effects[display] == null) return;
            int index = display * 16 + next[display]; next[display] = (next[display] + 1) % 16;
            var p = popups[index];
            float width = wall ? 1500 : 440, height = wall ? 230 : 105;
            if (p == null)
            {
                var rect = MakeRect(effects[display].transform, "Reward number");
                rect.sizeDelta = new Vector2(width, height);
                var label = rect.gameObject.AddComponent<TextMeshProUGUI>(); if (font != null) label.font = font;
                label.raycastTarget = false; label.alignment = TextAlignmentOptions.Center; label.fontStyle = FontStyles.Bold;
                if (PopupMaterial != null) label.fontSharedMaterial = PopupMaterial;
                else { label.outlineColor = new Color32(12, 18, 26, 255); label.outlineWidth = .2f; }
                label.enableAutoSizing = true; label.fontSizeMin = wall ? 70 : 30; label.fontSizeMax = wall ? 140 : 65;
                label.textWrappingMode = TextWrappingModes.NoWrap; popups[index] = p = new Popup { text = label };
            }
            var size = areas[display].rect.size;
            point -= areas[display].rect.center;
            var area = new Rect(-size * .5f, size);
            p.origin = new Vector2(Mathf.Clamp(point.x, area.xMin + width * .55f, area.xMax - width * .55f),
                Mathf.Clamp(point.y + height * .65f, area.yMin + height, area.yMax - height * 2));
            if (StackPopups)
            {
                for (int guard = 0; guard < 6; guard++)
                {
                    bool moved = false;
                    for (int j = display * 16; j < display * 16 + 16; j++)
                    {
                        var o = popups[j];
                        if (o == null || o == p || !o.text.gameObject.activeSelf) continue;
                        var at = o.text.rectTransform.anchoredPosition;
                        if (Mathf.Abs(at.x - p.origin.x) < width * .8f && Mathf.Abs(at.y - p.origin.y) < height * .9f) { p.origin.y = at.y + height * .95f; moved = true; }
                    }
                    if (!moved) break;
                }
                p.origin.y = Mathf.Min(p.origin.y, area.yMax - height);
            }
            p.tint = tint; p.age = 0; p.text.text = text; p.text.color = tint;
            p.text.rectTransform.anchoredPosition = p.origin; p.text.transform.localScale = Vector3.one * 1.12f; p.text.gameObject.SetActive(true);
        }
        public Vector2 Local(Transform target, bool wall = false) => areas[wall ? 1 : 0].InverseTransformPoint(target.position);
        public void Celebrate(Color tint)
        {
            Clear();
            for (int display = 0; display < 2; display++)
            {
                if (areas[display] == null) continue; var r = areas[display].rect;
                for (int side = -1; side <= 1; side += 2)
                    Burst(r.center + new Vector2(side * r.width * .28f, -r.height * .25f), tint, r.height * .3f, SpaceJuice.Transition, display == 1);
            }
        }
        public void Advance(float dt)
        {
            ClockDelta = dt; if (dt <= 0) return;
            impact?.Advance(dt);
            foreach (var effect in effects) if (effect != null) effect.Advance(dt);
            for (int i = 0; i < popups.Length; i++)
            {
                var p = popups[i]; if (p == null || !p.text.gameObject.activeSelf) continue;
                p.age += dt; float t = p.age / .85f;
                if (t >= 1) { p.text.gameObject.SetActive(false); continue; }
                p.text.rectTransform.anchoredPosition = p.origin + Vector2.up * (i < 16 ? 80 : 160) * (1 - (1 - t) * (1 - t));
                p.text.transform.localScale = Vector3.one * (1 + .12f * Mathf.Exp(-p.age * 18));
                var c = p.tint; c.a *= 1 - Mathf.SmoothStep(.35f, 1, t); p.text.color = c;
            }
        }
        void Update() { if (!ManualClock) Advance(Time.deltaTime); }
        public void Clear() { impact?.Clear(); foreach (var effect in effects) if (effect != null) effect.Clear(); foreach (var p in popups) if (p != null) p.text.gameObject.SetActive(false); ClockDelta = 0; }
        void OnDisable() { Clear(); }
        void OnDestroy() { foreach (var root in roots) if (root != null) Destroy(root); }
    }

    [RequireComponent(typeof(TMP_Text))]
    public sealed class ArcadeNumberPulse : MonoBehaviour
    {
        public ArcadeJuice Owner;
        TMP_Text label; string previous; Vector3 baseline; float age = 1;
        void Awake() { label = GetComponent<TMP_Text>(); baseline = transform.localScale; previous = label.text; }
        void LateUpdate()
        {
            if (Owner == null || Owner.ClockDelta <= 0) return;
            if (label.text != previous) { previous = label.text; age = 0; }
            age += Owner.ClockDelta; transform.localScale = baseline * (1 + (age < .28f ? .075f * Mathf.Sin(age / .28f * Mathf.PI) : 0));
        }
        void OnDisable() { transform.localScale = baseline; age = 1; if (label != null) previous = label.text; }
    }
}
