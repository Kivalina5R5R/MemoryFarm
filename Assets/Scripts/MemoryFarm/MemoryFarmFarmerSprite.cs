using UnityEngine;
using UnityEngine.Rendering;

namespace Deepspace
{
    // A hand-drawn farmer already holding an animal, on a flat card with its feet at the origin.
    // The art lives in ART/Farmers (listed in Resources/MemoryFarm/FarmerArt) as {Male|Female}_{Animal}.png plus _ShirtMask.png.
    public sealed class MemoryFarmFarmerSprite : MonoBehaviour
    {
        const string CatalogPath = "MemoryFarm/FarmerArt";
        public const float CardHeight = 1.85f;
        const float StepsPerCrossing = 4f;
        const float BobHeight = .035f;
        const float SwayDegrees = 2.2f;

        Mesh card;
        Material material;

        // Returns null when this animal has no drawing, and the parade then shows nobody for it.
        public static MemoryFarmFarmerSprite TryCreate(string animalName, bool female, Color shirtColor, Transform parent)
        {
            var catalog = Resources.Load<ArtCatalog>(CatalogPath);
            if (catalog == null) return null;
            string key = (female ? "Female_" : "Male_") + animalName;
            var art = catalog.Find(key);
            var shirtMask = catalog.Find(key + "_ShirtMask");
            var shader = Resources.Load<Shader>("MemoryFarmFarmerSprite");
            if (art == null || shirtMask == null || shader == null) return null;

            var go = new GameObject("Farmer Sprite " + animalName, typeof(MeshFilter), typeof(MeshRenderer));
            go.layer = Visual3DFollower.WallLayer;
            go.transform.SetParent(parent, false);
            var sprite = go.AddComponent<MemoryFarmFarmerSprite>();
            sprite.Build(art, shirtMask, shader, shirtColor);
            sprite.Animate(0f);
            return sprite;
        }

        void Build(Texture2D art, Texture2D shirtMask, Shader shader, Color shirtColor)
        {
            material = new Material(shader) { name = "Farmer Sprite" };
            material.SetTexture("_MainTex", art);
            material.SetTexture("_ShirtMask", shirtMask);
            material.SetColor("_ShirtColor", shirtColor);
            card = CreateCard(CardHeight * art.width / art.height, CardHeight);
            GetComponent<MeshFilter>().sharedMesh = card;
            var meshRenderer = GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        static Mesh CreateCard(float width, float height)
        {
            float halfWidth = width * .5f;
            var mesh = new Mesh { name = "Farmer Sprite Card" };
            mesh.vertices = new[]
            {
                new Vector3(-halfWidth, 0, 0), new Vector3(-halfWidth, height, 0),
                new Vector3(halfWidth, height, 0), new Vector3(halfWidth, 0, 0),
            };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }

        // A drawing has no legs to swing, so the walk is a hop and a rock around the feet.
        public void Animate(float progress)
        {
            float phase = progress * Mathf.PI * 2f * StepsPerCrossing;
            transform.localPosition = Vector3.up * Mathf.Abs(Mathf.Sin(phase)) * BobHeight;
            transform.localRotation = Quaternion.Euler(0, 0, Mathf.Cos(phase) * SwayDegrees);
        }

        void OnDestroy()
        {
            if (card != null) Destroy(card);
            if (material != null) Destroy(material);
        }
    }
}
