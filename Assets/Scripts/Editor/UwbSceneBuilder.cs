using Fortal.Configuration;
using Fortal.Display;
using Fortal.Players;
using Fortal.UWB;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Fortal.Editor
{
    // Builds the UWB installation layout into the MemoryFarm scene: systems, floor and wall outputs,
    // the floor prop scatter (no art yet) and the player spawner. Taken from Bounty_Hunter's
    // UwbPrototypeSceneBuilder without its retired kiosk. The floor is 11 x 5.26 world units seen from
    // above, with an 8192 x 2160 wall canvas standing along its back edge; both are plain black for now.
    // It only adds what is missing, so it is safe to run again from the Tools menu.
    [InitializeOnLoad]
    public static class UwbSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/MemoryFarm.unity";
        private const string PlayerPrefabPath = "Assets/Prefabs/Player/UWBPlayer.prefab";
        // Raised to rebuild a scene made by an older version of this builder.
        private const string AutoBuildKey = "MemoryFarm.UwbSceneBuilt.BlackDisplays";

        // The floor is 11 world units wide; its depth follows the 2944 x 1408 LED floor output.
        private const float ArenaWidth = 11f;
        private const float FloorCameraSize = 2.6304348f;
        private const float WallCameraSize = 1.5576172f;
        // The wall canvas works in the wall output's own pixels and exactly fills what the wall camera sees.
        private static readonly Vector2 WallCanvasSize = new Vector2(8192f, 2160f);
        private static readonly float WallCanvasScale = WallCameraSize * 2f / WallCanvasSize.y;
        private const float WallCanvasDepth = 2.52f;
        private static readonly Vector2 FloorCanvasSize = new Vector2(2944f, 1408f);

        static UwbSceneBuilder()
        {
            // Builds the scene once after this script is first imported.
            EditorApplication.delayCall += () =>
            {
                Scene scene = SceneManager.GetActiveScene();
                if (!scene.isLoaded || scene.path != ScenePath || EditorApplication.isPlayingOrWillChangePlaymode) return;
                if (EditorPrefs.GetBool(AutoBuildKey, false)) return;

                EditorPrefs.SetBool(AutoBuildKey, true);
                bool wasDirty = scene.isDirty;
                Build();
                if (!wasDirty) EditorSceneManager.SaveScene(scene);
            };
        }

        [MenuItem("Tools/UWB/Build Scene Layout")]
        public static void Build()
        {
            var playerPrefab = AssetDatabase.LoadAssetAtPath<UWBPlayerController>(PlayerPrefabPath);
            if (playerPrefab == null)
            {
                Debug.LogError($"[UwbSceneBuilder] Player prefab missing at {PlayerPrefabPath}");
                return;
            }

            // UWB reads and both outputs must keep running when another window has focus.
            PlayerSettings.runInBackground = true;
            NameDisplayLayers();

            RemoveDefaultMainCamera();
            UWBManager uwbManager = BuildSystems();
            BuildDisplays(out Camera wallCamera, out Camera floorCamera);
            Canvas wallCanvas = BuildMap(wallCamera, floorCamera);
            BuildPlayers(playerPrefab);
            BuildEventSystem();

            var displayData = new SerializedObject(Object.FindFirstObjectByType<DisplayManager>(FindObjectsInactive.Include));
            displayData.FindProperty("sideCamera").objectReferenceValue = wallCamera;
            displayData.FindProperty("floorCamera").objectReferenceValue = floorCamera;
            displayData.FindProperty("sideDisplayBackground").colorValue = Color.black;
            SerializedProperty sideCanvases = displayData.FindProperty("sideCanvases");
            sideCanvases.arraySize = 1;
            sideCanvases.GetArrayElementAtIndex(0).objectReferenceValue = wallCanvas;
            displayData.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[UwbSceneBuilder] Scene layout ready (UWB {uwbManager.name}). Game view: Display 1 floor, Display 2 wall.");
        }

        private static UWBManager BuildSystems()
        {
            GameObject systems = FindOrCreateRoot("Systems");
            GetOrAdd<UWBConfigManager>(systems);
            UWBManager manager = systems.GetComponent<UWBManager>();
            if (manager != null) return manager;

            manager = systems.AddComponent<UWBManager>();
            // Start in Simulation so the scene runs without hardware; switch to Serial on site.
            var data = new SerializedObject(manager);
            data.FindProperty("transportMode").enumValueIndex = (int)UWBTransportMode.Simulation;
            data.ApplyModifiedPropertiesWithoutUndo();
            return manager;
        }

        private static void BuildDisplays(out Camera wallCamera, out Camera floorCamera)
        {
            GameObject display = FindOrCreateRoot("Display");
            GetOrAdd<DisplayManager>(display);

            Transform wallRoot = FindOrCreateChild(display.transform, "Display2_Wall");
            wallCamera = FindOrCreateCamera(wallRoot, "SideCamera", out bool wallCreated);
            if (wallCreated)
            {
                wallCamera.transform.localPosition = new Vector3(0f, WallCameraSize, WallCanvasDepth - 10f);
                wallCamera.orthographic = true;
                wallCamera.orthographicSize = WallCameraSize;
                wallCamera.clearFlags = CameraClearFlags.SolidColor;
                wallCamera.targetDisplay = DisplayOutput.WallDisplayIndex;
            }
            wallCamera.backgroundColor = Color.black;

            Transform floorRoot = FindOrCreateChild(display.transform, "Display1_Floor");
            floorCamera = FindOrCreateCamera(floorRoot, "FloorCamera", out bool floorCreated);
            if (floorCreated)
            {
                floorCamera.transform.localPosition = new Vector3(0f, 10f, 0f);
                floorCamera.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                floorCamera.orthographic = true;
                floorCamera.orthographicSize = FloorCameraSize;
                floorCamera.clearFlags = CameraClearFlags.SolidColor;
                floorCamera.backgroundColor = Color.black;
                floorCamera.targetDisplay = DisplayOutput.FloorDisplayIndex;
                floorCamera.gameObject.AddComponent<AudioListener>();
            }
        }

        private static Canvas BuildMap(Camera wallCamera, Camera floorCamera)
        {
            GameObject map = FindOrCreateRoot("Map");
            Transform arena = FindOrCreateChild(map.transform, "Arena");
            BuildFloorDisplay(arena, floorCamera);
            BuildProps(arena);

            Transform existing = map.transform.Find("SideDisplay");
            Canvas wall = existing != null ? existing.GetComponent<Canvas>() : CreateWallCanvas(map.transform, wallCamera);
            FitWallCanvas((RectTransform)wall.transform);
            BuildBlackBackground(wall.transform);
            return wall;
        }

        // World-space wall canvas standing upright along the back edge of the floor.
        private static Canvas CreateWallCanvas(Transform map, Camera wallCamera)
        {
            var canvasObject = new GameObject("SideDisplay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(map, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = wallCamera;
            canvas.targetDisplay = DisplayOutput.WallDisplayIndex;
            canvasObject.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 16f;
            return canvas;
        }

        private static void FitWallCanvas(RectTransform rect)
        {
            rect.sizeDelta = WallCanvasSize;
            rect.localScale = Vector3.one * WallCanvasScale;
            rect.localPosition = new Vector3(0f, WallCameraSize, WallCanvasDepth);
        }

        // World-space floor canvas lying flat over the arena, seen by the floor camera.
        private static void BuildFloorDisplay(Transform arena, Camera floorCamera)
        {
            Transform floor = arena.Find("FloorDisplay");
            if (floor == null)
            {
                var canvasObject = new GameObject("FloorDisplay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvasObject.transform.SetParent(arena, false);
                canvasObject.transform.SetAsFirstSibling();
                var canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = floorCamera;
                canvas.targetDisplay = DisplayOutput.FloorDisplayIndex;
                var rect = (RectTransform)canvasObject.transform;
                rect.sizeDelta = FloorCanvasSize;
                rect.localScale = Vector3.one * (ArenaWidth / FloorCanvasSize.x);
                rect.localRotation = Quaternion.Euler(90f, 0f, 0f);
                rect.localPosition = Vector3.zero;
                floor = canvasObject.transform;
            }

            BuildBlackBackground(floor);
        }

        // Fills a canvas with plain black, replacing a picture background from an older build. A
        // background that is already a plain Image is left alone.
        private static void BuildBlackBackground(Transform canvas)
        {
            Transform existing = canvas.Find("Background");
            if (existing != null)
            {
                if (existing.GetComponent<RawImage>() == null) return;
                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
            background.layer = canvas.gameObject.layer;
            background.transform.SetParent(canvas, false);
            background.transform.SetAsFirstSibling();
            var rect = (RectTransform)background.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;

            var image = background.GetComponent<Image>();
            image.color = Color.black;
            image.raycastTarget = false;
        }

        // Random floor props, spawned by BushScatter when Play starts. It lays nothing until sprites are
        // assigned to it; entries whose art has been deleted are dropped here.
        private static void BuildProps(Transform arena)
        {
            Transform existing = arena.Find("Bushes");
            if (existing == null)
            {
                existing = new GameObject("Bushes", typeof(BushScatter)).transform;
                existing.SetParent(arena, false);
            }

            var data = new SerializedObject(existing.GetComponent<BushScatter>());
            SerializedProperty array = data.FindProperty("bushSprites");
            var sprites = Enumerable.Range(0, array.arraySize)
                .Select(i => array.GetArrayElementAtIndex(i).objectReferenceValue)
                .Where(sprite => sprite != null)
                .ToArray();
            array.arraySize = sprites.Length;
            for (int i = 0; i < sprites.Length; i++)
            {
                array.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
            }
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildPlayers(UWBPlayerController playerPrefab)
        {
            GameObject players = FindOrCreateRoot("Players");
            if (players.GetComponent<UWBPlayerSpawner>() != null) return;

            UWBPlayerSpawner spawner = players.AddComponent<UWBPlayerSpawner>();
            var data = new SerializedObject(spawner);
            data.FindProperty("playerPrefab").objectReferenceValue = playerPrefab;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include) != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        // The game renders each output's effects on its own layer; these are the numbers it uses.
        private static void NameDisplayLayers()
        {
            var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tags.FindProperty("layers");
            layers.GetArrayElementAtIndex(Deepspace.Visual3DFollower.WallLayer).stringValue = "WallLayer";
            layers.GetArrayElementAtIndex(Deepspace.Visual3DFollower.FloorLayer).stringValue = "FloorLayer";
            tags.ApplyModifiedPropertiesWithoutUndo();
        }

        // The template's Main Camera would render scene objects over the floor output.
        private static void RemoveDefaultMainCamera()
        {
            GameObject mainCamera = GameObject.Find("Main Camera");
            if (mainCamera != null && mainCamera.transform.parent == null)
            {
                Undo.DestroyObjectImmediate(mainCamera);
            }
        }

        private static Camera FindOrCreateCamera(Transform parent, string name, out bool created)
        {
            Transform existing = parent.Find(name);
            created = existing == null;
            if (!created) return existing.GetComponent<Camera>();

            var cameraObject = new GameObject(name, typeof(Camera));
            cameraObject.transform.SetParent(parent, false);
            return cameraObject.GetComponent<Camera>();
        }

        private static GameObject FindOrCreateRoot(string name)
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name == name) return root;
            }

            return new GameObject(name);
        }

        private static Transform FindOrCreateChild(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null) return existing;

            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static T GetOrAdd<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }
    }
}
