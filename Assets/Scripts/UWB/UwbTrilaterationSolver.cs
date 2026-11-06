using System;
using UnityEngine;

namespace Fortal.UWB
{
    internal static class UwbTrilaterationSolver
    {

        internal static bool TrySolve(
            float[] distances,
            Vector3[] anchors,
            int count,
            float[,] leastSquaresAta,
            float[] leastSquaresAtb,
            out Vector3 position)
        {
            position = default;
            if (distances == null || anchors == null || count < 3 ||
                count > distances.Length || count > anchors.Length)
            {
                return false;
            }

            if (TryCalculateFromCoplanarTopAnchors(distances, anchors, count, out position))
            {
                return true;
            }

            return SolveLeastSquares(
                anchors,
                distances,
                count,
                leastSquaresAta,
                leastSquaresAtb,
                out position);
        }

        private static bool TryCalculateFromCoplanarTopAnchors(
            float[] distances,
            Vector3[] anchors,
            int count,
            out Vector3 position)
        {
            position = default;
            if (count < 3)
            {
                return false;
            }

            float anchorY = anchors[0].y;
            for (int i = 1; i < count; i++)
            {
                if (Mathf.Abs(anchors[i].y - anchorY) > 0.03f)
                {
                    return false;
                }
            }

            int reference = -1;
            for (int i = 0; i < count; i++)
            {
                if (distances[i] > 0.01f)
                {
                    reference = i;
                    break;
                }
            }

            if (reference < 0)
            {
                return false;
            }

            Vector3 p0 = anchors[reference];
            float r0 = distances[reference];
            float ata00 = 0f;
            float ata01 = 0f;
            float ata11 = 0f;
            float atb0 = 0f;
            float atb1 = 0f;
            int equationCount = 0;

            for (int i = 0; i < count; i++)
            {
                if (i == reference || distances[i] <= 0.01f)
                {
                    continue;
                }

                Vector3 pi = anchors[i];
                float ri = distances[i];
                float a = 2f * (pi.x - p0.x);
                float b = 2f * (pi.z - p0.z);
                float d = (r0 * r0) - (ri * ri) -
                    ((p0.x * p0.x) + (p0.z * p0.z)) +
                    ((pi.x * pi.x) + (pi.z * pi.z));

                ata00 += a * a;
                ata01 += a * b;
                ata11 += b * b;
                atb0 += a * d;
                atb1 += b * d;
                equationCount++;
            }

            if (equationCount < 2)
            {
                return false;
            }

            float determinant = (ata00 * ata11) - (ata01 * ata01);
            if (Mathf.Abs(determinant) <= 0.000001f)
            {
                return false;
            }

            float x = ((atb0 * ata11) - (ata01 * atb1)) / determinant;
            float z = ((ata00 * atb1) - (ata01 * atb0)) / determinant;
            position = new Vector3(x, anchorY, z);
            return true;
        }

        private static bool SolveLeastSquares(
            Vector3[] anchors,
            float[] distances,
            int count,
            float[,] ata,
            float[] atb,
            out Vector3 position)
        {
            position = default;
            if (anchors == null || distances == null || count < 3 ||
                count > anchors.Length || count > distances.Length ||
                ata == null || ata.GetLength(0) < 3 || ata.GetLength(1) < 3 ||
                atb == null || atb.Length < 3)
            {
                return false;
            }

            Array.Clear(ata, 0, ata.Length);
            Array.Clear(atb, 0, atb.Length);
            Vector3 p0 = anchors[0];
            float d0 = distances[0];

            for (int i = 1; i < count; i++)
            {
                Vector3 pi = anchors[i];
                Vector3 row = 2f * (pi - p0);
                float b = d0 * d0 - distances[i] * distances[i] - p0.sqrMagnitude + pi.sqrMagnitude;

                ata[0, 0] += row.x * row.x;
                ata[0, 1] += row.x * row.y;
                ata[0, 2] += row.x * row.z;
                ata[1, 0] += row.y * row.x;
                ata[1, 1] += row.y * row.y;
                ata[1, 2] += row.y * row.z;
                ata[2, 0] += row.z * row.x;
                ata[2, 1] += row.z * row.y;
                ata[2, 2] += row.z * row.z;
                atb[0] += row.x * b;
                atb[1] += row.y * b;
                atb[2] += row.z * b;
            }

            return Solve3x3(ata, atb, out position);
        }

        private static bool Solve3x3(float[,] matrix, float[] values, out Vector3 position)
        {
            position = default;
            float determinant =
                matrix[0, 0] * (matrix[1, 1] * matrix[2, 2] - matrix[1, 2] * matrix[2, 1]) -
                matrix[0, 1] * (matrix[1, 0] * matrix[2, 2] - matrix[1, 2] * matrix[2, 0]) +
                matrix[0, 2] * (matrix[1, 0] * matrix[2, 1] - matrix[1, 1] * matrix[2, 0]);

            if (Mathf.Abs(determinant) < 0.00001f)
            {
                return false;
            }

            float inverseDeterminant = 1f / determinant;
            position.x = Determinant3(
                values[0], matrix[0, 1], matrix[0, 2],
                values[1], matrix[1, 1], matrix[1, 2],
                values[2], matrix[2, 1], matrix[2, 2]) * inverseDeterminant;
            position.y = Determinant3(
                matrix[0, 0], values[0], matrix[0, 2],
                matrix[1, 0], values[1], matrix[1, 2],
                matrix[2, 0], values[2], matrix[2, 2]) * inverseDeterminant;
            position.z = Determinant3(
                matrix[0, 0], matrix[0, 1], values[0],
                matrix[1, 0], matrix[1, 1], values[1],
                matrix[2, 0], matrix[2, 1], values[2]) * inverseDeterminant;
            return true;
        }

        private static float Determinant3(
            float a00,
            float a01,
            float a02,
            float a10,
            float a11,
            float a12,
            float a20,
            float a21,
            float a22)
        {
            return a00 * (a11 * a22 - a12 * a21) -
                a01 * (a10 * a22 - a12 * a20) +
                a02 * (a10 * a21 - a11 * a20);
        }
    }
}
