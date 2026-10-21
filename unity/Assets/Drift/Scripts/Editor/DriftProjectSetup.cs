using System;
using System.IO;
using Drift.Game;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Drift.EditorTools
{
    /// <summary>
    /// One-click project setup, run automatically the first time the project opens
    /// (and any time from Drift > Setup Project):
    ///   - a mobile-quality URP asset (no HDR, 2x MSAA, one shadow cascade) so laptop numbers aren't flattering,
    ///   - shared materials in Resources/Materials so their shaders ship in the build,
    ///   - the Main scene (one GameObject with DriftGame) in Build Settings,
    ///   - Android player settings: landscape, IL2CPP, ARM64, cleartext HTTP for the local API, frame timing stats.
    /// </summary>
    [InitializeOnLoad]
    public static class DriftProjectSetup
    {
        public const string ScenePath = "Assets/Scenes/Main.unity";
        private const string SettingsFolder = "Assets/Settings";
        private const string MaterialsFolder = "Assets/Resources/Materials";
        private const string PipelinePath = SettingsFolder + "/Drift_Mobile_URP.asset";
        private const string RendererPath = SettingsFolder + "/Drift_Mobile_Renderer.asset";

        static DriftProjectSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(ScenePath) && !Application.isBatchMode) Run();
            };
        }

        [MenuItem("Drift/Setup Project", priority = 0)]
        public static void Run()
        {
            EnsureFolder("Assets/Scenes");
            EnsureFolder(SettingsFolder);
            EnsureFolder("Assets/Resources");
            EnsureFolder(MaterialsFolder);

            SetupPipeline();
            CreateMaterials();
            ConfigurePlayer();
            CreateScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[Drift] Project set up. Press Play in Assets/Scenes/Main.unity.");
        }

        [MenuItem("Drift/Rebuild Materials", priority = 1)]
        public static void CreateMaterials()
        {
            foreach (var entry in MaterialLibrary.Palette)
            {
                string path = MaterialsFolder + "/" + entry.name + ".mat";
                Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                Material fresh = MaterialLibrary.Create(entry.name, entry.color);
                if (existing == null)
                {
                    AssetDatabase.CreateAsset(fresh, path);
                }
                else
                {
                    existing.shader = fresh.shader;
                    existing.CopyPropertiesFromMaterial(fresh);
                    existing.shaderKeywords = fresh.shaderKeywords;
                    existing.renderQueue = fresh.renderQueue;
                    EditorUtility.SetDirty(existing);
                }
            }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Drift/Clear Local Ghost and Best Lap", priority = 20)]
        public static void ClearLocalGhost()
        {
            GhostStore.Clear("harbor");
            Debug.Log("[Drift] Local ghost and personal best cleared.");
        }

        private static void SetupPipeline()
        {
            if (GraphicsSettings.defaultRenderPipeline != null) return;
            try
            {
                var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
                if (pipeline == null)
                {
                    var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                    AssetDatabase.CreateAsset(rendererData, RendererPath);
                    pipeline = UniversalRenderPipelineAsset.Create(rendererData);
                    pipeline.supportsHDR = false;
                    pipeline.msaaSampleCount = 2;
                    pipeline.renderScale = 1f;
                    pipeline.shadowDistance = 60f;
                    pipeline.shadowCascadeCount = 1;
                    AssetDatabase.CreateAsset(pipeline, PipelinePath);
                }
                GraphicsSettings.defaultRenderPipeline = pipeline;
                int current = QualitySettings.GetQualityLevel();
                for (int i = 0; i < QualitySettings.names.Length; i++)
                {
                    QualitySettings.SetQualityLevel(i, false);
                    QualitySettings.renderPipeline = pipeline;
                }
                QualitySettings.SetQualityLevel(current, false);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Drift] Could not create a URP asset automatically (" + e.Message +
                                 "). The game still runs on the built-in pipeline; to use URP, assign a URP asset in " +
                                 "Project Settings > Graphics, then run Drift > Rebuild Materials.");
            }
        }

        private static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "Joy Kukreja";
            PlayerSettings.productName = "Drift";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.joykukreja.drift");
            PlayerSettings.bundleVersion = DriftGame.ClientVersion;

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.enableFrameTimingStats = true;
            // The local API in Docker is plain HTTP; Android blocks cleartext unless this is set.
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
        }

        private static void CreateScene()
        {
            if (File.Exists(ScenePath))
            {
                AddSceneToBuild();
                return;
            }
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Drift");
            root.AddComponent<DriftGame>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuild();
        }

        private static void AddSceneToBuild()
        {
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
