using UnityEngine;

namespace Drift.Core
{
    /// <summary>Frame-rate independent smoothing: p &lt;- lerp(p, target, 1 - exp(-k * dt)).</summary>
    public static class Damping
    {
        public static float Factor(float k, float dt)
        {
            return 1f - Mathf.Exp(-k * dt);
        }

        public static float Damp(float current, float target, float k, float dt)
        {
            return Mathf.Lerp(current, target, Factor(k, dt));
        }

        public static Vector3 Damp(Vector3 current, Vector3 target, float k, float dt)
        {
            return Vector3.Lerp(current, target, Factor(k, dt));
        }

        public static Quaternion Damp(Quaternion current, Quaternion target, float k, float dt)
        {
            return Quaternion.Slerp(current, target, Factor(k, dt));
        }
    }
}
