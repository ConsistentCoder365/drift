using System;
using UnityEngine;

namespace Drift.Core
{
    /// <summary>
    /// A lap of recorded transforms at a fixed sample rate, in preallocated arrays so recording
    /// never allocates during a race. Sample i was taken i / SampleRate seconds after the lap began.
    /// </summary>
    public sealed class GhostBuffer
    {
        public const int DefaultSampleRate = 50;

        public readonly Vector3[] Positions;
        public readonly Quaternion[] Rotations;
        public readonly ushort[] Sequence;
        public readonly int SampleRate;

        public int Count { get; private set; }
        public int Capacity { get { return Positions.Length; } }

        /// <summary>Seconds covered by the samples (first to last).</summary>
        public float Duration
        {
            get { return Count > 1 ? (Count - 1) / (float)SampleRate : 0f; }
        }

        public GhostBuffer(int capacity, int sampleRate = DefaultSampleRate)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            Positions = new Vector3[capacity];
            Rotations = new Quaternion[capacity];
            Sequence = new ushort[capacity];
            SampleRate = sampleRate;
        }

        public void Clear()
        {
            Count = 0;
        }

        /// <summary>Append a sample. Returns false (and drops it) when the buffer is full.</summary>
        public bool Add(ushort seq, Vector3 position, Quaternion rotation)
        {
            if (Count >= Positions.Length) return false;
            Sequence[Count] = seq;
            Positions[Count] = position;
            Rotations[Count] = rotation;
            Count++;
            return true;
        }

        public void CopyFrom(GhostBuffer other)
        {
            if (other.Count > Capacity) throw new ArgumentException("Source ghost is larger than this buffer.");
            Array.Copy(other.Positions, Positions, other.Count);
            Array.Copy(other.Rotations, Rotations, other.Count);
            Array.Copy(other.Sequence, Sequence, other.Count);
            Count = other.Count;
        }

        /// <summary>
        /// Pose at `time` seconds into the lap: find the two samples around it, lerp position
        /// and slerp rotation, so playback is smooth at any frame rate. Clamps at both ends.
        /// </summary>
        public void Evaluate(float time, out Vector3 position, out Quaternion rotation)
        {
            if (Count == 0)
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
                return;
            }
            float f = time * SampleRate;
            if (f <= 0f || Count == 1)
            {
                position = Positions[0];
                rotation = Rotations[0];
                return;
            }
            int i0 = (int)f;
            if (i0 >= Count - 1)
            {
                position = Positions[Count - 1];
                rotation = Rotations[Count - 1];
                return;
            }
            float t = f - i0;
            position = Vector3.Lerp(Positions[i0], Positions[i0 + 1], t);
            rotation = Quaternion.Slerp(Rotations[i0], Rotations[i0 + 1], t);
        }

        /// <summary>Axis-aligned bounds of all samples.</summary>
        public void GetBounds(out Vector3 min, out Vector3 max)
        {
            min = Vector3.zero;
            max = Vector3.zero;
            if (Count == 0) return;
            min = Positions[0];
            max = Positions[0];
            for (int i = 1; i < Count; i++)
            {
                Vector3 p = Positions[i];
                min = new Vector3(Mathf.Min(min.x, p.x), Mathf.Min(min.y, p.y), Mathf.Min(min.z, p.z));
                max = new Vector3(Mathf.Max(max.x, p.x), Mathf.Max(max.y, p.y), Mathf.Max(max.z, p.z));
            }
        }
    }
}
