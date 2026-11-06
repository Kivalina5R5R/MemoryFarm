using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Deepspace
{
    // Rocks, flowers and leaves scattered at random over the floor picture when the game starts. They stay still. Sizes
    // and gaps are fractions of the floor height.
    public sealed class FloorScatter : MonoBehaviour
    {
        [Serializable]
        public sealed class Piece
        {
            public Texture2D Art;
            [Tooltip("The largest this picture may show, as a fraction of the floor height.")]
            public float Height = .1f;
        }

        [SerializeField] private Piece[] pieces;
        [Tooltip("The smallest a piece may show, as a share of its Height.")]
        [SerializeField, Range(.1f, 1f)] private float smallestSize = .8f;
        [SerializeField] private float lowestY = .02f, highestY = .85f;
        [Tooltip("Gap between pieces along the floor.")]
        [SerializeField] private float minGap = .02f, maxGap = .25f;

        // Also used by the Inspector's preview, outside Play.
        public void Scatter()
        {
            Clear();
            var usable = new List<Piece>();
            if (pieces != null)
            {
                foreach (Piece piece in pieces)
                {
                    if (piece.Art != null) usable.Add(piece);
                }
            }

            if (usable.Count == 0) return;

            Rect rect = ((RectTransform)transform).rect;
            Piece last = null;
            float x = 0f;
            while (x < rect.width)
            {
                int index = UnityEngine.Random.Range(0, usable.Count);
                if (usable.Count > 1 && usable[index] == last) index = (index + 1) % usable.Count;
                last = usable[index];
                float height = last.Height * UnityEngine.Random.Range(smallestSize, 1f) * rect.height;
                float y = UnityEngine.Random.Range(lowestY, highestY) * rect.height;
                x += Add(last.Art, height, x, y) + UnityEngine.Random.Range(minGap, maxGap) * rect.height;
            }
        }

        public void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
        }

        // Returns the piece's width.
        private float Add(Texture2D picture, float height, float x, float y)
        {
            var piece = new GameObject(picture.name, typeof(RectTransform), typeof(RawImage));
            piece.layer = gameObject.layer;
            // A preview outside Play must never be saved into the prefab.
            if (!Application.isPlaying) piece.hideFlags = HideFlags.DontSave;
            var rect = (RectTransform)piece.transform;
            rect.SetParent(transform, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            float width = height * picture.width / picture.height;
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, y);
            var image = piece.GetComponent<RawImage>();
            image.texture = picture;
            image.raycastTarget = false;
            return width;
        }
    }
}
