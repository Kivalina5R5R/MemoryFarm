using UnityEngine;
using UnityEngine.UI;

namespace Deepspace
{
    // Shows the painted floor picture full screen, cropped evenly to the floor's shape rather than stretched.
    public static class FarmPicture
    {
        private const string FloorPicture = "MemoryFarm/Background/BGFloor_MM";

        public static void ShowFloor(RectTransform target)
        {
            Texture2D picture = Resources.Load<Texture2D>(FloorPicture);
            if (!target.TryGetComponent(out RawImage image)) image = target.gameObject.AddComponent<RawImage>();
            image.texture = picture;
            image.color = Color.white;
            image.raycastTarget = false;
            if (picture == null) return;

            float screenAspect = FloorDisplaySettings.Width / (float)FloorDisplaySettings.Height;
            float pictureAspect = picture.width / (float)picture.height;
            float width = Mathf.Min(1f, screenAspect / pictureAspect);
            float height = Mathf.Min(1f, pictureAspect / screenAspect);
            image.uvRect = new Rect((1f - width) / 2f, (1f - height) / 2f, width, height);
        }
    }
}
