using System;
using UnityEngine;

namespace Drift.Core
{
    [Serializable]
    public class TrackPoint
    {
        public float x;
        public float z;
    }

    /// <summary>
    /// A closed track: Catmull-Rom control points on the ground plane (x, z) and the control
    /// point indices that act as checkpoint gates. Index 0 in `checkpoints` is the start/finish.
    /// The server loads the same JSON, so gate geometry must stay identical on both sides.
    /// </summary>
    [Serializable]
    public class TrackDefinition
    {
        public string id = "harbor";
        public string name = "Harbor Loop";
        public int laps = 3;
        public float road_width = 14f;
        public float gate_margin = 3f;
        public TrackPoint[] points = new TrackPoint[0];
        public int[] checkpoints = new int[0];

        public Vector2 Point(int i)
        {
            int n = points.Length;
            TrackPoint p = points[((i % n) + n) % n];
            return new Vector2(p.x, p.z);
        }
    }

    /// <summary>A checkpoint line across the road. Positions are (x, z).</summary>
    public struct Gate
    {
        public Vector2 Center;
        /// <summary>Unit direction of travel through the gate.</summary>
        public Vector2 Forward;
        /// <summary>Unit direction along the gate line.</summary>
        public Vector2 Across;
        public float HalfWidth;

        /// <summary>True if moving from prev to cur crosses this gate in the forward direction.</summary>
        public bool Crossed(Vector2 prev, Vector2 cur)
        {
            float sidePrev = Vector2.Dot(prev - Center, Forward);
            float sideCur = Vector2.Dot(cur - Center, Forward);
            if (!(sidePrev < 0f && sideCur >= 0f)) return false;
            float t = sidePrev / (sidePrev - sideCur);
            Vector2 hit = Vector2.Lerp(prev, cur, t);
            return Mathf.Abs(Vector2.Dot(hit - Center, Across)) <= HalfWidth;
        }
    }

    public static class TrackGeometry
    {
        /// <summary>Uniform Catmull-Rom between p1 and p2.</summary>
        public static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (2f * p1
                           + (-p0 + p2) * t
                           + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                           + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        /// <summary>Tangent of the spline at control point i: (P[i+1] - P[i-1]) / 2, normalised.</summary>
        public static Vector2 TangentAtControlPoint(TrackDefinition def, int i)
        {
            return (def.Point(i + 1) - def.Point(i - 1)).normalized;
        }

        public static Gate[] BuildGates(TrackDefinition def)
        {
            var gates = new Gate[def.checkpoints.Length];
            for (int g = 0; g < gates.Length; g++)
            {
                int i = def.checkpoints[g];
                Vector2 fwd = TangentAtControlPoint(def, i);
                gates[g] = new Gate
                {
                    Center = def.Point(i),
                    Forward = fwd,
                    // Right-hand perpendicular on the ground plane (x, z).
                    Across = new Vector2(fwd.y, -fwd.x),
                    HalfWidth = def.road_width * 0.5f + def.gate_margin,
                };
            }
            return gates;
        }

        /// <summary>Dense samples of the closed centreline, `perSegment` per control-point span.</summary>
        public static Vector2[] SampleCenterline(TrackDefinition def, int perSegment)
        {
            int n = def.points.Length;
            var result = new Vector2[n * perSegment];
            for (int i = 0; i < n; i++)
            {
                Vector2 p0 = def.Point(i - 1), p1 = def.Point(i), p2 = def.Point(i + 1), p3 = def.Point(i + 2);
                for (int k = 0; k < perSegment; k++)
                    result[i * perSegment + k] = CatmullRom(p0, p1, p2, p3, k / (float)perSegment);
            }
            return result;
        }

        /// <summary>Resample a closed polyline to roughly even spacing (for mesh building).</summary>
        public static Vector2[] ResampleClosed(Vector2[] dense, float spacing)
        {
            int n = dense.Length;
            var cumulative = new float[n + 1];
            for (int i = 0; i < n; i++)
                cumulative[i + 1] = cumulative[i] + Vector2.Distance(dense[i], dense[(i + 1) % n]);
            float length = cumulative[n];
            int count = Mathf.Max(3, Mathf.RoundToInt(length / spacing));
            float step = length / count;
            var result = new Vector2[count];
            int seg = 0;
            for (int k = 0; k < count; k++)
            {
                float d = k * step;
                while (seg < n - 1 && cumulative[seg + 1] < d) seg++;
                float segLen = cumulative[seg + 1] - cumulative[seg];
                float t = segLen > 1e-6f ? (d - cumulative[seg]) / segLen : 0f;
                result[k] = Vector2.Lerp(dense[seg], dense[(seg + 1) % n], t);
            }
            return result;
        }

        public static float ClosedLength(Vector2[] pts)
        {
            float length = 0f;
            for (int i = 0; i < pts.Length; i++) length += Vector2.Distance(pts[i], pts[(i + 1) % pts.Length]);
            return length;
        }
    }

    public enum LapEvent
    {
        None,
        /// <summary>First crossing of the start line: lap 1 timing begins.</summary>
        LapStarted,
        Checkpoint,
        /// <summary>Crossed the start line after every checkpoint in order. The next lap begins at once.</summary>
        LapCompleted,
    }

    /// <summary>Gates must be crossed in order, forwards; the lap ends back at gate 0.</summary>
    public sealed class LapTracker
    {
        private readonly Gate[] _gates;

        public int NextGate { get; private set; }
        public bool Started { get; private set; }
        public int CompletedLaps { get; private set; }

        public LapTracker(Gate[] gates)
        {
            if (gates == null || gates.Length == 0) throw new ArgumentException("A track needs at least one gate.");
            _gates = gates;
        }

        public void Reset()
        {
            NextGate = 0;
            Started = false;
            CompletedLaps = 0;
        }

        public LapEvent Step(Vector2 prev, Vector2 cur)
        {
            if (!_gates[NextGate].Crossed(prev, cur)) return LapEvent.None;

            if (NextGate == 0)
            {
                NextGate = _gates.Length > 1 ? 1 : 0;
                if (!Started)
                {
                    Started = true;
                    return LapEvent.LapStarted;
                }
                CompletedLaps++;
                return LapEvent.LapCompleted;
            }

            NextGate = (NextGate + 1) % _gates.Length;
            return LapEvent.Checkpoint;
        }
    }
}
