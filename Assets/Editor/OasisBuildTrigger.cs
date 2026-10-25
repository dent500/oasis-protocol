#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

[InitializeOnLoad]
public static class OasisBuildTrigger
{
    private const string TriggerFile = "Temp/trigger_build.txt";
    private const string TriggerLinuxFile = "Temp/trigger_build_linux.txt";
    private const string TriggerAllFile = "Temp/trigger_build_all.txt";

    static OasisBuildTrigger()
    {
        EditorApplication.update += PollTrigger;
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

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"[OasisBuildTrigger] Build completed with result: {report.summary.result} in {report.summary.totalTime.TotalSeconds:F1}s ({report.summary.totalErrors} errors)");
    }

    [MenuItem("Oasis/Build Linux Standalone")]
    public static void BuildLinuxStandalone()
    {
        Debug.Log("[OasisBuildTrigger] Starting Linux Standalone build...");

        string[] scenes = new string[]
        {
            "Assets/Scenes/MainMenu/MainMenu.unity",
            "Assets/Scenes/Oasis/OasisScene.unity"
        };

        string buildPath = "Builds/Linux/Oasis Protocol.x86_64";
        string buildDir = Path.GetDirectoryName(buildPath);
        if (!string.IsNullOrEmpty(buildDir) && !Directory.Exists(buildDir))
        {
            Directory.CreateDirectory(buildDir);
        }

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = buildPath,
            target = BuildTarget.StandaloneLinux64,
            options = BuildOptions.None
        };

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"[OasisBuildTrigger] Linux build completed with result: {report.summary.result} in {report.summary.totalTime.TotalSeconds:F1}s ({report.summary.totalErrors} errors)");
    }

    private static void PollTrigger()
    {
        if (File.Exists(TriggerAllFile))
        {
            try { File.Delete(TriggerAllFile); } catch {}
            BuildStandalone();
            BuildLinuxStandalone();
        }
        else if (File.Exists(TriggerFile))
        {
            try { File.Delete(TriggerFile); } catch {}
            BuildStandalone();
        }
        else if (File.Exists(TriggerLinuxFile))
        {
            try { File.Delete(TriggerLinuxFile); } catch {}
            BuildLinuxStandalone();
        }
    }
}
#endif
