using Deepspace;
using UnityEditor;
using UnityEngine;

namespace Fortal.Editor
{
    // Preview buttons, to see the random floor pieces while editing the prefab. What they add is never saved.
    [CustomEditor(typeof(FloorScatter))]
    public sealed class FloorScatterInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var scatter = (FloorScatter)target;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Preview")) scatter.Scatter();
                if (GUILayout.Button("Clear Preview")) scatter.Clear();
            }

            EditorGUILayout.Space();
            DrawDefaultInspector();
        }
    }
}
