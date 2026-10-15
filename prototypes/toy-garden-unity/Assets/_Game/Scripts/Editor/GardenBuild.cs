using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ToyGarden.Editor
{
    public static class GardenBuild
    {
        [MenuItem("Toy Garden/Open Prototype Scene")]
        public static void OpenPrototype()
        {
            EditorSceneManager.OpenScene(GardenSceneBuilder.ScenePath);
        }

        [MenuItem("Toy Garden/Build macOS Prototype")]
        public static void BuildMac()
        {
            if (!File.Exists(GardenSceneBuilder.ScenePath)) GardenSceneBuilder.Build();
            string output = Path.GetFullPath("Builds/ToyGarden.app");
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-gardenBuildPath") output = Path.GetFullPath(args[i + 1]);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            PlayerSettings.companyName = "ToyGarden";
            PlayerSettings.productName = "风车谷";
            PlayerSettings.defaultScreenWidth = 1440;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.runInBackground = false;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { GardenSceneBuilder.ScenePath },
                locationPathName = output,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"Prototype build failed: {report.summary.totalErrors} errors.");
            Debug.Log($"Toy Garden macOS build succeeded: {output} ({report.summary.totalSize} bytes)");
        }
    }
}
