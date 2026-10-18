#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

[InitializeOnLoad]
public static class OasisBuildTrigger
{
    private const string TriggerFile = "Temp/trigger_build.txt";

    static OasisBuildTrigger()
    {
        EditorApplication.delayCall += CheckTrigger;
    }

    [MenuItem("Oasis/Build Windows Standalone")]
    public static void BuildStandalone()
    {
        Debug.Log("[OasisBuildTrigger] Starting Windows Standalone build...");

        string[] scenes = new string[]
        {
            "Assets/Scenes/MainMenu/MainMenu.unity",
            "Assets/Scenes/Oasis/OasisScene.unity"
        };

        string buildPath = "Builds/Oasis Protocol.exe";
        string buildDir = Path.GetDirectoryName(buildPath);
        if (!string.IsNullOrEmpty(buildDir) && !Directory.Exists(buildDir))
        {
            Directory.CreateDirectory(buildDir);
        }

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = buildPath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"[OasisBuildTrigger] Build completed with result: {report.summary.result} in {report.summary.totalTime.TotalSeconds:F1}s ({report.summary.totalErrors} errors)");
    }

    private static void CheckTrigger()
    {
        if (File.Exists(TriggerFile))
        {
            try
            {
                File.Delete(TriggerFile);
            }
            catch {}
            BuildStandalone();
        }
    }
}
#endif
