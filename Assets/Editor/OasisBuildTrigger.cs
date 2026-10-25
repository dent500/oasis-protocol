#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class OasisBuildTrigger
{
    private const string TriggerFile = "Temp/trigger_build.txt";
    private const string OpenOasisTrigger = "Temp/open_oasis_scene.txt";

    static OasisBuildTrigger()
    {
        EditorApplication.update += PollTrigger;
    }

    [MenuItem("Oasis/Scenes/Open Oasis Scene (Combat Map)", priority = 10)]
    public static void OpenOasisScene()
    {
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Oasis/OasisScene.unity");
            Debug.Log("<color=cyan><b>[Oasis]</b> OasisScene successfully opened in Editor Scene view.</color>");
        }
    }

    [MenuItem("Oasis/Scenes/Open Main Menu", priority = 11)]
    public static void OpenMainMenuScene()
    {
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            EditorSceneManager.OpenScene("Assets/Scenes/MainMenu/MainMenu.unity");
            Debug.Log("<color=cyan><b>[Oasis]</b> MainMenu successfully opened in Editor Scene view.</color>");
        }
    }

    [MenuItem("Oasis/Build Windows Standalone", priority = 20)]
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
        Debug.Log($"[OasisBuildTrigger] Windows build completed with result: {report.summary.result} in {report.summary.totalTime.TotalSeconds:F1}s ({report.summary.totalErrors} errors)");
    }

    private static void PollTrigger()
    {
        if (File.Exists(OpenOasisTrigger))
        {
            try { File.Delete(OpenOasisTrigger); } catch {}
            OpenOasisScene();
        }
        else if (File.Exists(TriggerFile))
        {
            try { File.Delete(TriggerFile); } catch {}
            BuildStandalone();
        }
    }
}
#endif
