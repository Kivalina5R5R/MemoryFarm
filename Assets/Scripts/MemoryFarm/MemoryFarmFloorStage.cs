using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Deepspace
{
    // The floor minigames' layer. Picture cards (ART/Minigame, listed in Resources/MemoryFarm/MinigameArt) stand in a
    // far-away studio and one tilted orthographic camera renders them onto a clear RawImage stretched over the floor, between
    // the meadow and the player cursors. Ground point (x, y) in floor metres sits at studio (x, 0, y / sin tilt), so a card's
    // bottom edge lands exactly where the floor rule puts it. Each card faces the camera, so the picture is never skewed.
    public sealed class MemoryFarmFloorStage : MonoBehaviour
    {
        const float Tilt = 52f;
        const string CatalogPath = "MemoryFarm/MinigameArt", ShaderName = "MemoryFarmFarmerSprite";
        static readonly Vector3 Origin = new Vector3(-20000f, 20000f, -20000f);

        readonly Dictionary<(string, Color), Material> materials = new();
        Camera cam;
        RenderTexture texture;
        RawImage view;
        Transform studio;
        Mesh card;
        ArtCatalog catalog;
        Vector2 meters;
        bool open;

        public void Open(RectTransform floor, Vector2 floorMeters)
        {
            meters = floorMeters;
            if (studio == null)
            {
                studio = new GameObject("Memory Farm Floor Studio") { hideFlags = HideFlags.DontSave }.transform;
                studio.position = Origin;
                var camObject = new GameObject("Floor Minigame Camera");
                camObject.transform.SetParent(studio, false);
                cam = camObject.AddComponent<Camera>();
                cam.enabled = false;
                cam.orthographic = true;
                cam.nearClipPlane = .1f;
                cam.farClipPlane = 60f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.clear;
                cam.cullingMask = 1 << Visual3DFollower.WallLayer;
                var data = camObject.AddComponent<UniversalAdditionalCameraData>();
                data.renderShadows = false;
                data.renderPostProcessing = false;
                int width = 1408, height = Mathf.Clamp(Mathf.RoundToInt(width * meters.y / Mathf.Max(.1f, meters.x)), 256, 1408);
                texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { name = "Memory Farm Floor Minigame", antiAliasing = 1 };
                texture.Create();
                cam.targetTexture = texture;
                cam.aspect = (float)width / height;
            }
            float sin = Mathf.Sin(Tilt * Mathf.Deg2Rad);
            cam.orthographicSize = meters.y * .5f;
            cam.transform.rotation = Quaternion.Euler(Tilt, 0, 0);
            var centre = Origin + new Vector3(meters.x * .5f, 0, meters.y * .5f / sin);
            cam.transform.position = centre - cam.transform.forward * 30f;
            if (view == null)
            {
                var rect = UiThemeKit.Child(floor, "Minigame Floor Stage");
                UiThemeKit.Stretch(rect, 0);
                if (!rect.TryGetComponent(out view)) view = rect.gameObject.AddComponent<RawImage>();
                view.raycastTarget = false;
                view.texture = texture;
                view.color = Color.white;
            }
            // Just above the meadow, under the answer signs, cursors and juice.
            var meadow = floor.Find("Meadow");
            view.transform.SetSiblingIndex(meadow != null ? meadow.GetSiblingIndex() + 1 : 0);
            view.gameObject.SetActive(true);
            open = true;
        }

        public void Close()
        {
            open = false;
            if (view != null) view.gameObject.SetActive(false);
            if (studio == null) return;
            for (int i = studio.childCount - 1; i >= 0; i--)
            {
                var child = studio.GetChild(i);
                if (cam == null || child != cam.transform) Destroy(child.gameObject);
            }
        }

        // A holder the game moves, with the named picture on it. height is in floor metres; tint multiplies the picture.
        public Transform Spawn(string pictureName, float height, Color? tint = null)
        {
            if (studio == null) return null;
            if (catalog == null) catalog = Resources.Load<ArtCatalog>(CatalogPath);
            var picture = catalog != null ? catalog.Find(pictureName) : null;
            var material = picture != null ? MaterialFor(picture, tint ?? Color.white) : null;
            if (material == null) return null;

            var holder = new GameObject(pictureName).transform;
            holder.gameObject.layer = Visual3DFollower.WallLayer;
            holder.SetParent(studio, false);
            var go = new GameObject("Picture", typeof(MeshFilter), typeof(MeshRenderer));
            go.layer = Visual3DFollower.WallLayer;
            go.transform.SetParent(holder, false);
            go.transform.localScale = new Vector3(height * picture.width / picture.height, height, 1f);
            go.GetComponent<MeshFilter>().sharedMesh = Card();
            var meshRenderer = go.GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            return holder;
        }

        public void Place(Transform holder, Vector2 floorPoint, float lift = 0, float roll = 0)
        {
            if (holder == null) return;
            float sin = Mathf.Sin(Tilt * Mathf.Deg2Rad);
            holder.position = Origin + new Vector3(floorPoint.x, lift, floorPoint.y / sin);
            holder.rotation = cam.transform.rotation * Quaternion.Euler(0, 0, roll);
        }

        // The pictures face right; this mirrors one to face left.
        public static void FaceLeft(Transform holder, bool left)
        {
            if (holder == null || holder.childCount == 0) return;
            var picture = holder.GetChild(0);
            var scale = picture.localScale;
            scale.x = Mathf.Abs(scale.x) * (left ? -1f : 1f);
            picture.localScale = scale;
        }

        Material MaterialFor(Texture2D picture, Color tint)
        {
            var key = (picture.name, tint);
            if (materials.TryGetValue(key, out var existing)) return existing;
            var shader = Resources.Load<Shader>(ShaderName);
            if (shader == null) return null;
            // The farmer shader tints where its mask is white; an all-white mask tints the whole picture.
            var material = new Material(shader) { name = "Floor " + picture.name };
            material.SetTexture("_MainTex", picture);
            material.SetTexture("_ShirtMask", Texture2D.whiteTexture);
            material.SetColor("_ShirtColor", tint);
            materials.Add(key, material);
            return material;
        }

        // One unit square standing on its bottom edge.
        Mesh Card()
        {
            if (card != null) return card;
            card = new Mesh { name = "Floor Picture Card" };
            card.vertices = new[] { new Vector3(-.5f, 0, 0), new Vector3(-.5f, 1, 0), new Vector3(.5f, 1, 0), new Vector3(.5f, 0, 0) };
            card.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            card.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            card.RecalculateBounds();
            return card;
        }

        void LateUpdate()
        {
            if (!open || cam == null || texture == null) return;
            RenderPipeline.SubmitRenderRequest(cam, new UniversalRenderPipeline.SingleCameraRequest { destination = texture });
        }

        void OnDestroy()
        {
            if (view != null && view.texture == texture) view.texture = null;
            if (studio != null) Destroy(studio.gameObject);
            if (texture != null)
            {
                texture.Release();
                Destroy(texture);
            }
            if (card != null) Destroy(card);
            foreach (var material in materials.Values) Destroy(material);
        }
    }
}
