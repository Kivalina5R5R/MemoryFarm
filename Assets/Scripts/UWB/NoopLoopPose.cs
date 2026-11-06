using UnityEngine;

namespace Fortal.UWB
{
    // One decoded NoopLoop LinkTrack UWB frame (TagFrame0 or AnchorFrame0).
    public readonly struct NoopLoopPose
    {
        public NoopLoopPose(
            byte id,
            byte role,
            string frameType,
            Vector3 positionMeters,
            float[] anchorDistancesMeters,
            float voltage,
            byte[] nodeIds = null,
            byte[] nodeRoles = null,
            Vector3[] nodePositionsMeters = null,
            float[][] nodeAnchorDistancesMeters = null)
        {
            Id = id;
            Role = role;
            FrameType = frameType;
            PositionMeters = positionMeters;
            AnchorDistancesMeters = anchorDistancesMeters;
            Voltage = voltage;
            NodeIds = nodeIds;
            NodeRoles = nodeRoles;
            NodePositionsMeters = nodePositionsMeters;
            NodeAnchorDistancesMeters = nodeAnchorDistancesMeters;
        }

        public byte Id { get; }
        public byte Role { get; }
        public string FrameType { get; }

        // Raw NoopLoop protocol axis order (X, Y, Z), not Unity space. UWBManager applies the configured axis conversion.
        public Vector3 PositionMeters { get; }
        public float[] AnchorDistancesMeters { get; }
        public float Voltage { get; }
        public byte[] NodeIds { get; }
        public byte[] NodeRoles { get; }

        // Raw NoopLoop protocol axis order (X, Y, Z), same caveat as PositionMeters.
        public Vector3[] NodePositionsMeters { get; }
        public float[][] NodeAnchorDistancesMeters { get; }
    }
}
