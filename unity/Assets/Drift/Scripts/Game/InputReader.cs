using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Drift.Game
{
    /// <summary>
    /// Merges the touch pads and the keyboard (for laptop testing) into one set of controls.
    /// Keyboard: A/D or arrows steer, Space drifts, Shift or N fires nitro, Esc pauses.
    /// Taps are latched in Update and consumed in FixedUpdate so none are lost or doubled.
    /// </summary>
    public sealed class InputReader : MonoBehaviour
    {
        private const float SteerRamp = 7f; // steer units per second, so digital input eases in

        private HoldButton _left, _right, _drift, _nitro;
        private float _steer;
        private bool _nitroLatched;
        private bool _pauseLatched;

        public float Steer { get { return _steer; } }
        public bool DriftHeld { get; private set; }

        public void Bind(HoldButton left, HoldButton right, HoldButton drift, HoldButton nitro)
        {
            _left = left;
            _right = right;
            _drift = drift;
            _nitro = nitro;
        }

        /// <summary>True once per nitro press.</summary>
        public bool ConsumeNitro()
        {
            bool pressed = _nitroLatched;
            _nitroLatched = false;
            return pressed;
        }

        public bool ConsumePause()
        {
            bool pressed = _pauseLatched;
            _pauseLatched = false;
            return pressed;
        }

        public void ClearLatches()
        {
            _nitroLatched = false;
            _pauseLatched = false;
            _steer = 0f;
            if (_nitro != null) _nitro.PressPending = false;
        }

        private void Update()
        {
            float target = 0f;
            bool drift = false;

            if (_left != null && _left.IsHeld) target -= 1f;
            if (_right != null && _right.IsHeld) target += 1f;
            if (_drift != null && _drift.IsHeld) drift = true;
            if (_nitro != null && _nitro.PressPending)
            {
                _nitro.PressPending = false;
                _nitroLatched = true;
            }

#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.leftArrowKey.isPressed || kb.aKey.isPressed) target -= 1f;
                if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) target += 1f;
                if (kb.spaceKey.isPressed) drift = true;
                if (kb.leftShiftKey.wasPressedThisFrame || kb.rightShiftKey.wasPressedThisFrame || kb.nKey.wasPressedThisFrame) _nitroLatched = true;
                if (kb.escapeKey.wasPressedThisFrame || kb.pKey.wasPressedThisFrame) _pauseLatched = true;
            }
            Gamepad pad = Gamepad.current;
            if (pad != null)
            {
                target += pad.leftStick.ReadValue().x;
                if (pad.rightTrigger.isPressed || pad.buttonSouth.isPressed) drift = true;
                if (pad.buttonWest.wasPressedThisFrame || pad.leftTrigger.wasPressedThisFrame) _nitroLatched = true;
                if (pad.startButton.wasPressedThisFrame) _pauseLatched = true;
            }
#else
            if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A)) target -= 1f;
            if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) target += 1f;
            if (Input.GetKey(KeyCode.Space)) drift = true;
            if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift) || Input.GetKeyDown(KeyCode.N)) _nitroLatched = true;
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P)) _pauseLatched = true;
#endif

            target = Mathf.Clamp(target, -1f, 1f);
            // Unscaled so steering doesn't freeze oddly around pause; the car ignores it while paused anyway.
            float dt = Time.unscaledDeltaTime;
            float ramp = Mathf.Abs(target) < Mathf.Abs(_steer) || target * _steer < 0f ? SteerRamp * 2f : SteerRamp;
            _steer = Mathf.MoveTowards(_steer, target, ramp * dt);
            DriftHeld = drift;
        }
    }
}
