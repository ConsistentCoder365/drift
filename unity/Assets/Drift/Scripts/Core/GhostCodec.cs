using System;
using UnityEngine;

namespace Drift.Core
{
    /// <summary>
    /// Binary ghost format, little-endian. Mirrored by backend/app/ghost.py; change both together.
    ///
    /// Header (36 bytes)
    ///   0  char[4]  magic "DRGH"
    ///   4  u8       version (1)
    ///   5  u8       sample rate in Hz (50)
    ///   6  u16      reserved (0)
    ///   8  u32      sample count
    ///   12 f32[3]   bounds min (x, y, z)
    ///   24 f32[3]   bounds max (x, y, z)
    ///
    /// Sample (12 bytes, vs 32 raw: 7 floats + a sequence number)
    ///   0  u16      sequence number (physics step, wraps at 65536)
    ///   2  u16[3]   position, quantised to 16 bits per axis inside the header bounds
    ///   8  u32      rotation, smallest-three: bits 31-30 = index of the dropped (largest)
    ///               component, then the other three in x,y,z,w order at 10 bits each,
    ///               mapped from [-1/sqrt2, 1/sqrt2] onto codes 0..1022 (511 = exactly 0).
    /// </summary>
    public static class GhostCodec
    {
        public const int HeaderSize = 36;
        public const int SampleSize = 12;
        public const int RawSampleSize = 32;
        public const byte Version = 1;

        private const float RotRange = 0.70710678f; // 1/sqrt(2): bound on the three smallest components
        private const int RotBits = 10;
        private const uint RotMask = (1 << RotBits) - 1;
        // 1022 steps, not 1023: with an even step count 0 lands exactly on code 511, so the
        // zero x and z components of a yaw-only rotation (every rotation this car makes) are exact.
        private const int RotSteps = (1 << RotBits) - 2;

        public static int EncodedSize(int sampleCount)
        {
            return HeaderSize + sampleCount * SampleSize;
        }

        // ---- position --------------------------------------------------------------------

        public static ushort QuantizePosition(float value, float min, float max)
        {
            float range = max - min;
            if (range <= 0f) return 0;
            float t = Mathf.Clamp01((value - min) / range);
            return (ushort)Mathf.RoundToInt(t * 65535f);
        }

        public static float DequantizePosition(ushort q, float min, float max)
        {
            return min + (q / 65535f) * (max - min);
        }

        // ---- rotation (smallest three) ---------------------------------------------------

        public static uint PackRotation(Quaternion q)
        {
            float x = q.x, y = q.y, z = q.z, w = q.w;
            float len = Mathf.Sqrt(x * x + y * y + z * z + w * w);
            if (len < 1e-6f) { x = 0f; y = 0f; z = 0f; w = 1f; }
            else { x /= len; y /= len; z /= len; w /= len; }

            int largest = 0;
            float maxAbs = Mathf.Abs(x);
            if (Mathf.Abs(y) > maxAbs) { largest = 1; maxAbs = Mathf.Abs(y); }
            if (Mathf.Abs(z) > maxAbs) { largest = 2; maxAbs = Mathf.Abs(z); }
            if (Mathf.Abs(w) > maxAbs) { largest = 3; }

            // q and -q are the same rotation: flip so the dropped component is positive,
            // then it can be rebuilt as sqrt(1 - a^2 - b^2 - c^2).
            float largestValue = largest == 0 ? x : largest == 1 ? y : largest == 2 ? z : w;
            if (largestValue < 0f) { x = -x; y = -y; z = -z; w = -w; }

            float a, b, c;
            switch (largest)
            {
                case 0: a = y; b = z; c = w; break;
                case 1: a = x; b = z; c = w; break;
                case 2: a = x; b = y; c = w; break;
                default: a = x; b = y; c = z; break;
            }
            return ((uint)largest << 30)
                   | (QuantizeComponent(a) << 20)
                   | (QuantizeComponent(b) << 10)
                   | QuantizeComponent(c);
        }

        public static Quaternion UnpackRotation(uint packed)
        {
            int largest = (int)(packed >> 30);
            float a = DequantizeComponent((packed >> 20) & RotMask);
            float b = DequantizeComponent((packed >> 10) & RotMask);
            float c = DequantizeComponent(packed & RotMask);
            float d = Mathf.Sqrt(Mathf.Max(0f, 1f - a * a - b * b - c * c));

            float x, y, z, w;
            switch (largest)
            {
                case 0: x = d; y = a; z = b; w = c; break;
                case 1: x = a; y = d; z = b; w = c; break;
                case 2: x = a; y = b; z = d; w = c; break;
                default: x = a; y = b; z = c; w = d; break;
            }
            float len = Mathf.Sqrt(x * x + y * y + z * z + w * w);
            return new Quaternion(x / len, y / len, z / len, w / len);
        }

