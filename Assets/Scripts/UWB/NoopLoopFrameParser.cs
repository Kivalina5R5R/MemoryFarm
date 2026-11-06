using System;
using UnityEngine;

namespace Fortal.UWB
{
    // Streaming decoder for the NoopLoop LinkTrack UWB serial protocol.
    // Feed raw bytes with Push; it recognizes TagFrame0 (single tag pose)
    // and AnchorFrame0 (full node list reported by one anchor) frames.
    public sealed class NoopLoopFrameParser
    {
        public const byte FrameHeader = 0x55;
        public const byte FunctionMark = 0x01;
        public const int FrameSize = 128;
        public const byte AnchorFrame0FunctionMark = 0x00;
        public const int AnchorFrame0Size = 896;

        private const float PositionScale = 1000f;
        private const float VoltageScale = 1000f;
        private const int AnchorFrame0NodeSize = 27;
        private const int LinkTrackRoleAnchor = 1;
        private const int LinkTrackRoleTag = 2;

        private readonly byte[] buffer = new byte[AnchorFrame0Size * 2];
        private int count;

        public int BufferedByteCount => count;
        public long CandidateFrameCount { get; private set; }
        public long ValidFrameCount { get; private set; }
        public long ChecksumFailCount { get; private set; }
        public long DroppedByteCount { get; private set; }
        public long AnchorFrame0Count { get; private set; }
        public long TagFrame0Count { get; private set; }
        public byte LastFunctionMark { get; private set; }

        public bool Push(byte value, out NoopLoopPose pose)
        {
            pose = default;

            if (count == buffer.Length)
            {
                DroppedByteCount += count;
                count = 0;
            }

            buffer[count++] = value;

            while (count >= 2)
            {
                int start = FindFrameStart();
                if (start < 0)
                {
                    DroppedByteCount += Mathf.Max(0, count - 1);
                    KeepPossibleHeaderByte();
                    return false;
                }

                if (start > 0)
                {
                    DroppedByteCount += start;
                    Discard(start);
                }

                LastFunctionMark = buffer[1];
                int frameSize = GetFrameSize(buffer[1]);
                if (frameSize == 0)
                {
                    Discard(1);
                    continue;
                }

                if (count < frameSize)
                {
                    return false;
                }

                CandidateFrameCount++;
                if (!IsFrameValid(buffer, frameSize))
                {
                    ChecksumFailCount++;
                    Discard(1);
                    continue;
                }

                pose = buffer[1] == FunctionMark ? ParseTagFrame0(buffer) : ParseAnchorFrame0(buffer);
                ValidFrameCount++;
                if (buffer[1] == FunctionMark)
                {
                    TagFrame0Count++;
                }
                else
                {
                    AnchorFrame0Count++;
                }

                Discard(frameSize);
                return true;
            }

            return false;
        }

        private int FindFrameStart()
        {
            for (int i = 0; i <= count - 2; i++)
            {
                if (buffer[i] == FrameHeader)
                {
                    return i;
                }
            }

            return -1;
        }

        private void KeepPossibleHeaderByte()
        {
            if (count > 0 && buffer[count - 1] == FrameHeader)
            {
                buffer[0] = FrameHeader;
                count = 1;
            }
            else
            {
                count = 0;
            }
        }

        private void Discard(int length)
        {
            int remaining = count - length;
            if (remaining > 0)
            {
                Buffer.BlockCopy(buffer, length, buffer, 0, remaining);
            }

            count = Math.Max(remaining, 0);
        }

        private static int GetFrameSize(byte functionMark)
        {
            if (functionMark == FunctionMark)
            {
                return FrameSize;
            }

            if (functionMark == AnchorFrame0FunctionMark)
            {
                return AnchorFrame0Size;
            }

            return 0;
        }

        private static bool IsFrameValid(byte[] data, int length)
        {
            if (data[1] == FunctionMark)
            {
                return HasValidChecksum(data, length);
            }

            if (data[1] == AnchorFrame0FunctionMark)
            {
                return length == AnchorFrame0Size && data[AnchorFrame0Size - 1] == 0xEE;
            }

            return false;
        }

        private static bool HasValidChecksum(byte[] data, int length)
        {
            byte sum = 0;
            for (int i = 0; i < length - 1; i++)
            {
                unchecked
                {
                    sum += data[i];
                }
            }

            return sum == data[length - 1];
        }

