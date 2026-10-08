using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Drift.Game
{
    /// <summary>
    /// Shared materials. Drift > Setup Project saves these into Assets/Resources/Materials so the
    /// right shaders ship in the build; if they are missing (first play before setup) they are
    /// created at runtime from the active pipeline's default shader.
    /// One material per colour, shared by every renderer, keeps batching possible.
    /// </summary>
    public static class MaterialLibrary
    {
        public const string Folder = "Materials";

        public static readonly (string name, Color color)[] Palette =
        {
            ("Ground", new Color(0.32f, 0.55f, 0.30f)),
            ("Road", new Color(0.22f, 0.23f, 0.26f)),
            ("RoadLine", new Color(0.92f, 0.92f, 0.88f)),
            ("CurbRed", new Color(0.85f, 0.18f, 0.16f)),
            ("CurbWhite", new Color(0.95f, 0.95f, 0.95f)),
            ("Wall", new Color(0.70f, 0.72f, 0.76f)),
            ("Checker", new Color(0.08f, 0.08f, 0.08f)),
            ("CarBody", new Color(0.96f, 0.42f, 0.10f)),
            ("CarCabin", new Color(0.12f, 0.14f, 0.18f)),
            ("Tire", new Color(0.06f, 0.06f, 0.06f)),
            ("Cone", new Color(1.00f, 0.50f, 0.05f)),
            ("Trunk", new Color(0.40f, 0.27f, 0.16f)),
            ("Leaves", new Color(0.20f, 0.45f, 0.22f)),
            ("Lamp", new Color(0.35f, 0.36f, 0.40f)),
            ("Skid", new Color(0.05f, 0.05f, 0.05f, 0.55f)),
            ("Ghost", new Color(0.35f, 0.80f, 1.00f, 0.38f)),
        };

        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();

        public static Material Get(string name)
        {
            if (Cache.TryGetValue(name, out Material m) && m != null) return m;

            m = Resources.Load<Material>(Folder + "/" + name);
            if (m == null) m = CreateRuntime(name);
            Cache[name] = m;
            return m;
        }

        public static void SetInstancing(bool enabled)
        {
            foreach (var entry in Palette) Get(entry.name).enableInstancing = enabled;
        }

        public static Shader LitShader()
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            Shader shader = pipeline != null ? pipeline.defaultShader : null;
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            return shader;
        }

        public static Material Create(string name, Color color)
        {
            var m = new Material(LitShader()) { name = name };
            SetColor(m, color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.15f);
            if (color.a < 0.999f) MakeTransparent(m);
            return m;
        }

        public static void SetColor(Material m, Color color)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        }

        /// <summary>Alpha blending for both URP Lit and the built-in Standard shader.</summary>
        public static void MakeTransparent(Material m)
        {
            if (m.HasProperty("_Surface"))
            {
                // URP Lit
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Transparent");
            }
            else if (m.HasProperty("_Mode"))
            {
                // Built-in Standard, "Fade" mode
                m.SetFloat("_Mode", 2f);
                m.EnableKeyword("_ALPHABLEND_ON");
                m.SetOverrideTag("RenderType", "Transparent");
            }
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            m.SetShaderPassEnabled("DepthOnly", false);
            m.SetShaderPassEnabled("ShadowCaster", false);
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        private static Material CreateRuntime(string name)
        {
            foreach (var entry in Palette)
                if (entry.name == name) return Create(name, entry.color);
            return Create(name, Color.magenta);
        }
    }
}
