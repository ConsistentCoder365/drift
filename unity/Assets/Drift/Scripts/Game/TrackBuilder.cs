using System.Collections.Generic;
using Drift.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Drift.Game
{
    /// <summary>What the race needs to know about the built track.</summary>
    public sealed class TrackRuntime
    {
        public TrackDefinition Definition;
        public Gate[] Gates;
        public Vector3 SpawnPosition;
        public Quaternion SpawnRotation;
        /// <summary>Quantisation bounds for ghost positions.</summary>
        public Vector3 GhostBoundsMin;
        public Vector3 GhostBoundsMax;
        public float Length;
        public GameObject Root;
        public GameObject Props;
    }

    /// <summary>
    /// Builds the whole track from its JSON definition at load: road, curbs, walls with
    /// colliders, start gantry, scenery. No art assets are needed.
    /// Road, curbs and walls are one mesh each (few draw calls by construction); the scenery is
    /// left as many small objects on purpose, so static batching and GPU instancing have
    /// something to fix, and you can measure the before and after (see PerfOptions).
    /// </summary>
    public static class TrackBuilder
    {
        private const float Spacing = 3f;
        private const float CurbWidth = 1.2f;
        private const float WallThickness = 1.0f;
        private const float WallHeight = 1.0f;

        public static TrackRuntime Build(TrackDefinition def, bool spawnScenery = true)
        {
            var root = new GameObject("Track");
            Vector2[] center = TrackGeometry.ResampleClosed(TrackGeometry.SampleCenterline(def, 24), Spacing);
            int n = center.Length;
            var tangent = new Vector2[n];
            var right = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                tangent[i] = (center[(i + 1) % n] - center[(i - 1 + n) % n]).normalized;
                right[i] = new Vector2(tangent[i].y, -tangent[i].x);
            }

            float half = def.road_width * 0.5f;
            BuildGround(root.transform, center);
            BuildStrip(root.transform, "Road", center, right, -half, half, 0.01f, MaterialLibrary.Get("Road"), def.road_width);
            BuildCenterLine(root.transform, center, right);
            BuildCurbs(root.transform, center, right, half);
            BuildWalls(root.transform, center, right, half + CurbWidth + WallThickness * 0.5f);

            Gate[] gates = TrackGeometry.BuildGates(def);
            BuildStartGantry(root.transform, gates[0], def.road_width);

            GameObject props = null;
            if (spawnScenery) props = BuildScenery(root.transform, center, right, half);

            Vector2 min = center[0], max = center[0];
            for (int i = 1; i < n; i++)
            {
                min = Vector2.Min(min, center[i]);
                max = Vector2.Max(max, center[i]);
            }
            float margin = half + 20f;

            Vector3 fwd3 = new Vector3(gates[0].Forward.x, 0f, gates[0].Forward.y);
            Vector3 gate3 = new Vector3(gates[0].Center.x, 0f, gates[0].Center.y);
            return new TrackRuntime
            {
                Definition = def,
                Gates = gates,
                SpawnPosition = gate3 - fwd3 * 6f,
                SpawnRotation = Quaternion.LookRotation(fwd3, Vector3.up),
                GhostBoundsMin = new Vector3(min.x - margin, -1f, min.y - margin),
                GhostBoundsMax = new Vector3(max.x + margin, 3f, max.y + margin),
                Length = TrackGeometry.ClosedLength(center),
                Root = root,
                Props = props,
            };
        }

        private static Vector3 V3(Vector2 p, float y)
        {
            return new Vector3(p.x, y, p.y);
        }

        private static GameObject MeshObject(Transform parent, string name, Mesh mesh, params Material[] materials)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = materials;
            return go;
        }

        /// <summary>A flat closed ribbon between two lateral offsets.</summary>
        private static void BuildStrip(Transform parent, string name, Vector2[] c, Vector2[] r, float from, float to, float y, Material mat, float uvScale)
        {
            int n = c.Length;
            var verts = new Vector3[(n + 1) * 2];
            var uvs = new Vector2[verts.Length];
            var normals = new Vector3[verts.Length];
            var tris = new int[n * 6];
            float dist = 0f;
            for (int i = 0; i <= n; i++)
            {
                int k = i % n;
                if (i > 0) dist += Vector2.Distance(c[i - 1], c[k]);
                verts[i * 2] = V3(c[k] + r[k] * from, y);
                verts[i * 2 + 1] = V3(c[k] + r[k] * to, y);
                uvs[i * 2] = new Vector2(0f, dist / uvScale);
                uvs[i * 2 + 1] = new Vector2(1f, dist / uvScale);
                normals[i * 2] = normals[i * 2 + 1] = Vector3.up;
            }
            for (int i = 0; i < n; i++)
            {
                int a = i * 2, t = i * 6;
                // Clockwise seen from above, so the face points up.
                tris[t] = a; tris[t + 1] = a + 2; tris[t + 2] = a + 1;
                tris[t + 3] = a + 1; tris[t + 4] = a + 2; tris[t + 5] = a + 3;
            }
            var mesh = new Mesh { name = name };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.normals = normals;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            MeshObject(parent, name, mesh, mat);
        }

        private static void BuildCenterLine(Transform parent, Vector2[] c, Vector2[] r)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            const float w = 0.18f;
            for (int i = 0; i < c.Length; i += 2)
            {
                int j = (i + 1) % c.Length;
                int b = verts.Count;
                verts.Add(V3(c[i] - r[i] * w, 0.02f));
                verts.Add(V3(c[i] + r[i] * w, 0.02f));
                verts.Add(V3(c[j] - r[j] * w, 0.02f));
                verts.Add(V3(c[j] + r[j] * w, 0.02f));
                tris.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
            }
            var mesh = new Mesh { name = "CenterLine" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            MeshObject(parent, "CenterLine", mesh, MaterialLibrary.Get("RoadLine"));
        }

        /// <summary>Red and white curbs on both edges: one mesh, two submeshes.</summary>
        private static void BuildCurbs(Transform parent, Vector2[] c, Vector2[] r, float half)
        {
            var verts = new List<Vector3>();
            var red = new List<int>();
            var white = new List<int>();
            int n = c.Length;
            for (int side = -1; side <= 1; side += 2)
            {
                float inner = side * half, outer = side * (half + CurbWidth);
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    int b = verts.Count;
                    verts.Add(V3(c[i] + r[i] * inner, 0.03f));
                    verts.Add(V3(c[i] + r[i] * outer, 0.03f));
                    verts.Add(V3(c[j] + r[j] * inner, 0.03f));
                    verts.Add(V3(c[j] + r[j] * outer, 0.03f));
                    List<int> target = i % 2 == 0 ? red : white;
                    if (side < 0) target.AddRange(new[] { b, b + 1, b + 2, b + 2, b + 1, b + 3 });
                    else target.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
                }
            }
            var mesh = new Mesh { name = "Curbs", subMeshCount = 2 };
            mesh.SetVertices(verts);
            mesh.SetTriangles(red, 0);
            mesh.SetTriangles(white, 1);
            mesh.RecalculateNormals();
            MeshObject(parent, "Curbs", mesh, MaterialLibrary.Get("CurbRed"), MaterialLibrary.Get("CurbWhite"));
        }

        /// <summary>
        /// Both walls as one visual mesh, plus a static BoxCollider per segment. Box colliders
        /// are cheaper and more robust for a fast car than one big concave MeshCollider.
        /// </summary>
        private static void BuildWalls(Transform parent, Vector2[] c, Vector2[] r, float offset)
        {
            var wallRoot = new GameObject("Walls");
            wallRoot.transform.SetParent(parent, false);
            var colliderRoot = new GameObject("WallColliders");
            colliderRoot.transform.SetParent(wallRoot.transform, false);
            var wallMaterial = Compat.Frictionless();

            var verts = new List<Vector3>();
            var tris = new List<int>();
            int n = c.Length;
            float ht = WallThickness * 0.5f;

            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    Vector2 a = c[i] + r[i] * (side * offset);
                    Vector2 b = c[j] + r[j] * (side * offset);

                    // Faces: inner (toward the road), top, outer.
                    AddQuad(verts, tris, V3(c[i] + r[i] * (side * (offset - ht)), 0f), V3(c[j] + r[j] * (side * (offset - ht)), 0f),
                            V3(c[j] + r[j] * (side * (offset - ht)), WallHeight), V3(c[i] + r[i] * (side * (offset - ht)), WallHeight), side < 0);
                    AddQuad(verts, tris, V3(c[i] + r[i] * (side * (offset - ht)), WallHeight), V3(c[j] + r[j] * (side * (offset - ht)), WallHeight),
                            V3(c[j] + r[j] * (side * (offset + ht)), WallHeight), V3(c[i] + r[i] * (side * (offset + ht)), WallHeight), side < 0);
                    AddQuad(verts, tris, V3(c[i] + r[i] * (side * (offset + ht)), 0f), V3(c[j] + r[j] * (side * (offset + ht)), 0f),
                            V3(c[j] + r[j] * (side * (offset + ht)), WallHeight), V3(c[i] + r[i] * (side * (offset + ht)), WallHeight), side > 0);

                    // Collider for this segment, slightly longer than the segment so there are no gaps.
                    Vector2 mid = (a + b) * 0.5f;
                    Vector2 dir = b - a;
                    float len = dir.magnitude;
                    if (len < 1e-3f) continue;
                    var col = new GameObject("Wall");
                    col.transform.SetParent(colliderRoot.transform, false);
                    col.transform.position = V3(mid, WallHeight * 0.5f);
                    col.transform.rotation = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y), Vector3.up);
                    var box = col.AddComponent<BoxCollider>();
                    box.size = new Vector3(WallThickness, WallHeight + 0.4f, len + 0.8f);
                    box.sharedMaterial = wallMaterial;
                    col.AddComponent<TrackWall>();
                }
            }

            var mesh = new Mesh { name = "Walls" };
            if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            MeshObject(wallRoot.transform, "WallMesh", mesh, MaterialLibrary.Get("Wall"));
        }

        /// <summary>Adds quad a-b-c-d, flipping winding so it faces the requested side.</summary>
        private static void AddQuad(List<Vector3> verts, List<int> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 d, bool flip)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            if (flip) tris.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
            else tris.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
        }

        private static void BuildGround(Transform parent, Vector2[] c)
        {
            Vector2 min = c[0], max = c[0];
            foreach (Vector2 p in c)
            {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            Vector2 mid = (min + max) * 0.5f;
            Vector2 size = (max - min) + new Vector2(400f, 400f);
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            Object.DestroyImmediate(ground.GetComponent<Collider>());
            ground.transform.SetParent(parent, false);
            ground.transform.position = new Vector3(mid.x, 0f, mid.y);
            ground.transform.localScale = new Vector3(size.x / 10f, 1f, size.y / 10f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.Get("Ground");
        }

        private static void BuildStartGantry(Transform parent, Gate gate, float roadWidth)
        {
            var root = new GameObject("StartFinish");
            root.transform.SetParent(parent, false);
            Vector3 center = new Vector3(gate.Center.x, 0f, gate.Center.y);
            Vector3 fwd = new Vector3(gate.Forward.x, 0f, gate.Forward.y);
            root.transform.SetPositionAndRotation(center, Quaternion.LookRotation(fwd, Vector3.up));

            // Checkered line: two rows of alternating squares.
            const int squares = 14;
            float sq = roadWidth / squares;
            Material dark = MaterialLibrary.Get("Checker");
            Material light = MaterialLibrary.Get("CurbWhite");
            for (int row = 0; row < 2; row++)
            for (int i = 0; i < squares; i++)
            {
                GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.DestroyImmediate(q.GetComponent<Collider>());
                q.transform.SetParent(root.transform, false);
                q.transform.localPosition = new Vector3(-roadWidth * 0.5f + sq * (i + 0.5f), 0.04f, (row - 0.5f) * sq);
                q.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                q.transform.localScale = new Vector3(sq, sq, 1f);
                q.GetComponent<MeshRenderer>().sharedMaterial = (i + row) % 2 == 0 ? dark : light;
            }

            Material post = MaterialLibrary.Get("Lamp");
            float x = roadWidth * 0.5f + CurbWidth + 1.5f;
            Block(root.transform, post, new Vector3(-x, 3f, 0f), new Vector3(0.6f, 6f, 0.6f));
            Block(root.transform, post, new Vector3(x, 3f, 0f), new Vector3(0.6f, 6f, 0.6f));
            Block(root.transform, MaterialLibrary.Get("CarBody"), new Vector3(0f, 6.2f, 0f), new Vector3(x * 2f + 0.6f, 1.2f, 0.5f));
        }

        private static GameObject Block(Transform parent, Material mat, Vector3 localPos, Vector3 scale)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>
        /// Trees, lamp posts and cones along both sides, a few hundred renderers in all.
        /// Placement is seeded, so the scenery is the same every run and every build.
        /// </summary>
        private static GameObject BuildScenery(Transform parent, Vector2[] c, Vector2[] r, float half)
        {
            var root = new GameObject("Scenery");
            root.transform.SetParent(parent, false);
            var rng = new System.Random(1234);
            Material trunk = MaterialLibrary.Get("Trunk");
            Material leaves = MaterialLibrary.Get("Leaves");
            Material lamp = MaterialLibrary.Get("Lamp");
            Material cone = MaterialLibrary.Get("Cone");
            float clear = half + CurbWidth + WallThickness + 3f;

            for (int i = 0; i < c.Length; i += 3)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    float offset = clear + (float)rng.NextDouble() * 14f;
                    Vector2 p = c[i] + r[i] * (side * offset);
                    if (DistanceToCenterline(p, c) < clear) continue;

                    var item = new GameObject("Prop");
                    item.transform.SetParent(root.transform, false);
                    item.transform.position = V3(p, 0f);
                    item.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                    double roll = rng.NextDouble();
                    if (roll < 0.55)
                    {
                        float h = 2.5f + (float)rng.NextDouble() * 2f;
                        Block(item.transform, trunk, new Vector3(0f, h * 0.5f, 0f), new Vector3(0.45f, h, 0.45f));
                        GameObject top = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                        Object.DestroyImmediate(top.GetComponent<Collider>());
                        top.transform.SetParent(item.transform, false);
                        top.transform.localPosition = new Vector3(0f, h + 1.2f, 0f);
                        top.transform.localScale = Vector3.one * (2.6f + (float)rng.NextDouble());
                        top.GetComponent<MeshRenderer>().sharedMaterial = leaves;
                    }
                    else if (roll < 0.8)
                    {
                        Block(item.transform, lamp, new Vector3(0f, 3.5f, 0f), new Vector3(0.25f, 7f, 0.25f));
                        Block(item.transform, lamp, new Vector3(0f, 7f, side * -0.8f), new Vector3(0.25f, 0.25f, 1.8f));
                    }
                    else
                    {
                        for (int k = 0; k < 3; k++)
                            Block(item.transform, cone, new Vector3(k * 0.9f - 0.9f, 0.35f, 0f), new Vector3(0.45f, 0.7f, 0.45f));
                    }
                }
            }
            return root;
        }

        private static float DistanceToCenterline(Vector2 p, Vector2[] c)
        {
            float best = float.MaxValue;
            for (int i = 0; i < c.Length; i++)
            {
                float d = (c[i] - p).sqrMagnitude;
                if (d < best) best = d;
            }
            return Mathf.Sqrt(best);
        }
    }
}
