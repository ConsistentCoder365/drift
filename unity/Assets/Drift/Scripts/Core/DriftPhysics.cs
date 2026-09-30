using UnityEngine;

namespace Drift.Core
{
    public struct CarInput
    {
        /// <summary>-1 = full left, 1 = full right.</summary>
        public float Steer;
        /// <summary>0..1. The race sets this to 1 after the countdown (auto-accelerate).</summary>
        public float Throttle;
        /// <summary>Drift button held: lowers grip so the car slides.</summary>
        public bool Drift;
        /// <summary>A nitro boost is burning this step.</summary>
        public bool Nitro;
    }

    /// <summary>
    /// The arcade car model as pure functions of Vector3/Quaternion, so it can be unit tested
    /// outside play mode. CarController feeds it the Rigidbody state each physics step.
    /// </summary>
    public static class DriftPhysics
    {
        /// <summary>Split v into the part along forward and the part along right.</summary>
        public static void Decompose(Vector3 v, Vector3 forward, Vector3 right, out Vector3 vFwd, out Vector3 vLat)
        {
            vFwd = Vector3.Dot(v, forward) * forward;
            vLat = Vector3.Dot(v, right) * right;
        }

        /// <summary>
        /// Fraction of sideways velocity left after dt. Exponential, so applying it once over
        /// 2*dt gives the same result as twice over dt: the feel survives physics-rate changes.
        /// </summary>
        public static float GripRetention(float kGrip, float dt)
        {
            return Mathf.Exp(-kGrip * dt);
        }

        /// <summary>v &lt;- v_fwd + v_lat * exp(-k * dt). The vertical part of v is left alone.</summary>
        public static Vector3 ApplyGrip(Vector3 v, Vector3 forward, Vector3 right, float kGrip, float dt)
        {
            Decompose(v, forward, right, out Vector3 vFwd, out Vector3 vLat);
            Vector3 vRest = v - vFwd - vLat;
            return vFwd + vLat * GripRetention(kGrip, dt) + vRest;
        }

        /// <summary>
        /// Grip alone throws away the sideways speed it removes, so long slides stall the car.
        /// Arcade racers hand most of that speed back along the new direction of travel:
        /// the result keeps the gripped direction with a length between |after| and |before|.
        /// Only applies while moving forwards.
        /// </summary>
        public static Vector3 KeepMomentum(Vector3 before, Vector3 after, Vector3 forward, float keep)
        {
            if (Vector3.Dot(after, forward) <= 0f) return after;
            float afterSpeed = after.magnitude;
            if (afterSpeed < 1e-4f) return after;
            float speed = Mathf.Lerp(afterSpeed, before.magnitude, Mathf.Clamp01(keep));
            return after * (speed / afterSpeed);
        }

        /// <summary>
        /// Signed angle (radians) between where the car points and where it moves, around up:
        /// theta = atan2((f x v) . u, f . v). Positive when the car slides to its right.
        /// </summary>
        public static float DriftAngle(Vector3 forward, Vector3 v, Vector3 up)
        {
            float sin = Vector3.Dot(Vector3.Cross(forward, v), up);
            float cos = Vector3.Dot(forward, v);
            if (sin * sin + cos * cos < 1e-10f) return 0f;
            return Mathf.Atan2(sin, cos);
        }

        /// <summary>Forward acceleration that fades out as forward speed approaches top speed.</summary>
        public static float DriveAcceleration(float forwardSpeed, float topSpeed, float accel, float throttle)
        {
            if (topSpeed <= 0f) return 0f;
            float headroom = Mathf.Clamp(1f - forwardSpeed / topSpeed, -1f, 1f);
            return accel * Mathf.Clamp01(throttle) * headroom;
        }

        /// <summary>
        /// Target yaw rate (rad/s) for a steer input. Scales with speed up to steer_full_speed,
        /// so a stopped car cannot spin, and is boosted while drifting to swing the tail out.
        /// </summary>
        public static float TargetYawRate(float steer, float speed, bool drifting, TuningValues t)
        {
            float speedFactor = t.steer_full_speed > 0f ? Mathf.Clamp01(Mathf.Abs(speed) / t.steer_full_speed) : 1f;
            float rate = Mathf.Clamp(steer, -1f, 1f) * t.max_yaw_rate * speedFactor;
            return drifting ? rate * t.drift_yaw_boost : rate;
        }

        /// <summary>Angular acceleration (rad/s^2) that drives the current yaw rate to the target.</summary>
        public static float YawAcceleration(float currentYawRate, float targetYawRate, float response)
        {
            return (targetYawRate - currentYawRate) * response;
        }

        /// <summary>
        /// One full step of the planar velocity model. Returns the new velocity; the caller
        /// writes it back to the Rigidbody. `grip` is the current (smoothed) grip constant.
        /// </summary>
        public static Vector3 StepVelocity(Vector3 v, Vector3 forward, Vector3 right, float grip,
                                           CarInput input, TuningValues t, float dt)
        {
            // Grip first: kill sideways velocity, then hand some of the lost speed back.
            Vector3 gripped = ApplyGrip(v, forward, right, grip, dt);
            Vector3 result = KeepMomentum(v, gripped, forward, t.slide_momentum_keep);

            // Drive along the nose.
            float forwardSpeed = Vector3.Dot(result, forward);
            float top = input.Nitro ? t.nitro_top_speed : t.top_speed;
            float accel = input.Nitro ? t.nitro_acceleration : t.acceleration;
            result += forward * (DriveAcceleration(forwardSpeed, top, accel, input.Throttle) * dt);

            // Drag: drifting scrubs speed, coasting (no throttle) rolls to a stop.
            float drag = 0f;
            if (input.Drift) drag += t.drift_drag;
            if (input.Throttle <= 0f) drag += t.coast_drag;
            if (drag > 0f) result *= Mathf.Exp(-drag * dt);

            // Hard cap so collisions can never push the car past what server validation allows.
            float max = t.nitro_top_speed * 1.05f;
            float sq = result.sqrMagnitude;
            if (sq > max * max) result *= max / Mathf.Sqrt(sq);
            return result;
        }
    }
}
