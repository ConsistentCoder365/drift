using System;
using System.Globalization;
using System.IO;
using System.Text;
using Drift.Core;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    /// <summary>
    /// Writes a ghost encoded by the C# codec, plus the exact input poses, to
    /// backend/tests/fixtures. The Python tests decode it, which proves the client and server
    /// read the same bytes the same way. Regenerate with DRIFT_WRITE_FIXTURES=1 dotnet test.
    /// </summary>
    public class CrossLanguageFixture
    {
        [Test]
        public void WriteCSharpGhostFixture()
        {
            if (Environment.GetEnvironmentVariable("DRIFT_WRITE_FIXTURES") != "1")
                Assert.Ignore("Set DRIFT_WRITE_FIXTURES=1 to regenerate backend/tests/fixtures.");

            var min = new Vector3(-170f, -1f, -40f);
            var max = new Vector3(205f, 3f, 180f);
            var ghost = new GhostBuffer(64);
            var json = new StringBuilder("{\n  \"bounds_min\": [-170, -1, -40],\n  \"bounds_max\": [205, 3, 180],\n  \"samples\": [\n");
            for (int i = 0; i < 64; i++)
            {
                var p = new Vector3(-150f + i * 5.3f, 0f, 10f + 3f * Mathf.Sin(i * 0.3f));
                Quaternion r = Quaternion.Euler(i * 0.7f, i * 11f - 200f, -i * 0.4f);
                var seq = (ushort)(65500 + i); // crosses the 16-bit wrap
                ghost.Add(seq, p, r);
                json.Append(string.Format(CultureInfo.InvariantCulture,
                    "    {{\"seq\": {0}, \"pos\": [{1:R}, {2:R}, {3:R}], \"rot\": [{4:R}, {5:R}, {6:R}, {7:R}]}}{8}\n",
                    seq, p.x, p.y, p.z, r.x, r.y, r.z, r.w, i < 63 ? "," : ""));
            }
            json.Append("  ]\n}\n");

            string dir = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "../../../../../backend/tests/fixtures"));
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "csharp_ghost.bin"), GhostCodec.Encode(ghost, min, max));
            File.WriteAllText(Path.Combine(dir, "csharp_ghost.json"), json.ToString());
            TestContext.WriteLine("Wrote fixtures to " + dir);
        }
    }
}
