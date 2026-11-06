using Fortal.UWB;
using UnityEngine;

namespace Fortal.Players
{
    // Moves one player on the floor plane (world XZ) from its UWB tag position.
    // Game rules belong in separate components on the player prefab.
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public sealed class UWBPlayerController : MonoBehaviour
    {
        // Far beyond any play area; a reading further out than this is garbage.
        private const float FarthestSensibleMetres = 1000f;

        // No UWB tag yet (a player waiting for UWBPlayerSpawner to give it the next tag that connects).
        public const int NoTag = -1;

        [Header("Identity")]
        [SerializeField, Min(0)] private int playerId = 1;
        [SerializeField, Min(0)] private int tagId = 1;

        [Header("UWB")]
        [SerializeField] private UWBManager uwbManager;
        [SerializeField] private bool hideWhenOffline;

        [SerializeField, Min(0f)] private float floorHeight = 0.12f;

        [Header("Player Size")]
        [Tooltip("Uniform scale applied to the Player root, including its marker and collider.")]
        [SerializeField, Min(0.05f)] private float playerScale = 1f;

        [Header("Editor Simulation")]
        [Tooltip("During Play Mode, dragging this Player in Scene View moves its standalone position, or writes it back to the simulated UWB tag when using UWB Simulation.")]
        [SerializeField] private bool allowSceneViewDragInSimulation = true;
        [SerializeField, Min(0.0001f)] private float sceneViewDragThreshold = 0.002f;

        [Header("Visual")]
        [SerializeField] private GameObject marker;

        [Header("Runtime (Read Only)")]
        [SerializeField] private bool isTracking;
        [SerializeField] private float sampleAgeSeconds = 999f;

        private Rigidbody body;
        private bool isRegistered;
        private bool useUwbTracking = true;
        private bool hasControllerPosition;
        private Vector3 lastControllerPosition;
        private Vector3 baseLocalScale = Vector3.one;
        private bool baseScaleCaptured;

        public int PlayerId => playerId;
        public int TagId => tagId;
        public bool IsTracking => isTracking;
        public float SampleAgeSeconds => sampleAgeSeconds;

        private void Awake()
        {
            if (body == null) body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            ApplyPlayerScale();
            RecordControllerPosition(transform.position);
        }

        private void OnEnable()
        {
            FindAndRegisterManager();
        }

        private void OnDisable()
        {
            UnregisterManager();
        }

        private void Update()
        {
            if (!useUwbTracking)
            {
                CaptureStandaloneSimulationDrag();
                isTracking = false;
                sampleAgeSeconds = 0f;
                SetMarkerVisible(true);
                return;
            }

            if (!isRegistered)
            {
                FindAndRegisterManager();
            }

            if (CaptureSceneViewSimulationDrag())
            {
                return;
            }

            if (uwbManager == null ||
                !uwbManager.TryGetArenaPosition2D(tagId, out Vector2 measuredPosition2D, out sampleAgeSeconds))
            {
                isTracking = false;
                SetMarkerVisible(!hideWhenOffline);
                return;
            }

            isTracking = true;
            SetMarkerVisible(true);

            // A garbled reading (a corrupt serial frame, or trilateration from almost-in-line
            // anchors) can be NaN, infinite or kilometres away; the player stays where it was
            // instead of taking everything that follows players along with it.
            if (!IsSensible(measuredPosition2D))
            {
                return;
            }

            Vector3 measuredPosition = new Vector3(measuredPosition2D.x, floorHeight, measuredPosition2D.y);

            // UWBManager already applies the smoothing configured in UWBConfig.json.
            body.position = measuredPosition;
            RecordControllerPosition(measuredPosition);
        }

        // Finite and within FarthestSensibleMetres of the arena's origin on both axes.
        private static bool IsSensible(Vector2 position)
        {
            return float.IsFinite(position.x) && float.IsFinite(position.y) &&
                   Mathf.Abs(position.x) < FarthestSensibleMetres && Mathf.Abs(position.y) < FarthestSensibleMetres;
        }

        // Applies the player identity and UWB tag.
        public void Configure(int newPlayerId, int newTagId)
        {
            if (body == null) body = GetComponent<Rigidbody>();
            playerId = Mathf.Max(1, newPlayerId);
            SetTagId(newTagId);
            NameAfterTag();
        }

        public void SetTagId(int newTagId)
        {
            newTagId = newTagId < 0 ? NoTag : newTagId;
            if (tagId == newTagId)
            {
                return;
            }

            UnregisterManager();
            tagId = newTagId;
            NameAfterTag();
            FindAndRegisterManager();
        }

        private void NameAfterTag()
        {
            gameObject.name = tagId == NoTag ? $"Player{playerId:00}_NoTag" : $"Player{playerId:00}_Tag{tagId}";
        }

        // Chooses whether this player reads its position from UWB or stays where it is placed.
        public void SetUwbTrackingEnabled(bool enabled)
        {
            if (useUwbTracking == enabled)
            {
                return;
            }

            useUwbTracking = enabled;
            if (useUwbTracking)
            {
                FindAndRegisterManager();
                return;
            }

            UnregisterManager();
            isTracking = false;
            sampleAgeSeconds = 0f;
            SetMarkerVisible(true);
        }

        // Places the player on the floor for standalone simulation. Vector2.y is world Z.
        public void SetStandaloneWorldPosition(Vector2 worldPosition)
        {
            Vector3 targetPosition = new Vector3(worldPosition.x, floorHeight, worldPosition.y);
            transform.position = targetPosition;
            if (body != null)
            {
                body.position = targetPosition;
            }

            RecordControllerPosition(targetPosition);
        }

        public void SetPlayerScale(float newScale)
        {
            playerScale = Mathf.Max(0.05f, newScale);
            ApplyPlayerScale();
        }

        private bool CaptureSceneViewSimulationDrag()
        {
#if UNITY_EDITOR
            if (uwbManager == null || !uwbManager.IsSimulationMode || !TryReadSceneViewDrag(out Vector3 committedPosition))
            {
                return false;
            }

            if (!uwbManager.SetSimulatedArenaPosition2D(tagId, new Vector2(committedPosition.x, committedPosition.z)))
            {
                return false;
            }

            body.position = committedPosition;
            RecordControllerPosition(committedPosition);
            return true;
#else
            return false;
#endif
        }

        private void CaptureStandaloneSimulationDrag()
        {
#if UNITY_EDITOR
            if (!TryReadSceneViewDrag(out Vector3 committedPosition))
            {
                return;
            }

            body.position = committedPosition;
            RecordControllerPosition(committedPosition);
#endif
        }

        // Detects a Scene View drag since the last position this controller wrote.
        private bool TryReadSceneViewDrag(out Vector3 committedPosition)
        {
            committedPosition = default;
            if (!allowSceneViewDragInSimulation || !Application.isPlaying || body == null || !hasControllerPosition)
            {
                return false;
            }

            Vector3 currentPosition = transform.position;
            Vector2 planarDelta = new Vector2(
                currentPosition.x - lastControllerPosition.x,
                currentPosition.z - lastControllerPosition.z);
            if (planarDelta.sqrMagnitude <= sceneViewDragThreshold * sceneViewDragThreshold)
            {
                return false;
            }

            committedPosition = new Vector3(currentPosition.x, floorHeight, currentPosition.z);
            return true;
        }

        private void RecordControllerPosition(Vector3 position)
        {
            lastControllerPosition = position;
            hasControllerPosition = true;
            transform.hasChanged = false;
        }

        private void ApplyPlayerScale()
        {
            if (!baseScaleCaptured)
            {
                baseLocalScale = transform.localScale;
                baseScaleCaptured = true;
            }

            transform.localScale = baseLocalScale * Mathf.Max(0.05f, playerScale);
        }

        private void SetMarkerVisible(bool visible)
        {
            if (marker != null && marker.activeSelf != visible)
            {
                marker.SetActive(visible);
            }
        }

        private void FindAndRegisterManager()
        {
            if (!useUwbTracking)
            {
                return;
            }

            if (uwbManager == null)
            {
                uwbManager = FindAnyObjectByType<UWBManager>();
            }

            if (uwbManager == null || isRegistered || tagId == NoTag)
            {
                return;
            }

            uwbManager.RegisterTag(tagId);
            isRegistered = true;
        }

        private void UnregisterManager()
        {
            if (uwbManager != null && isRegistered)
            {
                uwbManager.UnregisterTag(tagId);
            }

            isRegistered = false;
        }
    }
}
