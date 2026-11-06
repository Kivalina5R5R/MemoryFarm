using System;
using UnityEngine;

namespace Deepspace
{
    // The farmers' walk on the wall picture: out of the barn door, down the dirt road and off the left edge, growing as they
    // come closer. Points are wall fractions (0-1, y = 0 at the bottom) and mark where the feet are; heights are fractions of
    // the wall's height.
    [Serializable]
    public sealed class FarmerWalkPath
    {
        [Tooltip("Where the feet start: the barn doorway.")]
        [SerializeField] private Vector2 start = new(.59f, .17f);
        [Tooltip("Pulls the curve of the road towards this point.")]
        [SerializeField] private Vector2 bend = new(.45f, .12f);
        [Tooltip("Where the feet end: past the left edge of the wall.")]
        [SerializeField] private Vector2 end = new(-.08f, .06f);
        [Tooltip("Farmer's height in the doorway.")]
        [SerializeField, Range(.02f, 1f)] private float startHeight = .15f;
        [Tooltip("Farmer's height at the end of the walk.")]
        [SerializeField, Range(.02f, 1f)] private float endHeight = .72f;
        [Tooltip("How much of the walk the farmer takes to appear out of the dark doorway.")]
        [SerializeField, Range(.01f, .5f)] private float emergePart = .08f;

        public Vector2 FootAt(float t)
        {
            float rest = 1f - t;
            return rest * rest * start + 2f * rest * t * bend + t * t * end;
        }

        public float HeightAt(float t) => Mathf.Lerp(startHeight, endHeight, t);

        // 0 still in the dark of the doorway, 1 fully out.
        public float EmergedAt(float t) => Mathf.Clamp01(t / emergePart);
    }
}
