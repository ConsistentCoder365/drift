using UnityEngine;
using UnityEngine.Rendering;

namespace Drift.Game
{
    /// <summary>Wheel spin and steer, plus skid-mark trails that emit while sliding.</summary>
    public sealed class CarVisual : MonoBehaviour
    {
        private const float WheelRadius = 0.36f;
        private const float MaxSteerAngle = 28f;

        private TrailRenderer _skidLeft, _skidRight;
        private float _spinAngle;

        // Each wheel spins around its local X axis on top of its rest rotation; front wheels also steer.
        private Transform[] _wheels = new Transform[0];
        private Quaternion[] _rest = new Quaternion[0];
        private bool[] _front = new bool[0];

        public void AddWheel(Transform wheel, bool front)
        {
            int n = _wheels.Length;
            System.Array.Resize(ref _wheels, n + 1);
            System.Array.Resize(ref _rest, n + 1);
            System.Array.Resize(ref _front, n + 1);
            _wheels[n] = wheel;
            _rest[n] = wheel.localRotation;
            _front[n] = front;
        }

        /// <summary>For imported models: any child whose name contains "wheel" spins; "front" ones steer.</summary>
        public void CollectWheels(Transform model)
        {
            foreach (Transform t in model.GetComponentsInChildren<Transform>())
            {
                string n = t.name.ToLowerInvariant();
                if (!n.Contains("wheel") || t.GetComponent<Renderer>() == null) continue;
                AddWheel(t, n.Contains("front"));
            }
        }

        public void AddSkidTrails()
        {
            _skidLeft = Trail("SkidLeft", new Vector3(-CarBuilder.Width * 0.5f + 0.15f, 0.03f, -1.35f));
            _skidRight = Trail("SkidRight", new Vector3(CarBuilder.Width * 0.5f - 0.15f, 0.03f, -1.35f));
        }

        private TrailRenderer Trail(string name, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            // Point the trail's Z axis up so its quads lie flat on the road.
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var trail = go.AddComponent<TrailRenderer>();
            trail.alignment = LineAlignment.TransformZ;
            trail.time = 2.5f;
            trail.minVertexDistance = 0.4f;
            trail.widthMultiplier = 0.32f;
            trail.sharedMaterial = MaterialLibrary.Get("Skid");
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.emitting = false;
            trail.numCapVertices = 0;
            return trail;
        }

        public void ClearTrails()
        {
            if (_skidLeft != null) _skidLeft.Clear();
            if (_skidRight != null) _skidRight.Clear();
        }

        /// <summary>Called every frame by the car with its forward speed, steer input and slide state.</summary>
        public void Animate(float forwardSpeed, float steer, bool sliding, float dt)
        {
            _spinAngle = (_spinAngle + forwardSpeed / WheelRadius * Mathf.Rad2Deg * dt) % 360f;
            Quaternion spin = Quaternion.Euler(_spinAngle, 0f, 0f);
            Quaternion steerRot = Quaternion.Euler(0f, steer * MaxSteerAngle, 0f);
            for (int i = 0; i < _wheels.Length; i++)
                _wheels[i].localRotation = (_front[i] ? steerRot : Quaternion.identity) * spin * _rest[i];

            if (_skidLeft != null)
            {
                _skidLeft.emitting = sliding;
                _skidRight.emitting = sliding;
            }
        }
    }
}
