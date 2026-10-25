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
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Oasis/OasisScene.unity");
            FocusSceneCameraOnArena();
            Debug.Log("<color=cyan><b>[Oasis]</b> OasisScene successfully opened in Editor Scene view.</color>");
        }
    }

    [MenuItem("Oasis/Reimport All Shaders and Materials", priority = 2)]
    public static void ReimportShadersAndMaterials()
    {
        AssetDatabase.ImportAsset("Assets/Scenes/Oasis/Shaders", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset("Assets/Scenes/Oasis/Art", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var sceneView = SceneView.lastActiveSceneView ?? (SceneView.sceneViews.Count > 0 ? SceneView.sceneViews[0] as SceneView : null);
        if (sceneView != null) sceneView.Repaint();
        Debug.Log("<color=cyan><b>[Oasis]</b> Shaders and materials forcefully reimported.</color>");
    }

    [MenuItem("Oasis/Focus Scene Camera on Combat Arena", priority = 1)]
    public static void FocusSceneCameraOnArena()
    {
        var sceneView = SceneView.lastActiveSceneView ?? (SceneView.sceneViews.Count > 0 ? SceneView.sceneViews[0] as SceneView : null);
        if (sceneView != null)
        {
            sceneView.pivot = new Vector3(0f, 2.5f, -145f);
            sceneView.rotation = Quaternion.Euler(8f, 180f, 0f);
            sceneView.size = 28f;
            sceneView.Repaint();
            Debug.Log("<color=cyan><b>[Oasis]</b> Scene view camera focused on combat arena.</color>");
        }

        var player = GameObject.FindWithTag("Player") ?? GameObject.Find("FPS_Controller");
        if (player != null)
        {
            Selection.activeGameObject = player;
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
        string resultMsg = $"Result: {report.summary.result}, Errors: {report.summary.totalErrors}, Warnings: {report.summary.totalWarnings}, Time: {report.summary.totalTime.TotalSeconds:F1}s";
        Debug.Log($"[OasisBuildTrigger] Windows build completed with {resultMsg}");
        try
        {
            using (var sw = new StreamWriter("Temp/build_result.txt"))
            {
                sw.WriteLine(resultMsg);
                foreach (var step in report.steps)
                {
                    foreach (var msg in step.messages)
                    {
                        if (msg.type == LogType.Error || msg.type == LogType.Exception || msg.type == LogType.Warning)
                        {
                            sw.WriteLine($"[{msg.type}] {msg.content}");
                        }
                    }
                }
            }
        }
        catch {}
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
