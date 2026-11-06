using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fortal.Editor
{
    // One-time cleanup: the parade now uses drawn farmers, so this takes the old farmer and animal
    // models out of the open scene, saves it, deletes their files, then deletes itself.
    public static class MemoryFarmOldModelRemover
    {
        const string ScenePath = "Assets/Scenes/MemoryFarm.unity";
        const string PrefabFolder = "Assets/Prefabs/MemoryFarm/Models";
        const string ModelFolder = "Assets/Models";
        const string SelfPath = "Assets/Scripts/Editor/MemoryFarmOldModelRemover.cs";

        [MenuItem("Tools/Memory Farm/Remove Old Parade Models")]
        static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[Memory Farm] Stop Play mode first, then run Remove Old Parade Models again.");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var oldModels = new List<GameObject>();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                    if (IsOldModel(transform.gameObject)) oldModels.Add(transform.gameObject);
            foreach (var model in oldModels) Object.DestroyImmediate(model);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
            {
                Debug.LogError("[Memory Farm] Could not save the scene, so no model files were deleted.");
                return;
            }

            AssetDatabase.DeleteAsset(PrefabFolder);
            AssetDatabase.DeleteAsset(ModelFolder);
            Debug.Log("[Memory Farm] Removed " + oldModels.Count + " old models from the scene and deleted their files.");
            AssetDatabase.DeleteAsset(SelfPath);
        }

        static bool IsOldModel(GameObject candidate)
        {
            if (!PrefabUtility.IsOutermostPrefabInstanceRoot(candidate)) return false;
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(candidate);
            return path.StartsWith(PrefabFolder + "/");
        }
    }
}
