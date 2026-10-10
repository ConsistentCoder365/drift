using Drift.Core;
using UnityEngine;

namespace Drift.Game
{
    /// <summary>
    /// Turns input into forces on a Rigidbody. All of the maths is in DriftPhysics; this class
    /// only reads the body's state, calls it, and writes the result back. RaceManager calls
    /// PhysicsStep from its FixedUpdate so laps, recording and forces run in a fixed order.
    ///
    /// The track is flat, so the body is locked to the ground plane (no gravity, no pitch/roll):
    /// only yaw and planar motion are simulated. Walls are the only colliders it meets.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class CarController : MonoBehaviour
    {
        private const float WallHitCooldown = 0.3f;

        private Rigidbody _rb;
        private TuningValues _t;
        private CarVisual _visual;
        private float _grip;
        private bool _wallHitPending;
        private float _lastWallHitTime = -10f;
        private float _visualSteer;

        public Rigidbody Body { get { return _rb; } }
        public Vector3 Velocity { get; private set; }
        public float Speed { get; private set; }
        public float ForwardSpeed { get; private set; }
        public float DriftAngleRad { get; private set; }
        public bool DriftHeld { get; private set; }
        public bool NitroOn { get; private set; }
        public float Grip { get { return _grip; } }

        public void Init(TuningValues tuning, CarVisual visual)
        {
            _t = tuning;
            _visual = visual;
            _grip = tuning.grip_normal;

            _rb = GetComponent<Rigidbody>();
            _rb.mass = 1200f;
            _rb.useGravity = false;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rb.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            _rb.maxAngularVelocity = 10f;
            Compat.SetDamping(_rb, 0f, 0f);

            var box = gameObject.GetComponent<BoxCollider>();
            if (box == null) box = gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.6f, 0f);
            box.size = new Vector3(CarBuilder.Width, 1.0f, CarBuilder.Length);
            box.sharedMaterial = Compat.Frictionless();
        }

        public void SetTuning(TuningValues tuning)
        {
            _t = tuning;
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            _rb.position = position;
            _rb.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            Compat.SetVelocity(_rb, Vector3.zero);
            _rb.angularVelocity = Vector3.zero;
            _grip = _t.grip_normal;
            _wallHitPending = false;
            Velocity = Vector3.zero;
            Speed = 0f;
            ForwardSpeed = 0f;
            DriftAngleRad = 0f;
            DriftHeld = false;
            NitroOn = false;
            if (_visual != null) _visual.ClearTrails();
        }

        /// <summary>True once after the car hits a wall.</summary>
        public bool ConsumeWallHit()
        {
            bool hit = _wallHitPending;
            _wallHitPending = false;
            return hit;
        }

        public void PhysicsStep(CarInput input, float dt)
        {
            Quaternion rot = _rb.rotation;
            Vector3 forward = rot * Vector3.forward;
            Vector3 right = rot * Vector3.right;
            Vector3 v = Compat.GetVelocity(_rb);

            // Holding Drift drops grip at once; releasing it lets grip recover smoothly,
            // so the car straightens out of a slide instead of snapping.
            _grip = input.Drift ? _t.grip_drift : Damping.Damp(_grip, _t.grip_normal, _t.grip_recovery, dt);

            Vector3 newV = DriftPhysics.StepVelocity(v, forward, right, _grip, input, _t, dt);
            newV.y = 0f;
            Compat.SetVelocity(_rb, newV);

            float forwardSpeed = Vector3.Dot(newV, forward);
            float targetYaw = DriftPhysics.TargetYawRate(input.Steer, forwardSpeed, input.Drift, _t);
            float yawAccel = DriftPhysics.YawAcceleration(_rb.angularVelocity.y, targetYaw, _t.yaw_response);
            _rb.AddTorque(new Vector3(0f, yawAccel, 0f), ForceMode.Acceleration);

            Velocity = newV;
            Speed = newV.magnitude;
            ForwardSpeed = forwardSpeed;
            DriftAngleRad = DriftPhysics.DriftAngle(forward, newV, Vector3.up);
            DriftHeld = input.Drift;
            NitroOn = input.Nitro;
            _visualSteer = input.Steer;
        }

        private void Update()
        {
            if (_visual == null) return;
            bool sliding = Speed > _t.drift_min_speed && Mathf.Abs(DriftAngleRad) * Mathf.Rad2Deg > _t.drift_min_angle_deg;
            _visual.Animate(ForwardSpeed, _visualSteer, sliding, Time.deltaTime);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!collision.collider.TryGetComponent(out TrackWall _)) return;
            if (Time.time - _lastWallHitTime < WallHitCooldown) return;
            _lastWallHitTime = Time.time;
            _wallHitPending = true;
            Compat.SetVelocity(_rb, Compat.GetVelocity(_rb) * _t.wall_speed_keep);
        }
    }
}
