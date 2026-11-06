using UnityEngine;

namespace Fortal.Configuration
{

    [System.Serializable]
    public class UWBAxisConversion
    {
        [Tooltip("Where the tracker's raw X goes in Unity. Tokens: x, -x, y, -y, z, -z, none.")]
        public string rawXTo = "x";
        [Tooltip("Where the tracker's raw Y goes in Unity. Tokens: x, -x, y, -y, z, -z, none.")]
        public string rawYTo = "z";
        [Tooltip("Where the tracker's raw Z goes in Unity. Tokens: x, -x, y, -y, z, -z, none.")]
        public string rawZTo = "none";

        public Vector3 Apply(Vector3 raw)
        {
            Vector3 result = Vector3.zero;
            Accumulate(rawXTo, raw.x, ref result);
            Accumulate(rawYTo, raw.y, ref result);
            Accumulate(rawZTo, raw.z, ref result);
            return result;
        }

        public void Validate()
        {
            int[] destinationCounts = new int[3];
            CountDestination(rawXTo, nameof(rawXTo), destinationCounts);
            CountDestination(rawYTo, nameof(rawYTo), destinationCounts);
            CountDestination(rawZTo, nameof(rawZTo), destinationCounts);

            string[] axisNames = { "X", "Y", "Z" };
            for (int i = 0; i < destinationCounts.Length; i++)
            {
                if (destinationCounts[i] > 1)
                {
                    Debug.LogWarning($"[UWBAxisConversion] {destinationCounts[i]} raw axes map onto Unity {axisNames[i]}; their values are summed. Set one to \"none\" if that isn't intended.");
                }
            }
        }

        private static void Accumulate(string token, float value, ref Vector3 result)
        {
            if (TryParseToken(token, out int axisIndex, out float sign))
            {
                result[axisIndex] += sign * value;
            }
        }

        private static void CountDestination(string token, string fieldName, int[] destinationCounts)
        {
            if (TryParseToken(token, out int axisIndex, out _))
            {
                destinationCounts[axisIndex]++;
                return;
            }

            string axis = token?.Trim().ToLowerInvariant();
            bool intentionalDrop = string.IsNullOrEmpty(axis) || axis == "none" || axis == "0";
            if (!intentionalDrop)
            {
                Debug.LogWarning($"[UWBAxisConversion] {fieldName} has unknown token '{token}'; that axis is dropped.");
            }
        }

        private static bool TryParseToken(string token, out int axisIndex, out float sign)
        {
            axisIndex = -1;
            sign = 1f;

            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            string axis = token.Trim().ToLowerInvariant();
            if (axis == "none" || axis == "0")
            {
                return false;
            }

            if (axis[0] == '-')
            {
                sign = -1f;
                axis = axis.Substring(1);
            }
            else if (axis[0] == '+')
            {
                axis = axis.Substring(1);
            }

            switch (axis)
            {
                case "x":
                    axisIndex = 0;
                    return true;
                case "y":
                    axisIndex = 1;
                    return true;
                case "z":
                    axisIndex = 2;
                    return true;
                default:
                    return false;
            }
        }
    }

    [System.Serializable]
    public class UWBTrackingSettings
    {
        public float maxPoseAgeSeconds = 1f;
        public float maxTrackingPredictionSeconds = 0.35f;
        public bool rejectTrackingPositionJumps = true;
        public float maxTrackingPositionJumpMeters = 0.75f;
        public float maxTrackingPredictionSpeedMetersPerSecond = 2.5f;
        public float maxTrackingRecoveryStepMeters = 0.15f;
        public float maxUwbMotionSpeedMetersPerSecond = 1.8f;

        public bool smoothTagPosition = true;
        public float trackingPositionDeadZoneMeters = 0.08f;
        public bool averageUwbPositionFrames = true;
        public int uwbPositionAverageFrameCount = 4;
        public float movingLatestFrameBlend = 0.7f;
        public float stationaryPositionLerp = 0.25f;
        public float movingPositionLerp = 0.85f;

        public float trackerSmoothTime = 0.15f;
        public float trackerDeadzoneMeters = 0.03f;
        public float trackerFilterStrength = 0.6f;
        public float trackerSnapDistanceMeters = 2f;

        public void Validate()
        {
            maxPoseAgeSeconds = Mathf.Max(0.05f, maxPoseAgeSeconds);
            maxTrackingPredictionSeconds = Mathf.Clamp(maxTrackingPredictionSeconds, 0f, 1f);
            maxTrackingPositionJumpMeters = Mathf.Clamp(maxTrackingPositionJumpMeters, 0.1f, 2f);
            maxTrackingPredictionSpeedMetersPerSecond = Mathf.Clamp(maxTrackingPredictionSpeedMetersPerSecond, 0.5f, 5f);
            maxTrackingRecoveryStepMeters = Mathf.Clamp(maxTrackingRecoveryStepMeters, 0.05f, 0.35f);
            maxUwbMotionSpeedMetersPerSecond = Mathf.Clamp(maxUwbMotionSpeedMetersPerSecond, 0.5f, 3f);
            trackingPositionDeadZoneMeters = Mathf.Clamp(trackingPositionDeadZoneMeters, 0f, 0.1f);
            uwbPositionAverageFrameCount = Mathf.Clamp(uwbPositionAverageFrameCount, 3, 4);
            movingLatestFrameBlend = Mathf.Clamp01(movingLatestFrameBlend);
            stationaryPositionLerp = Mathf.Clamp(stationaryPositionLerp, 0.01f, 1f);
            movingPositionLerp = Mathf.Clamp(movingPositionLerp, 0.01f, 1f);
            trackerSmoothTime = Mathf.Clamp(trackerSmoothTime, 0.05f, 0.5f);
            trackerDeadzoneMeters = Mathf.Clamp(trackerDeadzoneMeters, 0.01f, 0.2f);
            trackerFilterStrength = Mathf.Clamp(trackerFilterStrength, 0f, 0.95f);
            trackerSnapDistanceMeters = Mathf.Clamp(trackerSnapDistanceMeters, 0.5f, 5f);
        }
    }

    [System.Serializable]
    public class UWBConfigData
    {
        [Header("UWB")]
        public string UWBSerialPort = "COM5";
        public int UWBBaudRate = 921600;

        [Tooltip("Per-axis remap of the raw tracker position onto the configured Unity world frame.")]
        public UWBAxisConversion axisConversion = new UWBAxisConversion();

        [Tooltip("Meters added to the device position after axis conversion.")]
        public Vector3 UWBInputOffset = Vector3.zero;

        [Tooltip("Per-axis multiplier applied after the offset. Unity 0,0 stays fixed; values below 1 shorten the movement on that axis, above 1 lengthen it.")]
        public Vector3 UWBInputAxisScale = Vector3.one;

        [Tooltip("Multiplier applied to raw UWB positions (real-world meters) to convert them into game-world units. 1 = 1 real meter maps to 1 Unity unit.")]
        public float metersToWorldScale = 1f;

        [Header("Tracking / Smoothing")]
        public UWBTrackingSettings tracking = new UWBTrackingSettings();

        [Header("UWB Anchors")]
        [Tooltip("World-space meters position of each NoopLoop anchor. Matched by index to UWBManager's Scene Anchors / Anchor Device Ids.")]
        public Vector3[] UWBAnchorPositions = new Vector3[4];
    }
}
