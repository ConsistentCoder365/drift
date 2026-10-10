using Drift.Core;
using UnityEngine;

namespace Drift.Game
{
    /// <summary>
    /// Damped chase camera. It sits behind the car along a blend of the car's nose and its
    /// velocity, so during a drift it swings out and shows the slide. Position, rotation and
    /// field of view all use 1 - exp(-k * dt) damping, so the feel is the same at 30 or 120 fps.
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public float Distance = 8.5f;
        public float Height = 3.4f;
        public float LookHeight = 1.1f;
        public float LookAhead = 5f;
        [Range(0f, 1f)] public float VelocityBlend = 0.65f;
        public float PositionDamping = 7f;
        public float RotationDamping = 10f;
        public float FovDamping = 3f;
        public float BaseFov = 58f;
        public float MaxSpeedFov = 70f;
        public float NitroFovBoost = 8f;

        private Camera _cam;
        private CarController _car;
        private TuningValues _t;
        private Vector3 _dir = Vector3.forward;

        public Camera Camera { get { return _cam; } }

        public void Bind(Camera cam, CarController car, TuningValues tuning)
        {
            _cam = cam;
            _car = car;
            _t = tuning;
            _cam.fieldOfView = BaseFov;
        }

        public void SetTuning(TuningValues tuning)
        {
            _t = tuning;
        }

        /// <summary>Jump straight to the target pose (race start, restart).</summary>
        public void Snap()
        {
            if (_car == null) return;
            _dir = _car.transform.forward;
            Vector3 target = TargetPosition();
            transform.position = target;
            transform.rotation = Quaternion.LookRotation(LookPoint() - target, Vector3.up);
            _cam.fieldOfView = BaseFov;
        }

        private void LateUpdate()
        {
            if (_car == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // Chase along the velocity once the car is really moving, so a slide stays visible.
            Vector3 nose = _car.transform.forward;
            Vector3 vel = _car.Velocity;
            vel.y = 0f;
            Vector3 desired = nose;
            float speed = vel.magnitude;
            if (speed > 4f)
            {
                Vector3 blended = Vector3.Lerp(nose, vel / speed, VelocityBlend);
                if (blended.sqrMagnitude > 1e-4f) desired = blended.normalized;
            }
            _dir = Damping.Damp(_dir, desired, RotationDamping, dt).normalized;

            transform.position = Damping.Damp(transform.position, TargetPosition(), PositionDamping, dt);
            Quaternion look = Quaternion.LookRotation(LookPoint() - transform.position, Vector3.up);
            transform.rotation = Damping.Damp(transform.rotation, look, RotationDamping, dt);

            float speed01 = _t != null && _t.top_speed > 0f ? Mathf.Clamp01(speed / _t.top_speed) : 0f;
            float fov = Mathf.Lerp(BaseFov, MaxSpeedFov, speed01) + (_car.NitroOn ? NitroFovBoost : 0f);
            _cam.fieldOfView = Damping.Damp(_cam.fieldOfView, fov, FovDamping, dt);
        }

        private Vector3 TargetPosition()
        {
            return _car.transform.position - _dir * Distance + Vector3.up * Height;
        }

        private Vector3 LookPoint()
        {
            return _car.transform.position + Vector3.up * LookHeight + _dir * LookAhead;
        }
    }
}
