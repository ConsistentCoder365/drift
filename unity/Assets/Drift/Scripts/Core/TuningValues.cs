using System;

namespace Drift.Core
{
    /// <summary>
    /// Every gameplay tuning value in one place. Field names match the server's /config JSON
    /// so JsonUtility can overwrite them directly; the initialisers are the offline defaults.
    /// </summary>
    [Serializable]
    public class TuningValues
    {
        public int version = 1;

        // Grip constants (1/s). Sideways velocity is multiplied by exp(-k * dt) every step.
        public float grip_normal = 20f;
        public float grip_drift = 3.5f;
        // How fast grip returns to grip_normal after Drift is released (1/s, exponential).
        public float grip_recovery = 6f;
        // Fraction of speed kept when grip turns sideways velocity into scrubbed speed.
        // 0 = pure grip formula (sliding bleeds speed), 1 = sliding never costs speed.
        public float slide_momentum_keep = 0.85f;

        // Speeds (m/s) and accelerations (m/s^2).
        public float top_speed = 38f;
        public float nitro_top_speed = 50f;
        public float acceleration = 14f;
        public float nitro_acceleration = 26f;
        public float drift_drag = 0.12f;
        public float coast_drag = 0.6f;

        // Steering. Yaw rate scales with speed up to steer_full_speed so the car can't spin in place.
        public float max_yaw_rate = 2.1f;
        public float drift_yaw_boost = 1.45f;
        public float steer_full_speed = 14f;
        public float yaw_response = 12f;

        // Drift scoring.
        public float drift_min_angle_deg = 8f;
        public float drift_min_speed = 8f;
        public float drift_end_grace = 0.15f;
        public float points_per_degree_meter = 0.1f;
        public float combo_window = 1.5f;
        public int combo_max_multiplier = 5;

        // Nitro.
        public float nitro_gain_per_point = 0.0025f;
        public float nitro_full_duration = 3f;
        public float nitro_min_to_fire = 0.15f;

        // Fraction of speed kept after hitting a wall.
        public float wall_speed_keep = 0.7f;

        public TuningValues Clone()
        {
            return (TuningValues)MemberwiseClone();
        }
    }
}
