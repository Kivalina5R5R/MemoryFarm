using UnityEngine;

namespace Fortal.UWB
{
    // Median spike rejection and speed-dependent smoothing shared by every tracked tag.
    // All positions are already in the calibrated world frame.
    public sealed class AdaptiveUwbPosition
    {
        private bool initialized;
        private Vector3 older, previous, filtered, velocity, target, displayed;
        private double lastSampleTime;
        private float lastRenderTime;

        public void Reset()
        {
            initialized = false;
        }

        public Vector3 Sample(Vector3 position, double sampleTime, float now, float deadband)
        {
            if (!initialized || sampleTime < lastSampleTime || sampleTime - lastSampleTime > 0.25)
            {
                initialized = true;
                older = previous = filtered = target = displayed = position;
                velocity = Vector3.zero;
                lastSampleTime = sampleTime;
                lastRenderTime = now;
                return target;
            }

            float dt = (float)(sampleTime - lastSampleTime);
            if (dt <= 0.0001f)
            {
                return target;
            }

            Vector3 median = new Vector3(
                Median(position.x, previous.x, older.x),
                Median(position.y, previous.y, older.y),
                Median(position.z, previous.z, older.z));
            older = previous;
            previous = position;
            velocity = Vector3.Lerp(velocity, (median - filtered) / dt, 1f - Mathf.Exp(-dt / 0.06f));
            float tau = Mathf.Lerp(0.085f, 0.012f, Mathf.Clamp01(velocity.magnitude / 2f));
            filtered = Vector3.Lerp(filtered, median, 1f - Mathf.Exp(-dt / tau));
            Vector3 delta = filtered - target;
            float distance = delta.magnitude;
            float band = Mathf.Clamp(deadband, 0f, 0.1f);
            if (distance > band)
            {
                target = filtered - delta * (band / distance);
            }

            lastSampleTime = sampleTime;
            return target;
        }

        // Interpolate between samples without extrapolating when the radio stops.
        public Vector3 Present(float now)
        {
            float dt = Mathf.Clamp(now - lastRenderTime, 0f, 0.1f);
            lastRenderTime = now;
            displayed = Vector3.Lerp(displayed, target, 1f - Mathf.Exp(-dt / 0.012f));
            return displayed;
        }

        private static float Median(float a, float b, float c)
        {
            return Mathf.Max(Mathf.Min(a, b), Mathf.Min(Mathf.Max(a, b), c));
        }
    }
}
