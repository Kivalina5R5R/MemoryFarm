using UnityEngine;

namespace Fortal.UWB
{
    // Attach to a follow-target GameObject. Registers itself with a UWBManager by
    // tagId and applies drift-free smoothing (low-pass filter + deadzone + SmoothDamp)
    // to the positions the manager resolves each frame.
    public sealed class UWBTracker : MonoBehaviour
    {
        [Header("UWB Tag")]
        [Tooltip("NoopLoop UWB tag ID this target follows.")]
        public int tagId = 1;

        [Header("Manager")]
        [Tooltip("Left empty, the tracker finds the first UWBManager in the scene.")]
        [SerializeField] private UWBManager manager;

        [Header("Smoothing")]
        [Tooltip("SmoothDamp time (higher = smoother but slower response)")]
        [Range(0.05f, 0.5f)] public float smoothTime = 0.15f;
        [Tooltip("Ignore movement smaller than this many meters (prevents jitter when standing still)")]
        [Range(0.01f, 0.2f)] public float deadzone = 0.03f;
        [Tooltip("Low-pass filter strength (0 = no filter, 0.95 = very smooth). Filters raw UWB noise.")]
        [Range(0f, 0.95f)] public float filterStrength = 0.6f;
        [Tooltip("If position jumps more than this many meters, snap instantly instead of smoothing")]
        [Range(0.5f, 5f)] public float snapDistance = 2f;

        [Header("Status (Read Only)")]
        public bool isTracking;
        public float ageSeconds = 999f;
        public Vector3 velocity;

        private Vector3 filteredPosition;
        private Vector3 targetPosition;
        private Vector3 smoothVelocity;
        private Vector3 lastAppliedPosition;
        private bool hasFirstData;

        private void OnEnable()
        {
            if (manager == null)
            {
                manager = FindAnyObjectByType<UWBManager>();
            }

            ApplyTrackingSettingsFromConfig();

            manager?.AddTag(this);
        }

        private void Start()
        {
            // Start runs after the scene config manager has initialized, even when this
            // tracker was enabled before the manager's Awake callback.
            ApplyTrackingSettingsFromConfig();
        }

        private void ApplyTrackingSettingsFromConfig()
        {
            Fortal.Configuration.UWBConfigData config = Fortal.Configuration.UWBConfigManager.GetConfig();
            if (config == null)
            {
                return;
            }

            Fortal.Configuration.UWBTrackingSettings tracking = config.tracking;
            if (tracking == null)
            {
                return;
            }

            tracking.Validate();
            smoothTime = tracking.trackerSmoothTime;
            deadzone = tracking.trackerDeadzoneMeters;
            filterStrength = tracking.trackerFilterStrength;
            snapDistance = tracking.trackerSnapDistanceMeters;
        }

        private void OnDisable()
        {
            manager?.RemoveTag(this);
        }

        // Changes tagId and re-registers with the manager under the new key.
        public void SetTagId(int newTagId)
        {
            if (tagId == newTagId)
            {
                return;
            }

            if (isActiveAndEnabled)
            {
                manager?.RemoveTag(this);
            }

            tagId = newTagId;

            if (isActiveAndEnabled)
            {
                if (manager == null)
                {
                    manager = FindAnyObjectByType<UWBManager>();
                }

                manager?.AddTag(this);
            }
        }

        private void Update()
        {
            if (manager == null)
            {
                manager = FindAnyObjectByType<UWBManager>();
                manager?.AddTag(this);
            }

            if (!hasFirstData || !isTracking || (manager != null && manager.UsesAdaptiveTracking))
            {
                return;
            }

            // SmoothDamp gives natural acceleration/deceleration (much better than Lerp).
            transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref smoothVelocity, smoothTime);
        }

        // Called by the owning UWBManager whenever it resolves a fresh or predicted position for tagId.
        public void ApplyTrackedPosition(Vector3 positionMeters, float sampleAgeSeconds)
        {
            isTracking = true;
            ageSeconds = sampleAgeSeconds;

            // The manager has already applied axis conversion, offset and world scale.
            if (manager != null && manager.UsesAdaptiveTracking)
            {
                velocity = hasFirstData
                    ? (positionMeters - lastAppliedPosition) / Mathf.Max(Time.unscaledDeltaTime, 0.0001f)
                    : Vector3.zero;
                filteredPosition = targetPosition = lastAppliedPosition = positionMeters;
                transform.position = positionMeters;
                smoothVelocity = Vector3.zero;
                hasFirstData = true;
                return;
            }

            if (!hasFirstData)
            {
                filteredPosition = positionMeters;
                targetPosition = positionMeters;
                transform.position = positionMeters;
                lastAppliedPosition = positionMeters;
                hasFirstData = true;
                return;
            }

            // Low-pass filter to smooth out raw UWB noise before the deadzone check.
            filteredPosition = Vector3.Lerp(positionMeters, filteredPosition, filterStrength);

            float distToTarget = Vector3.Distance(filteredPosition, targetPosition);
            if (distToTarget > snapDistance)
            {
                targetPosition = filteredPosition;
                smoothVelocity = Vector3.zero;
            }
            else if (distToTarget > deadzone)
            {
                targetPosition = filteredPosition;
            }
            // else: within deadzone, don't update target -> object stays still.

            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            velocity = (positionMeters - lastAppliedPosition) / dt;
            lastAppliedPosition = positionMeters;
        }

        // Called by the owning UWBManager when this tag's data has gone stale.
        public void SetOffline()
        {
            isTracking = false;
            hasFirstData = false;
            smoothVelocity = velocity = Vector3.zero;
        }
    }
}
