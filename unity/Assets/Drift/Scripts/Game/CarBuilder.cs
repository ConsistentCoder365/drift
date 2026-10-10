using UnityEngine;
using UnityEngine.Rendering;

namespace Drift.Game
{
    /// <summary>
    /// Builds the car's look. If a prefab named "CarModel" exists in a Resources folder (for
    /// example a car from Kenney's Car Kit), it is used and any child named like "wheel..." spins;
    /// otherwise a low-poly car is assembled from primitives.
    /// </summary>
    public static class CarBuilder
    {
        public const float Length = 4.2f;
        public const float Width = 1.9f;

        public static CarVisual Build(Transform parent, bool ghost)
        {
            var root = new GameObject(ghost ? "GhostVisual" : "CarVisual");
            root.transform.SetParent(parent, false);
            var visual = root.AddComponent<CarVisual>();

            GameObject model = Resources.Load<GameObject>("CarModel");
            if (model != null)
            {
                GameObject instance = Object.Instantiate(model, root.transform, false);
                StripColliders(instance);
                visual.CollectWheels(instance.transform);
            }
            else
            {
                BuildPrimitiveCar(root.transform, visual);
            }

            if (ghost)
            {
                Material ghostMat = MaterialLibrary.Get("Ghost");
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
                {
                    var mats = new Material[r.sharedMaterials.Length];
                    for (int i = 0; i < mats.Length; i++) mats[i] = ghostMat;
                    r.sharedMaterials = mats;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                }
            }
            else
            {
                visual.AddSkidTrails();
            }
            return visual;
        }

        private static void BuildPrimitiveCar(Transform root, CarVisual visual)
        {
            Material body = MaterialLibrary.Get("CarBody");
            Material cabin = MaterialLibrary.Get("CarCabin");
            Material tire = MaterialLibrary.Get("Tire");

            Part(root, PrimitiveType.Cube, body, new Vector3(0f, 0.55f, 0f), new Vector3(Width, 0.55f, Length));
            Part(root, PrimitiveType.Cube, body, new Vector3(0f, 0.72f, 1.55f), new Vector3(Width * 0.92f, 0.25f, 1.0f));
            Part(root, PrimitiveType.Cube, cabin, new Vector3(0f, 1.05f, -0.25f), new Vector3(Width * 0.82f, 0.5f, 1.9f));
            Part(root, PrimitiveType.Cube, cabin, new Vector3(0f, 1.0f, -2.0f), new Vector3(Width * 0.95f, 0.08f, 0.35f));
            Part(root, PrimitiveType.Cube, cabin, new Vector3(-0.7f, 0.85f, -2.0f), new Vector3(0.08f, 0.35f, 0.2f));
            Part(root, PrimitiveType.Cube, cabin, new Vector3(0.7f, 0.85f, -2.0f), new Vector3(0.08f, 0.35f, 0.2f));

            float wx = Width * 0.5f;
            const float wz = 1.35f;
            visual.AddWheel(Wheel(root, tire, new Vector3(-wx, 0.36f, wz)), true);
            visual.AddWheel(Wheel(root, tire, new Vector3(wx, 0.36f, wz)), true);
            visual.AddWheel(Wheel(root, tire, new Vector3(-wx, 0.36f, -wz)), false);
            visual.AddWheel(Wheel(root, tire, new Vector3(wx, 0.36f, -wz)), false);
        }

        private static Transform Wheel(Transform root, Material tire, Vector3 position)
        {
            // A cylinder laid on its side: its axle ends up along the car's X axis.
            GameObject w = Part(root, PrimitiveType.Cylinder, tire, position, new Vector3(0.72f, 0.18f, 0.72f));
            w.name = "Wheel";
            w.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            return w.transform;
        }

        private static GameObject Part(Transform parent, PrimitiveType type, Material mat, Vector3 position, Vector3 scale)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        private static void StripColliders(GameObject go)
        {
            foreach (Collider c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        }
    }
}
