using Drift.Core;
using UnityEngine;

namespace Drift.Game
{
    /// <summary>
    /// Plays a recorded lap beside the player. Each lap the ghost restarts from the line;
    /// every frame it samples the two recorded poses around the current time and blends them,
    /// so it moves smoothly at any frame rate.
    /// </summary>
    public sealed class GhostPlayer : MonoBehaviour
    {
        private GhostBuffer _ghost;
        private GameObject _visual;
        private float _lapStartTime;
        private bool _running;
        private bool _visible = true;

        public bool HasGhost { get { return _ghost != null && _ghost.Count > 1; } }

        public bool Visible
        {
            get { return _visible; }
            set { _visible = value; RefreshVisibility(); }
        }

        public void Init(GameObject visual)
        {
            _visual = visual;
            RefreshVisibility();
        }

        public void SetGhost(GhostBuffer ghost)
        {
            _ghost = ghost;
            Hold();
            RefreshVisibility();
        }

        /// <summary>Park on the first sample until the player's lap begins.</summary>
        public void Hold()
        {
            _running = false;
            Apply(0f);
        }

        /// <param name="fixedTime">Time.fixedTime of the step that started the player's lap.</param>
        public void Restart(float fixedTime)
        {
            _lapStartTime = fixedTime;
            _running = true;
        }

        private void RefreshVisibility()
        {
            if (_visual != null) _visual.SetActive(_visible && HasGhost);
        }

        private void Update()
        {
            if (!_visible || !HasGhost) return;
            // The player's car is drawn with Rigidbody interpolation, which shows it one physics
            // step behind; the ghost lags by the same step so equal laps overlap exactly.
            float t = _running ? Time.time - Time.fixedDeltaTime - _lapStartTime : 0f;
            Apply(t);
        }

        private void Apply(float time)
        {
            if (!HasGhost) return;
            _ghost.Evaluate(time, out Vector3 position, out Quaternion rotation);
            transform.SetPositionAndRotation(position, rotation);
        }
    }
}
