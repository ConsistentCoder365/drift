using UnityEngine;

namespace Drift.Core
{
    /// <summary>
    /// Records transforms (not inputs) every physics step. Physics isn't deterministic across
    /// devices, so replayed inputs would wander off the racing line; recorded transforms can't.
    ///
    /// A lap's samples run from the step that crossed the start line to the step that crossed
    /// it again, inclusive: a lap of N steps has N + 1 samples and lasts N x 20 ms at 50 Hz.
    /// The server checks exactly that relationship.
    /// </summary>
    public sealed class GhostRecorder
    {
        private readonly GhostBuffer _current;
        private readonly GhostBuffer _best;

        /// <summary>Best completed lap of this run (empty until a lap completes).</summary>
        public GhostBuffer BestLap { get { return _best; } }
        public int BestLapSteps { get; private set; }
        public bool HasBestLap { get { return _best.Count > 0; } }
        public bool IsRecording { get; private set; }
        public bool Overflowed { get; private set; }

        /// <param name="maxLapSeconds">Laps longer than this are not kept (buffer is preallocated).</param>
        public GhostRecorder(float maxLapSeconds = 120f, int sampleRate = GhostBuffer.DefaultSampleRate)
        {
            int capacity = Mathf.CeilToInt(maxLapSeconds * sampleRate) + 1;
            _current = new GhostBuffer(capacity, sampleRate);
            _best = new GhostBuffer(capacity, sampleRate);
        }

        public void Reset()
        {
            _current.Clear();
            _best.Clear();
            BestLapSteps = 0;
            IsRecording = false;
            Overflowed = false;
        }

        /// <summary>Start a lap with its first sample (the step that crossed the line).</summary>
        public void BeginLap(int step, Vector3 position, Quaternion rotation)
        {
            _current.Clear();
            Overflowed = false;
            IsRecording = true;
            _current.Add((ushort)step, position, rotation);
        }

        public void Record(int step, Vector3 position, Quaternion rotation)
        {
            if (!IsRecording) return;
            if (!_current.Add((ushort)step, position, rotation)) Overflowed = true;
        }

        /// <summary>
        /// Close the lap with the sample from the step that crossed the line again.
        /// Returns true if it became the run's best lap. The caller then calls BeginLap.
        /// </summary>
        public bool EndLap(int step, Vector3 position, Quaternion rotation, int lapSteps)
        {
            if (!IsRecording) return false;
            Record(step, position, rotation);
            IsRecording = false;
            if (Overflowed) return false;
            if (_best.Count > 0 && lapSteps >= BestLapSteps) return false;
            _best.CopyFrom(_current);
            BestLapSteps = lapSteps;
            return true;
        }
    }
}
