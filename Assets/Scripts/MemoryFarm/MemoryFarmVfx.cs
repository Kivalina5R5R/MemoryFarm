using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Deepspace
{
    [DisallowMultipleComponent]
    public sealed class MemoryFarmVfx : MonoBehaviour
    {
        MemoryFarmVfxGraphic floor, wall;
        public void Configure(RectTransform floorArea, RectTransform wallArea)
        {
            if (floor == null && floorArea != null) floor = Create(floorArea, "Memory Farm VFX == Floor", Visual3DFollower.FloorLayer);
            if (wall == null && wallArea != null) wall = Create(wallArea, "Memory Farm VFX == Wall", Visual3DFollower.WallLayer);
            foreach (var camera in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (camera.gameObject.scene != gameObject.scene || camera.targetTexture != null) continue;
                if (camera.targetDisplay == Fortal.Display.DisplayOutput.FloorDisplayIndex)
                    camera.cullingMask = (camera.cullingMask | (1 << Visual3DFollower.FloorLayer)) & ~(1 << Visual3DFollower.WallLayer);
                else if (camera.targetDisplay == 1)
                    camera.cullingMask = (camera.cullingMask | (1 << Visual3DFollower.WallLayer)) & ~(1 << Visual3DFollower.FloorLayer);
            }
        }
        static MemoryFarmVfxGraphic Create(RectTransform parent, string name, int layer)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(MemoryFarmVfxGraphic));
            go.layer = layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            // Separate canvas batch so each camera can cull this display layer.
            go.AddComponent<Canvas>();
            var graphic = go.GetComponent<MemoryFarmVfxGraphic>();
            graphic.raycastTarget = false;
            return graphic;
        }
        public void Answer(RectTransform cursor, bool correct, int playerIndex, int playerCount, int streak)
        {
            if (!isActiveAndEnabled) return;
            if (floor != null && cursor != null)
                floor.Burst(floor.rectTransform.InverseTransformPoint(cursor.position), correct, streak);

        }
        // A player pad has just shrunk away: daisies and leaves in the player's colour, the size of the stump that stood there.
        public void PadBurst(RectTransform pad, Color playerColor)
        {
            if (!isActiveAndEnabled || floor == null || pad == null) return;
            var (center, radius) = PlayerPadArt.RoundPiece(pad);
            floor.PadPop(floor.rectTransform.InverseTransformPoint(center), radius, playerColor);
        }
        // An inactive graphic never ages its particles, so it does not count as playing.
        public bool FloorPlaying => floor != null && floor.isActiveAndEnabled && floor.ParticleCount > 0;
        public void Celebrate()
        {
            Clear();
            if (wall != null) wall.Celebrate();
            if (floor != null) floor.Celebrate();
        }
        public void Clear() { if (floor != null) floor.Clear(); if (wall != null) wall.Clear(); }
        void OnDisable() => Clear();
        void OnDestroy()
        {
            if (floor != null) Destroy(floor.gameObject);
            if (wall != null) Destroy(wall.gameObject);
        }
    }

    // One bounded UI mesh per display; no per-particle objects or material instances.
    public sealed class MemoryFarmVfxGraphic : MaskableGraphic
    {
        struct Particle
        {
            public Vector2 origin, velocity;
            public float age, life, size, angle, spin, gravity;
            public int shape; // 0 leaf, 1 flower, 2 ring, 3 cross
            public Color tint;
        }
        const int Capacity = 384;
        readonly List<Particle> particles = new List<Particle>(Capacity);
        readonly System.Random random = new System.Random(417);
        static readonly Color Green = new Color(.43f, .87f, .29f);
        static readonly Color Gold = new Color(1f, .79f, .25f);
        static readonly Color Cream = new Color(1f, .96f, .68f);
        static readonly Color Wrong = new Color(1f, .44f, .29f);
        public int ParticleCount => particles.Count;
        float Unit => Mathf.Max(1f, Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) / 1280f);
        float Range(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());
        void Add(Particle p)
        {
            if (particles.Count >= Capacity) particles.RemoveAt(0);
            particles.Add(p);
        }
        public void Burst(Vector2 position, bool correct, int streak)
        {
            transform.SetAsLastSibling();
            float u = Unit;
            Add(new Particle { origin = position, life = 1.05f, size = 75f * u, shape = 2, tint = correct ? Green : Wrong });
            Add(new Particle { origin = position, life = .8f, size = 53f * u, shape = 2, tint = correct ? Gold : Cream });
            if (!correct) Add(new Particle { origin = position, life = 1.1f, size = 52f * u, shape = 3, tint = Wrong });
            int count = correct ? 24 + Mathf.Min(24, Mathf.Max(0, streak - 1) * 6) : 14;
            for (int i = 0; i < count; i++)
            {
                float angle = Range(0, Mathf.PI * 2);
                var velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Range(110, correct ? 280 : 150) * u;
                velocity.y += (correct ? 130 : 40) * u;
                Add(new Particle { origin = position, velocity = velocity, gravity = 170 * u, life = Range(.95f, 1.55f),
                    size = Range(16, 28) * u, angle = angle, spin = Range(-3, 3), shape = correct && i % 3 == 0 ? 1 : 0,
                    tint = correct ? (i % 3 == 0 ? Cream : i % 2 == 0 ? Gold : Green) : new Color(1f,.64f,.32f) });
            }
            if (correct && (streak == 3 || streak == 5))
            {
                int petals = streak == 5 ? 20 : 12;
                for (int i = 0; i < petals; i++)
                {
                    float a = i * Mathf.PI * 2 / petals;
                    var direction = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    Add(new Particle { origin = position + direction * (streak == 5 ? 155 : 110) * u,
                        velocity = direction * 35 * u, life = 1.8f, size = (streak == 5 ? 44 : 34) * u,
                        angle = a, spin = .6f, shape = 1, tint = i % 2 == 0 ? Gold : Cream });
                }
            }
            SetVerticesDirty();
        }
        // Above the player pads, which sit on a later panel than this graphic's parent.
        const int PadPopSortingOrder = 100;

        public void PadPop(Vector2 center, float radius, Color color)
        {
            if (TryGetComponent(out Canvas ownCanvas)) { ownCanvas.overrideSorting = true; ownCanvas.sortingOrder = PadPopSortingOrder; }
            for (int i = 0; i < 36; i++)
            {
                float angle = Range(0, Mathf.PI * 2);
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Add(new Particle { origin = center + direction * radius * Range(.5f, .9f), velocity = direction * radius * Range(.5f, 1.3f),
                    gravity = radius * .7f, life = Range(.7f, 1.2f), size = radius * Range(.07f, .12f), angle = angle, spin = Range(-3, 3),
                    shape = i % 3 == 0 ? 1 : 0, tint = i % 3 == 0 ? Cream : color });
            }
            SetVerticesDirty();
        }
        public void Celebrate()
        {
            transform.SetAsLastSibling();
            var r = rectTransform.rect; float u = Unit;
            for (int side = 0; side < 2; side++)
                for (int i = 0; i < 80; i++)
                    Add(new Particle { origin = new Vector2(side == 0 ? r.xMin + r.width * .08f : r.xMax - r.width * .08f, r.yMin + r.height * .12f),
                        velocity = new Vector2((side == 0 ? 1 : -1) * Range(.09f, .25f) * r.width, Range(.8f, 1.35f) * r.height), gravity = r.height * .9f,
                        life = Range(2.2f, 3f), size = Range(.018f, .033f) * r.height, angle = Range(0, 6.28f), spin = Range(-4, 4),
                        shape = i % 4 == 0 ? 1 : 0, tint = i % 3 == 0 ? Cream : i % 2 == 0 ? Green : Gold });
            SetVerticesDirty();
        }
        public void Clear() { particles.Clear(); SetVerticesDirty(); }
        void Update()
        {
            if (particles.Count == 0 || Time.deltaTime <= 0) return;
            for (int i = particles.Count - 1; i >= 0; i--)
            {
                var p = particles[i]; p.age += Time.deltaTime;
                if (p.age >= p.life) particles.RemoveAt(i); else particles[i] = p;
            }
            SetVerticesDirty();
        }
        protected override void OnDisable() { Clear(); base.OnDisable(); }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            foreach (var p in particles)
            {
                float t = p.age / p.life;
                var center = p.origin + p.velocity * p.age + Vector2.down * (.5f * p.gravity * p.age * p.age);
                var tint = p.tint; tint.a *= Mathf.Min(1f, (1f - t) * 3f);
                float size = p.size * (p.shape == 2 ? 1 + t * 1.5f : .7f + .3f * Mathf.Sin(Mathf.PI * Mathf.Min(1, t * 3)));
                float angle = p.angle + p.spin * p.age;
                int start = vh.currentVertCount;
                if (p.shape == 2)
                {
                    for (int i = 0; i < 32; i++)
                    {
                        float a = i * Mathf.PI / 16;
                        var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                        vh.AddVert(center + d * size, tint, Vector2.zero);
                        vh.AddVert(center + d * (size - 10 * Unit), tint, Vector2.zero);
                        int next = (i + 1) % 32;
                        vh.AddTriangle(start + i * 2, start + next * 2, start + i * 2 + 1);
                        vh.AddTriangle(start + i * 2 + 1, start + next * 2, start + next * 2 + 1);
                    }
                    continue;
                }
                if (p.shape == 3)
                {
                    for (int arm = 0; arm < 2; arm++)
                    {
                        float a = Mathf.PI * (.25f + arm * .5f);
                        var along = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * size;
                        var across = new Vector2(-Mathf.Sin(a), Mathf.Cos(a)) * size * .23f;
                        int v = vh.currentVertCount;
                        vh.AddVert(center - along - across, tint, Vector2.zero); vh.AddVert(center + along - across, tint, Vector2.zero);
                        vh.AddVert(center + along + across, tint, Vector2.zero); vh.AddVert(center - along + across, tint, Vector2.zero);
                        vh.AddTriangle(v,v+1,v+2); vh.AddTriangle(v,v+2,v+3);
                    }
                    continue;
                }
                int count = p.shape == 1 ? 20 : 4;
                vh.AddVert(center, tint, Vector2.zero);
                for (int i = 0; i < count; i++)
                {
                    float a = angle + i * Mathf.PI * 2 / count;
                    float radius = size * (p.shape == 1 ? (i % 4 == 0 ? .4f : 1f) : (i % 2 == 0 ? 1f : .4f));
                    vh.AddVert(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, tint, Vector2.zero);
                    vh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % count);
                }
            }
        }
    }
}
