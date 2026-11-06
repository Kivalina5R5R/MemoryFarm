using UnityEngine;
using UnityEngine.UI;

namespace Fortal.Display
{
    // A resolution-independent filled circle UI Image, tinted and sized in the authored prefab.
    public sealed class TagCircle : Image
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect();
            Vector2 center = rect.center;
            float radius = Mathf.Min(rect.width, rect.height) * 0.5f;
            mesh.AddVert(center, color, Vector2.zero);
            const int segments = 64;
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                mesh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius,
                    color, Vector2.zero);
                mesh.AddTriangle(0, i + 1, (i + 1) % segments + 1);
            }
        }
    }
}
