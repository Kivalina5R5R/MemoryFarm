using UnityEngine;

namespace Deepspace
{
    // Separate presentation clock; never offsets the tracked play area or hit zones.
    public sealed class BossUltimateImpactFrame : MonoBehaviour
    {
        public DeepspaceImpactGraphic Wall { get; private set; }
        public DeepspaceImpactGraphic Floor { get; private set; }
        public bool Playing { get; private set; }
        public int TriggerCount { get; private set; }
        public float Age { get; private set; }
        public Vector2 ShakeOffset { get; private set; }
        RectTransform background;
        Vector3 restPosition, restScale;
        bool hasRest, beam;
        public bool IsUltimateFrame {get;private set;}
        Vector2 floorFocus;
        public bool CanPlay(bool ultimate)=>!Playing || Age>=.12f || (ultimate&&!IsUltimateFrame);
        public bool ManualPlayback {get;set;}
        public bool ExternalShake {get;private set;}
        public void SetExternalShake(bool value){if(value&&!ExternalShake)RestoreBackground();ExternalShake=value;}
        float amplitude;
        Vector2 focus;

        public void Initialize(RectTransform wall, RectTransform floor, RectTransform floorBackground)
        {
            background = floorBackground;
            Wall = Create(wall, "Boss ultimate wall impact");
            Floor = Create(floor, "Boss ultimate floor shockwave");
            Clear();
        }
        static DeepspaceImpactGraphic Create(RectTransform area, string name)
        {
            if (area == null) return null;
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasRenderer), typeof(DeepspaceImpactGraphic));
            go.layer = area.gameObject.layer;
            go.transform.SetParent(area, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 205; // Keep the ultimate title (210) legible.
            var graphic = go.GetComponent<DeepspaceImpactGraphic>();
            graphic.raycastTarget = false;
            var rect = graphic.rectTransform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return graphic;
        }
        public void Play(Vector2 wallFocus, bool isBeam, float strength, bool ultimate=true, Vector2? floorOrigin=null)
        {
            // Several hazards released in one beat produce one flash and one shake.
            if (!CanPlay(ultimate)) return;
            RestoreBackground();
            if (background != null && !ExternalShake)
            {
                restPosition = background.localPosition;
                restScale = background.localScale;
                hasRest = true;
            }
            focus = wallFocus; beam = isBeam; IsUltimateFrame=ultimate; floorFocus=floorOrigin??new Vector2(.5f,.9f);
            amplitude = Mathf.Clamp(strength, .2f, 1.5f);
            Age = 0; Playing = true; TriggerCount++;
            if (Wall != null) Wall.gameObject.SetActive(true);
            if (Floor != null) Floor.gameObject.SetActive(true);
            Refresh();
        }
        public void Advance(float dt)
        {
            if (!Playing || dt <= 0) return;
            Age += dt;
            if (Age >= DeepspaceImpactGraphic.Duration/(IsUltimateFrame?1f:2f)) { Clear(); return; }
            Refresh();
        }
        void Refresh()
        {
            float visualAge=Age*(IsUltimateFrame?1f:2f);
            if (Wall != null) { Wall.SetFrame(visualAge, focus, beam, true); Wall.canvasRenderer.SetAlpha(IsUltimateFrame?1f:.42f); }
            // No full-screen flash or dark frame on the floor: warnings stay readable.
            if (Floor != null) { Floor.SetFrame(visualAge, floorFocus, beam, false); Floor.canvasRenderer.SetAlpha(IsUltimateFrame?1f:.5f); }
            float decay = Mathf.Pow(Mathf.Clamp01(1 - Age / (IsUltimateFrame?.55f:.25f)), 2);
            float pixels = background != null ? Mathf.Min(background.rect.width, background.rect.height) * .012f : 0;
            ShakeOffset = new Vector2(Mathf.Sin(Age * 137f + .8f), Mathf.Sin(Age * 181f)) * (pixels * amplitude * decay);
            if (background != null && hasRest && !ExternalShake)
            {
                background.localPosition = restPosition + (Vector3)ShakeOffset;
                background.localScale = Vector3.Scale(restScale, new Vector3(IsUltimateFrame?1.04f:1.012f, IsUltimateFrame?1.04f:1.012f, 1));
            }
        }
        void RestoreBackground()
        {
            if (background != null && hasRest)
            {
                background.localPosition = restPosition;
                background.localScale = restScale;
            }
            hasRest = false; ShakeOffset = Vector2.zero;
        }
        public void Clear()
        {
            Playing = false;
            RestoreBackground();
            if (Wall != null) Wall.gameObject.SetActive(false);
            if (Floor != null) Floor.gameObject.SetActive(false);
        }
        void LateUpdate() { if(!ManualPlayback) Advance(Time.deltaTime); }
        void OnDisable() { Clear(); }
        void OnDestroy()
        {
            Clear();
            if (Wall != null) Destroy(Wall.gameObject);
            if (Floor != null) Destroy(Floor.gameObject);
        }
    }
}
