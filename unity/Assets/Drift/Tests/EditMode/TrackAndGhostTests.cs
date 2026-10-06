using Drift.Core;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    public class TrackTests
    {
        /// <summary>A rounded 200 x 100 loop: out along +x on the bottom straight, back along -x on the top one.</summary>
        private static TrackDefinition Box()
        {
            return new TrackDefinition
            {
                road_width = 10f,
                gate_margin = 2f,
                laps = 3,
                points = new[]
                {
                    new TrackPoint { x = 0f, z = 0f },
                    new TrackPoint { x = 100f, z = 0f },
                    new TrackPoint { x = 200f, z = 0f },
                    new TrackPoint { x = 200f, z = 100f },
                    new TrackPoint { x = 100f, z = 100f },
                    new TrackPoint { x = 0f, z = 100f },
                },
                checkpoints = new[] { 1, 4 },
            };
        }

        [Test]
        public void CatmullRom_PassesThroughItsControlPoints()
        {
            var p0 = new Vector2(0f, 0f);
            var p1 = new Vector2(10f, 3f);
            var p2 = new Vector2(20f, -4f);
            var p3 = new Vector2(30f, 0f);
            Vector2 a = TrackGeometry.CatmullRom(p0, p1, p2, p3, 0f);
            Vector2 b = TrackGeometry.CatmullRom(p0, p1, p2, p3, 1f);
            Assert.That(Vector2.Distance(a, p1), Is.LessThan(1e-4f));
            Assert.That(Vector2.Distance(b, p2), Is.LessThan(1e-4f));
        }

        [Test]
        public void Gates_SitOnControlPoints_FacingAlongTheTrack()
        {
            Gate[] gates = TrackGeometry.BuildGates(Box());
            Assert.That(gates.Length, Is.EqualTo(2));
            Assert.That(Vector2.Distance(gates[0].Center, new Vector2(100f, 0f)), Is.LessThan(1e-4f));
            Assert.That(Vector2.Distance(gates[0].Forward, new Vector2(1f, 0f)), Is.LessThan(1e-4f));
            Assert.That(Vector2.Distance(gates[1].Forward, new Vector2(-1f, 0f)), Is.LessThan(1e-4f));
            Assert.That(Vector2.Dot(gates[0].Forward, gates[0].Across), Is.EqualTo(0f).Within(1e-5f));
            Assert.That(gates[0].HalfWidth, Is.EqualTo(7f));
        }

        [Test]
        public void Gate_CountsForwardCrossingsInsideItsWidthOnly()
        {
            Gate g = TrackGeometry.BuildGates(Box())[0];
            Assert.That(g.Crossed(new Vector2(99f, 0f), new Vector2(101f, 0f)), Is.True);
            Assert.That(g.Crossed(new Vector2(101f, 0f), new Vector2(99f, 0f)), Is.False, "backwards");
            Assert.That(g.Crossed(new Vector2(99f, 30f), new Vector2(101f, 30f)), Is.False, "outside the road");
            Assert.That(g.Crossed(new Vector2(98f, 0f), new Vector2(99f, 0f)), Is.False, "not reached yet");
            Assert.That(g.Crossed(new Vector2(99f, 0f), new Vector2(100f, 0f)), Is.True, "landing exactly on the line counts once");
            Assert.That(g.Crossed(new Vector2(100f, 0f), new Vector2(101f, 0f)), Is.False, "...and not again on the next step");
        }

        [Test]
        public void LapTracker_RequiresEveryGateInOrder()
        {
            var laps = new LapTracker(TrackGeometry.BuildGates(Box()));
            Vector2 beforeStart = new Vector2(95f, 0f), afterStart = new Vector2(105f, 0f);
            Vector2 beforeBack = new Vector2(105f, 100f), afterBack = new Vector2(95f, 100f);

            Assert.That(laps.Step(beforeStart, afterStart), Is.EqualTo(LapEvent.LapStarted));
            // Crossing the start again without visiting gate 1 does nothing (e.g. reversing over the line).
            Assert.That(laps.Step(beforeStart, afterStart), Is.EqualTo(LapEvent.None));
            Assert.That(laps.Step(beforeBack, afterBack), Is.EqualTo(LapEvent.Checkpoint));
            Assert.That(laps.Step(beforeStart, afterStart), Is.EqualTo(LapEvent.LapCompleted));
            Assert.That(laps.CompletedLaps, Is.EqualTo(1));
            Assert.That(laps.NextGate, Is.EqualTo(1));
        }

        [Test]
        public void ResampleClosed_GivesEvenSpacing()
        {
            Vector2[] dense = TrackGeometry.SampleCenterline(Box(), 20);
            Vector2[] even = TrackGeometry.ResampleClosed(dense, 5f);
            float length = TrackGeometry.ClosedLength(dense);
            float step = length / even.Length;
            for (int i = 0; i < even.Length; i++)
                Assert.That(Vector2.Distance(even[i], even[(i + 1) % even.Length]), Is.EqualTo(step).Within(step * 0.2f));
        }
    }

    public class GhostBufferTests
    {
        [Test]
        public void Evaluate_InterpolatesBetweenSamples_AndClampsAtTheEnds()
        {
            var g = new GhostBuffer(10);
            g.Add(0, new Vector3(0f, 0f, 0f), Quaternion.identity);
            g.Add(1, new Vector3(1f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));
            g.Add(2, new Vector3(2f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));

            g.Evaluate(0.01f, out Vector3 p, out Quaternion r); // half way between samples 0 and 1 at 50 Hz
            Assert.That(p.x, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(Quaternion.Angle(r, Quaternion.Euler(0f, 45f, 0f)), Is.LessThan(0.01f));

            g.Evaluate(-1f, out p, out _);
            Assert.That(p.x, Is.EqualTo(0f));
            g.Evaluate(100f, out p, out _);
            Assert.That(p.x, Is.EqualTo(2f));
            Assert.That(g.Duration, Is.EqualTo(0.04f).Within(1e-6f));
        }

        [Test]
        public void Add_RefusesSamplesWhenFull()
        {
            var g = new GhostBuffer(2);
            Assert.That(g.Add(0, Vector3.zero, Quaternion.identity), Is.True);
            Assert.That(g.Add(1, Vector3.zero, Quaternion.identity), Is.True);
            Assert.That(g.Add(2, Vector3.zero, Quaternion.identity), Is.False);
            Assert.That(g.Count, Is.EqualTo(2));
        }
    }

    public class GhostRecorderTests
    {
        private static void DriveLap(GhostRecorder rec, int startStep, int steps, out bool best)
        {
            rec.BeginLap(startStep, new Vector3(0f, 0f, 0f), Quaternion.identity);
            for (int s = startStep + 1; s < startStep + steps; s++) rec.Record(s, new Vector3(s, 0f, 0f), Quaternion.identity);
            best = rec.EndLap(startStep + steps, new Vector3(startStep + steps, 0f, 0f), Quaternion.identity, steps);
        }

        [Test]
        public void ALapOfNSteps_HasNPlusOneSamples_WithContinuousSequenceNumbers()
        {
            var rec = new GhostRecorder(10f);
            DriveLap(rec, 37, 200, out bool best);
            Assert.That(best, Is.True);
            Assert.That(rec.BestLap.Count, Is.EqualTo(201));
            Assert.That(rec.BestLapSteps, Is.EqualTo(200));
            for (int i = 1; i < rec.BestLap.Count; i++)
                Assert.That((ushort)(rec.BestLap.Sequence[i] - rec.BestLap.Sequence[i - 1]), Is.EqualTo(1));
        }

        [Test]
        public void OnlyAFasterLapReplacesTheBest()
        {
            var rec = new GhostRecorder(10f);
            DriveLap(rec, 0, 300, out bool first);
            DriveLap(rec, 300, 320, out bool slower);
            DriveLap(rec, 620, 280, out bool faster);
            Assert.That(first, Is.True);
            Assert.That(slower, Is.False);
            Assert.That(faster, Is.True);
            Assert.That(rec.BestLapSteps, Is.EqualTo(280));
            Assert.That(rec.BestLap.Count, Is.EqualTo(281));
        }

        [Test]
        public void ALapTooLongForTheBuffer_IsNotKept()
        {
            var rec = new GhostRecorder(1f); // 51 samples
            DriveLap(rec, 0, 100, out bool best);
            Assert.That(best, Is.False);
            Assert.That(rec.HasBestLap, Is.False);
        }

        [Test]
        public void SequenceNumbersWrapAt65536()
        {
            var rec = new GhostRecorder(10f);
            DriveLap(rec, 65530, 10, out _);
            Assert.That(rec.BestLap.Sequence[0], Is.EqualTo(65530));
            Assert.That(rec.BestLap.Sequence[6], Is.EqualTo(0));
        }
    }
}
