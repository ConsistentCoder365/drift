using UnityEngine;
using UnityEngine.EventSystems;

namespace Drift.Game
{
    /// <summary>
    /// A touch button that reports held state and taps. Each button tracks its own pointer,
    /// so steering with one thumb and drifting with the other works with multi-touch.
    /// </summary>
    public sealed class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        private int _pointerId = int.MinValue;
        private int _pressedFrame = -1;

        public bool IsHeld { get; private set; }

        /// <summary>True on the frame the button went down.</summary>
        public bool WasPressedThisFrame
        {
            get { return _pressedFrame == Time.frameCount; }
        }

        /// <summary>Set by the reader once it has consumed a press (taps are read in FixedUpdate).</summary>
        public bool PressPending { get; set; }

        public void OnPointerDown(PointerEventData eventData)
        {
            _pointerId = eventData.pointerId;
            IsHeld = true;
            _pressedFrame = Time.frameCount;
            PressPending = true;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != _pointerId) return;
            _pointerId = int.MinValue;
            IsHeld = false;
        }

        private void OnDisable()
        {
            IsHeld = false;
            PressPending = false;
            _pointerId = int.MinValue;
        }
    }
}
