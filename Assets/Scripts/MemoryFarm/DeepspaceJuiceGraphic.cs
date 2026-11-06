using UnityEngine;
using UnityEngine.UI;

namespace Deepspace
{
    public enum SpaceJuice { Shot, Kill, Pickup, Hurt, Respawn, Transition, Click }

    // All bursts share one mesh per display. No per-shot objects, materials or textures.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class DeepspaceJuiceGraphic : MaskableGraphic
    {
        const int Capacity = 64;
        struct Burst { public Vector2 point; public Color tint; public float age, duration, radius; public SpaceJuice kind; public Rect clip; public Vector2 end; public bool travel; }
        readonly Burst[] bursts = new Burst[Capacity];
        int next;
        float clock;
        public bool Ambient;
        public bool ManualPlayback;
        public int ActiveCount { get { int n = 0; foreach (var b in bursts) if (b.duration > 0 && b.age < b.duration) n++; return n; } }
        public void Emit(SpaceJuice kind, Vector2 point, Color tint, float radius, Rect clip)
        {
            bursts[next] = new Burst { point = point, tint = tint, radius = radius, clip = clip, kind = kind,
                duration = kind == SpaceJuice.Shot ? .13f : kind == SpaceJuice.Respawn ? .9f : kind == SpaceJuice.Transition ? 1.1f : .55f };
            next = (next + 1) % Capacity; SetVerticesDirty();
        }
        public void Travel(Vector2 start, Vector2 end, Color tint, float size, Rect clip, float delay = 0)
        {
            bursts[next] = new Burst { point = start, end = end, travel = true, tint = tint, radius = size, clip = clip, age = -delay, duration = .65f };
            next = (next + 1) % Capacity; SetVerticesDirty();
        }
        public void Clear() { System.Array.Clear(bursts, 0, Capacity); SetVerticesDirty(); }
        public void Advance(float dt)
        {
            if (dt <= 0) return;
            bool wasActive = ActiveCount > 0;
            clock += dt;
            for (int i = 0; i < Capacity; i++) if (bursts[i].age < bursts[i].duration) bursts[i].age += dt;
            if (Ambient || wasActive) SetVerticesDirty();
        }
        void Update() { if (!ManualPlayback) Advance(Time.deltaTime); }
        protected override void OnDisable() { Clear(); base.OnDisable(); }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); Rect screen = rectTransform.rect;
            if (Ambient)
            {
                // Fine moving dust confined to the edge band, leaving the play field readable.
                for (int i = 0; i < 42; i++)
                {
                    float seed = Mathf.Repeat(i * .618034f, 1), drift = Mathf.Repeat(i * .414214f - clock * (.006f + seed * .01f), 1);
                    float x = Mathf.Lerp(screen.xMin, screen.xMax, seed);
                    float y = Mathf.Lerp(screen.yMin, screen.yMax, drift);
                    float edge = Mathf.Max(Mathf.Abs(seed - .5f), Mathf.Abs(drift - .5f));
                    if (edge < .4f) continue;
                    var c = new Color(.3f, .8f, 1, (.07f + .14f * Mathf.Pow(.5f + .5f * Mathf.Sin(clock * 1.6f + i), 6)));
                    float s = screen.height * (.0007f + seed * .001f);
                    Diamond(vh, new Vector2(x, y), s, s * 2.8f, c, screen);
                }
            }
            foreach (var b in bursts)
            {
                if (b.duration <= 0 || b.age < 0 || b.age >= b.duration) continue;
                if (b.travel)
                {
                    float progress = b.age / b.duration;
                    for (int trail = 0; trail < 5; trail++)
                    {
                        float v = Mathf.Clamp01(progress - trail * .045f);
                        var point = Vector2.Lerp(b.point, b.end, v * v * (3 - 2 * v)) + Vector2.up * Mathf.Sin(v * Mathf.PI) * b.radius * 2;
                        var tint = b.tint; tint.a *= (1 - progress * .6f) * (1 - trail * .17f);
                        Diamond(vh, point, b.radius * (1 - trail * .12f), b.radius * 1.25f, tint, b.clip);
                    }
                    continue;
                }
                float t = b.age / b.duration, fade = (1 - t) * (1 - t);
                Color c = b.tint; c.a *= fade;
                float size = b.radius;
                if (b.kind == SpaceJuice.Shot)
                {
                    Diamond(vh, b.point, size * (.12f + .18f * (1 - t)), size * (1 - t), new Color(.8f, 1, 1, fade * .85f), b.clip);
                    Line(vh, b.point - Vector2.right * size * .55f, b.point + Vector2.right * size * .55f, size * .07f * (1 - t), c, b.clip);
                    continue;
                }
                float r = size * (.16f + .9f * (1 - Mathf.Pow(1 - t, 3)));
                bool hurt = b.kind == SpaceJuice.Hurt;
                Ring(vh, b.point, r, size * .025f * (1 - t), c, b.clip, hurt ? 4 : 40, clock * .1f);
                if (b.kind == SpaceJuice.Respawn || b.kind == SpaceJuice.Pickup || b.kind == SpaceJuice.Transition)
                    Ring(vh, b.point, r * .73f, size * .012f * (1 - t), c * new Color(1, 1, 1, .55f), b.clip, 32, 0);
                int rays = b.kind == SpaceJuice.Kill ? 18 : b.kind == SpaceJuice.Transition ? 28 : 12;
                for (int i = 0; i < rays; i++)
                {
                    float seed = Mathf.Repeat(i * .618034f, 1), a = i * Mathf.PI * 2 / rays;
                    var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    float travel = b.kind == SpaceJuice.Pickup ? Mathf.Lerp(1, .08f, t) : .25f + t * (1 + seed * .65f);
                    Vector2 p = b.point + dir * size * travel;
                    if (i % 3 == 0) Line(vh, p, p + dir * size * .2f * (1 - t), size * .012f * (1 - t), c, b.clip);
                    else Diamond(vh, p, size * (.018f + seed * .028f) * (1 - t), size * .05f * (1 - t), c, b.clip);
                }
                if (b.kind == SpaceJuice.Pickup)
                {
                    var hot = new Color(.85f, 1, .8f, fade);
                    Line(vh, b.point + Vector2.down * size * .2f, b.point + Vector2.up * size * .2f, size * .04f * fade, hot, b.clip);
                    Line(vh, b.point + Vector2.left * size * .2f, b.point + Vector2.right * size * .2f, size * .04f * fade, hot, b.clip);
                }
            }
        }
        public static void Ring(VertexHelper vh, Vector2 p, float r, float width, Color c, Rect clip, int segments, float phase)
        {
            for (int i = 0; i < segments; i++)
            {
                float a = phase + i * Mathf.PI * 2 / segments, b = phase + (i + 1) * Mathf.PI * 2 / segments;
                Line(vh, p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r, p + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * r, width, c, clip);
            }
        }
        public static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color c, Rect clip)
        { var d = b - a; var side = new Vector2(-d.y, d.x).normalized * width; Quad(vh, a - side, b - side, b + side, a + side, c, clip); }
        public static void Diamond(VertexHelper vh, Vector2 p, float w, float h, Color c, Rect clip)
        { Quad(vh, p + Vector2.left * w, p + Vector2.down * h, p + Vector2.right * w, p + Vector2.up * h, c, clip); }
        static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color, Rect clip)
        {
            if (color.a <= .001f) return;
            int start = vh.currentVertCount;
            Add(vh, a, color, clip); Add(vh, b, color, clip); Add(vh, c, color, clip); Add(vh, d, color, clip);
            vh.AddTriangle(start, start + 1, start + 2); vh.AddTriangle(start, start + 2, start + 3);
        }
        static void Add(VertexHelper vh, Vector2 p, Color c, Rect clip)
        { vh.AddVert(new Vector3(Mathf.Clamp(p.x, clip.xMin, clip.xMax), Mathf.Clamp(p.y, clip.yMin, clip.yMax)), c, Vector2.zero); }
    }
}