        private static uint QuantizeComponent(float v)
        {
            float t = Mathf.Clamp01((v + RotRange) / (2f * RotRange));
            return (uint)Mathf.RoundToInt(t * RotSteps);
        }

        private static float DequantizeComponent(uint q)
        {
            return Mathf.Min(q, RotSteps) / (float)RotSteps * (2f * RotRange) - RotRange;
        }

        // ---- whole ghost -----------------------------------------------------------------

        public static byte[] Encode(GhostBuffer ghost, Vector3 boundsMin, Vector3 boundsMax)
        {
            var bytes = new byte[EncodedSize(ghost.Count)];
            Encode(ghost, boundsMin, boundsMax, bytes);
            return bytes;
        }

        /// <summary>Encode into a caller-supplied array. Returns the number of bytes written.</summary>
        public static int Encode(GhostBuffer ghost, Vector3 boundsMin, Vector3 boundsMax, byte[] dest)
        {
            int size = EncodedSize(ghost.Count);
            if (dest.Length < size) throw new ArgumentException("Destination buffer is too small.");

            dest[0] = (byte)'D'; dest[1] = (byte)'R'; dest[2] = (byte)'G'; dest[3] = (byte)'H';
            dest[4] = Version;
            dest[5] = (byte)ghost.SampleRate;
            WriteU16(dest, 6, 0);
            WriteU32(dest, 8, (uint)ghost.Count);
            WriteF32(dest, 12, boundsMin.x); WriteF32(dest, 16, boundsMin.y); WriteF32(dest, 20, boundsMin.z);
            WriteF32(dest, 24, boundsMax.x); WriteF32(dest, 28, boundsMax.y); WriteF32(dest, 32, boundsMax.z);

            int o = HeaderSize;
            for (int i = 0; i < ghost.Count; i++, o += SampleSize)
            {
                Vector3 p = ghost.Positions[i];
                WriteU16(dest, o, ghost.Sequence[i]);
                WriteU16(dest, o + 2, QuantizePosition(p.x, boundsMin.x, boundsMax.x));
                WriteU16(dest, o + 4, QuantizePosition(p.y, boundsMin.y, boundsMax.y));
                WriteU16(dest, o + 6, QuantizePosition(p.z, boundsMin.z, boundsMax.z));
                WriteU32(dest, o + 8, PackRotation(ghost.Rotations[i]));
            }
            return size;
        }

        /// <summary>Decode into `into` (which must be big enough). Returns false with a reason on bad data.</summary>
        public static bool TryDecode(byte[] data, GhostBuffer into, out string error)
        {
            error = null;
            if (data == null || data.Length < HeaderSize) { error = "too short"; return false; }
            if (data[0] != 'D' || data[1] != 'R' || data[2] != 'G' || data[3] != 'H') { error = "bad magic"; return false; }
            if (data[4] != Version) { error = "unsupported version"; return false; }
            if (data[5] != into.SampleRate) { error = "sample rate mismatch"; return false; }

            uint count = ReadU32(data, 8);
            if (data.Length != HeaderSize + (long)count * SampleSize) { error = "length mismatch"; return false; }
            if (count > into.Capacity) { error = "too many samples"; return false; }

            var min = new Vector3(ReadF32(data, 12), ReadF32(data, 16), ReadF32(data, 20));
            var max = new Vector3(ReadF32(data, 24), ReadF32(data, 28), ReadF32(data, 32));

            into.Clear();
            int o = HeaderSize;
            for (int i = 0; i < count; i++, o += SampleSize)
            {
                var p = new Vector3(
                    DequantizePosition(ReadU16(data, o + 2), min.x, max.x),
                    DequantizePosition(ReadU16(data, o + 4), min.y, max.y),
                    DequantizePosition(ReadU16(data, o + 6), min.z, max.z));
                into.Add(ReadU16(data, o), p, UnpackRotation(ReadU32(data, o + 8)));
            }
            return true;
        }

        // ---- little-endian helpers (no BinaryWriter, so no allocations) --------------------

        private static void WriteU16(byte[] b, int o, ushort v)
        {
            b[o] = (byte)v;
            b[o + 1] = (byte)(v >> 8);
        }

        private static void WriteU32(byte[] b, int o, uint v)
        {
            b[o] = (byte)v;
            b[o + 1] = (byte)(v >> 8);
            b[o + 2] = (byte)(v >> 16);
            b[o + 3] = (byte)(v >> 24);
        }

        private static void WriteF32(byte[] b, int o, float v)
        {
            WriteU32(b, o, (uint)BitConverter.SingleToInt32Bits(v));
        }

        private static ushort ReadU16(byte[] b, int o)
        {
            return (ushort)(b[o] | (b[o + 1] << 8));
        }

        private static uint ReadU32(byte[] b, int o)
        {
            return (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
        }

        private static float ReadF32(byte[] b, int o)
        {
            return BitConverter.Int32BitsToSingle((int)ReadU32(b, o));
        }
    }
}