        private static NoopLoopPose ParseTagFrame0(byte[] data)
        {
            byte id = data[2];
            byte role = data[3];
            // Raw NoopLoop protocol axis order (X, Y, Z) — NOT Unity space yet.
            // UWBManager applies the configured axis conversion before using this.
            Vector3 position = new Vector3(
                ReadInt24(data, 4) / PositionScale,
                ReadInt24(data, 7) / PositionScale,
                ReadInt24(data, 10) / PositionScale);
            float voltage = ReadUInt16(data, 116) / VoltageScale;
            float[] anchorDistances = new float[8];
            for (int i = 0; i < anchorDistances.Length; i++)
            {
                anchorDistances[i] = ReadInt24(data, 22 + (i * 3)) / PositionScale;
            }

            return new NoopLoopPose(id, role, "TagFrame0", position, anchorDistances, voltage);
        }

        private static NoopLoopPose ParseAnchorFrame0(byte[] data)
        {
            float voltage = ReadUInt16(data, 887) / VoltageScale;
            byte localId = data[893];
            byte localRole = data[894];
            Vector3 tagPosition = Vector3.zero;
            byte tagId = localId;
            byte tagRole = localRole;
            float[] tagDistances = new float[8];
            bool foundTag = false;
            byte fallbackTagId = localId;
            byte fallbackTagRole = localRole;
            Vector3 fallbackTagPosition = Vector3.zero;
            float[] fallbackTagDistances = new float[8];
            int fallbackDistanceCount = 0;

            var nodeIds = new System.Collections.Generic.List<byte>(30);
            var nodeRoles = new System.Collections.Generic.List<byte>(30);
            var nodePositions = new System.Collections.Generic.List<Vector3>(30);
            var nodeAnchorDistances = new System.Collections.Generic.List<float[]>(30);

            for (int i = 0; i < 30; i++)
            {
                int offset = 2 + (i * AnchorFrame0NodeSize);
                byte nodeId = data[offset];
                if (nodeId == 0xFF)
                {
                    continue;
                }

                byte nodeRole = data[offset + 1];
                // Raw NoopLoop protocol axis order (X, Y, Z), same as ParseTagFrame0.
                Vector3 position = new Vector3(
                    ReadInt24(data, offset + 2) / PositionScale,
                    ReadInt24(data, offset + 5) / PositionScale,
                    ReadInt24(data, offset + 8) / PositionScale);
                float[] nodeDistances = new float[8];
                for (int d = 0; d < nodeDistances.Length; d++)
                {
                    nodeDistances[d] = ReadUInt16(data, offset + 11 + (d * 2)) / 100f;
                }

                nodeIds.Add(nodeId);
                nodeRoles.Add(nodeRole);
                nodePositions.Add(position);
                nodeAnchorDistances.Add(nodeDistances);

                if (nodeRole == LinkTrackRoleAnchor)
                {
                    continue;
                }

                if (nodeRole != LinkTrackRoleTag || foundTag)
                {
                    int nodeDistanceCount = CountPositiveDistances(nodeDistances);
                    if (!foundTag && nodeId != localId && nodeDistanceCount > fallbackDistanceCount)
                    {
                        fallbackTagId = nodeId;
                        fallbackTagRole = nodeRole;
                        fallbackTagPosition = position;
                        fallbackTagDistances = nodeDistances;
                        fallbackDistanceCount = nodeDistanceCount;
                    }

                    continue;
                }

                tagId = nodeId;
                tagRole = nodeRole;
                tagPosition = position;
                for (int d = 0; d < tagDistances.Length; d++)
                {
                    tagDistances[d] = nodeDistances[d];
                }

                foundTag = true;
            }

            bool usedFallbackTag = false;
            if (!foundTag && fallbackDistanceCount > 0)
            {
                tagId = fallbackTagId;
                tagRole = fallbackTagRole;
                tagPosition = fallbackTagPosition;
                tagDistances = fallbackTagDistances;
                foundTag = true;
                usedFallbackTag = true;
            }

            string frameType = foundTag ? (usedFallbackTag ? "AnchorFrame0-FallbackTag" : "AnchorFrame0") : "AnchorFrame0-NoTag";
            return new NoopLoopPose(tagId, tagRole, frameType, tagPosition, tagDistances, voltage, nodeIds.ToArray(), nodeRoles.ToArray(), nodePositions.ToArray(), nodeAnchorDistances.ToArray());
        }

        private static int CountPositiveDistances(float[] distances)
        {
            int count = 0;
            for (int i = 0; i < distances.Length; i++)
            {
                if (distances[i] > 0.01f)
                {
                    count++;
                }
            }

            return count;
        }

        private static int ReadInt24(byte[] data, int offset)
        {
            int value = data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16);
            if ((value & 0x800000) != 0)
            {
                value |= unchecked((int)0xFF000000);
            }

            return value;
        }

        private static ushort ReadUInt16(byte[] data, int offset)
        {
            return (ushort)(data[offset] | (data[offset + 1] << 8));
        }
    }
}
