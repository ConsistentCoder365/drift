using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Drift.EditorTools
{
    /// <summary>
    /// Android builds from the menu or the command line:
    ///   Unity -batchmode -quit -projectPath unity -executeMethod Drift.EditorTools.DriftBuild.BuildAndroidCli [-development]
    /// The development build connects to the Profiler and enables draw-call counters in the overlay.
    /// </summary>
    public static class DriftBuild
    {
        private const string OutputDir = "Builds/Android";

        [MenuItem("Drift/Build Android APK (Release)", priority = 40)]
        public static void BuildRelease()
        {
            Build(false);
        }

        [MenuItem("Drift/Build Android APK (Development + Profiler)", priority = 41)]
        public static void BuildDevelopment()
        {
            Build(true);
        }

        public static void BuildAndroidCli()
        {
            bool dev = Array.IndexOf(Environment.GetCommandLineArgs(), "-development") >= 0;
            bool ok = Build(dev);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        private static bool Build(bool development)
        {
            if (!File.Exists(DriftProjectSetup.ScenePath)) DriftProjectSetup.Run();
            Directory.CreateDirectory(OutputDir);
            EditorUserBuildSettings.buildAppBundle = false;

            string apk = Path.Combine(OutputDir, development ? "Drift-dev.apk" : "Drift.apk");
            var options = new BuildPlayerOptions
            {
                scenes = new[] { DriftProjectSetup.ScenePath },
                locationPathName = apk,
                target = BuildTarget.Android,
                options = development
                    ? BuildOptions.Development | BuildOptions.ConnectWithProfiler | BuildOptions.AllowDebugging
                    : BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary s = report.summary;
            if (s.result != BuildResult.Succeeded)
            {
                Debug.LogError("[Drift] Build failed: " + s.result + ", " + s.totalErrors + " errors.");
                return false;
            }
            Debug.Log("[Drift] Built " + apk + ": " + (s.totalSize / (1024f * 1024f)).ToString("0.0") + " MB in " + s.totalTime.TotalSeconds.ToString("0") + " s.");
            return true;
        }
    }
}
