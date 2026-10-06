using System;
using Drift.Core;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    public class GhostCodecTests
    {
        private static readonly Vector3 Min = new Vector3(-170f, -1f, -40f);
        private static readonly Vector3 Max = new Vector3(205f, 3f, 180f);

        /// <summary>Angle in degrees between two rotations (q and -q count as equal).</summary>
        private static float AngleBetween(Quaternion a, Quaternion b)
        {
            float dot = Mathf.Abs(a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w);
            return 2f * Mathf.Acos(Mathf.Min(1f, dot)) * Mathf.Rad2Deg;
        }

        private static Quaternion RandomRotation(System.Random rng)
        {
            // Uniform random rotation (Shoemake).
            double u1 = rng.NextDouble(), u2 = rng.NextDouble() * 2 * Math.PI, u3 = rng.NextDouble() * 2 * Math.PI;
            double a = Math.Sqrt(1 - u1), b = Math.Sqrt(u1);
            return new Quaternion((float)(a * Math.Sin(u2)), (float)(a * Math.Cos(u2)), (float)(b * Math.Sin(u3)), (float)(b * Math.Cos(u3)));
        }

        /// <summary>A plausible lap: a car driving a wobbly loop for `samples` steps.</summary>
        private static GhostBuffer MakeLap(int samples)
        {
            var g = new GhostBuffer(samples);
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)samples * Mathf.PI * 2f;
                var p = new Vector3(20f + 150f * Mathf.Cos(t), 0f, 70f + 90f * Mathf.Sin(t) + 5f * Mathf.Sin(7f * t));
                Quaternion r = Quaternion.Euler(0f, -t * Mathf.Rad2Deg + 20f * Mathf.Sin(3f * t), 0f);
                g.Add((ushort)(1000 + i), p, r);
            }
            return g;
        }

        [Test]
        public void Position_QuantisationErrorIsUnderHalfAStep()
        {
            float range = Max.x - Min.x;
            float halfStep = range / 65535f * 0.5f;
            var rng = new System.Random(7);
            for (int i = 0; i < 2000; i++)
            {
                float v = Min.x + (float)rng.NextDouble() * range;
                float back = GhostCodec.DequantizePosition(GhostCodec.QuantizePosition(v, Min.x, Max.x), Min.x, Max.x);
                Assert.That(Mathf.Abs(back - v), Is.LessThanOrEqualTo(halfStep + 1e-4f));
            }
        }

        [Test]
        public void Position_OutsideBoundsIsClamped()
        {
            Assert.That(GhostCodec.QuantizePosition(-1000f, Min.x, Max.x), Is.EqualTo(0));
            Assert.That(GhostCodec.QuantizePosition(1000f, Min.x, Max.x), Is.EqualTo(65535));
        }

        [Test]
        public void Rotation_SmallestThreeRoundTripIsWithinAQuarterDegree()
        {
            var rng = new System.Random(42);
            float worst = 0f;
            for (int i = 0; i < 5000; i++)
            {
                Quaternion q = RandomRotation(rng);
                Quaternion back = GhostCodec.UnpackRotation(GhostCodec.PackRotation(q));
                worst = Mathf.Max(worst, AngleBetween(q, back));
            }
            Assert.That(worst, Is.LessThan(0.25f), "worst-case error " + worst + " degrees");
        }

        [Test]
        public void Rotation_QAndMinusQPackTheSame()
        {
            Quaternion q = Quaternion.Euler(10f, 250f, -30f);
            var neg = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            Assert.That(GhostCodec.PackRotation(neg), Is.EqualTo(GhostCodec.PackRotation(q)));
        }

        [Test]
        public void Rotation_IdentityAndYawOnlyRoundTrip()
        {
            Assert.That(AngleBetween(Quaternion.identity, GhostCodec.UnpackRotation(GhostCodec.PackRotation(Quaternion.identity))), Is.LessThan(0.01f));
            for (int deg = -180; deg <= 180; deg += 15)
            {
                Quaternion q = Quaternion.Euler(0f, deg, 0f);
                Assert.That(AngleBetween(q, GhostCodec.UnpackRotation(GhostCodec.PackRotation(q))), Is.LessThan(0.2f), "yaw " + deg);
            }
        }

        [Test]
        public void Encode_ThenDecode_RoundTripsALap()
        {
            GhostBuffer lap = MakeLap(1500);
            byte[] bytes = GhostCodec.Encode(lap, Min, Max);
            Assert.That(bytes.Length, Is.EqualTo(GhostCodec.HeaderSize + 1500 * GhostCodec.SampleSize));

            var decoded = new GhostBuffer(6000);
            Assert.That(GhostCodec.TryDecode(bytes, decoded, out string error), Is.True, error);
            Assert.That(decoded.Count, Is.EqualTo(lap.Count));

            float posTolerance = (Max.x - Min.x) / 65535f; // one quantisation step
            for (int i = 0; i < lap.Count; i++)
            {
                Assert.That(decoded.Sequence[i], Is.EqualTo(lap.Sequence[i]));
                Assert.That(Vector3.Distance(decoded.Positions[i], lap.Positions[i]), Is.LessThan(posTolerance * 1.5f));
                Assert.That(AngleBetween(decoded.Rotations[i], lap.Rotations[i]), Is.LessThan(0.25f));
            }
        }

        [Test]
        public void Encode_IsAboutTwoAndAHalfTimesSmallerThanRaw()
        {
            GhostBuffer lap = MakeLap(1500);
            int raw = lap.Count * GhostCodec.RawSampleSize;
            int packed = GhostCodec.Encode(lap, Min, Max).Length;
            float ratio = raw / (float)packed;
            Assert.That(ratio, Is.GreaterThan(2.6f));
            TestContext.WriteLine("1500 samples: raw " + raw + " B, packed " + packed + " B, ratio " + ratio.ToString("0.00") + "x");
        }

        [Test]
        public void Encode_WritesTheDocumentedHeader()
        {
            GhostBuffer lap = MakeLap(3);
            byte[] b = GhostCodec.Encode(lap, Min, Max);
            Assert.That(new[] { b[0], b[1], b[2], b[3] }, Is.EqualTo(new[] { (byte)'D', (byte)'R', (byte)'G', (byte)'H' }));
            Assert.That(b[4], Is.EqualTo(GhostCodec.Version));
            Assert.That(b[5], Is.EqualTo(50));
            Assert.That(b[8] | (b[9] << 8) | (b[10] << 16) | (b[11] << 24), Is.EqualTo(3), "sample count is little-endian u32");
            Assert.That(BitConverter.ToSingle(b, 12), Is.EqualTo(Min.x), "BitConverter is little-endian on every Unity target");
            Assert.That(b[GhostCodec.HeaderSize] | (b[GhostCodec.HeaderSize + 1] << 8), Is.EqualTo(1000), "first sequence number");
        }

        [Test]
        public void Decode_RejectsBadData()
        {
            byte[] good = GhostCodec.Encode(MakeLap(10), Min, Max);
            var into = new GhostBuffer(100);

            Assert.That(GhostCodec.TryDecode(new byte[5], into, out _), Is.False);
            Assert.That(GhostCodec.TryDecode(null, into, out _), Is.False);

            var badMagic = (byte[])good.Clone();
            badMagic[0] = (byte)'X';
            Assert.That(GhostCodec.TryDecode(badMagic, into, out string e1), Is.False);
            Assert.That(e1, Is.EqualTo("bad magic"));

            var truncated = new byte[good.Length - 1];
            Array.Copy(good, truncated, truncated.Length);
            Assert.That(GhostCodec.TryDecode(truncated, into, out string e2), Is.False);
            Assert.That(e2, Is.EqualTo("length mismatch"));

            var tooSmall = new GhostBuffer(5);
            Assert.That(GhostCodec.TryDecode(good, tooSmall, out string e3), Is.False);
            Assert.That(e3, Is.EqualTo("too many samples"));
        }

        [Test]
        public void Encode_IntoCallerBuffer_DoesNotNeedANewArray()
        {
            GhostBuffer lap = MakeLap(100);
            var dest = new byte[GhostCodec.EncodedSize(100) + 50];
            int written = GhostCodec.Encode(lap, Min, Max, dest);
            Assert.That(written, Is.EqualTo(GhostCodec.EncodedSize(100)));
            Assert.Throws<ArgumentException>(() => GhostCodec.Encode(lap, Min, Max, new byte[10]));
        }
    }
}
