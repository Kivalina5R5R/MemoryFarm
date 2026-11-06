using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Deepspace
{
    // Draws the walking farmer onto the wall: a far-away camera renders the sprite into a see-through picture the size of the
    // whole wall, so a place on FarmerWalkPath is the same place on the wall picture. The farmer's shadow lies under that
    // picture and the wall's foreground plants over it.
    public sealed class MemoryFarmModelStage : MonoBehaviour
    {
        const int TextureWidth = 4096;
        const float WallPictureWidth = 8192f, WallPictureHeight = 2160f;
        const float ShadowWidth = .62f, ShadowHeight = .1f, ShadowAlpha = .38f;
        const float DoorwayShade = .25f;
        const string ForegroundArt = "MemoryFarm/Background/BGWall_Foreground";

        static Texture2D shadowArt;

        Camera camera;
        RenderTexture texture;
        RawImage display, shadow;
        Texture previous;
        Material previousMaterial, silhouette;
        GameObject studio, pair;
        FarmerWalkPath path;
        float progress, wallHeight;
        MemoryFarmFarmerSprite spriteRig;

        public void Show(string animalName, bool female, Color shirt, RectTransform window, FarmerWalkPath walkPath)
        {
            Clear();
            if (window == null) return;
            display = window.GetComponent<RawImage>();
            if (display == null) return;
            if (studio == null) BuildStudio(window);
            path = walkPath;
            pair = new GameObject("Farmer Carrying Animal");
            pair.transform.SetParent(studio.transform, false);
            spriteRig = MemoryFarmFarmerSprite.TryCreate(animalName, female, shirt, pair.transform);
            if (spriteRig == null) return;
            progress = 0f;
            Render();
        }

        void BuildStudio(RectTransform window)
        {
            // The picture fills the wall, so its fractions are the wall's.
            if (window.parent is RectTransform panel) UiThemeKit.Stretch(panel, 0);
            UiThemeKit.Stretch(window, 0);
            wallHeight = window.rect.height;

            previous = display.texture;
            previousMaterial = display.material;
            var silhouetteShader = Resources.Load<Shader>("MemoryFarmSilhouette");
            if (silhouetteShader != null)
            {
                silhouette = new Material(silhouetteShader) { name = "Farm Clean Silhouette" };
                display.material = silhouette;
            }

            studio = new GameObject("Memory Farm Model Studio") { hideFlags = HideFlags.DontSave };
            studio.transform.position = new Vector3(20000, 20000, 20000);
            var cameraObject = new GameObject("Full Body Camera");
            cameraObject.transform.SetParent(studio.transform, false);
            cameraObject.transform.localPosition = new Vector3(0, 0, -10f);
            camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 1f;
            camera.nearClipPlane = .01f;
            camera.farClipPlane = 30f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.cullingMask = 1 << Visual3DFollower.WallLayer;
            var data = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderShadows = false;
            data.renderPostProcessing = false;

            int height = Mathf.Clamp(Mathf.RoundToInt(TextureWidth * wallHeight / Mathf.Max(1f, window.rect.width)), 256, 2160);
            texture = new RenderTexture(TextureWidth, height, 24, RenderTextureFormat.ARGB32) { name = "Memory Farm Full Body Window", antiAliasing = 1 };
            texture.Create();
            camera.targetTexture = texture;
            camera.aspect = (float)TextureWidth / height;
            display.texture = texture;

            shadow = BuildShadow(window);
            BuildForeground(window);
        }

        static RawImage BuildShadow(RectTransform window)
        {
            var rect = UiThemeKit.Child((RectTransform)window.parent, "Farmer Shadow");
            rect.SetSiblingIndex(window.GetSiblingIndex());
            if (!rect.TryGetComponent(out RawImage image)) image = rect.gameObject.AddComponent<RawImage>();
            image.texture = ShadowArt();
            image.raycastTarget = false;
            rect.gameObject.SetActive(false);
            return image;
        }

        // The plants at the wall's bottom left, cut from the wall picture, so the farmer walks behind them.
        static void BuildForeground(RectTransform window)
        {
            var art = Resources.Load<Texture2D>(ForegroundArt);
            if (art == null) return;
            var rect = UiThemeKit.Child((RectTransform)window.parent, "Wall Foreground");
            rect.SetAsLastSibling();
            UiThemeKit.Place(rect, 0f, 0f, art.width / WallPictureWidth, art.height / WallPictureHeight);
            if (!rect.TryGetComponent(out RawImage image)) image = rect.gameObject.AddComponent<RawImage>();
            image.texture = art;
            image.raycastTarget = false;
        }

        // A soft dark oval, made in code.
        static Texture2D ShadowArt()
        {
            if (shadowArt != null) return shadowArt;
            const int size = 128;
            shadowArt = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Farmer Shadow" };
            var pixels = new Color32[size * size];
            float half = size / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = new Vector2(x + .5f - half, y + .5f - half).magnitude / half;
                    float alpha = Mathf.Clamp01((1f - distance) / .55f);
                    pixels[y * size + x] = new Color32(30, 22, 14, (byte)(alpha * alpha * 255));
                }
            }
            shadowArt.SetPixels32(pixels);
            shadowArt.Apply();
            return shadowArt;
        }

        public void SetProgress(float t)
        {
            progress = Mathf.Clamp01(t);
        }

        void LateUpdate()
        {
            if (pair != null) Render();
        }

        void Render()
        {
            if (camera == null || pair == null || spriteRig == null || path == null) return;
            Vector2 foot = path.FootAt(progress);
            float height = path.HeightAt(progress);
            float emerged = path.EmergedAt(progress);

            // The camera sees two units of height, so a wall fraction is twice that in the studio. The art faces right
            // and the walk heads left, so the farmer is mirrored.
            float scale = height * 2f / MemoryFarmFarmerSprite.CardHeight;
            pair.transform.localScale = new Vector3(-scale, scale, 1f);
            pair.transform.localPosition = new Vector3((foot.x - .5f) * 2f * camera.aspect, (foot.y - .5f) * 2f, 0f);
            spriteRig.Animate(progress);

            float shade = Mathf.Lerp(DoorwayShade, 1f, emerged);
            display.color = new Color(shade, shade, shade, emerged);
            PlaceShadow(foot, height, emerged);

            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = texture };
            RenderPipeline.SubmitRenderRequest(camera, request);
        }

        void PlaceShadow(Vector2 foot, float height, float emerged)
        {
            if (shadow == null) return;
            var rect = shadow.rectTransform;
            if (!rect.gameObject.activeSelf) rect.gameObject.SetActive(true);
            rect.anchorMin = rect.anchorMax = foot;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(ShadowWidth * height, ShadowHeight * height) * wallHeight;
            shadow.color = new Color(1f, 1f, 1f, ShadowAlpha * emerged);
        }

        public void Clear()
        {
            spriteRig = null;
            if (shadow != null) shadow.gameObject.SetActive(false);
            if (pair != null)
            {
                pair.SetActive(false);
                Destroy(pair);
                pair = null;
            }
            if (texture == null) return;
            var old = RenderTexture.active;
            RenderTexture.active = texture;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = old;
        }

        void OnDestroy()
        {
            if (display != null && display.texture == texture) display.texture = previous;
            if (display != null && display.material == silhouette) display.material = previousMaterial;
            if (silhouette != null) Destroy(silhouette);
            if (studio != null) Destroy(studio);
            if (texture != null)
            {
                texture.Release();
                Destroy(texture);
            }
        }
    }
}
